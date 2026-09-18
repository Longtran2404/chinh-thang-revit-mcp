using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin
{
    public static class HseIssueRules
    {
        public static JObject Validate(JObject input)
        {
            string Text(string key,int max,bool required=false) {
                var token=input[key];if(token!=null&&token.Type!=JTokenType.String)throw new ArgumentException(key+" must be text.");
                string s=((string)token??"").Trim();if((required&&s.Length==0)||s.Length>max)throw new ArgumentException(key+" is missing or too long.");return s;
            }
            string key=Text("issue_key",80,true),title=Text("title",200,true),hazard=Text("hazard",40,true),status=Text("status",30,true);
            if(!new[]{"FallEdge","FloorOpening","Access","Lifting","Electrical","Fire","Other"}.Contains(hazard))throw new ArgumentException("Unsupported hazard classification.");
            if(!new[]{"Open","InProgress","Resolved"}.Contains(status))throw new ArgumentException("status must be Open, InProgress or Resolved.");
            string owner=Text("owner",160,status!="Open"),reviewer=Text("reviewer",160,status=="Resolved"),evidence=Text("evidence",2000,status=="Resolved"),due=Text("due_date",10);
            if(due.Length>0&&!DateTime.TryParseExact(due,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))throw new ArgumentException("due_date must be yyyy-MM-dd.");
            var ids=input["element_ids"] as JArray;
            if(ids==null||ids.Count>100||ids.Any(t=>t.Type!=JTokenType.Integer||(long)t<=0))throw new ArgumentException("element_ids must be an explicit array of at most 100 positive integer IDs; [] permits a site-only issue.");
            return new JObject{["issue_key"]=key,["title"]=title,["hazard"]=hazard,["status"]=status,["owner"]=owner,["reviewer"]=reviewer,["evidence"]=evidence,["due_date"]=due,["notes"]=Text("notes",4000),["element_ids"]=new JArray(ids.Values<long>().Distinct()),["evidence_independently_verified"]=false};
        }
    }
}
