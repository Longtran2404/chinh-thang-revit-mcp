// Run through send_code in Revit 2024. Creates only a new scratch document; never edits the active project.
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
  var report=new JObject();
  var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ChinhThangRevitMcp","qa","rebar-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
  Directory.CreateDirectory(root);
  Document d=null;
  try {
   d=app.Application.NewProjectDocument(UnitSystem.Metric);
   Wall wall; RebarBarType barType;
   using(var t=new Transaction(d,"QA setup")) {
    t.Start();
    var level=Level.Create(d,0);
    var material=(Material)d.GetElement(Material.Create(d,"QA concrete"));
    material.StructuralAssetId=PropertySetElement.Create(d,new StructuralAsset("QA concrete",StructuralAssetClass.Concrete)).Id;
    var wt=(WallType)new FilteredElementCollector(d).OfClass(typeof(WallType)).Cast<WallType>().First(x=>x.Kind==WallKind.Basic).Duplicate("QA wide concrete host");
    var cs=CompoundStructure.CreateSimpleCompoundStructure(new List<CompoundStructureLayer>{new CompoundStructureLayer(3000/304.8,MaterialFunctionAssignment.Structure,material.Id)});
    wt.SetCompoundStructure(cs);
    wall=Wall.Create(d,Line.CreateBound(new XYZ(0,0,0),new XYZ(10000/304.8,0,0)),wt.Id,level.Id,4000/304.8,0,false,true);
    barType=RebarBarType.Create(d);barType.Name="QA D16";
    barType.BarModelDiameter=16/304.8;barType.StandardBendDiameter=96/304.8;
    t.Commit();
   }
   report["valid_host"]=RebarHostData.IsValidHost(wall);
   if(!RebarHostData.IsValidHost(wall)) throw new Exception("Invalid concrete fixture host");
   var tests=new [] {
    new {name="anchored_slab",mode="ShapeDriven",points="[[500,-1000,600],[500,-1000,200],[3500,-1000,200],[3500,-1000,600]]",rule="FixedNumber",qty=6,len=1000.0,spacing=0.0},
    new {name="stair_zigzag",mode="ShapeDriven",points="[[500,-1000,1000],[1000,-1000,1000],[2500,-1000,2500],[3500,-1000,2500]]",rule="MaximumSpacing",qty=1,len=900.0,spacing=200.0},
    new {name="planar_chair",mode="ShapeDriven",points="[[4500,-1000,200],[4800,-1000,200],[4800,-1000,600],[5500,-1000,600],[5500,-1000,200],[5800,-1000,200]]",rule="Single",qty=1,len=0.0,spacing=0.0},
    new {name="spatial_chair",mode="FreeForm",points="[[6500,-1000,200],[6800,-1000,200],[6800,-1000,600],[6800,-500,600],[6800,-500,200],[7100,-500,200]]",rule="FixedNumber",qty=3,len=800.0,spacing=0.0}
   };
   var rows=new JArray();var ids=new List<ElementId>();
   foreach(var test in tests) {
    var req=new JObject { ["host_id"]=wall.Id.Value,["bar_type_id"]=barType.Id.Value,["points_json"]=test.points,["mode"]=test.mode,["normal_x"]=0,["normal_y"]=1,["normal_z"]=0,["layout_rule"]=test.rule,["quantity"]=test.qty,["distribution_length_mm"]=test.len,["spacing_mm"]=test.spacing,["dry_run"]=true };
    var before=new FilteredElementCollector(d).OfClass(typeof(Rebar)).GetElementCount();
    var preview=CreateRebarPathHandler.Create(d,req);
    if(!preview.Success) throw new Exception(test.name+" preview: "+preview.Error);
    if(new FilteredElementCollector(d).OfClass(typeof(Rebar)).GetElementCount()!=before) throw new Exception("Dry-run leaked elements");
    req["dry_run"]=false;
    var result=CreateRebarPathHandler.Create(d,req);
    if(!result.Success) throw new Exception(test.name+": "+result.Error);
    var row=JObject.FromObject(result.Data);row["test"]=test.name;row["dry_run_clean"]=true;
    var bar=(Rebar)d.GetElement(new ElementId(row.Value<long>("created_id")));ids.Add(bar.Id);
    if(bar.GetHostId()!=wall.Id) throw new Exception("Host mismatch");
    var curves=bar.GetCenterlineCurves(false,false,false,MultiplanarOption.IncludeAllMultiplanarCurves,0);
    row["centreline_curves"]=curves.Count;row["has_bend_arcs"]=curves.Any(c=>c is Arc);
    if(!curves.Any(c=>c is Arc)) throw new Exception("Bends were not created");
    if(bar.Quantity!=(test.rule=="Single" ? 1 : test.rule=="FixedNumber" ? test.qty : (int)Math.Ceiling(test.len/test.spacing)+1)) throw new Exception("Quantity mismatch");
    rows.Add(row);
   }
   report["cases"]=rows;
   // Native shape-driven edit proves the set remains editable after creation.
   using(var t=new Transaction(d,"QA edit layout")) { t.Start();((Rebar)d.GetElement(ids[0])).GetShapeDrivenAccessor().SetLayoutAsFixedNumber(8,1400/304.8,true,true,true);t.Commit(); }
   report["edit_shape_driven_quantity"]=((Rebar)d.GetElement(ids[0])).Quantity;
   var free=(Rebar)d.GetElement(ids[3]);
   using(var t=new Transaction(d,"QA edit spatial chair")) {
    t.Start();var loops=new List<CurveLoop>();
    var vertices=RebarPathInput.Points(tests[3].points);
    for(int j=0;j<3;j++) {
     var curves=new List<Curve>();
     for(int k=1;k<vertices.Length;k++) {
      var a=vertices[k-1];var b=vertices[k];
      curves.Add(Line.CreateBound(new XYZ(a[0]/304.8,(a[1]+j*400)/304.8,(a[2]+50)/304.8),new XYZ(b[0]/304.8,(b[1]+j*400)/304.8,(b[2]+50)/304.8)));
     }
     loops.Add(CurveLoop.Create(curves));
    }
    var status=free.GetFreeFormAccessor().SetCurves(loops);
    if(status!=RebarFreeFormValidationResult.Success) throw new Exception("FreeForm edit failed: "+status);
    t.Commit();report["edit_freeform_curves"]=status.ToString();
   }
   var countBefore=new FilteredElementCollector(d).OfClass(typeof(Rebar)).GetElementCount();
   bool rejected=false;
   try {
    var invalid=new JObject { ["host_id"]=wall.Id.Value,["bar_type_id"]=barType.Id.Value,["points_json"]="[[500,0,500],[502,0,500],[502,0,502]]",["normal_y"]=1,["dry_run"]=true };
    rejected=!CreateRebarPathHandler.Create(d,invalid).Success;
   } catch { rejected=true; }
   if(!rejected || new FilteredElementCollector(d).OfClass(typeof(Rebar)).GetElementCount()!=countBefore) throw new Exception("Invalid bend did not reject/rollback cleanly");
   report["invalid_bend_rollback"]=true;
   var save=Path.Combine(root,"CT_Rebar_Check.rvt");d.SaveAs(save,new SaveAsOptions());d.Close(false);d=null;
   d=app.Application.OpenDocumentFile(save);
   report["reopened_rebar_sets"]=new FilteredElementCollector(d).OfClass(typeof(Rebar)).GetElementCount();
   if(report.Value<int>("reopened_rebar_sets")!=4) throw new Exception("Reopen count mismatch");
   report["reopened_total_bars"]=new FilteredElementCollector(d).OfClass(typeof(Rebar)).Cast<Rebar>().Sum(b=>b.Quantity);
   report["saved_model"]=save;
   report["status"]="PASS";
  } catch(Exception ex) { report["status"]="FAIL";report["error"]=ex.ToString(); }
  finally { if(d!=null)d.Close(false); }
  File.WriteAllText(Path.Combine(root,"verification.json"),report.ToString());
  return report;
 }
}
