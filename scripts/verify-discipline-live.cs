// Revit 2024 send_code fixture. Creates a separate scratch RVT; never edits the active document.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using RvtMcp.Plugin.Handlers;
public class McpDynamicScript
{
 public static object Run(UIApplication app)
 {
  var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ChinhThangRevitMcp","qa","disciplines-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
  Directory.CreateDirectory(root);var report=new JObject();Document d=null;
  try {
   d=app.Application.NewProjectDocument(UnitSystem.Metric);Floor floor;Room room;Level level;Pipe pipe=null;
   CurveLoop Rectangle(double x0,double y0,double x1,double y1,bool reverse) {
    var pts=new[]{new XYZ(x0/304.8,y0/304.8,0),new XYZ(x1/304.8,y0/304.8,0),new XYZ(x1/304.8,y1/304.8,0),new XYZ(x0/304.8,y1/304.8,0)};
    if(reverse)Array.Reverse(pts);var loop=new CurveLoop();for(int i=0;i<4;i++)loop.Append(Line.CreateBound(pts[i],pts[(i+1)%4]));return loop;
   }
   using(var tx=new Transaction(d,"QA discipline fixtures")) {
    tx.Start();level=Level.Create(d,0);
    var ft=new FilteredElementCollector(d).OfClass(typeof(FloorType)).Cast<FloorType>().First();
    floor=Floor.Create(d,new List<CurveLoop>{Rectangle(0,0,6000,5000,false),Rectangle(2000,2000,3000,3000,true)},ft.Id,level.Id);
    room=d.Create.NewRoom(d.Phases.get_Item(d.Phases.Size-1));
    var pt=new FilteredElementCollector(d).OfClass(typeof(PipeType)).Cast<PipeType>().FirstOrDefault();
    var st=new FilteredElementCollector(d).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().FirstOrDefault();
    if(pt!=null&&st!=null)pipe=Pipe.Create(d,st.Id,pt.Id,level.Id,new XYZ(0,0,3),new XYZ(10,0,3.1));
    if(tx.Commit()!=TransactionStatus.Committed)throw new Exception("Fixture setup rolled back");
   }
   JObject Result(CommandResult r){if(!r.Success)throw new Exception(r.Error);return JObject.FromObject(r.Data);}
   var hse=Result(new DisciplineReadinessHandler("hse").Inspect(d,new JObject{["element_ids"]=new JArray(floor.Id.Value)}));
   var rings=(JArray)hse["items"][0]["boundary_candidates"];
   if(rings.Count!=2 || rings.Sum(r=>((JArray)r["segments"]).Count)!=8)throw new Exception("Floor opening/perimeter extraction mismatch");
   if(hse.Value<bool>("compliance_verified"))throw new Exception("Must not certify HSE compliance");
   report["hse_floor_boundary_rings"]=rings.Count;
   var arch=Result(new DisciplineReadinessHandler("architecture").Inspect(d,new JObject{["element_ids"]=new JArray(room.Id.Value)}));
   if(!arch["items"][0]["observations"].Values<string>().Contains("ROOM_UNPLACED"))throw new Exception("Unplaced room not detected");
   report["architecture_unplaced_room"]=true;
   var limitCheck=Result(new DisciplineReadinessHandler("architecture").Inspect(d,new JObject{["element_ids"]=new JArray(floor.Id.Value,room.Id.Value),["limit"]=1}));
   if(!limitCheck.Value<bool>("truncated"))throw new Exception("Truncation not reported");
   report["limit_reporting"]=true;
   if(pipe!=null) {
    var mep=Result(new DisciplineReadinessHandler("mep").Inspect(d,new JObject{["element_ids"]=new JArray(pipe.Id.Value)}));
    var row=mep["items"][0];var ends=(JArray)row["physical_end_connectors"];
    if(ends.Count!=2||ends.Any(c=>c.Value<bool>("connected"))||Math.Abs(row.Value<double>("absolute_slope_percent")-1)>0.0001)throw new Exception("MEP open ends/slope mismatch: "+row);
    report["mep_two_open_ends_and_one_percent_slope"]=true;
   } else report["mep_runtime_status"]="NOT_TESTED: default project lacks pipe/system types";
   var issue=new JObject{["issue_key"]="QA-OPENING-01",["title"]="Review test floor opening",["hazard"]="FloorOpening",["status"]="Open",["element_ids"]=new JArray(floor.Id.Value)};
   Result(HseIssueRegisterHandler.Run(d,new JObject{["action"]="upsert",["issue_json"]=issue.ToString()}));
   bool rejected=false;issue["status"]="Resolved";
   try{HseIssueRegisterHandler.Run(d,new JObject{["action"]="upsert",["issue_json"]=issue.ToString()});}catch(ArgumentException){rejected=true;}
   if(!rejected)throw new Exception("Resolution without evidence accepted");
   issue["owner"]="QA Owner";issue["reviewer"]="QA Reviewer";issue["evidence"]="Synthetic fixture check only";
   Result(HseIssueRegisterHandler.Run(d,new JObject{["action"]="upsert",["issue_json"]=issue.ToString()}));
   var save=Path.Combine(root,"CT_Architecture_MEP_HSE_Check.rvt");d.SaveAs(save,new SaveAsOptions());d.Close(false);d=null;
   d=app.Application.OpenDocumentFile(save);
   var restored=Result(HseIssueRegisterHandler.Run(d,new JObject{["action"]="list"}));
   if(restored.Value<int>("total")!=1||restored["issues"][0].Value<string>("status")!="Resolved"||restored["issues"][0].Value<int>("history_count")!=1)throw new Exception("HSE register persistence/revision mismatch");
   report["hse_saved_reopened_with_revision"]=true;report["resolution_without_evidence_rejected"]=true;report["saved_model"]=save;report["status"]="PASS";
  }catch(Exception ex){report["status"]="FAIL";report["error"]=ex.ToString();}
  finally{if(d!=null)d.Close(false);}
  File.WriteAllText(Path.Combine(root,"verification.json"),report.ToString());return report;
 }
}
