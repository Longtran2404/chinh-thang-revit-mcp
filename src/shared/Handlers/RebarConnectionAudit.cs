using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class AuditRebarConnectionsHandler : IRevitCommand
    {
        public string Name=>"audit_rebar_connections";
        public string Description=>"Read actual distributed rebar curves; report parallel body overlaps and coaxial gap candidates. Never certifies anchorage or capacity.";
        public string ParametersSchema=>@"{""type"":""object"",""properties"":{""request_json"":{""type"":""string""}},""required"":[""request_json""]}";
        public CommandResult Execute(UIApplication app,string json)
        {
            try {
                var d=app.ActiveUIDocument?.Document;if(d==null)return CommandResult.Fail("No document is open.");
                return Audit(d,JObject.Parse(JObject.Parse(json).Value<string>("request_json")));
            } catch(Exception e){return CommandResult.Fail("Rebar audit: "+e.Message);}
        }
        public static CommandResult Audit(Document d,JObject p)
        {
            var ids=p["rebar_ids"] as JArray;
            if(ids==null||ids.Count<1||ids.Count>250||ids.Any(x=>x.Type!=JTokenType.Integer))throw new ArgumentException("Provide 1-250 explicit rebar_ids; split larger audits by joint/region.");
            var bars=ids.Values<long>().Distinct().Select(id=>d.GetElement(RevitCompat.ToElementId(id)) as Rebar??throw new ArgumentException("Every id must resolve to native Rebar.")).ToArray();
            double gap=p.Value<double?>("max_gap_mm")??500;
            if(double.IsNaN(gap)||double.IsInfinity(gap)||gap<=0||gap>2000)throw new ArgumentException("max_gap_mm must be >0 and <=2000.");
            var rows=RebarConnectionAudit.Read(d,bars);
            var findings=RebarConnectionAudit.Compare(rows,null,gap);
            return CommandResult.Ok(new {status=findings.Count>0?"REVIEW_REQUIRED":"NO_FINDINGS_IN_BOUNDED_CHECK",checked_sets=bars.Length,checked_bars=rows.Count,findings,
                anchorage_verified=false,lap_length_verified=false,cover_verified=false,capacity_verified=false,construction_ready=false,
                scope="Only explicitly selected sets, all existing positions. Exact complete-path duplicates and parallel straight-body overlaps; coaxial straight-end gap candidates across different hosts. Couplers, intentional free ends and joint semantics require review. No nonparallel/curved-body clash, receiving-concrete or design-length certification. No findings is not a pass for reinforcement detailing."});
        }
    }
    internal static class RebarConnectionAudit
    {
        internal class Row
        {
            public long Id,Host; public int Position; public double Diameter;
            public IList<Curve> Curves; public string Key;
            public JObject Reference()=>new JObject{["rebar_id"]=Id,["position"]=Position,["host_id"]=Host};
        }
        static double[] Mm(XYZ p)=>new[]{p.X*304.8,p.Y*304.8,p.Z*304.8};
        static string Point(XYZ p)=>string.Join(",",Mm(p).Select(x=>Math.Round(x,2).ToString("F2",CultureInfo.InvariantCulture)));
        internal static List<Row> Read(Document d,IEnumerable<Rebar> bars)
        {
            var result=new List<Row>();
            foreach(var bar in bars) {
                var type=(RebarBarType)d.GetElement(bar.GetTypeId());
                double diameter=type.get_Parameter(BuiltInParameter.REBAR_MODEL_BAR_DIAMETER).AsDouble()*304.8;
                for(int i=0;i<bar.NumberOfBarPositions;i++) {
                    if(!bar.DoesBarExistAtPosition(i))continue;
                    if(result.Count>=6000)throw new ArgumentException("Audit exceeds 6000 physical bars. Narrow the selection; no partial pass is returned.");
                    var curves=bar.GetTransformedCenterlineCurves(false,false,false,MultiplanarOption.IncludeAllMultiplanarCurves,i);
                    if(curves.Count==0)throw new ArgumentException("A bar has no readable centreline; audit incomplete.");
                    var keys=curves.Select(c=>c.GetType().Name+":"+string.Join("|",new[]{Point(c.GetEndPoint(0)),Point(c.GetEndPoint(1))}.OrderBy(x=>x))+":"+Point(c.Evaluate(.5,true))+":"+Math.Round(c.Length*304.8,2).ToString("F2",CultureInfo.InvariantCulture)).OrderBy(x=>x);
                    result.Add(new Row{Id=RevitCompat.GetId(bar.Id),Host=RevitCompat.GetId(bar.GetHostId()),Position=i,Diameter=diameter,Curves=curves,Key=string.Join(";",keys)});
                }
            }return result;
        }
        internal static JArray Compare(List<Row> rows,HashSet<long> newIds,double maxGap)
        {
            var findings=new JArray();long comparisons=0;
            for(int i=0;i<rows.Count;i++)for(int j=i+1;j<rows.Count;j++) {
                var a=rows[i];var b=rows[j];if(newIds!=null&&!newIds.Contains(a.Id)&&!newIds.Contains(b.Id))continue;
                if(++comparisons>4000000)throw new ArgumentException("Audit comparison budget exceeded; narrow the region. No partial pass is returned.");
                string kind=null;double distance=0,amount=0;
                if(a.Key==b.Key)kind="DUPLICATE_PATH";
                else foreach(var ca in a.Curves.OfType<Line>())foreach(var cb in b.Curves.OfType<Line>()) {
                    var r=RebarContinuityMath.Compare(Mm(ca.GetEndPoint(0)),Mm(ca.GetEndPoint(1)),Mm(cb.GetEndPoint(0)),Mm(cb.GetEndPoint(1)));
                    if(r!=null&&r[1]>.1&&r[0]<(a.Diameter+b.Diameter)/2-.01){kind="PARALLEL_BODY_OVERLAP";distance=r[0];amount=r[1];}
                }
                // Only actual path terminal straight segments, not internal bend stations.
                if(kind==null&&maxGap>0&&a.Host!=b.Host)foreach(var ca in new[]{a.Curves.First(),a.Curves.Last()}.OfType<Line>())foreach(var cb in new[]{b.Curves.First(),b.Curves.Last()}.OfType<Line>()) {
                    var r=RebarContinuityMath.Compare(Mm(ca.GetEndPoint(0)),Mm(ca.GetEndPoint(1)),Mm(cb.GetEndPoint(0)),Mm(cb.GetEndPoint(1)));
                    if(r!=null&&r[0]<=.1&&r[1]<-.1&&-r[1]<=maxGap){kind="COAXIAL_GAP_CANDIDATE";distance=r[0];amount=-r[1];}
                }
                if(kind!=null){findings.Add(new JObject{["kind"]=kind,["first"]=a.Reference(),["second"]=b.Reference(),["axis_distance_mm"]=distance,["overlap_or_gap_mm"]=amount});if(findings.Count>=1000)throw new ArgumentException("More than 999 findings; narrow selection. No partial pass is returned.");}
            }return findings;
        }
        internal static void RejectOverlaps(Document d,Rebar created)
        {
            d.Regenerate();var bb=created.get_BoundingBox(null);if(bb==null)throw new ArgumentException("Cannot inspect new rebar bounds.");
            var delta=new XYZ(.01,.01,.01);
            var nearby=new FilteredElementCollector(d).OfClass(typeof(Rebar)).WherePasses(new BoundingBoxIntersectsFilter(new Outline(bb.Min-delta,bb.Max+delta))).Cast<Rebar>().ToList();
            if(!nearby.Any(x=>x.Id==created.Id))nearby.Add(created);
            var findings=Compare(Read(d,nearby),new HashSet<long>{RevitCompat.GetId(created.Id)},0);
            if(findings.Count>0)throw new ArgumentException("Rebar duplicate/parallel-body overlap: "+findings.ToString(Newtonsoft.Json.Formatting.None)+". Creation rolled back; existing bars were not deleted.");
        }
    }
}
