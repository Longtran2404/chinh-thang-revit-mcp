using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class CreateSlabSupportsHandler : IRevitCommand
    {
        public string Name=>"create_slab_supports";
        public string Description=>"Populate a horizontal slab with native rebar chairs at explicit project density/spacing, using existing top/bottom reinforcement layers.";
        public string ParametersSchema=>@"{""type"":""object"",""properties"":{""request_json"":{""type"":""string""}},""required"":[""request_json""]}";
        public CommandResult Execute(UIApplication app,string json)
        {
            var d=app.ActiveUIDocument?.Document;
            if(d==null) return CommandResult.Fail("No document is open.");
            try { return Create(d,JObject.Parse(JObject.Parse(json).Value<string>("request_json"))); }
            catch(Exception ex) { return CommandResult.Fail("Slab supports: "+ex.Message); }
        }
        public static CommandResult Create(Document d,JObject p)
        {
            var floor=d.GetElement(RevitCompat.ToElementId(p.Value<long>("host_id"))) as Floor;
            if(floor==null || !RebarHostData.IsValidHost(floor)) throw new ArgumentException("A concrete Floor host is required.");
            var faces=HostObjectUtils.GetTopFaces(floor).Select(r=>floor.GetGeometryObjectFromReference(r)).OfType<PlanarFace>().ToList();
            if(faces.Count!=1 || Math.Abs(faces[0].FaceNormal.Z)<0.999999) throw new ArgumentException("Use one planar horizontal slab; split sloping, warped or multi-face floors into separately reviewed regions.");
            var face=faces[0];
            var upper=d.GetElement(RevitCompat.ToElementId(p.Value<long>("upper_rebar_id"))) as Rebar;
            var lower=d.GetElement(RevitCompat.ToElementId(p.Value<long>("lower_rebar_id"))) as Rebar;
            if(upper==null || lower==null || upper.GetHostId()!=floor.Id || lower.GetHostId()!=floor.Id) throw new ArgumentException("Explicit upper/lower Rebar layers hosted by this floor are required.");
            var barType=d.GetElement(RevitCompat.ToElementId(p.Value<long>("bar_type_id"))) as RebarBarType;
            if(barType==null) throw new ArgumentException("Explicit support bar_type_id required.");
            double diameter=barType.get_Parameter(BuiltInParameter.REBAR_MODEL_BAR_DIAMETER).AsDouble()*304.8;
            double lo=Layer(d,lower,true)+diameter/2,hi=Layer(d,upper,false)-diameter/2;
            if(hi-lo<=diameter) throw new ArgumentException("No room for the requested chair between reinforcement layers.");
            var profile=DetailingStorage.Read(DetailingStorage.Profile(d));
            var layout=p["layout"] as JObject ?? profile?["support_regions"]?[p.Value<string>("zone_key")??""] as JObject ?? profile?["support_layout"] as JObject;
            if(layout==null) throw new ArgumentException("Provide layout or save support_layout in this project's detailing profile.");
            SlabSupportLayout.Validate(layout);
            double width=DetailingDesign.Required(p,"seat_width_mm"),foot=DetailingDesign.Required(p,"foot_length_mm");
            var kind=p.Value<string>("kind");
            if(kind!="PlanarChair" && kind!="SpatialChair" && kind!="ZigzagRail") throw new ArgumentException("kind must be PlanarChair, SpatialChair or ZigzagRail.");
            double depth=kind=="SpatialChair" ? DetailingDesign.Required(p,"seat_depth_mm") : 0;
            var zone=p.Value<string>("zone_key");
            if(string.IsNullOrWhiteSpace(zone) || zone.Length>80) throw new ArgumentException("Provide zone_key (1-80 characters) for repeatable replacement of this tool's own supports.");
            var outline=face.GetEdgesAsCurveLoops().SelectMany(l=>l).SelectMany(c=>c.Tessellate()).ToArray();
            double minX=outline.Min(v=>v.X)*304.8,maxX=outline.Max(v=>v.X)*304.8,minY=outline.Min(v=>v.Y)*304.8,maxY=outline.Max(v=>v.Y)*304.8;
            // Rectangular project zones intersect the true slab footprint including openings.
            var box=p["zone_box_mm"] as JArray;
            if(box!=null) {
                if(box.Count!=4) throw new ArgumentException("zone_box_mm must be [minX,minY,maxX,maxY].");
                minX=Math.Max(minX,(double)box[0]);minY=Math.Max(minY,(double)box[1]);maxX=Math.Min(maxX,(double)box[2]);maxY=Math.Min(maxY,(double)box[3]);
            }
            if(maxX<=minX || maxY<=minY) throw new ArgumentException("Zone does not intersect the slab.");
            // Exact net area from intersection of 1-foot footprint extrusions, including holes.
            var slabMask=GeometryCreationUtilities.CreateExtrusionGeometry(face.GetEdgesAsCurveLoops(),XYZ.BasisZ,1);
            double z=face.Origin.Z;
            var rect=new[]{new XYZ(minX/304.8,minY/304.8,z),new XYZ(maxX/304.8,minY/304.8,z),new XYZ(maxX/304.8,maxY/304.8,z),new XYZ(minX/304.8,maxY/304.8,z)};
            var loop=new CurveLoop();for(int i=0;i<4;i++) loop.Append(Line.CreateBound(rect[i],rect[(i+1)%4]));
            var mask=GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop>{loop},XYZ.BasisZ,1);
            double area=BooleanOperationsUtils.ExecuteBooleanOperation(slabMask,mask,BooleanOperationsType.Intersect).Volume*0.09290304;
            bool density=layout["area_per_support_m2"]!=null;
            double sx=density ? Math.Sqrt((double)layout["area_per_support_m2"])*1000 : (double)layout["spacing_x_mm"];
            double sy=density ? sx : (double)layout["spacing_y_mm"];
            double requiredCount=density ? Math.Ceiling(area/(double)layout["area_per_support_m2"]) : 0;
            if(requiredCount>2000 || area<=0) throw new ArgumentException("Zone must have positive area and require no more than 2000 supports.");
            int target=(int)requiredCount;
            var paths=new List<double[][]>();
            for(int attempt=0;attempt<8;attempt++) {
                paths.Clear();
                foreach(var point in SlabSupportLayout.Grid(minX,minY,maxX,maxY,sx,sy)) {
                    double x=point[0],y=point[1];
                    double[][] pts;
                    if(kind=="SpatialChair") pts=new[]{new[]{x-width/2-foot,y-depth/2,lo},new[]{x-width/2,y-depth/2,lo},new[]{x-width/2,y-depth/2,hi},new[]{x+width/2,y+depth/2,hi},new[]{x+width/2,y+depth/2,lo},new[]{x+width/2+foot,y+depth/2,lo}};
                    else if(kind=="ZigzagRail") pts=new[]{new[]{x-width/2-foot,y,lo},new[]{x-width/2,y,hi},new[]{x+width/2,y,hi},new[]{x+width/2+foot,y,lo}};
                    else pts=new[]{new[]{x-width/2-foot,y,lo},new[]{x-width/2,y,lo},new[]{x-width/2,y,hi},new[]{x+width/2,y,hi},new[]{x+width/2,y,lo},new[]{x+width/2+foot,y,lo}};
                    if(Fits(face,pts,minX,minY,maxX,maxY,diameter/2)) paths.Add(pts);
                }
                if(!density || paths.Count>=target) break;
                sx*=0.8;sy*=0.8;
            }
            if(paths.Count==0 || paths.Count>2000 || (density && paths.Count<target)) throw new ArgumentException("Requested density/footprint cannot fit the zone. Adjust chair size or split the zone; no partial layout was created.");
            var old=new FilteredElementCollector(d).OfClass(typeof(Rebar)).Cast<Rebar>().Where(e=> {
                var recipe=DetailingStorage.Read(e);return recipe?.Value<string>("kind")=="SlabSupport" && recipe.Value<string>("host_unique_id")==floor.UniqueId && recipe.Value<string>("zone_key")==zone;
            }).ToArray();
            if(old.Any(e=>e.GetDependentElements(null).Any(id=>d.GetElement(id) is RebarCoupler || d.GetElement(id) is Dimension || d.GetElement(id) is IndependentTag)))
                throw new ArgumentException("Existing zone supports have dependent couplers/dimensions/tags. Resolve those references before replacing the zone.");
            bool dry=p.Value<bool?>("dry_run")??false;
            var created=new List<long>();
            using(var group=new TransactionGroup(d,"Chinh Thang: Slab support region")) {
                group.Start();
                if(old.Length>0) using(var tx=new Transaction(d,"Chinh Thang: Replace owned support region")) {
                    tx.Start();foreach(var e in old)d.Delete(e.Id);
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Could not replace the owned support region.");
                }
                foreach(var pts in paths) {
                    var request=new JObject {["host_id"]=RevitCompat.GetId(floor.Id),["bar_type_id"]=RevitCompat.GetId(barType.Id),["points_json"]=JArray.FromObject(pts).ToString(),["mode"]=kind=="SpatialChair" ? "FreeForm" : "ShapeDriven",["normal_x"]=0,["normal_y"]=1,["normal_z"]=0};
                    var r=CreateRebarPathHandler.Create(d,request);
                    if(!r.Success) throw new InvalidOperationException(r.Error);
                    created.Add(JObject.FromObject(r.Data).Value<long>("created_id"));
                }
                using(var tx=new Transaction(d,"Chinh Thang: Support recipes")) {
                    tx.Start();
                    foreach(long id in created) DetailingStorage.Write(d.GetElement(RevitCompat.ToElementId(id)),new JObject {["kind"]="SlabSupport",["host_unique_id"]=floor.UniqueId,["zone_key"]=zone,["request"]=p.DeepClone(),["layout"]=layout.DeepClone()});
                    if(tx.Commit()!=TransactionStatus.Committed) throw new InvalidOperationException("Could not save support recipes.");
                }
                if(dry) group.RollBack();else group.Assimilate();
            }
            return CommandResult.Ok(new {dry_run=dry,created_ids=dry ? new long[0] : created.ToArray(),count=created.Count,replaced_count=old.Length,net_area_m2=area,actual_area_per_support_m2=area/created.Count,lower_seat_mm=lo,upper_seat_mm=hi,layout,zone_key=zone,checks="Density uses net slab/zone area and excludes openings. Footprint sampled at <=25mm with diameter clearance. Review bar clashes and construction loading separately."});
        }
        private static double Layer(Document d,Rebar bar,bool top)
        {
            var lines=bar.GetCenterlineCurves(false,false,false,MultiplanarOption.IncludeAllMultiplanarCurves,0).OfType<Line>().Where(l=>Math.Abs(l.Direction.Z)<1e-6).OrderByDescending(l=>l.Length).ToArray();
            if(lines.Length==0) throw new ArgumentException("Selected layer has no horizontal reinforcement segment.");
            double diameter=d.GetElement(bar.GetTypeId()).get_Parameter(BuiltInParameter.REBAR_MODEL_BAR_DIAMETER).AsDouble()*304.8;
            return lines[0].GetEndPoint(0).Z*304.8+(top ? 1 : -1)*diameter/2;
        }
        private static bool Fits(PlanarFace face,double[][] pts,double x0,double y0,double x1,double y1,double radius)
        {
            for(int i=1;i<pts.Length;i++) {
                var a=pts[i-1];var b=pts[i];int n=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(Math.Pow(b[0]-a[0],2)+Math.Pow(b[1]-a[1],2))/25));
                for(int k=0;k<=n;k++) {
                    double x=a[0]+(b[0]-a[0])*k/n,y=a[1]+(b[1]-a[1])*k/n;
                    if(x-radius<x0 || x+radius>x1 || y-radius<y0 || y+radius>y1) return false;
                    foreach(var offset in new[]{new[]{radius,0.0},new[]{-radius,0.0},new[]{0.0,radius},new[]{0.0,-radius}}) {
                        var hit=face.Project(new XYZ((x+offset[0])/304.8,(y+offset[1])/304.8,face.Origin.Z));
                        if(hit==null || !face.IsInside(hit.UVPoint)) return false;
                    }
                }
            }
            return true;
        }
    }
}
