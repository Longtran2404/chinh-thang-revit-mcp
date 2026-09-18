using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class CreateRebarSpliceHandler : IRevitCommand
    {
        public string Name=>"create_rebar_splice";
        public string Description=>"Create a native two-bar beam splice: calculated parallel lap or explicitly cranked offset lap.";
        public string ParametersSchema=>@"{""type"":""object"",""properties"":{""request_json"":{""type"":""string""}},""required"":[""request_json""]}";
        public CommandResult Execute(UIApplication app,string json)
        {
            var d=app.ActiveUIDocument?.Document;if(d==null)return CommandResult.Fail("No document is open.");
            try{return Create(d,JObject.Parse(JObject.Parse(json).Value<string>("request_json")));}catch(Exception ex){return CommandResult.Fail("Splice: "+ex.Message);}
        }
        public static CommandResult Create(Document d,JObject p)
        {
            XYZ Point(string key){var a=(JArray)p[key];if(a==null||a.Count!=3)throw new ArgumentException(key+" requires [x,y,z] mm.");return new XYZ((double)a[0],(double)a[1],(double)a[2]);}
            var start=Point("start_mm");var finish=Point("end_mm");var center=Point("splice_center_mm");var direction=(finish-start).Normalize();
            var offset=Point("offset_direction");offset=offset-direction*offset.DotProduct(direction);
            if(offset.GetLength()<1e-6)throw new ArgumentException("Offset direction must be perpendicular to bar direction.");offset=offset.Normalize();
            if((center-start).CrossProduct(direction).GetLength()>0.01)throw new ArgumentException("Splice center must lie on the body centreline.");
            var type=d.GetElement(RevitCompat.ToElementId(p.Value<long>("bar_type_id"))) as RebarBarType;
            if(type==null)throw new ArgumentException("Explicit bar type required.");
            var design=(JObject)(p["design"] as JObject)?.DeepClone();if(design==null)throw new ArgumentException("Explicit design required.");design["operation"]="Lap";design["end_shape"]="Straight";
            var calculation=DetailingDesign.Calculate(design);
            double nominal=type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER).AsDouble()*304.8;
            if(Math.Abs(nominal-DetailingDesign.Required(design,"diameter_mm"))>0.01)throw new ArgumentException("Design and bar type nominal diameters differ.");
            double diameter=type.get_Parameter(BuiltInParameter.REBAR_MODEL_BAR_DIAMETER).AsDouble()*304.8;
            double gap=DetailingDesign.Required(p,"centerline_offset_mm");
            if(gap<diameter || gap>(design.Value<string>("standard")=="EN1992-1-1:2004"?Math.Min(4*nominal,50):4*nominal))throw new ArgumentException("Lap centreline offset must avoid bar overlap and remain <=4 nominal diameters; review transverse detailing.");
            double half=calculation.Value<double>("required_length_mm")/2;
            var left=center-direction*half;var right=center+direction*half;
            if((left-start).DotProduct(direction)<=1 || (finish-right).DotProduct(direction)<=1)throw new ArgumentException("Requested lap does not fit within the supplied body span.");
            var mode=p.Value<string>("mode");XYZ[] first;
            if(mode=="ParallelLap")first=new[]{start+offset*gap,right+offset*gap};
            else if(mode=="CrankedLap") {
                var run=DetailingDesign.Required(p,"crank_run_mm");
                if(type.StandardBendDiameter*304.8+0.01<calculation.Value<double>("minimum_bend_diameter_mm"))throw new ArgumentException("Bar type bend diameter is below the selected standard minimum.");
                // Finish the real rounded bend before the lap start, not just its sharp construction vertex.
                double radius=(type.StandardBendDiameter*304.8+diameter)/2;
                double tangent=radius*Math.Tan(Math.Atan2(gap,run)/2);
                if((left-start).DotProduct(direction)<=run+2*tangent)throw new ArgumentException("Crank transition does not fit before the lap zone.");
                first=new[]{start,left-direction*(run+tangent),left-direction*tangent+offset*gap,right+offset*gap};
            } else throw new ArgumentException("mode must be ParallelLap or CrankedLap. A mechanically reduced/pressed bar end is a coupler/manufacturer-specific detail, not a bent bar.");
            var second=new[]{left,finish};var normal=direction.CrossProduct(offset).Normalize();bool dry=p.Value<bool?>("dry_run")??false;
            var ids=new JArray();
            using(var group=new TransactionGroup(d,"Chinh Thang: Rebar splice pair")) {
                group.Start();
                foreach(var points in new[]{first,second}) {
                    var req=new JObject{["host_id"]=p["host_id"],["bar_type_id"]=p["bar_type_id"],["points_json"]=JArray.FromObject(points.Select(v=>new[]{v.X,v.Y,v.Z})).ToString(),["normal_x"]=normal.X,["normal_y"]=normal.Y,["normal_z"]=normal.Z};
                    var r=CreateRebarPathHandler.Create(d,req);if(!r.Success)throw new InvalidOperationException(r.Error);ids.Add(JObject.FromObject(r.Data)["created_id"]);
                }
                using(var tx=new Transaction(d,"Chinh Thang: Splice recipe")) {
                    tx.Start();string key=Guid.NewGuid().ToString();foreach(long id in ids.Values<long>())DetailingStorage.Write(d.GetElement(RevitCompat.ToElementId(id)),new JObject{["kind"]="SplicePair",["component_key"]=key,["request"]=p.DeepClone(),["calculation"]=calculation.DeepClone()});
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Could not save splice recipes.");
                }
                if(dry)group.RollBack();else group.Assimilate();
            }
            return CommandResult.Ok(new {dry_run=dry,created_ids=dry?new JArray():ids,mode,calculation,checks="Lap length computed. Check stirrup capacity, permitted splice zones, stagger, clear distances and crank specification; these are not inferred from geometry."});
        }
    }
}
