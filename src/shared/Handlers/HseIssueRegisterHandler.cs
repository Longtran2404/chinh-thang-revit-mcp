using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class HseIssueRegisterHandler : IRevitCommand
    {
        public string Name=>"hse_issue_register";
        public string Description=>"Read or upsert the local project HSE issue register. Resolution requires named owner/reviewer and recorded evidence; no automatic safety certification.";
        public string ParametersSchema=>@"{""type"":""object"",""properties"":{""action"":{""type"":""string"",""enum"":[""list"",""upsert""]},""issue_json"":{""type"":""string""}},""required"":[""action""]}";
        public CommandResult Execute(UIApplication app,string json)
        {
            var d=app.ActiveUIDocument?.Document;if(d==null)return CommandResult.Fail("No active project.");
            try{return Run(d,JObject.Parse(json));}catch(Exception ex){return CommandResult.Fail("HSE register: "+ex.Message);}
        }
        public static CommandResult Run(Document d,JObject p)
        {
            var storage=new FilteredElementCollector(d).OfClass(typeof(Autodesk.Revit.DB.ExtensibleStorage.DataStorage)).FirstOrDefault(e=>e.Name=="CT_HSE_Register_V1");
            var register=DetailingStorage.Read(storage)??new JObject{["kind"]="HseRegister",["issues"]=new JArray()};
            var issues=(JArray)register["issues"];
            string action=p.Value<string>("action");
            if(action=="list") {
                int start=p.Value<int?>("start")??0,limit=p.Value<int?>("limit")??100;
                if(start<0||limit<1||limit>100)throw new ArgumentException("start >=0 and limit 1-100 required.");
                var page=new JArray(issues.Skip(start).Take(limit).OfType<JObject>().Select(Summary));
                return CommandResult.Ok(new {issues=page,total=issues.Count,start,next_start=start+page.Count<issues.Count?(int?)(start+page.Count):null,compliance_verified=false});
            }
            if(action!="upsert")throw new ArgumentException("action must be list or upsert.");
            string raw=p.Value<string>("issue_json");if(raw==null||raw.Length>16000)throw new ArgumentException("issue_json required, max 16000 characters.");
            var issue=HseIssueRules.Validate(JObject.Parse(raw));
            var elements=issue["element_ids"].Values<long>().Select(id=>d.GetElement(RevitCompat.ToElementId(id))).ToArray();
            if(elements.Any(e=>e==null||e is ElementType))throw new ArgumentException("All referenced elements must exist as instances in this document.");
            issue["element_unique_ids"]=new JArray(elements.Select(e=>e.UniqueId));
            var previous=issues.OfType<JObject>().FirstOrDefault(i=>i.Value<string>("issue_key")==issue.Value<string>("issue_key"));
            if(previous==null&&issues.Count>=500)throw new ArgumentException("Register limited to 500 issues per project.");
            issue["updated_utc"]=DateTime.UtcNow.ToString("o");issue["created_utc"]=previous?["created_utc"]??issue["updated_utc"];
            var history=previous?["history"] as JArray??new JArray();
            if(previous!=null){var snapshot=(JObject)previous.DeepClone();snapshot.Remove("history");history.Add(snapshot);}
            if(history.Count>20)throw new ArgumentException("Issue reached 20 retained revisions. Export/archive the register before further changes; history will not be discarded silently.");
            issue["history"]=history;
            if(previous==null)issues.Add(issue);else previous.Replace(issue);
            using(var tx=new Transaction(d,"Chinh Thang: Update HSE register")) {
                tx.Start();if(storage==null){storage=Autodesk.Revit.DB.ExtensibleStorage.DataStorage.Create(d);storage.Name="CT_HSE_Register_V1";}
                DetailingStorage.Write(storage,register);
                if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("HSE register was not committed.");
            }
            return CommandResult.Ok(new {saved=true,issue=Summary(issue),compliance_verified=false});
        }
        private static JObject Summary(JObject issue){var result=(JObject)issue.DeepClone();result["history_count"]=(result["history"] as JArray)?.Count??0;result.Remove("history");return result;}
    }
}
