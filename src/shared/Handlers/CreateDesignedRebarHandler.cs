using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class CreateDesignedRebarHandler : IRevitCommand
    {
        public string Name=>"create_designed_rebar";
        public string Description=>"Create a stored native rebar recipe with independently enabled start/end anchorage calculated from an explicit design profile.";
        public string ParametersSchema=>@"{""type"":""object"",""properties"":{""request_json"":{""type"":""string""}},""required"":[""request_json""]}";
        public CommandResult Execute(UIApplication app,string json)
        {
            var d=app.ActiveUIDocument?.Document;
            if(d==null)return CommandResult.Fail("No document is open.");
            try{return Create(d,JObject.Parse(JObject.Parse(json).Value<string>("request_json")));}
            catch(Exception ex){return CommandResult.Fail("Designed rebar: "+ex.Message);}
        }
        public static CommandResult Create(Document d,JObject input)
        {
            var p=(JObject)input.DeepClone();
            var previous=p.Value<long?>("replace_component_id");
            Rebar old=null;
            if(previous.HasValue) {
                old=d.GetElement(RevitCompat.ToElementId(previous.Value)) as Rebar;
                var stored=DetailingStorage.Read(old);
                if(stored?.Value<string>("kind")!="DesignedRebar") throw new ArgumentException("Replacement is restricted to a component created by this tool.");
                var saved=(JObject)stored["request"].DeepClone();saved.Merge(p,new JsonMergeSettings {MergeArrayHandling=MergeArrayHandling.Replace});p=saved;
                if(old.GetDependentElements(null).Any(id=>d.GetElement(id) is RebarCoupler || d.GetElement(id) is Dimension || d.GetElement(id) is IndependentTag)) throw new ArgumentException("Component has dependent couplers/dimensions/tags. Resolve those references before rebuilding; they are not deleted silently.");
            }
            var type=d.GetElement(RevitCompat.ToElementId(p.Value<long>("bar_type_id"))) as RebarBarType;
            if(type==null)throw new ArgumentException("bar_type_id must resolve to RebarBarType.");
            var body=RebarPathInput.Points(p.Value<string>("body_points_json")).Select(a=>new XYZ(a[0],a[1],a[2])).ToList();
            var design=p["design"] as JObject;
            if(design==null)throw new ArgumentException("Explicit design inputs are required.");
            double diameter=type.get_Parameter(BuiltInParameter.REBAR_MODEL_BAR_DIAMETER).AsDouble()*304.8;
            double nominal=type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble()*304.8;
            if(Math.Abs(DetailingDesign.Required(design,"diameter_mm")-nominal)>0.01)throw new ArgumentException("Design diameter must match the bar type nominal diameter.");
            var start=p["start_anchor"] as JObject;var end=p["end_anchor"] as JObject;
            if(start==null||end==null)throw new ArgumentException("Specify start_anchor and end_anchor with enabled and style.");
            var startCalc=AnchorDesign(design,start);var endCalc=AnchorDesign(design,end);
            double minimum=Math.Max(startCalc.Value<double>("minimum_bend_diameter_mm"),endCalc.Value<double>("minimum_bend_diameter_mm"));
            if(type.StandardBendDiameter*304.8+0.01<minimum && (body.Count>2 || start.Value<string>("style")!="Straight" || end.Value<string>("style")!="Straight"))throw new ArgumentException("Selected bar type bend diameter is below the calculated selected standard minimum. Duplicate/edit the bar type explicitly; shared types are not changed automatically.");
            double radius=(type.StandardBendDiameter*304.8+diameter)/2;
            var resultPoints=new List<XYZ>();
            resultPoints.AddRange(EndPoints(body[0],(body[0]-body[1]).Normalize(),start,startCalc,radius).Reverse());
            resultPoints.AddRange(body);
            resultPoints.AddRange(EndPoints(body.Last(),(body.Last()-body[body.Count-2]).Normalize(),end,endCalc,radius));
            // Remove construction stations that lie on a straight leg; they remain in the stored recipe.
            for(int i=resultPoints.Count-2;i>0;i--){var a=(resultPoints[i]-resultPoints[i-1]).Normalize();var b=(resultPoints[i+1]-resultPoints[i]).Normalize();if(a.DotProduct(b)>0.999999)resultPoints.RemoveAt(i);}
            var req=new JObject {["host_id"]=p["host_id"],["bar_type_id"]=p["bar_type_id"],["points_json"]=JArray.FromObject(resultPoints.Select(v=>new[]{v.X,v.Y,v.Z})).ToString(),["normal_x"]=p["normal_x"],["normal_y"]=p["normal_y"],["normal_z"]=p["normal_z"],["mode"]="ShapeDriven"};
            foreach(var k in new[]{"layout_rule","quantity","distribution_length_mm","spacing_mm"})if(p[k]!=null)req[k]=p[k];
            bool dry=p.Value<bool?>("dry_run")??false;
            using(var group=new TransactionGroup(d,"Chinh Thang: Designed rebar")){
                group.Start();
                if(old!=null)using(var tx=new Transaction(d,"Chinh Thang: Replace owned bar")) {
                    tx.Start();d.Delete(old.Id);
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Could not replace the owned bar.");
                }
                var r=CreateRebarPathHandler.Create(d,req);if(!r.Success)throw new InvalidOperationException(r.Error);
                var data=JObject.FromObject(r.Data);var bar=d.GetElement(RevitCompat.ToElementId(data.Value<long>("created_id")));
                using(var tx=new Transaction(d,"Chinh Thang: Store design recipe")){
                    tx.Start();p.Remove("replace_component_id");p.Remove("dry_run");
                    DetailingStorage.Write(bar,new JObject{["kind"]="DesignedRebar",["request"]=p,["start_calculation"]=startCalc,["end_calculation"]=endCalc});
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Could not store the component recipe.");
                }
                data["start_calculation"]=startCalc;data["end_calculation"]=endCalc;data["start_anchor_enabled"]=start.Value<bool>("enabled");data["end_anchor_enabled"]=end.Value<bool>("enabled");
                data["dry_run"]=dry;data["replaced_component_id"]=previous;data["anchor_check_status"]=start.Value<bool>("enabled")&&end.Value<bool>("enabled") ? "Calculated length; verify cover and containment at critical sections" : "ANCHORAGE_DISABLED_REQUIRES_DESIGN_REVIEW";
                if(dry){group.RollBack();data["created_id"]=null;}else group.Assimilate();
                return CommandResult.Ok(data);
            }
        }
        private static JObject AnchorDesign(JObject design,JObject anchor)
        {
            if(anchor["enabled"]?.Type!=JTokenType.Boolean)throw new ArgumentException("Each anchor needs explicit enabled=true/false.");
            string style=anchor.Value<string>("style");if(style!="Straight"&&style!="Up"&&style!="Down")throw new ArgumentException("Anchor style must be Straight, Up or Down.");
            var p=(JObject)design.DeepClone();p["operation"]="Anchorage";p["end_shape"]=style=="Straight"?"Straight":"L";return DetailingDesign.Calculate(p);
        }
        private static IEnumerable<XYZ> EndPoints(XYZ station,XYZ outward,JObject anchor,JObject calculation,double radius)
        {
            if(!anchor.Value<bool>("enabled"))return new XYZ[0];
            double required=calculation.Value<double>("required_length_mm");string style=anchor.Value<string>("style");
            if(style=="Straight")return new[]{station+outward*required};
            if(Math.Abs(outward.Z)>1e-6)throw new ArgumentException("Up/Down anchorage currently requires a horizontal terminal body segment; provide a separate reviewed geometry for inclined ends.");
            double run=DetailingDesign.Required(anchor,"horizontal_embedment_mm");
            if(run<=radius)throw new ArgumentException("Horizontal embedment must exceed the centreline bend radius.");
            // Revit replaces the sharp 90-degree corner by a circular centreline arc.
            double leg=Math.Max(radius+1,required-run+(2-Math.PI/2)*radius);
            var corner=station+outward*run;
            return new[]{corner,corner+XYZ.BasisZ*(style=="Up"?leg:-leg)};
        }
    }
}
