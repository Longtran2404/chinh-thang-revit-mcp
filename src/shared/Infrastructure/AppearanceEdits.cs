using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin
{
    // Schema-neutral, strict input validation; never silently drops a requested channel.
    public static class AppearanceEdits
    {
        public static JArray Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JArray();
            var edits = JArray.Parse(json);
            if (edits.Count > 64) throw new ArgumentException("At most 64 appearance edits per call.");
            foreach (var token in edits)
            {
                var edit = token as JObject ?? throw new ArgumentException("Each edit must be an object.");
                if (edit.Properties().Any(p => p.Name != "path" && p.Name != "value" && p.Name != "bitmap"))
                    throw new ArgumentException("Unknown edit option. Use path and exactly one of value or bitmap.");
                var path = edit["path"] as JArray;
                if (path == null || path.Count == 0 || path.Count > 5 || path.Any(p => p.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)p)))
                    throw new ArgumentException("path must contain 1-5 exact property names from get_material_properties(includeAssets=true).");
                if ((edit["value"] != null) == (edit["bitmap"] != null))
                    throw new ArgumentException("Specify exactly one of value or bitmap.");
                if (edit["value"]?.Type == JTokenType.Null) throw new ArgumentException("Null property values are not supported.");
                if (edit["bitmap"] != null)
                {
                    var bitmap = edit["bitmap"] as JObject ?? throw new ArgumentException("bitmap must be an object.");
                    var allowed = new[] { "file", "scaleXmm", "scaleYmm", "offsetXmm", "offsetYmm", "rotationDegrees", "invert", "repeatU", "repeatV" };
                    if (bitmap.Properties().Any(p => !allowed.Contains(p.Name))) throw new ArgumentException("Unknown bitmap option.");
                    if (bitmap["file"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)bitmap["file"]))
                        throw new ArgumentException("bitmap.file must be a local absolute image path.");
                    foreach (var key in new[] { "scaleXmm", "scaleYmm", "offsetXmm", "offsetYmm", "rotationDegrees" })
                    {
                        if (bitmap[key] == null) continue;
                        if (bitmap[key].Type != JTokenType.Integer && bitmap[key].Type != JTokenType.Float) throw new ArgumentException(key + " must be numeric.");
                        var value = (double)bitmap[key];
                        if (double.IsNaN(value) || double.IsInfinity(value) || (key.StartsWith("scale") && value <= 0)) throw new ArgumentException(key + " is invalid.");
                    }
                    foreach (var key in new[] { "invert", "repeatU", "repeatV" })
                        if (bitmap[key] != null && bitmap[key].Type != JTokenType.Boolean) throw new ArgumentException(key + " must be boolean.");
                }
            }
            return edits;
        }
    }
}
