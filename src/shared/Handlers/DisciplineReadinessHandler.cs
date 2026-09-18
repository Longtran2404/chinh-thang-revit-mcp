// Chinh Thang: bounded read-only discipline checks; observations are not compliance certificates.
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class DisciplineReadinessHandler : IRevitCommand
    {
        private readonly string discipline;
        public DisciplineReadinessHandler(string discipline) { this.discipline=discipline; }
        public string Name=>"audit_"+discipline+"_readiness";
        public string Description=>"Read-only "+discipline+" observations with explicit scope, errors and unverified checks.";
        public string ParametersSchema=>@"{""type"":""object"",""properties"":{""element_ids"":{""type"":""array"",""items"":{""type"":""integer""}},""limit"":{""type"":""integer"",""minimum"":1,""maximum"":500}}}";
        private static readonly BuiltInCategory[] ArchitecturalCategories={BuiltInCategory.OST_Walls,BuiltInCategory.OST_Floors,BuiltInCategory.OST_Ceilings,BuiltInCategory.OST_Roofs,BuiltInCategory.OST_Doors,BuiltInCategory.OST_Windows,BuiltInCategory.OST_Furniture,BuiltInCategory.OST_Rooms};
        public CommandResult Execute(UIApplication app,string json)
        {
            var d=app.ActiveUIDocument?.Document;
            if(d==null)return CommandResult.Fail("No active project.");
            try{return Inspect(d,JObject.Parse(string.IsNullOrWhiteSpace(json)?"{}":json));}
            catch(Exception ex){return CommandResult.Fail(Name+": "+ex.Message);}
        }
        public CommandResult Inspect(Document d,JObject p)
        {
            int limit=p.Value<int?>("limit")??100;
            if(limit<1||limit>500)throw new ArgumentException("limit must be 1-500.");
            IEnumerable<Element> candidates;
            if(p["element_ids"] is JArray ids) {
                if(ids.Count<1||ids.Count>500||ids.Any(t=>t.Type!=JTokenType.Integer || (long)t<=0))throw new ArgumentException("element_ids must contain 1-500 positive integer IDs.");
                candidates=ids.Values<long>().Distinct().Select(id=>d.GetElement(RevitCompat.ToElementId(id))).ToArray();
                if(candidates.Any(e=>e==null||e is ElementType))throw new ArgumentException("Every ID must resolve to a current project instance.");
            } else {
                var collector=new FilteredElementCollector(d).WhereElementIsNotElementType();
                if(discipline=="architecture")collector.WherePasses(new ElementMulticategoryFilter(ArchitecturalCategories));
                else if(discipline=="hse")collector.OfClass(typeof(Floor));
                else collector.WherePasses(new LogicalOrFilter(new ElementClassFilter(typeof(MEPCurve)),new ElementClassFilter(typeof(FamilyInstance))));
                candidates=collector;
                if(discipline=="mep")candidates=candidates.Where(e=>e is MEPCurve || (e as FamilyInstance)?.MEPModel!=null);
            }
            var sample=candidates.Take(limit+1).ToArray();var rows=new JArray();
            foreach(var e in sample.Take(limit)) {
                var row=new JObject{["element_id"]=RevitCompat.GetId(e.Id),["unique_id"]=e.UniqueId,["category"]=e.Category?.Name,["name"]=e.Name};
                try {
                    if(discipline=="architecture")Architecture(d,e,row);
                    else if(discipline=="mep")Mep(e,row);
                    else if(discipline=="hse")Hse(e,row);
                    else throw new ArgumentException("Unsupported discipline.");
                } catch(Exception ex){row["inspection_error"]=ex.Message;row["status"]="NOT_INSPECTED";}
                rows.Add(row);
            }
            return CommandResult.Ok(new {discipline,scope="Current document instances only; linked models and other phases are not independently audited. Returned phase IDs must be filtered for the construction stage.",returned=rows.Count,truncated=sample.Length>limit,items=rows,compliance_verified=false,notes=discipline=="hse"?"Floor boundaries are REVIEW CANDIDATES, not confirmed unprotected edges. No fall height, protection, construction stage, site condition or statutory compliance has been determined.":"Read-only model observations. Engineering sizing, code compliance and family flex require separate checks."});
        }
        private static void Architecture(Document d,Element e,JObject row)
        {
            row["created_phase_id"]=RevitCompat.GetId(e.CreatedPhaseId);
            var observations=new JArray();row["observations"]=observations;
            if(e is Room room) {
                row["room_number"]=room.Number;row["area_m2"]=room.Area*0.09290304;
                if(room.Location==null)observations.Add("ROOM_UNPLACED");else if(room.Area<=0)observations.Add("ROOM_NOT_ENCLOSED_OR_REDUNDANT");
            } else if(e is FamilyInstance fi) {
                var family=fi.Symbol.Family;
                row["family"]=family.Name;row["family_editable"]=family.IsEditable;row["family_in_place"]=family.IsInPlace;
                row["host_id"]=fi.Host==null?(long?)null:RevitCompat.GetId(fi.Host.Id);
                row["family_flex_verified"]=false;
                if(!family.IsEditable||family.IsInPlace)observations.Add("REVIEW_EDITABLE_LOADABLE_FAMILY_REQUIREMENT");
                if(fi.Host==null&&(e.Category.Id==new ElementId(BuiltInCategory.OST_Doors)||e.Category.Id==new ElementId(BuiltInCategory.OST_Windows)))observations.Add("REVIEW_UNHOSTED_OPENING_FAMILY");
            } else if(e is DirectShape||e is ImportInstance)observations.Add("PROXY_OR_IMPORT_REQUIRES_NATIVE_MODEL_REVIEW");
            else if(!(e is Wall||e is Floor||e is Ceiling||e is RoofBase))observations.Add("OUTSIDE_ARCHITECTURAL_AUDIT_SCOPE");
            if(!(e is Room)) {
                var materials=e.GetMaterialIds(false);row["material_count"]=materials.Count;
                if(materials.Count==0)observations.Add("NO_REPORTED_BODY_MATERIALS");
                row["materials_without_appearance"]=new JArray(materials.Where(id=>(d.GetElement(id) as Material)?.AppearanceAssetId==ElementId.InvalidElementId).Select(id=>RevitCompat.GetId(id)));
            }
            row["status"]="OBSERVATIONS_ONLY";
        }
        private static void Mep(Element e,JObject row)
        {
            var manager=(e as MEPCurve)?.ConnectorManager??(e as FamilyInstance)?.MEPModel?.ConnectorManager;
            if(manager==null)throw new ArgumentException("Element has no accessible MEP connector manager.");
            var ends=new JArray();
            foreach(Connector c in manager.Connectors) {
                if(c.ConnectorType!=ConnectorType.End)continue;
                var item=new JObject{["connector_id"]=c.Id};ends.Add(item);
                try {
                    item["domain"]=c.Domain.ToString();item["origin_mm"]=Point(c.Origin);item["connected"]=c.IsConnected;
                    item["status"]=c.IsConnected?"CONNECTED":"OPEN_END_REVIEW";
                    item["shape"]=c.Shape.ToString();
                    if(c.Shape==ConnectorProfileType.Round)item["diameter_mm"]=2*c.Radius*304.8;
                    else if(c.Shape==ConnectorProfileType.Rectangular||c.Shape==ConnectorProfileType.Oval){item["width_mm"]=c.Width*304.8;item["height_mm"]=c.Height*304.8;}
                }catch(Exception ex){item["inspection_error"]=ex.Message;item["status"]="NOT_INSPECTED";}
            }
            row["physical_end_connectors"]=ends;row["open_end_is_necessarily_fault"]=false;
            if(e is MEPCurve mep) {
                row["system_id"]=mep.MEPSystem==null?(long?)null:RevitCompat.GetId(mep.MEPSystem.Id);
                var curve=(mep.Location as LocationCurve)?.Curve;
                if(curve is Line line) {
                    var delta=line.GetEndPoint(1)-line.GetEndPoint(0);double horizontal=Math.Sqrt(delta.X*delta.X+delta.Y*delta.Y);
                    row["length_mm"]=line.Length*304.8;
                    row["absolute_slope_percent"]=horizontal<1e-9?(double?)null:100*Math.Abs(delta.Z)/horizontal;
                    row["vertical"]=horizontal<1e-9;row["slope_is_flow_direction"]=false;
                } else row["slope_status"]="NON_LINEAR_OR_UNAVAILABLE";
            }
            row["status"]="OBSERVATIONS_ONLY";row["sizing_verified"]=false;
        }
        private static JArray Point(XYZ p)=>new JArray(p.X*304.8,p.Y*304.8,p.Z*304.8);
        private static void Hse(Element e,JObject row)
        {
            if(!(e is Floor floor))throw new ArgumentException("HSE boundary scan currently supports Floor instances only.");
            row["created_phase_id"]=RevitCompat.GetId(e.CreatedPhaseId);row["demolished_phase_id"]=RevitCompat.GetId(e.DemolishedPhaseId);
            var boundaries=new JArray();row["boundary_candidates"]=boundaries;
            int faceIndex=0,segmentCount=0;
            foreach(var reference in HostObjectUtils.GetTopFaces(floor)) {
                if(!(floor.GetGeometryObjectFromReference(reference) is Face face))continue;
                int ringIndex=0;
                foreach(var loop in face.GetEdgesAsCurveLoops()) {
                    var ring=new JObject{["face_index"]=faceIndex,["ring_index"]=ringIndex++,["classification"]="PERIMETER_OR_OPENING_REVIEW",["protection_verified"]=false};
                    var segments=new JArray();ring["segments"]=segments;boundaries.Add(ring);
                    foreach(var curve in loop) {
                        if(++segmentCount>500){row["boundaries_truncated"]=true;row["status"]="PARTIAL_REVIEW_CANDIDATES";return;}
                        segments.Add(new JObject{["start_mm"]=Point(curve.GetEndPoint(0)),["end_mm"]=Point(curve.GetEndPoint(1)),["length_mm"]=curve.Length*304.8,["curve_type"]=curve.GetType().Name});
                    }
                }
                faceIndex++;
            }
            row["status"]=boundaries.Count==0?"NO_BOUNDARIES_EXTRACTED":"REVIEW_CANDIDATES";
        }
    }
}
