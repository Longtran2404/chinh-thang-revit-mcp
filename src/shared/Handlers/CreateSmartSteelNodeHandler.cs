using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class CreateSmartSteelNodeHandler : IRevitCommand
    {
        public string Name=>"create_smart_steel_node";
        public string Description=>"Find the physical centroid axes of two I members, classify their joint and use a project-mapped native detailed connection type.";
        public string ParametersSchema=>@"{""type"":""object"",""properties"":{""request_json"":{""type"":""string""}},""required"":[""request_json""]}";
        public CommandResult Execute(UIApplication app,string json)
        {
            var d=app.ActiveUIDocument?.Document;if(d==null)return CommandResult.Fail("No document is open.");
            try{return Create(d,JObject.Parse(JObject.Parse(json).Value<string>("request_json")));}catch(Exception ex){return CommandResult.Fail("Steel node: "+ex.Message);}
        }
        public static CommandResult Create(Document d,JObject p)
        {
            string behavior=SteelDetailInput.Behavior(p);
            var boltCheck=p["bolt_layout"] is JObject layout?SteelDetailInput.CheckBoltLayout(layout):null;
            if(boltCheck!=null&&!boltCheck.Value<bool>("passed"))return CommandResult.Fail("TCVN bolt layout failed: "+boltCheck.ToString(Newtonsoft.Json.Formatting.None));
            var first=d.GetElement(RevitCompat.ToElementId(p.Value<long>("primary_id"))) as FamilyInstance;
            var second=d.GetElement(RevitCompat.ToElementId(p.Value<long>("secondary_id"))) as FamilyInstance;
            if(first==null||second==null||first.Id==second.Id)throw new ArgumentException("Two distinct structural I-section family instances required.");
            var a=Axis(first);var b=Axis(second);var u=(a[1]-a[0]).Normalize();var v=(b[1]-b[0]).Normalize();
            double tolerance=DetailingDesign.Required(p,"node_tolerance_mm")/304.8;
            if(tolerance>100/304.8)throw new ArgumentException("Node tolerance cannot exceed 100 mm; model offsets explicitly.");
            double dot=u.DotProduct(v),den=1-dot*dot;XYZ pa,pb;
            if(den<1e-8) {
                var pairs=(from x in a from y in b select new {x,y,distance=x.DistanceTo(y)}).OrderBy(x=>x.distance).ToArray();pa=pairs[0].x;pb=pairs[0].y;
            } else {
                var w=a[0]-b[0];double du=u.DotProduct(w),dv=v.DotProduct(w);
                double s=(dot*dv-du)/den,t=(dv-dot*du)/den;
                if(s < -tolerance || s>(a[1]-a[0]).GetLength()+tolerance || t < -tolerance || t>(b[1]-b[0]).GetLength()+tolerance)throw new ArgumentException("Physical member axes do not meet within the member spans and tolerance.");
                pa=a[0]+s*u;pb=b[0]+t*v;
            }
            if(pa.DistanceTo(pb)>tolerance)throw new ArgumentException("Physical centroid axes miss each other; no guessed centered plate was created.");
            var node=(pa+pb)/2;
            bool column=first.Category.Id==new ElementId(BuiltInCategory.OST_StructuralColumns)||second.Category.Id==new ElementId(BuiltInCategory.OST_StructuralColumns);
            string kind=den<1e-8?"Splice":column?"BeamToColumn":"BeamToBeam";
            var report=new JObject{["joint_kind"]=kind,["node_mm"]=JArray.FromObject(new[]{node.X*304.8,node.Y*304.8,node.Z*304.8}),["physical_axis_gap_mm"]=pa.DistanceTo(pb)*304.8,["method"]="Centroids of thin physical-solid sections at 25% and 75% of each I member; not bounding-box center.",["plate_center_verified"]=false,["capacity_checked"]=false};
            bool analyze=p.Value<bool?>("analyze_only")??true;
            report["connection_behavior_requested"]=behavior;
            report["bolt_layout_check"]=boltCheck;
            report["bolt_layout_matches_native_connection_verified"]=false;
            report["rigidity_verified"]=false;
            report["design_detail_reference"]=p.Value<string>("design_detail_reference");
            report["stiffeners_requested"]=(p["stiffeners"] as JArray)?.Count??0;
            if(analyze)return CommandResult.Ok(report);
            string ruleKey=behavior=="Unspecified"?kind:kind+"_"+behavior;
            long? typeId=p.Value<long?>("connection_type_id")??DetailingStorage.Read(DetailingStorage.Profile(d))?["steel_connection_rules"]?[ruleKey]?.Value<long>();
            if(!typeId.HasValue)throw new ArgumentException("Save a steel_connection_rules mapping for "+kind+" or provide connection_type_id. No automatic unreviewed connection type selection.");
            var type=d.GetElement(RevitCompat.ToElementId(typeId.Value)) as StructuralConnectionHandlerType;
            if(type==null||!type.IsDetailed()||type.IsGeneric())throw new ArgumentException("The selected type must be a loaded detailed native connection.");
            bool dry=p.Value<bool?>("dry_run")??true;
            using(var group=new TransactionGroup(d,"Chinh Thang: Smart steel node")) {
                group.Start();
                using(var tx=new Transaction(d,"Chinh Thang: Native connection")) {
                    tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new RebarPathFailures()).SetClearAfterRollback(true));
                    var c=StructuralConnectionHandler.Create(d,new List<ElementId>{first.Id,second.Id},type.Id);d.Regenerate();
                    if(c==null)throw new InvalidOperationException("Autodesk connection service returned no connection.");
                    var stiffeners=CheckedDetailGeometry.Stiffeners(d,p,node);
                    report["stiffeners"]=stiffeners;
                    DetailingStorage.Write(c,new JObject{["kind"]="SmartSteelNode",["request"]=p.DeepClone(),["node"]=report.DeepClone()});
                    report["created_id"]=dry?(long?)null:RevitCompat.GetId(c.Id);report["dry_run"]=dry;
                    var origin=c.GetOrigin();report["connection_origin_mm"]=JArray.FromObject(new[]{origin.X*304.8,origin.Y*304.8,origin.Z*304.8});
                    report["checks"]="Native connection service controls plate/bolt geometry. Review actual plate centering, offsets, bolts, welds and capacity against the chosen project detail; this is not a Tekla-equivalent design engine.";
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Revit rolled back the connection.");
                    var failureMethod=c.GetType().GetMethod("GetFailed",Type.EmptyTypes);
                    if(failureMethod!=null && failureMethod.ReturnType==typeof(bool) && (bool)failureMethod.Invoke(c,null))throw new InvalidOperationException("Native detailed connection generation failed; the whole node was rolled back.");
                    if(dry)foreach(var item in stiffeners)item["id"]=null;
                }
                if(dry)group.RollBack();else group.Assimilate();
            }
            return CommandResult.Ok(report);
        }
        public static XYZ[] Axis(FamilyInstance e)
        {
            if(e.Category.Id!=new ElementId(BuiltInCategory.OST_StructuralFraming)&&e.Category.Id!=new ElementId(BuiltInCategory.OST_StructuralColumns))throw new ArgumentException("Only structural framing/column members are supported.");
            var section=e.Symbol.GetStructuralSection();
            if(section==null || !section.GetType().Name.StartsWith("StructuralSectionI",StringComparison.Ordinal))throw new ArgumentException("Member must expose an I-section structural section definition.");
            var solids=Solids(e.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).Where(s=>s.Volume>1e-8).ToArray();
            if(solids.Length==0)throw new ArgumentException("No physical member solid available.");
            var location=(e.Location as LocationCurve)?.Curve;
            if(location!=null && !(location is Line))throw new ArgumentException("Curved members need a dedicated connection detail.");
            var direction=location is Line line ? line.Direction : e.GetTransform().BasisZ.Normalize();
            var vertices=solids.SelectMany(s=>s.Edges.Cast<Edge>()).SelectMany(edge=>edge.Tessellate()).ToArray();
            var origin=vertices[0];double low=vertices.Min(x=>(x-origin).DotProduct(direction)),high=vertices.Max(x=>(x-origin).DotProduct(direction));
            if(high-low<100/304.8)throw new ArgumentException("Member too short for reliable section sampling.");
            XYZ Centroid(double f) {
                var at=origin+direction*(low+(high-low)*f);double eps=0.5/304.8,total=0;var weighted=XYZ.Zero;
                foreach(var solid in solids) {
                    var cut=BooleanOperationsUtils.CutWithHalfSpace(solid,Plane.CreateByNormalAndOrigin(direction,at-direction*eps));
                    if(cut==null||cut.Volume<1e-12)continue;
                    cut=BooleanOperationsUtils.CutWithHalfSpace(cut,Plane.CreateByNormalAndOrigin(-direction,at+direction*eps));
                    if(cut==null||cut.Volume<1e-12)continue;
                    weighted+=cut.ComputeCentroid()*cut.Volume;total+=cut.Volume;
                }
                if(total<1e-12)throw new ArgumentException("Could not resolve physical section centroid.");return weighted/total;
            }
            var p=Centroid(0.25);var q=Centroid(0.75);
            if((q-p).CrossProduct(direction).GetLength()>0.1/304.8)throw new ArgumentException("Member section centroid shifts along its length; automatic centering needs a dedicated tapered/offset detail.");
            return new[]{p-direction*(high-low)*0.25,q+direction*(high-low)*0.25};
        }
        private static IEnumerable<Solid> Solids(GeometryElement g)
        {
            foreach(var o in g){if(o is Solid s)yield return s;else if(o is GeometryInstance i)foreach(var nested in Solids(i.GetInstanceGeometry()))yield return nested;}
        }
    }
}
