using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin
{
    // Uses the actual rendering schema, including Advanced/PBR assets. No assumed channel names.
    public static class MaterialAppearanceEditor
    {
        public static JObject Inspect(Asset asset)
        {
            var remaining = 512;
            return ReadAsset(asset, new JArray(), 0, ref remaining);
        }

        private static JObject ReadAsset(Asset asset, JArray parent, int depth, ref int remaining)
        {
            var properties = new JArray();
            var result = new JObject { ["schema"] = asset.Name, ["properties"] = properties };
            for (int i = 0; i < asset.Size && remaining > 0; i++, remaining--)
            {
                var prop = asset[i];
                var path = new JArray(parent.Select(p => p.DeepClone())) { prop.Name };
                var item = new JObject { ["name"] = prop.Name, ["path"] = path, ["type"] = prop.GetType().Name, ["read_only_snapshot"] = prop.IsReadOnly };
                try
                {
                    item["value"] = ReadValue(prop);
                    if (prop is AssetPropertyDistance distance) item["unit_type_id"] = distance.GetUnitTypeId().TypeId;
                    if (prop.NumberOfConnectedProperties > 0)
                    {
                        if (depth >= 4) item["connected_truncated"] = true;
                        else
                        {
                            var child = prop.GetSingleConnectedAsset();
                            if (child != null) item["connected"] = ReadAsset(child, path, depth + 1, ref remaining);
                        }
                    }
                }
                catch (Exception ex) { item["read_error"] = ex.Message; }
                properties.Add(item);
            }
            if (remaining <= 0) result["truncated"] = true;
            return result;
        }

        private static JToken ReadValue(AssetProperty prop)
        {
            if (prop is AssetPropertyString s) return s.Value;
            if (prop is AssetPropertyBoolean b) return b.Value;
            if (prop is AssetPropertyInteger i) return i.Value;
            if (prop is AssetPropertyEnum e) return e.Value;
            if (prop is AssetPropertyDistance d) return d.Value;
            if (prop is AssetPropertyDouble n) return n.Value;
            if (prop is AssetPropertyFloat f) return f.Value;
            if (prop is AssetPropertyDoubleArray4d c) return new JArray(c.GetValueAsDoubles());
            if (prop is AssetPropertyDoubleArray3d v) return new JArray(v.GetValueAsDoubles());
            return JValue.CreateNull();
        }

        // Caller owns the Revit transaction. Any unsupported property or invalid bitmap throws,
        // rolling back the complete operation, including duplicated assets and graphics edits.
        public static JObject Apply(Document doc, Material material, JArray edits, long? sourceId, bool duplicate)
        {
            var assetId = sourceId.HasValue ? RevitCompat.ToElementId(sourceId.Value) : material.AppearanceAssetId;
            var element = doc.GetElement(assetId) as AppearanceAssetElement;
            if (element == null) throw new ArgumentException("Material has no Appearance Asset. Supply sourceAppearanceAssetId from a suitable material first.");
            if (duplicate)
                element = element.Duplicate("CT_Material_" + RevitCompat.GetId(material.Id) + "_" + Guid.NewGuid().ToString("N").Substring(0, 10));
            else
            {
                var otherUsers = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                    .Count(m => m.Id != material.Id && m.AppearanceAssetId == element.Id);
                if (otherUsers > 0) throw new ArgumentException("Appearance Asset is shared by other materials; keep duplicateAppearanceAsset=true.");
            }
            material.AppearanceAssetId = element.Id;
            var applied = new JArray();
            using (var scope = new AppearanceAssetEditScope(doc))
            {
                var asset = scope.Start(element.Id);
                foreach (JObject edit in edits)
                {
                    var path = (JArray)edit["path"];
                    Asset current = asset;
                    AssetProperty prop = null;
                    for (int i = 0; i < path.Count; i++)
                    {
                        prop = current.FindByName((string)path[i]);
                        if (prop == null) throw new ArgumentException("Unsupported property in this schema: " + string.Join("/", path.Values<string>()));
                        if (i < path.Count - 1)
                            current = prop.GetSingleConnectedAsset() ?? throw new ArgumentException("Missing connected asset at " + path[i]);
                    }
                    if (edit["bitmap"] is JObject bitmap) SetBitmap(prop, bitmap);
                    else SetValue(prop, edit["value"]);
                    applied.Add(path.DeepClone());
                }
                scope.Commit(true);
            }
            return new JObject { ["asset_id"] = RevitCompat.GetId(element.Id), ["duplicated"] = duplicate, ["applied_paths"] = applied };
        }

        private static void SetValue(AssetProperty prop, JToken value)
        {
            if (prop.IsReadOnly) throw new ArgumentException("Property is read-only: " + prop.Name);
            if (prop is AssetPropertyString s && value.Type == JTokenType.String) { s.Value = (string)value; return; }
            if (prop is AssetPropertyBoolean b && value.Type == JTokenType.Boolean) { b.Value = (bool)value; return; }
            if (prop is AssetPropertyInteger i && value.Type == JTokenType.Integer) { i.Value = (int)value; return; }
            if (prop is AssetPropertyEnum e && value.Type == JTokenType.Integer) { e.Value = (int)value; return; }
            if (value.Type == JTokenType.Float || value.Type == JTokenType.Integer)
            {
                var n = (double)value;
                if (double.IsNaN(n) || double.IsInfinity(n)) throw new ArgumentException("Finite numeric values are required.");
                if (prop is AssetPropertyDistance d) { d.Value = n; return; }
                if (prop is AssetPropertyDouble a) { a.Value = n; return; }
                if (prop is AssetPropertyFloat f) { f.Value = (float)n; return; }
            }
            if (value is JArray array && array.All(v => v.Type == JTokenType.Float || v.Type == JTokenType.Integer))
            {
                var values = array.Values<double>().ToArray();
                if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v))) throw new ArgumentException("Finite array values are required.");
                if (prop is AssetPropertyDoubleArray4d color && values.Length == 4) { color.SetValueAsDoubles(values); return; }
                if (prop is AssetPropertyDoubleArray3d vector && values.Length == 3) { vector.SetValueAsXYZ(new XYZ(values[0], values[1], values[2])); return; }
            }
            throw new ArgumentException("Unsupported value/type for " + prop.Name + " (" + prop.GetType().Name + "). Nothing was committed.");
        }

        private static void SetBitmap(AssetProperty prop, JObject bitmap)
        {
            var file = (string)bitmap["file"];
            if (!Path.IsPathRooted(file) || file.StartsWith(@"\\") || !File.Exists(file))
                throw new ArgumentException("Texture must be an existing local absolute file, not a URL or network share.");
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }.Contains(extension))
                throw new ArgumentException("Use PNG, JPEG, BMP or TIFF textures.");
            var connected = prop.GetSingleConnectedAsset();
            if (connected == null)
            {
                prop.AddConnectedAsset("UnifiedBitmap");
                connected = prop.GetSingleConnectedAsset();
            }
            var image = connected?.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString;
            if (image == null) throw new ArgumentException("Connected asset is not a UnifiedBitmap. Choose its exact nested property path; procedural assets are not replaced silently.");
            image.Value = Path.GetFullPath(file);
            SetDistance(connected, UnifiedBitmap.TextureRealWorldScaleX, bitmap["scaleXmm"]);
            SetDistance(connected, UnifiedBitmap.TextureRealWorldScaleY, bitmap["scaleYmm"]);
            SetDistance(connected, UnifiedBitmap.TextureRealWorldOffsetX, bitmap["offsetXmm"]);
            SetDistance(connected, UnifiedBitmap.TextureRealWorldOffsetY, bitmap["offsetYmm"]);
            SetOptional(connected, UnifiedBitmap.TextureWAngle, bitmap["rotationDegrees"]);
            SetOptional(connected, UnifiedBitmap.UnifiedbitmapInvert, bitmap["invert"]);
            SetOptional(connected, UnifiedBitmap.TextureURepeat, bitmap["repeatU"]);
            SetOptional(connected, UnifiedBitmap.TextureVRepeat, bitmap["repeatV"]);
        }
        private static void SetDistance(Asset asset, string name, JToken mm)
        {
            if (mm == null) return;
            var prop = asset.FindByName(name) as AssetPropertyDistance ?? throw new ArgumentException("Unsupported texture distance: " + name);
            prop.Value = UnitUtils.Convert((double)mm, UnitTypeId.Millimeters, prop.GetUnitTypeId());
        }
        private static void SetOptional(Asset asset, string name, JToken value)
        {
            if (value != null) SetValue(asset.FindByName(name) ?? throw new ArgumentException("Unsupported texture option: " + name), value);
        }
    }
}
