// Run using revit_send_code_to_revit after installing 1.2.1. Creates and closes only a separate scratch document.
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using Autodesk.Revit.DB;using Autodesk.Revit.DB.Structure;using Autodesk.Revit.UI;using Newtonsoft.Json.Linq;
public class McpDynamicScript {public static object Run(UIApplication app){
var plugin=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ChinhThangRevitMcp","app","plugin","RvtMcp.Plugin.dll");
var asm=Assembly.Load(File.ReadAllBytes(plugin));
Document d=null;var report=new JObject();var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ChinhThangRevitMcp","qa","rebar-guards-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
try{d=app.Application.NewProjectDocument(UnitSystem.Metric);Floor floor;RebarBarType bar,chair;
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

JObject Call(string handler,JObject req){try{var result=asm.GetType("RvtMcp.Plugin.Handlers."+handler).GetMethod(handler=="AuditRebarConnectionsHandler"?"Audit":"Create").Invoke(null,new object[]{d,req});var j=JObject.FromObject(result);if(!j.Value<bool>("Success"))throw new Exception(j.Value<string>("Error"));return (JObject)j["Data"];}catch(TargetInvocationException e){throw e.InnerException;}}
int Count()=>new FilteredElementCollector(d).OfClass(typeof(Rebar)).GetElementCount();
JObject PathReq(string points)=>new JObject{["host_id"]=floor.Id.Value,["bar_type_id"]=bar.Id.Value,["points_json"]=points,["normal_x"]=0,["normal_y"]=1,["normal_z"]=0};
void Reject(string name,Action action,string expected){int before=Count();string error=null;try{action();}catch(Exception e){error=e.Message;}if(error==null||error.IndexOf(expected,StringComparison.OrdinalIgnoreCase)<0||Count()!=before)throw new Exception("QA failed: "+name+" error="+error);report[name]=new JObject{["passed"]=true,["before"]=before,["after"]=Count(),["error"]=error};}
var first=Call("CreateRebarPathHandler",PathReq("[[500,500,-100],[2000,500,-100]]"));
Reject("duplicate_rollback",()=>Call("CreateRebarPathHandler",PathReq("[[500,500,-100],[2000,500,-100]]")),"overlap");
Reject("partial_overlap_rollback",()=>Call("CreateRebarPathHandler",PathReq("[[1500,500,-100],[2300,500,-100]]")),"overlap");
Call("CreateRebarPathHandler",PathReq("[[1500,532,-100],[2300,532,-100]]"));report["offset_lap_allowed"]=true;
var design=JObject.Parse(@"{'standard':'TCVN5574:2018','steel_stress':'Tension','surface':'HotRolledRibbed','diameter_mm':16,'rs_mpa':350,'rbt_mpa':1.05,'as_required_mm2':201,'as_provided_mm2':201,'concrete_kind':'NormalWeight'}");
var req=new JObject{["host_id"]=floor.Id.Value,["bar_type_id"]=bar.Id.Value,["body_points_json"]="[[1500,1000,-40],[4500,1000,-40]]",["normal_x"]=0,["normal_y"]=1,["normal_z"]=0,["design"]=design,["start_anchor"]=new JObject{["enabled"]=true,["style"]="Down",["horizontal_embedment_mm"]=400,["receiving_host_id"]=floor.Id.Value},["end_anchor"]=new JObject{["enabled"]=true,["style"]="Down",["horizontal_embedment_mm"]=400,["receiving_host_id"]=floor.Id.Value},["dry_run"]=true};
int n=Count();report["valid_anchors_dry_run"]=Call("CreateDesignedRebarHandler",req);if(n!=Count())throw new Exception("Dry run leaked");
var missing=(JObject)req.DeepClone();((JObject)missing["start_anchor"]).Remove("receiving_host_id");Reject("missing_receiver",()=>Call("CreateDesignedRebarHandler",missing),"receiving_host_id");
var outside=(JObject)req.DeepClone();outside["body_points_json"]="[[200,1000,-40],[4500,1000,-40]]";Reject("anchor_outside_concrete",()=>Call("CreateDesignedRebarHandler",outside),"receiving concrete");
var opening=(JObject)req.DeepClone();opening["body_points_json"]="[[2800,2500,-40],[4500,2500,-40]]";Reject("anchor_crosses_opening",()=>Call("CreateDesignedRebarHandler",opening),"receiving concrete");
var distributed=(JObject)req.DeepClone();distributed["layout_rule"]="FixedNumber";distributed["quantity"]=2;distributed["distribution_length_mm"]=4500;Reject("distributed_anchor_outside",()=>Call("CreateDesignedRebarHandler",distributed),"receiving concrete");
var disabled=(JObject)req.DeepClone();disabled["start_anchor"]["enabled"]=false;Reject("silent_disabled_anchor",()=>Call("CreateDesignedRebarHandler",disabled),"disabled_reason");

var committed=(JObject)req.DeepClone();committed["dry_run"]=false;var made=Call("CreateDesignedRebarHandler",committed);long managedId=made.Value<long>("created_id");
Reject("failed_replacement_restores_original",()=>Call("CreateDesignedRebarHandler",new JObject{["replace_component_id"]=managedId,["body_points_json"]="[[200,1000,-40],[4500,1000,-40]]"}),"receiving concrete");
if(d.GetElement(new ElementId(managedId))==null)throw new Exception("Failed replacement lost original bar");
report["status"]="PASS";
}catch(Exception e){report["status"]="FAIL";report["error"]=e.ToString();}finally{if(d!=null)d.Close(false);}
Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"mcp-guard-live-tests.json"),report.ToString());return report;
}}
