using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    internal static class RebarAnchorageGuard
    {
        internal static void ValidateContext(Document d,JObject anchor)
        {
            if(anchor.Value<bool>("enabled")) {
                var id=anchor["receiving_host_id"];
                if(id?.Type!=JTokenType.Integer)throw new ArgumentException("Each enabled anchor requires explicit receiving_host_id for the concrete column/beam/landing receiving the anchorage.");
                var e=d.GetElement(RevitCompat.ToElementId(id.Value<long>()));
                if(e==null||!RebarHostData.IsValidHost(e)||CheckedDetailGeometry.Solids(e).Count==0)throw new ArgumentException("Receiving host must be actual concrete with readable solids.");
            } else if(string.IsNullOrWhiteSpace(anchor.Value<string>("disabled_reason")))throw new ArgumentException("Disabled anchorage requires disabled_reason describing the reviewed free end or continuation/splice. It is never marked connection-verified.");
        }
        internal static void Verify(Document d,Rebar bar,XYZ requestedStart,JObject start,JObject end,JObject startCalc,JObject endCalc)
        {
            var initial=bar.GetTransformedCenterlineCurves(false,false,false,MultiplanarOption.IncludeAllMultiplanarCurves,0);
            bool reversed=initial.Last().GetEndPoint(1).DistanceTo(requestedStart)<initial.First().GetEndPoint(0).DistanceTo(requestedStart);
            for(int i=0;i<bar.NumberOfBarPositions;i++) {
                if(!bar.DoesBarExistAtPosition(i))continue;
                var curves=bar.GetTransformedCenterlineCurves(false,false,false,MultiplanarOption.IncludeAllMultiplanarCurves,i).ToList();
                CheckEnd(d,curves,start,startCalc,reversed);
                CheckEnd(d,curves,end,endCalc,!reversed);
            }
        }
        static void CheckEnd(Document d,List<Curve> path,JObject anchor,JObject calc,bool fromEnd)
        {
            if(!anchor.Value<bool>("enabled"))return;
            var solids=CheckedDetailGeometry.Solids(d.GetElement(RevitCompat.ToElementId(anchor.Value<long>("receiving_host_id"))));
            // Check the whole modeled extension back to the stated critical section,
            // even when its minimum bend leg makes it longer than the design minimum.
            double remaining=Math.Max(calc.Value<double>("required_length_mm"),calc.Value<double>("modeled_anchor_length_mm"))/304.8;
            var ordered=fromEnd?path.AsEnumerable().Reverse():path;
            foreach(var original in ordered) {
                if(remaining<=.01/304.8)break;
                if(!(original is Line)&&!(original is Arc))throw new ArgumentException("Anchor validation supports Line/Arc centrelines only.");
                double fraction=Math.Min(1,remaining/original.Length);
                var segment=original.Clone();
                segment.MakeBound(original.ComputeRawParameter(fromEnd?1-fraction:0),original.ComputeRawParameter(fromEnd?1:fraction));
                var intervals=new List<double[]>();
                foreach(var solid in solids)using(var intersection=solid.IntersectWithCurve(segment,new SolidCurveIntersectionOptions())) {
                    for(int k=0;k<intersection.SegmentCount;k++) {
                        var inside=intersection.GetCurveSegment(k);
                        var p=segment.Project(inside.GetEndPoint(0));var q=segment.Project(inside.GetEndPoint(1));
                        if(p==null||q==null)throw new ArgumentException("Cannot resolve anchor/concrete intersection.");
                        double a=segment.ComputeNormalizedParameter(p.Parameter),b=segment.ComputeNormalizedParameter(q.Parameter);
                        intervals.Add(new[]{Math.Max(0,Math.Min(a,b)),Math.Min(1,Math.Max(a,b))});
                    }
                }
                double covered=0,end=0;
                foreach(var interval in intervals.OrderBy(x=>x[0])){covered+=Math.Max(0,interval[1]-Math.Max(end,interval[0]));end=Math.Max(end,interval[1]);}
                if((1-covered)*segment.Length>.05/304.8)throw new ArgumentException("Calculated anchor centreline leaves receiving concrete or crosses an opening. Entire creation rolled back. Reposition/redesign the joint; do not shorten the required anchor.");
                remaining-=segment.Length;
            }
            if(remaining>.05/304.8)throw new ArgumentException("Actual bar is shorter than its calculated anchorage.");
        }
    }
}
