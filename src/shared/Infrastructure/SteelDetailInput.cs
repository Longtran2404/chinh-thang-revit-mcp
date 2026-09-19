// Chinh Thang, 2026-09-19: explicit geometry inputs, no inferred design sizes.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin
{
    public static class SteelDetailInput
    {
        // TCVN 5575:2024 14.2.2, Table 43. Deliberately bounded to regular,
        // non-staggered round-hole bearing joints, cut edges, ordinary grade B bolts.
        public static JObject CheckBoltLayout(JObject p)
        {
            if(p.Value<string>("standard")!="TCVN5575:2024"||p.Value<string>("joint")!="Bearing"||p.Value<string>("accuracy")!="B"||p.Value<string>("edge")!="Cut")throw new ArgumentException("Supported layout: TCVN5575:2024, Bearing, accuracy B, Cut edges only.");
            double db=Positive(p,"bolt_diameter_mm"),dh=Positive(p,"hole_diameter_mm"),t=Positive(p,"outer_thinnest_plate_mm"),fy=Positive(p,"fy_mpa"),w=Positive(p,"width_mm"),h=Positive(p,"height_mm");
            if(fy>540)throw new ArgumentException("Grade B multi-bolt scope requires fy <= 540 MPa (14.2.3).");
            var holes=p["holes_mm"] as JArray;if(holes==null||holes.Count<2||holes.Count>100)throw new ArgumentException("Provide 2-100 round holes on a complete rectangular grid.");
            var xy=holes.Select(a=>{if(!(a is JArray ar)||ar.Count!=2)throw new ArgumentException("Hole needs [x,y].");return Vector(new JArray(ar[0],ar[1],0),"hole");}).ToArray();
            var xs=xy.Select(a=>a[0]).Distinct().OrderBy(a=>a).ToArray();var ys=xy.Select(a=>a[1]).Distinct().OrderBy(a=>a).ToArray();
            if(xs.Length*ys.Length!=xy.Length||xy.Select(a=>a[0]+","+a[1]).Distinct().Count()!=xy.Length)throw new ArgumentException("Only complete rectangular, non-staggered hole grids are supported.");
            var force=p.Value<string>("force_axis");if(force!="X"&&force!="Y")throw new ArgumentException("Explicit force_axis X/Y required.");
            double minPitch=(fy<540?2.5:3)*dh,maxPitch=Math.Min(8*dh,12*t),minEnd=(fy<540?2:2.5)*dh,minSide=1.5*dh,maxEdge=Math.Min(4*dh,8*t);
            var checks=new JArray();Action<string,double,double,double> check=(n,value,min,max)=>checks.Add(new JObject{["check"]=n,["actual_mm"]=value,["min_mm"]=min,["max_mm"]=max,["passed"]=value>=min-1e-7&&value<=max+1e-7});
            checks.Add(new JObject{["check"]="Hole clearance note 1 (ordinary application)",["passed"]=new[]{1.0,2.0,3.0}.Any(v=>Math.Abs(dh-db-v)<1e-7)});
            for(int i=1;i<xs.Length;i++)check("X pitch",xs[i]-xs[i-1],minPitch,maxPitch);for(int i=1;i<ys.Length;i++)check("Y pitch",ys[i]-ys[i-1],minPitch,maxPitch);
            check("Left edge",xs.First(),force=="X"?minEnd:minSide,maxEdge);check("Right edge",w-xs.Last(),force=="X"?minEnd:minSide,maxEdge);check("Bottom edge",ys.First(),force=="Y"?minEnd:minSide,maxEdge);check("Top edge",h-ys.Last(),force=="Y"?minEnd:minSide,maxEdge);
            return new JObject{["standard"]="TCVN5575:2024",["clause"]="14.2.2 / Table 43 / note 1; 14.2.3",["passed"]=checks.All(c=>c.Value<bool>("passed")),["checks"]=checks,["inputs"]=p.DeepClone(),["scope"]="Round-hole regular grid, ordinary grade B bearing joint, cut edges, fy <= 540 MPa; conservative outer-row maximum for every adjacent row. Not anchor bolts, friction joints, cold-formed members, capacity or a complete connection design.",["capacity_checked"]=false};
        }
        public static double Positive(JObject p,string key)
        {
            var t=p[key];
            if(t==null||(t.Type!=JTokenType.Integer&&t.Type!=JTokenType.Float))throw new ArgumentException(key+" must be an explicit number.");
            var n=(double)t;if(double.IsNaN(n)||double.IsInfinity(n)||n<=0||n>1000000)throw new ArgumentException(key+" must be finite, positive and <= 1000000.");return n;
        }
        public static double[] Vector(JToken token,string name,bool direction=false)
        {
            var a=token as JArray;if(a==null||a.Count!=3||a.Any(t=>t.Type!=JTokenType.Float&&t.Type!=JTokenType.Integer))throw new ArgumentException(name+" needs three numbers.");
            var v=a.Values<double>().ToArray();if(v.Any(n=>double.IsNaN(n)||double.IsInfinity(n)||Math.Abs(n)>100000000))throw new ArgumentException(name+" is outside the finite supported range.");
            if(direction&&v.Sum(x=>x*x)<1e-12)throw new ArgumentException(name+" cannot be zero.");return v;
        }
        public static string Behavior(JObject p)
        {
            var b=p.Value<string>("connection_behavior")??"Unspecified";
            if(b!="Unspecified"&&b!="Pinned"&&b!="Moment")throw new ArgumentException("connection_behavior must be Unspecified, Pinned or Moment.");
            if(b=="Moment"&&string.IsNullOrWhiteSpace(p.Value<string>("design_detail_reference")))throw new ArgumentException("Moment connection needs an explicit design_detail_reference; geometry alone does not establish rigidity.");return b;
        }
        public static JArray Anchors(JObject p)
        {
            Positive(p,"diameter_mm");Positive(p,"length_mm");
            foreach(var k in new[]{"diameter_parameter","length_parameter"})if(string.IsNullOrWhiteSpace(p.Value<string>(k)))throw new ArgumentException(k+" is required.");
            if(p.Value<string>("diameter_parameter")==p.Value<string>("length_parameter"))throw new ArgumentException("Diameter and length parameters must differ.");
            var a=p["positions_mm"] as JArray;if(a==null||a.Count<1||a.Count>100)throw new ArgumentException("Provide 1-100 positions_mm.");
            foreach(var t in a)Vector(t,"positions_mm");
            if(a.Select(t=>string.Join(",",Vector(t,"position"))).Distinct().Count()!=a.Count)throw new ArgumentException("Duplicate anchor positions are not permitted.");
            if(p["axis"]!=null)Vector(p["axis"],"axis",true);
            if(p["allow_exposed"]!=null&&p.Value<bool>("allow_exposed"))throw new ArgumentException("This tool validates fully contained anchors only. Exposed threaded heads need a separately bounded project detail.");
            return a;
        }
    }
}
