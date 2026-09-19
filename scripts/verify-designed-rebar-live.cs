// Revit 2024, separate scratch document only. Test dimensions/materials are NOT project design defaults.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using RvtMcp.Plugin.Handlers;
public class McpDynamicScript
{
 public static object Run(UIApplication app)
 {
  var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ChinhThangRevitMcp","qa","designed-rebar-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
  Directory.CreateDirectory(root);var report=new JObject();Document d=null;
  try {
   d=app.Application.NewProjectDocument(UnitSystem.Metric);Floor floor;RebarBarType bar,chair;
   CurveLoop Rect(double x0,double y0,double x1,double y1,bool reverse) {
    var pts=new[]{new XYZ(x0/304.8,y0/304.8,0),new XYZ(x1/304.8,y0/304.8,0),new XYZ(x1/304.8,y1/304.8,0),new XYZ(x0/304.8,y1/304.8,0)};
    if(reverse)Array.Reverse(pts);var loop=new CurveLoop();for(int i=0;i<4;i++)loop.Append(Line.CreateBound(pts[i],pts[(i+1)%4]));return loop;
   }
   using(var tx=new Transaction(d,"QA concrete slab")) {
    tx.Start();var level=Level.Create(d,0);
    var m=(Material)d.GetElement(Material.Create(d,"QA concrete"));m.StructuralAssetId=PropertySetElement.Create(d,new StructuralAsset("QA concrete",StructuralAssetClass.Concrete)).Id;
    var ft=(FloorType)new FilteredElementCollector(d).OfClass(typeof(FloorType)).Cast<FloorType>().First().Duplicate("QA concrete 500");
    var compound=ft.GetCompoundStructure();compound.SetLayers(new List<CompoundStructureLayer>{new CompoundStructureLayer(500/304.8,MaterialFunctionAssignment.Structure,m.Id)});ft.SetCompoundStructure(compound);
    floor=Floor.Create(d,new List<CurveLoop>{Rect(0,0,6000,5000,false),Rect(2500,2000,3500,3000,true)},ft.Id,level.Id);
    floor.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL).Set(1);
    bar=RebarBarType.Create(d);bar.Name="QA nominal D16";bar.BarNominalDiameter=16/304.8;bar.BarModelDiameter=16/304.8;bar.StandardBendDiameter=96/304.8;
    chair=RebarBarType.Create(d);chair.Name="QA nominal D10";chair.BarNominalDiameter=10/304.8;chair.BarModelDiameter=10/304.8;chair.StandardBendDiameter=60/304.8;
    tx.Commit();
   }
   JObject Result(CommandResult r){if(!r.Success)throw new Exception(r.Error);return JObject.FromObject(r.Data);}
   JObject PathRequest(string points)=>new JObject{["host_id"]=floor.Id.Value,["bar_type_id"]=bar.Id.Value,["points_json"]=points,["normal_x"]=0,["normal_y"]=1,["normal_z"]=0};
   var upper=Result(CreateRebarPathHandler.Create(d,PathRequest("[[500,500,-40],[5500,500,-40]]"))).Value<long>("created_id");
   var lower=Result(CreateRebarPathHandler.Create(d,PathRequest("[[500,500,-450],[5500,500,-450]]"))).Value<long>("created_id");
   var support=new JObject{["host_id"]=floor.Id.Value,["bar_type_id"]=chair.Id.Value,["upper_rebar_id"]=upper,["lower_rebar_id"]=lower,["zone_key"]="QA-S1",["kind"]="PlanarChair",["seat_width_mm"]=200,["foot_length_mm"]=100,["layout"]=new JObject{["area_per_support_m2"]=4.0},["dry_run"]=true};
   int Count()=>new FilteredElementCollector(d).OfClass(typeof(Rebar)).GetElementCount();
   var before=Count();var preview=Result(CreateSlabSupportsHandler.Create(d,support));if(Count()!=before)throw new Exception("Support dry-run leaked bars");
   support["dry_run"]=false;var placed=Result(CreateSlabSupportsHandler.Create(d,support));
   if(Math.Abs(placed.Value<double>("net_area_m2")-29)>0.001||placed.Value<int>("count")<8)throw new Exception("Support net area/density mismatch");
   var after=Count();var replaced=Result(CreateSlabSupportsHandler.Create(d,support));if(Count()!=after||replaced.Value<int>("replaced_count")!=placed.Value<int>("count"))throw new Exception("Repeated support region duplicated bars");
   var kept=replaced["created_ids"].Values<long>().ToArray();bool invalidRejected=false;
   support["seat_width_mm"]=1;
   try{CreateSlabSupportsHandler.Create(d,support);}catch{invalidRejected=true;}
   support["seat_width_mm"]=200;
   if(!invalidRejected||Count()!=after||kept.Any(id=>d.GetElement(new ElementId(id))==null))throw new Exception("Failed replacement did not restore original supports");
   report["failed_replacement_preserves_originals"]=true;
   report["supports"]=placed;report["supports_dry_run_and_replace"]=true;
   var design=JObject.Parse(@"{'standard':'TCVN5574:2018','steel_stress':'Tension','surface':'HotRolledRibbed','diameter_mm':16,'rs_mpa':350,'rbt_mpa':1.05,'as_required_mm2':201,'as_provided_mm2':201,'concrete_kind':'NormalWeight','spliced_percent':50}");
   var request=new JObject{["host_id"]=floor.Id.Value,["bar_type_id"]=bar.Id.Value,["body_points_json"]="[[1500,1000,-40],[4500,1000,-40]]",["normal_x"]=0,["normal_y"]=1,["normal_z"]=0,["design"]=design,["start_anchor"]=new JObject{["enabled"]=true,["style"]="Down",["horizontal_embedment_mm"]=400,["receiving_host_id"]=floor.Id.Value},["end_anchor"]=new JObject{["enabled"]=true,["style"]="Down",["horizontal_embedment_mm"]=400,["receiving_host_id"]=floor.Id.Value}};
   var anchored=Result(CreateDesignedRebarHandler.Create(d,request));long anchoredId=anchored.Value<long>("created_id");
   var curves=((Rebar)d.GetElement(new ElementId(anchoredId))).GetCenterlineCurves(false,false,false,MultiplanarOption.IncludeAllMultiplanarCurves,0);
   if(curves.First().GetEndPoint(0).Z>=-40/304.8||curves.Last().GetEndPoint(1).Z>=-40/304.8||Math.Abs(curves.Sum(c=>c.Length)*304.8-(3000+2*535))>0.1)throw new Exception("Downturned ends or real anchor length mismatch");
   var toggle=Result(CreateDesignedRebarHandler.Create(d,new JObject{["replace_component_id"]=anchoredId,["start_anchor"]=new JObject{["enabled"]=false,["disabled_reason"]="QA intentional disabled end, not a verified joint"}}));
   if(d.GetElement(new ElementId(anchoredId))!=null||toggle.Value<bool>("start_anchor_enabled"))throw new Exception("Anchor toggle did not replace managed bar");
   report["both_ends_down_and_real_length_verified"]=true;report["independent_anchor_toggle"]=true;
   var splice=new JObject{["mode"]="CrankedLap",["host_id"]=floor.Id.Value,["bar_type_id"]=bar.Id.Value,["start_mm"]=new JArray(500,1500,-100),["end_mm"]=new JArray(5500,1500,-100),["splice_center_mm"]=new JArray(3000,1500,-100),["offset_direction"]=new JArray(0,0,1),["centerline_offset_mm"]=32,["crank_run_mm"]=192,["design"]=design};
   var joined=Result(CreateRebarSpliceHandler.Create(d,splice));
   var first=(Rebar)d.GetElement(new ElementId(joined["created_ids"][0].Value<long>()));var lastLine=first.GetCenterlineCurves(false,false,false,MultiplanarOption.IncludeAllMultiplanarCurves,0).OfType<Line>().Last();
   if(lastLine.Length*304.8+0.1<640)throw new Exception("Rounded crank consumed required lap");
   report["cranked_lap_parallel_length_mm"]=lastLine.Length*304.8;
   var save=Path.Combine(root,"CT_Designed_Rebar_Check.rvt");int expected=Count();d.SaveAs(save,new SaveAsOptions());d.Close(false);d=null;d=app.Application.OpenDocumentFile(save);
   if(Count()!=expected)throw new Exception("Rebar count changed on reopen");report["reopened_rebar_sets"]=expected;report["saved_model"]=save;report["status"]="PASS";
  }catch(Exception ex){report["status"]="FAIL";report["error"]=ex.ToString();}
  finally{if(d!=null)d.Close(false);}
  File.WriteAllText(Path.Combine(root,"verification.json"),report.ToString());return report;
 }
}
