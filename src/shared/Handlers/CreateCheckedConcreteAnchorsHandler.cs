// Chinh Thang, 2026-09-19. Fully contained editable anchors; no line/proxy fallback.
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class CreateCheckedConcreteAnchorsHandler:IRevitCommand
    {
        public string Name=>"create_checked_concrete_anchors";
        public string Description=>"Place native editable anchor families with explicit diameter/length and reject any actual solid outside concrete. All-or-nothing; dry-run by default.";
        public string ParametersSchema=>@"{""type"":""object"",""properties"":{""request_json"":{""type"":""string""}},""required"":[""request_json""]}";
        public CommandResult Execute(UIApplication app,string json)
        {var d=app.ActiveUIDocument?.Document;if(d==null)return CommandResult.Fail("No document is open.");try{return Create(d,JObject.Parse(JObject.Parse(json).Value<string>("request_json")));}catch(Exception e){return CommandResult.Fail("Concrete anchors: "+e.Message);}}
        public static CommandResult Create(Document d,JObject p)
        {
            var positions=SteelDetailInput.Anchors(p);double diameter=SteelDetailInput.Positive(p,"diameter_mm"),length=SteelDetailInput.Positive(p,"length_mm");
            var host=d.GetElement(RevitCompat.ToElementId(p.Value<long>("host_id")));
            if(host==null||!RebarHostData.IsValidHost(host))throw new ArgumentException("An actual concrete rebar host is required.");
            var concrete=CheckedDetailGeometry.Solids(host);if(concrete.Count==0)throw new ArgumentException("Concrete host has no physical solid.");
            var axis=p["axis"]==null?XYZ.BasisZ:CheckedDetailGeometry.Point(p["axis"],"axis",true).Normalize();bool dry=p.Value<bool?>("dry_run")??true;var rows=new JArray();var created=new System.Collections.Generic.List<ElementId>();
            using(var group=new TransactionGroup(d,"Chinh Thang: Checked concrete anchors")){group.Start();
                var failures=new RebarPathFailures();
                using(var tx=new Transaction(d,"Chinh Thang: Anchor geometry")){tx.Start();tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                    var symbol=CheckedDetailGeometry.Symbol(d,p.Value<long>("symbol_id"));
                    foreach(var position in positions){var f=CheckedDetailGeometry.Place(d,symbol,CheckedDetailGeometry.Point(position,"position")/304.8,axis);CheckedDetailGeometry.Length(f,p.Value<string>("diameter_parameter"),diameter);CheckedDetailGeometry.Length(f,p.Value<string>("length_parameter"),length);d.Regenerate();
                        created.Add(f.Id);var solids=CheckedDetailGeometry.Solids(f);if(solids.Count==0)throw new ArgumentException("Anchor must have physical native solid geometry.");
                        if(!solids.SelectMany(s=>s.Faces.Cast<Face>()).OfType<CylindricalFace>().Any(face=>{if(Math.Abs(face.get_Radius(0).GetLength()*609.6-diameter)>.01||Math.Abs(face.Axis.Normalize().DotProduct(axis))<.999)return false;var vertices=face.Triangulate().Vertices.Select(v=>v.DotProduct(axis)).ToArray();return vertices.Length>0&&Math.Abs((vertices.Max()-vertices.Min())*304.8-length)<.05;}))throw new ArgumentException("Actual straight cylindrical shank does not match requested diameter, length or axis.");
                        CheckedDetailGeometry.Contained(solids,concrete);
                        string label="CT-ANCHOR Ø"+diameter.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" L"+length.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
                        f.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(label+" #"+RevitCompat.GetId(f.Id));f.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("Fully contained anchor; geometry checked, design capacity/cover not checked.");
                        DetailingStorage.Write(f,new JObject{["kind"]="CheckedConcreteAnchor",["host_id"]=RevitCompat.GetId(host.Id),["request"]=p.DeepClone()});
                        rows.Add(new JObject{["id"]=dry?(long?)null:RevitCompat.GetId(f.Id),["label"]=label,["diameter_mm"]=diameter,["length_mm"]=length,["contained"]=true});
                    }
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Revit rolled back anchor placement: "+string.Join("; ",failures.Messages));
                }
                // Recheck committed geometry; joins/failure processing may have changed the host.
                concrete=CheckedDetailGeometry.Solids(host);foreach(var id in created)CheckedDetailGeometry.Contained(CheckedDetailGeometry.Solids(d.GetElement(id)),concrete);
                if(dry)group.RollBack();else group.Assimilate();
            }
            return CommandResult.Ok(new{dry_run=dry,anchors=rows,capacity_checked=false,minimum_cover_checked=false,exposed_thread_supported=false});
        }
    }
}
