using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin
{
    public static class SlabSupportLayout
    {
        public static void Validate(JObject p)
        {
            var density=p["area_per_support_m2"]!=null;
            if(density && (p["spacing_x_mm"]!=null || p["spacing_y_mm"]!=null)) throw new ArgumentException("Choose area_per_support_m2 OR both spacings, not both.");
            if(density) { if(DetailingDesign.Required(p,"area_per_support_m2")>100) throw new ArgumentException("area_per_support_m2 must be <=100."); }
            else { DetailingDesign.Required(p,"spacing_x_mm");DetailingDesign.Required(p,"spacing_y_mm"); }
        }
        // Cell-centred grid, explicitly clipped against the actual Revit face by the caller.
        public static List<double[]> Grid(double minX,double minY,double maxX,double maxY,double sx,double sy)
        {
            if(sx<=0 || sy<=0 || double.IsNaN(sx+sy) || double.IsInfinity(sx+sy)) throw new ArgumentException("Invalid support spacing.");
            var result=new List<double[]>();
            foreach(var value in new[]{minX,minY,maxX,maxY})
                if(double.IsNaN(value)||double.IsInfinity(value)) throw new ArgumentException("Grid bounds must be finite.");
            double countX=Math.Ceiling((maxX-minX)/sx),countY=Math.Ceiling((maxY-minY)/sy);
            if(countX<1 || countY<1 || countX>10000 || countY>10000) throw new ArgumentException("Invalid or oversized support grid.");
            int nx=(int)countX,ny=(int)countY;
            if(nx<1 || ny<1 || (long)nx*ny>10000) throw new ArgumentException("Grid must have 1-10000 candidate positions. Split large zones.");
            for(int j=0;j<ny;j++) for(int i=0;i<nx;i++) result.Add(new[]{minX+(i+0.5)*(maxX-minX)/nx,minY+(j+0.5)*(maxY-minY)/ny});
            return result;
        }
    }
}
