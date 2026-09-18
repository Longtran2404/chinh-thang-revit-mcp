using System;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using Xunit;
namespace RvtMcp.Tests
{
    public class HseIssueRulesTests
    {
        private static JObject Issue()=>JObject.Parse(@"{'issue_key':'S1-01','title':'Review floor opening','hazard':'FloorOpening','status':'Open','element_ids':[12,12,34]}");
        [Fact] public void Keeps_unique_element_references_without_certifying_safety(){var result=HseIssueRules.Validate(Issue());Assert.Equal(2,((JArray)result["element_ids"]).Count);Assert.False(result.Value<bool>("evidence_independently_verified"));}
        [Theory] [InlineData("owner")] [InlineData("reviewer")] [InlineData("evidence")]
        public void Resolution_requires_accountability(string missing){var p=Issue();p["status"]="Resolved";p["owner"]="Site engineer";p["reviewer"]="Inspector";p["evidence"]="Inspection record 001";p.Remove(missing);Assert.Throws<ArgumentException>(()=>HseIssueRules.Validate(p));}
        [Theory] [InlineData("status","Safe")] [InlineData("hazard","Unknown")] [InlineData("due_date","2026-02-30")] [InlineData("issue_key","")]
        public void Invalid_records_are_rejected(string field,string value){var p=Issue();p[field]=value;Assert.Throws<ArgumentException>(()=>HseIssueRules.Validate(p));}
        [Fact] public void Site_only_issue_has_explicit_empty_array(){var p=Issue();p["element_ids"]=new JArray();Assert.Empty((JArray)HseIssueRules.Validate(p)["element_ids"]);p.Remove("element_ids");Assert.Throws<ArgumentException>(()=>HseIssueRules.Validate(p));}
    }
}
