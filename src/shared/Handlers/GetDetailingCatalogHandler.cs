// Chinh Thang Revit MCP: inspect actual project types before detailing.
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class GetDetailingCatalogHandler : IRevitCommand
    {
        public string Name => "get_detailing_catalog";
        public string Description => "Read actual rebar types, bend diameters, cover types and steel connection types. Optional host validation.";
        public string ParametersSchema => @"{""type"":""object"",""properties"":{""host_id"":{""type"":""integer""}}}";
        public CommandResult Execute(UIApplication app,string paramsJson)
        {
            var doc=app.ActiveUIDocument?.Document;
            if(doc==null) return CommandResult.Fail("No document is open.");
            var req=JObject.Parse(paramsJson??"{}");
            var hostId=req.Value<long?>("host_id");
            var host=hostId.HasValue ? doc.GetElement(RevitCompat.ToElementId(hostId.Value)) : null;
            return CommandResult.Ok(new {
                host_id=hostId, valid_rebar_host=host!=null && RebarHostData.IsValidHost(host),
                bar_types=new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().Take(500).Select(t=>new { id=RevitCompat.GetId(t.Id),name=t.Name,nominal_diameter_mm=t.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER)?.AsDouble()*304.8,model_diameter_mm=t.get_Parameter(BuiltInParameter.REBAR_MODEL_BAR_DIAMETER)?.AsDouble()*304.8,bend_diameter_mm=t.StandardBendDiameter*304.8 }).ToArray(),
                cover_types=new FilteredElementCollector(doc).OfClass(typeof(RebarCoverType)).Cast<RebarCoverType>().Take(500).Select(t=>new {id=RevitCompat.GetId(t.Id),name=t.Name,distance_mm=t.CoverDistance*304.8}).ToArray(),
                connection_types=new FilteredElementCollector(doc).OfClass(typeof(StructuralConnectionHandlerType)).Cast<StructuralConnectionHandlerType>().Take(500).Select(t=>new {id=RevitCompat.GetId(t.Id),name=t.Name,detailed=t.IsDetailed(),generic=t.IsGeneric(),custom=t.IsCustom()}).ToArray(),
                coupler_types=new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Coupler).WhereElementIsElementType().Take(500).Select(t=>new {id=RevitCompat.GetId(t.Id),name=t.Name}).ToArray(),
                limit_per_category=500,
                design_note="Use explicit project dimensions. This catalog does not calculate anchorage, laps or structural capacity."
            });
        }
    }

    public class CreateSteelConnectionHandler : IRevitCommand
    {
        public string Name=>"create_steel_connection";
        public string Description=>"Create a native detailed steel connection using an explicitly loaded type; never substitute a generic marker.";
        public string ParametersSchema=>@"{""type"":""object"",""properties"":{""type_id"":{""type"":""integer""},""element_ids"":{""type"":""array"",""items"":{""type"":""integer""}},""dry_run"":{""type"":""boolean""}},""required"":[""type_id"",""element_ids""]}";
        public CommandResult Execute(UIApplication app,string paramsJson)
        {
            var doc=app.ActiveUIDocument?.Document;
            if(doc==null) return CommandResult.Fail("No document is open.");
            try
            {
                var req=JObject.Parse(paramsJson??"{}");
                var type=doc.GetElement(RevitCompat.ToElementId(req.Value<long>("type_id"))) as StructuralConnectionHandlerType;
                if(type==null || !type.IsDetailed() || type.IsGeneric()) return CommandResult.Fail("A loaded detailed connection type is required. Generic markers are not fabrication connections.");
                var raw=(req["element_ids"] as JArray)?.Values<long>().ToArray();
                if(raw==null || raw.Length<1 || raw.Length>20 || raw.Distinct().Count()!=raw.Length) return CommandResult.Fail("Provide 1-20 unique element IDs, primary element first.");
                var ids=raw.Select(RevitCompat.ToElementId).ToList();
                if(ids.Any(id=>doc.GetElement(id)==null)) return CommandResult.Fail("An input element does not exist.");
                var dry=req.Value<bool?>("dry_run")??false;
                using(var group=new TransactionGroup(doc,"Chinh Thang: Detailed steel connection"))
                {
                group.Start();
                using(var tx=new Transaction(doc,"Chinh Thang: Detailed steel connection"))
                {
                    tx.Start();
                    tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new RebarPathFailures()).SetClearAfterRollback(true));
                    var c=StructuralConnectionHandler.Create(doc,ids,type.Id);
                    doc.Regenerate();
                    if(c==null) throw new InvalidOperationException("Detailed connection generation failed for this input geometry.");
                    var result=new {dry_run=dry,created_id=dry ? (long?)null : RevitCompat.GetId(c.Id),type_id=RevitCompat.GetId(type.Id),type_name=type.Name,connected_ids=c.GetConnectedElementIds().Select(RevitCompat.GetId).ToArray(),capacity_checked=false,fabrication_geometry_verified=false};
                    if(tx.Commit()!=TransactionStatus.Committed) return CommandResult.Fail("Revit rolled back the steel connection.");
                    if(dry) group.RollBack(); else group.Assimilate();
                    return CommandResult.Ok(result);
                }
                }
            }
            catch(Exception ex) { return CommandResult.Fail("Steel connection: "+ex.Message+" Load the required detailed connection type/service in Revit; no generic fallback was created."); }
        }
    }
}
