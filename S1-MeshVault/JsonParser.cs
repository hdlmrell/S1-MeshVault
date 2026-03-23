using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace MeshVault
{
    /// <summary>
    /// Hand-rolled JSON parser for MeshDatabase.json. No Newtonsoft dependency.
    /// </summary>
    internal static class JsonParser
    {
        internal static void ParseDatabase(string json, Dictionary<string, MeshEntry> db)
        {
            int pos = 0;
            int len = json.Length;

            SkipWhitespace(json, ref pos, len);
            if (pos >= len || json[pos] != '{') return;
            pos++;

            while (pos < len)
            {
                SkipWhitespace(json, ref pos, len);
                if (pos >= len || json[pos] == '}') break;
                if (json[pos] == ',') { pos++; continue; }

                string key = ReadJsonString(json, ref pos, len);
                if (key == null) break;

                SkipWhitespace(json, ref pos, len);
                if (pos >= len || json[pos] != ':') break;
                pos++;

                SkipWhitespace(json, ref pos, len);
                if (pos >= len || json[pos] != '{') break;

                int braceStart = pos;
                int depth = 1;
                pos++;
                while (pos < len && depth > 0)
                {
                    if (json[pos] == '{') depth++;
                    else if (json[pos] == '}') depth--;
                    if (depth > 0) pos++;
                }
                if (pos >= len) break;
                pos++;

                string entryJson = json.Substring(braceStart, pos - braceStart);
                var entry = ParseEntry(entryJson);
                if (entry != null)
                    db[key] = entry;
            }
        }

        private static MeshEntry ParseEntry(string json)
        {
            var entry = new MeshEntry();
            try
            {
                // Build a parent-only view that excludes childMeshes content to prevent
                // child fields (colorTint, emissiveColor, etc.) from bleeding into the parent.
                string parentJson = json;
                int childIdx = json.IndexOf("\"childMeshes\"", StringComparison.Ordinal);
                if (childIdx >= 0)
                    parentJson = json.Substring(0, childIdx) + "}";

                entry.Vertices = ParseVec3Array(ExtractArrayValue(parentJson, "vertices"));
                entry.Normals = ParseVec3Array(ExtractArrayValue(parentJson, "normals"));
                entry.UVs = ParseVec2Array(ExtractArrayValue(parentJson, "uvs"));
                entry.Triangles = ParseIntArray(ExtractArrayValue(parentJson, "triangles"));
                entry.MaterialName = ExtractStringValue(parentJson, "materialName") ?? "Standard";
                entry.ShaderName = ExtractStringValue(parentJson, "shaderName") ?? "Standard";
                entry.Color = ParseFloatArray(ExtractArrayValue(parentJson, "color")) ?? MeshVaultAPI.DefaultColor;
                entry.BoundsCenter = ParseFloatArray(ExtractArrayValue(parentJson, "boundsCenter")) ?? new[] { 0f, 0f, 0f };
                entry.BoundsSize = ParseFloatArray(ExtractArrayValue(parentJson, "boundsSize")) ?? new[] { 1f, 1f, 1f };

                var triCounts = ParseIntArray(ExtractArrayValue(parentJson, "subMeshTriCounts"));
                if (triCounts != null && triCounts.Length > 0)
                {
                    entry.SubMeshTriCounts = triCounts;
                    entry.SubMeshMaterialNames = ParseStringArray(ExtractArrayValue(parentJson, "subMeshMaterialNames"));
                    entry.SubMeshShaderNames = ParseStringArray(ExtractArrayValue(parentJson, "subMeshShaderNames"));
                    entry.SubMeshTextureNames = ParseStringArray(ExtractArrayValue(parentJson, "subMeshTextureNames"));
                    entry.SubMeshColorTints = ParseFloat4ArrayOfArrays(ExtractArrayValue(parentJson, "subMeshColorTints"));
                }

                var texName = ExtractStringValue(parentJson, "textureName");
                entry.TextureName = string.IsNullOrEmpty(texName) ? null : texName;
                entry.BakedColorTint = ParseFloatArray(ExtractArrayValue(parentJson, "bakedColorTint"));

                // Material property overrides (all optional, null = no override)
                entry.ColorTint = ParseFloatArray(ExtractArrayValue(parentJson, "colorTint"));
                entry.Metallic = ExtractFloatValue(parentJson, "metallic");
                entry.Smoothness = ExtractFloatValue(parentJson, "smoothness");
                entry.EmissiveColor = ParseFloatArray(ExtractArrayValue(parentJson, "emissiveColor"));
                entry.EmissiveIntensity = ExtractFloatValue(parentJson, "emissiveIntensity");

                entry.ChildMeshes = ParseChildMeshArray(json);

                return entry;
            }
            catch (Exception ex)
            {
                Melon<MeshVaultPlugin>.Logger.Warning($"Parse entry failed: {ex.Message}");
                return null;
            }
        }

        internal static string ExtractArrayValue(string json, string key)
        {
            string search = $"\"{key}\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += search.Length;

            while (idx < json.Length && (json[idx] == ':' || json[idx] == ' ' || json[idx] == '\t')) idx++;
            if (idx >= json.Length || json[idx] != '[') return null;

            int bracketStart = idx;
            int depth = 1;
            idx++;
            while (idx < json.Length && depth > 0)
            {
                if (json[idx] == '[') depth++;
                else if (json[idx] == ']') depth--;
                idx++;
            }
            return json.Substring(bracketStart, idx - bracketStart);
        }

        internal static string ExtractStringValue(string json, string key)
        {
            string search = $"\"{key}\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += search.Length;

            while (idx < json.Length && (json[idx] == ':' || json[idx] == ' ' || json[idx] == '\t')) idx++;
            if (idx >= json.Length || json[idx] != '"') return null;

            int pos = idx;
            return ReadJsonString(json, ref pos, json.Length);
        }

        /// <summary>
        /// Extracts a single numeric value from a JSON object string by key name.
        /// Returns null if the key is not found or the value cannot be parsed.
        /// </summary>
        internal static float? ExtractFloatValue(string json, string key)
        {
            string search = $"\"{key}\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += search.Length;

            while (idx < json.Length && (json[idx] == ':' || json[idx] == ' ' || json[idx] == '\t'))
                idx++;
            if (idx >= json.Length) return null;

            int start = idx;
            while (idx < json.Length && json[idx] != ',' && json[idx] != '}' && json[idx] != '\n' && json[idx] != '\r')
                idx++;

            string valueStr = json.Substring(start, idx - start).Trim();
            if (string.IsNullOrEmpty(valueStr) || valueStr == "null") return null;

            if (float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;
            return null;
        }

        private static Vector3[] ParseVec3Array(string arrayStr)
        {
            if (string.IsNullOrEmpty(arrayStr)) return new Vector3[0];
            var result = new List<Vector3>();

            int pos = 1;
            int len = arrayStr.Length;
            while (pos < len)
            {
                int start = arrayStr.IndexOf('[', pos);
                if (start < 0) break;
                int end = arrayStr.IndexOf(']', start);
                if (end < 0) break;

                string inner = arrayStr.Substring(start + 1, end - start - 1);
                var parts = inner.Split(',');
                if (parts.Length >= 3)
                {
                    result.Add(new Vector3(
                        float.Parse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture),
                        float.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture),
                        float.Parse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture)));
                }
                pos = end + 1;
            }
            return result.ToArray();
        }

        private static Vector2[] ParseVec2Array(string arrayStr)
        {
            if (string.IsNullOrEmpty(arrayStr)) return new Vector2[0];
            var result = new List<Vector2>();

            int pos = 1;
            int len = arrayStr.Length;
            while (pos < len)
            {
                int start = arrayStr.IndexOf('[', pos);
                if (start < 0) break;
                int end = arrayStr.IndexOf(']', start);
                if (end < 0) break;

                string inner = arrayStr.Substring(start + 1, end - start - 1);
                var parts = inner.Split(',');
                if (parts.Length >= 2)
                {
                    result.Add(new Vector2(
                        float.Parse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture),
                        float.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture)));
                }
                pos = end + 1;
            }
            return result.ToArray();
        }

        private static int[] ParseIntArray(string arrayStr)
        {
            if (string.IsNullOrEmpty(arrayStr)) return new int[0];
            arrayStr = arrayStr.Trim();
            if (arrayStr.StartsWith("[")) arrayStr = arrayStr.Substring(1);
            if (arrayStr.EndsWith("]")) arrayStr = arrayStr.Substring(0, arrayStr.Length - 1);

            var parts = arrayStr.Split(',');
            var result = new List<int>();
            foreach (var p in parts)
            {
                string trimmed = p.Trim();
                if (trimmed.Length > 0 && int.TryParse(trimmed, out int v))
                    result.Add(v);
            }
            return result.ToArray();
        }

        private static float[] ParseFloatArray(string arrayStr)
        {
            if (string.IsNullOrEmpty(arrayStr)) return null;
            arrayStr = arrayStr.Trim();
            if (arrayStr.StartsWith("[")) arrayStr = arrayStr.Substring(1);
            if (arrayStr.EndsWith("]")) arrayStr = arrayStr.Substring(0, arrayStr.Length - 1);

            var parts = arrayStr.Split(',');
            var result = new List<float>();
            foreach (var p in parts)
            {
                string trimmed = p.Trim();
                if (trimmed.Length > 0 && float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    result.Add(v);
            }
            return result.ToArray();
        }

        private static string[] ParseStringArray(string arrayStr)
        {
            if (string.IsNullOrEmpty(arrayStr)) return null;
            var result = new List<string>();
            int pos = 0;
            int len = arrayStr.Length;

            while (pos < len && arrayStr[pos] != '[') pos++;
            if (pos < len) pos++;

            while (pos < len)
            {
                SkipWhitespace(arrayStr, ref pos, len);
                if (pos >= len || arrayStr[pos] == ']') break;
                if (arrayStr[pos] == ',') { pos++; continue; }

                string val = ReadJsonString(arrayStr, ref pos, len);
                if (val != null) result.Add(val);
                else break;
            }
            return result.Count > 0 ? result.ToArray() : null;
        }

        private static float[][] ParseFloat4ArrayOfArrays(string arrayStr)
        {
            if (string.IsNullOrEmpty(arrayStr)) return null;
            var result = new List<float[]>();

            int pos = 1;
            int len = arrayStr.Length;
            while (pos < len)
            {
                int start = arrayStr.IndexOf('[', pos);
                if (start < 0) break;
                int end = arrayStr.IndexOf(']', start);
                if (end < 0) break;

                string inner = arrayStr.Substring(start + 1, end - start - 1);
                var parts = inner.Split(',');
                if (parts.Length >= 4)
                {
                    result.Add(new[]
                    {
                        float.Parse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture),
                        float.Parse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture),
                        float.Parse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture),
                        float.Parse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture)
                    });
                }
                pos = end + 1;
            }
            return result.Count > 0 ? result.ToArray() : null;
        }

        private static ChildMeshEntry[] ParseChildMeshArray(string json)
        {
            string search = "\"childMeshes\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += search.Length;

            while (idx < json.Length && json[idx] != '[') idx++;
            if (idx >= json.Length) return null;

            int arrayStart = idx;
            int depth = 1;
            idx++;
            while (idx < json.Length && depth > 0)
            {
                if (json[idx] == '[') depth++;
                else if (json[idx] == ']') depth--;
                idx++;
            }
            string arrayContent = json.Substring(arrayStart + 1, idx - arrayStart - 2);

            var entries = new List<ChildMeshEntry>();
            int pos = 0;
            while (pos < arrayContent.Length)
            {
                int objStart = arrayContent.IndexOf('{', pos);
                if (objStart < 0) break;

                int objDepth = 1;
                int objEnd = objStart + 1;
                while (objEnd < arrayContent.Length && objDepth > 0)
                {
                    if (arrayContent[objEnd] == '{') objDepth++;
                    else if (arrayContent[objEnd] == '}') objDepth--;
                    objEnd++;
                }

                string objJson = arrayContent.Substring(objStart, objEnd - objStart);
                try
                {
                    var entry = new ChildMeshEntry
                    {
                        Vertices = ParseVec3Array(ExtractArrayValue(objJson, "vertices")),
                        Normals = ParseVec3Array(ExtractArrayValue(objJson, "normals")),
                        UVs = ParseVec2Array(ExtractArrayValue(objJson, "uvs")),
                        Triangles = ParseIntArray(ExtractArrayValue(objJson, "triangles")),
                        MaterialName = ExtractStringValue(objJson, "materialName") ?? "Standard",
                        ShaderName = ExtractStringValue(objJson, "shaderName") ?? "Standard",
                        Color = ParseFloatArray(ExtractArrayValue(objJson, "color")) ?? MeshVaultAPI.DefaultColor,
                        ColorTint = ParseFloatArray(ExtractArrayValue(objJson, "colorTint")),
                        Metallic = ExtractFloatValue(objJson, "metallic"),
                        Smoothness = ExtractFloatValue(objJson, "smoothness"),
                        EmissiveColor = ParseFloatArray(ExtractArrayValue(objJson, "emissiveColor")),
                        EmissiveIntensity = ExtractFloatValue(objJson, "emissiveIntensity")
                    };
                    entries.Add(entry);
                }
                catch (Exception ex)
                {
                    Melon<MeshVaultPlugin>.Logger.Warning($"Parse child mesh failed: {ex.Message}");
                }
                pos = objEnd;
            }

            return entries.Count > 0 ? entries.ToArray() : null;
        }

        private static void SkipWhitespace(string s, ref int pos, int len)
        {
            while (pos < len && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\n' || s[pos] == '\r'))
                pos++;
        }

        private static string ReadJsonString(string s, ref int pos, int len)
        {
            SkipWhitespace(s, ref pos, len);
            if (pos >= len || s[pos] != '"') return null;
            pos++;

            var sb = new StringBuilder();
            while (pos < len)
            {
                char c = s[pos];
                if (c == '\\' && pos + 1 < len)
                {
                    pos++;
                    sb.Append(s[pos]);
                }
                else if (c == '"')
                {
                    pos++;
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                }
                pos++;
            }
            return sb.ToString();
        }
    }
}
