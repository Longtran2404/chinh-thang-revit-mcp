// Chinh Thang Revit MCP: explicit detailing geometry, millimetres.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin
{
    public static class RebarPathInput
    {
        public static double[][] Points(string json)
        {
            var array = JArray.Parse(json ?? "[]");
            if (array.Count < 2 || array.Count > 128) throw new ArgumentException("Provide 2-128 centreline vertices in mm.");
            var points = array.Select(t =>
            {
                var p = t as JArray;
                if (p == null || p.Count != 3 || p.Any(v => v.Type != JTokenType.Integer && v.Type != JTokenType.Float))
                    throw new ArgumentException("Each vertex must be [x,y,z] with three numbers.");
                var values = p.Select(v => (double)v).ToArray();
                if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 100000000))
                    throw new ArgumentException("Coordinates must be finite and within 100 km of origin.");
                return values;
            }).ToArray();
            for (int i = 1; i < points.Length; i++)
            {
                var d = Enumerable.Range(0, 3).Select(k => points[i][k] - points[i-1][k]).ToArray();
                if (Math.Sqrt(d.Sum(v => v*v)) < 1) throw new ArgumentException("Segments must be at least 1 mm long.");
                if (i > 1)
                {
                    var prev = Enumerable.Range(0, 3).Select(k => points[i-1][k] - points[i-2][k]).ToArray();
                    var cosine = d.Zip(prev, (a,b) => a*b).Sum() / Math.Sqrt(d.Sum(v=>v*v)*prev.Sum(v=>v*v));
                    if (Math.Abs(cosine) > 0.999999) throw new ArgumentException("Merge collinear segments; reversing segments cannot define a bend.");
                }
            }
            return points;
        }

        public static void Layout(string mode, string rule, int quantity, double length, double spacing)
        {
            if (mode != "ShapeDriven" && mode != "FreeForm") throw new ArgumentException("mode must be ShapeDriven or FreeForm.");
            if (rule != "Single" && rule != "FixedNumber" && rule != "MaximumSpacing") throw new ArgumentException("Unknown layout rule.");
            if (quantity < 1 || quantity > 1000) throw new ArgumentException("quantity must be 1-1000.");
            if (double.IsNaN(length) || double.IsInfinity(length) || double.IsNaN(spacing) || double.IsInfinity(spacing)) throw new ArgumentException("Layout dimensions must be finite.");
            if (rule == "Single" && (quantity != 1 || length != 0 || spacing != 0)) throw new ArgumentException("Single requires quantity=1 and zero distribution length/spacing.");
            if (rule != "Single" && (length <= 0 || length > 100000)) throw new ArgumentException("Explicit distribution_length_mm in (0,100000] is required.");
            if (rule == "FixedNumber" && (quantity < 2 || spacing != 0)) throw new ArgumentException("FixedNumber requires quantity>=2; spacing is derived from the explicit length.");
            if (rule == "MaximumSpacing" && (spacing <= 0 || spacing > length || quantity != 1 || Math.Ceiling(length/spacing) > 999)) throw new ArgumentException("MaximumSpacing requires positive spacing<=length, quantity=1, and <=1000 positions.");
        }
    }
}
