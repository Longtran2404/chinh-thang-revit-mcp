using System;

namespace RvtMcp.Plugin
{
    // Millimetres. Intentionally limited to parallel straight segments.
    public static class RebarContinuityMath
    {
        public static double[] Compare(double[] a, double[] b, double[] c, double[] d)
        {
            var u = Sub(b, a); var v = Sub(d, c);
            double lu = Math.Sqrt(Dot(u,u)), lv = Math.Sqrt(Dot(v,v));
            if (lu < .01 || lv < .01) return null;
            for (int k=0;k<3;k++) {u[k]/=lu;v[k]/=lv;}
            if (Math.Abs(Dot(u,v)) < 1-1e-10) return null;
            var delta=Sub(c,a); double start=Dot(delta,u), end=Dot(Sub(d,a),u);
            var perpendicular=new[]{delta[0]-start*u[0],delta[1]-start*u[1],delta[2]-start*u[2]};
            double overlap=Math.Min(lu,Math.Max(start,end))-Math.Max(0,Math.Min(start,end));
            return new[]{Math.Sqrt(Dot(perpendicular,perpendicular)),overlap};
        }
        private static double[] Sub(double[] a,double[] b)=>new[]{a[0]-b[0],a[1]-b[1],a[2]-b[2]};
        private static double Dot(double[] a,double[] b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
    }
}
