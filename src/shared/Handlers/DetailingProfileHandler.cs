using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class GetDetailingProfileHandler : IRevitCommand
    {
        public string Name=>"get_detailing_profile";
        public string Description=>"Read the local document detailing profile; no guessed chair density or material strengths.";
        public string ParametersSchema=>@"{""type"":""object"",""properties"":{}}";
        public CommandResult Execute(UIApplication app,string json)
        {
            var d=app.ActiveUIDocument?.Document;
            return d==null ? CommandResult.Fail("No document is open.") : CommandResult.Ok(new {profile=DetailingStorage.Read(DetailingStorage.Profile(d)),implemented_calculation_codes=new[]{"TCVN5574:2018","EN1992-1-1:2004"}});
        }
    }
    public class SetDetailingProfileHandler : IRevitCommand
    {
        public string Name=>"set_detailing_profile";
        public string Description=>"Save explicit density/spacing and design settings inside this Revit project.";
        public string ParametersSchema=>@"{""type"":""object"",""properties"":{""profile_json"":{""type"":""string""}},""required"":[""profile_json""]}";
        public CommandResult Execute(UIApplication app,string json)
        {
            var d=app.ActiveUIDocument?.Document;
            if(d==null) return CommandResult.Fail("No document is open.");
            try {
                var p=JObject.Parse(JObject.Parse(json).Value<string>("profile_json"));
                if(p.ToString().Length>32000) throw new ArgumentException("Profile is limited to 32 KB.");
                if(p["support_layout"] is JObject layout) SlabSupportLayout.Validate(layout);
                if(p["support_regions"] is JObject regions) foreach(var region in regions.Properties()) {
                    if(!(region.Value is JObject regionLayout)) throw new ArgumentException("Each support_regions entry must contain a layout object.");
                    SlabSupportLayout.Validate(regionLayout);
                }
                using(var tx=new Transaction(d,"Chinh Thang: Detailing profile")) {
                    tx.Start();var storage=DetailingStorage.Profile(d)??Autodesk.Revit.DB.ExtensibleStorage.DataStorage.Create(d);storage.Name="CT_Detailing_Profile_V1";
                    DetailingStorage.Write(storage,p);
                    if(tx.Commit()!=TransactionStatus.Committed) return CommandResult.Fail("Profile update rolled back.");
                    return CommandResult.Ok(new {saved=true,profile=p});
                }
            } catch(Exception ex) { return CommandResult.Fail(ex.Message); }
        }
    }
}
