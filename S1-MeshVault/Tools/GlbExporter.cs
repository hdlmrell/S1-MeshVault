#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace MeshVault.Tools
{
    internal class ExportObject
    {
        public string Name;
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] UVs;
        public int[] Triangles;
        public Material Material;
    }

    internal static class GlbExporter
    {
        private const uint GLB_MAGIC = 0x46546C67;      // "glTF"
        private const uint GLB_VERSION = 2;
        private const uint CHUNK_TYPE_JSON = 0x4E4F534A; // "JSON"
        private const uint CHUNK_TYPE_BIN  = 0x004E4942; // "BIN\0"

        internal static string Export(List<ExportObject> objects, string outputPath)
        {
            var savedCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                var binStream = new MemoryStream();
                var bw = new BinaryWriter(binStream);

                var bufferViews = new List<BufferViewInfo>();
                var accessors = new List<AccessorInfo>();
                var meshInfos = new List<MeshInfo>();

                var materialMap = new Dictionary<Material, int>();
                var materialList = new List<MaterialInfo>();
                var textureMap = new Dictionary<Texture, int>();
                var textureList = new List<byte[]>();

                for (int objIdx = 0; objIdx < objects.Count; objIdx++)
                {
                    var obj = objects[objIdx];
                    int vertCount = obj.Vertices.Length;
                    int triCount = obj.Triangles.Length;
                    bool useShort = vertCount < 65536;

                    // Positions (VEC3 FLOAT)
                    int posViewIdx = bufferViews.Count;
                    int posAccIdx = accessors.Count;
                    long posOffset = binStream.Position;
                    var posMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    var posMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                    for (int i = 0; i < vertCount; i++)
                    {
                        float x = -obj.Vertices[i].x;
                        float y = obj.Vertices[i].y;
                        float z = obj.Vertices[i].z;
                        bw.Write(x); bw.Write(y); bw.Write(z);
                        if (x < posMin.x) posMin.x = x; if (x > posMax.x) posMax.x = x;
                        if (y < posMin.y) posMin.y = y; if (y > posMax.y) posMax.y = y;
                        if (z < posMin.z) posMin.z = z; if (z > posMax.z) posMax.z = z;
                    }
                    int posLen = (int)(binStream.Position - posOffset);
                    bufferViews.Add(new BufferViewInfo { Offset = (int)posOffset, Length = posLen, Target = 34962 });
                    accessors.Add(new AccessorInfo { ViewIdx = posViewIdx, CompType = 5126, Count = vertCount, Type = "VEC3",
                        Min = new[] { posMin.x, posMin.y, posMin.z }, Max = new[] { posMax.x, posMax.y, posMax.z } });

                    // Normals (VEC3 FLOAT)
                    int normViewIdx = bufferViews.Count;
                    int normAccIdx = accessors.Count;
                    long normOffset = binStream.Position;
                    for (int i = 0; i < vertCount; i++)
                    {
                        float x = -obj.Normals[i].x;
                        float y = obj.Normals[i].y;
                        float z = obj.Normals[i].z;
                        bw.Write(x); bw.Write(y); bw.Write(z);
                    }
                    int normLen = (int)(binStream.Position - normOffset);
                    bufferViews.Add(new BufferViewInfo { Offset = (int)normOffset, Length = normLen, Target = 34962 });
                    accessors.Add(new AccessorInfo { ViewIdx = normViewIdx, CompType = 5126, Count = vertCount, Type = "VEC3" });

                    // UVs (VEC2 FLOAT)
                    int uvViewIdx = bufferViews.Count;
                    int uvAccIdx = accessors.Count;
                    long uvOffset = binStream.Position;
                    for (int i = 0; i < vertCount; i++)
                    {
                        bw.Write(obj.UVs[i].x);
                        bw.Write(1f - obj.UVs[i].y);
                    }
                    int uvLen = (int)(binStream.Position - uvOffset);
                    bufferViews.Add(new BufferViewInfo { Offset = (int)uvOffset, Length = uvLen, Target = 34962 });
                    accessors.Add(new AccessorInfo { ViewIdx = uvViewIdx, CompType = 5126, Count = vertCount, Type = "VEC2" });

                    // Indices (SCALAR, USHORT or UINT, winding reversed)
                    int idxViewIdx = bufferViews.Count;
                    int idxAccIdx = accessors.Count;
                    long idxOffset = binStream.Position;
                    for (int i = 0; i < triCount; i += 3)
                    {
                        if (useShort)
                        {
                            bw.Write((ushort)obj.Triangles[i]);
                            bw.Write((ushort)obj.Triangles[i + 2]);
                            bw.Write((ushort)obj.Triangles[i + 1]);
                        }
                        else
                        {
                            bw.Write((uint)obj.Triangles[i]);
                            bw.Write((uint)obj.Triangles[i + 2]);
                            bw.Write((uint)obj.Triangles[i + 1]);
                        }
                    }
                    int idxLen = (int)(binStream.Position - idxOffset);
                    while (binStream.Position % 4 != 0) bw.Write((byte)0);
                    bufferViews.Add(new BufferViewInfo { Offset = (int)idxOffset, Length = idxLen, Target = 34963 });
                    accessors.Add(new AccessorInfo { ViewIdx = idxViewIdx, CompType = useShort ? 5123 : 5125, Count = triCount, Type = "SCALAR" });

                    // Material
                    int matIdx = -1;
                    if (obj.Material != null)
                    {
                        if (!materialMap.TryGetValue(obj.Material, out matIdx))
                        {
                            matIdx = materialList.Count;
                            materialMap[obj.Material] = matIdx;
                            materialList.Add(ExtractMaterial(obj.Material, textureMap, textureList));
                        }
                    }

                    meshInfos.Add(new MeshInfo
                    {
                        Name = obj.Name,
                        PosAcc = posAccIdx, NormAcc = normAccIdx, UvAcc = uvAccIdx, IdxAcc = idxAccIdx,
                        MaterialIdx = matIdx
                    });
                }

                // Write texture PNG data into BIN
                var textureViewIndices = new List<int>();
                for (int i = 0; i < textureList.Count; i++)
                {
                    while (binStream.Position % 4 != 0) bw.Write((byte)0);
                    int texViewIdx = bufferViews.Count;
                    long texOffset = binStream.Position;
                    bw.Write(textureList[i]);
                    int texLen = (int)(binStream.Position - texOffset);
                    bufferViews.Add(new BufferViewInfo { Offset = (int)texOffset, Length = texLen, Target = 0 });
                    textureViewIndices.Add(texViewIdx);
                }

                bw.Flush();
                int totalBinSize = (int)binStream.Length;

                string json = BuildJson(meshInfos, accessors, bufferViews, materialList, textureViewIndices, totalBinSize);
                WriteGlb(json, binStream.ToArray(), outputPath);

                bw.Dispose();
                binStream.Dispose();

                Melon<MeshVaultPlugin>.Logger.Msg($"[GlbExporter] Exported {objects.Count} objects to: {outputPath}");
                return outputPath;
            }
            catch (Exception ex)
            {
                Melon<MeshVaultPlugin>.Logger.Error($"[GlbExporter] Export failed: {ex}");
                return null;
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = savedCulture;
            }
        }

        private static MaterialInfo ExtractMaterial(Material mat, Dictionary<Texture, int> textureMap, List<byte[]> textureList)
        {
            var info = new MaterialInfo();
            info.Name = mat.name;

            if (mat.HasProperty("_BaseColor"))
                info.Color = mat.GetColor("_BaseColor");
            else if (mat.HasProperty("_Color"))
                info.Color = mat.GetColor("_Color");
            else
                info.Color = Color.white;

            if (mat.HasProperty("_Metallic"))
                info.Metallic = mat.GetFloat("_Metallic");

            if (mat.HasProperty("_Smoothness"))
                info.Roughness = 1f - mat.GetFloat("_Smoothness");
            else if (mat.HasProperty("_Glossiness"))
                info.Roughness = 1f - mat.GetFloat("_Glossiness");
            else
                info.Roughness = 0.5f;

            var allTexProps = mat.GetTexturePropertyNames();
            Texture tex = null;
            foreach (var propName in allTexProps)
            {
                var t = mat.GetTexture(propName);
                if (tex == null && t != null)
                    tex = t;
            }

            if (tex != null)
            {
                if (!textureMap.TryGetValue(tex, out int texIdx))
                {
                    byte[] png = ReadTexturePixels(tex);
                    if (png != null)
                    {
                        texIdx = textureList.Count;
                        textureMap[tex] = texIdx;
                        textureList.Add(png);
                    }
                    else
                    {
                        texIdx = -1;
                    }
                }
                info.TextureIndex = texIdx;
            }
            else
            {
                info.TextureIndex = -1;
            }

            return info;
        }

        private static byte[] ReadTexturePixels(Texture tex)
        {
            try
            {
                var prev = RenderTexture.active;
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                readable.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                byte[] png = readable.EncodeToPNG();
                UnityEngine.Object.Destroy(readable);
                return png;
            }
            catch (Exception ex)
            {
                Melon<MeshVaultPlugin>.Logger.Warning($"[GlbExporter] Texture read failed: {ex.Message}");
                return null;
            }
        }

        private static string BuildJson(
            List<MeshInfo> meshes, List<AccessorInfo> accessors, List<BufferViewInfo> bufferViews,
            List<MaterialInfo> materials, List<int> textureViewIndices, int binSize)
        {
            var sb = new StringBuilder();
            sb.Append("{");

            sb.Append("\"asset\":{\"version\":\"2.0\",\"generator\":\"MeshVault\"},");

            sb.Append("\"scene\":0,");
            sb.Append("\"scenes\":[{\"nodes\":[");
            for (int i = 0; i < meshes.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(i);
            }
            sb.Append("]}],");

            sb.Append("\"nodes\":[");
            for (int i = 0; i < meshes.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"{{\"name\":\"{JsonEscape(meshes[i].Name)}\",\"mesh\":{i}}}");
            }
            sb.Append("],");

            sb.Append("\"meshes\":[");
            for (int i = 0; i < meshes.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var m = meshes[i];
                sb.Append($"{{\"name\":\"{JsonEscape(m.Name)}\",\"primitives\":[{{");
                sb.Append($"\"attributes\":{{\"POSITION\":{m.PosAcc},\"NORMAL\":{m.NormAcc},\"TEXCOORD_0\":{m.UvAcc}}},");
                sb.Append($"\"indices\":{m.IdxAcc}");
                if (m.MaterialIdx >= 0)
                    sb.Append($",\"material\":{m.MaterialIdx}");
                sb.Append("}]}");
            }
            sb.Append("],");

            sb.Append("\"accessors\":[");
            for (int i = 0; i < accessors.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var a = accessors[i];
                sb.Append($"{{\"bufferView\":{a.ViewIdx},\"componentType\":{a.CompType},\"count\":{a.Count},\"type\":\"{a.Type}\"");
                if (a.Min != null)
                    sb.Append($",\"min\":[{a.Min[0]:G9},{a.Min[1]:G9},{a.Min[2]:G9}],\"max\":[{a.Max[0]:G9},{a.Max[1]:G9},{a.Max[2]:G9}]");
                sb.Append("}");
            }
            sb.Append("],");

            sb.Append("\"bufferViews\":[");
            for (int i = 0; i < bufferViews.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var bv = bufferViews[i];
                sb.Append($"{{\"buffer\":0,\"byteOffset\":{bv.Offset},\"byteLength\":{bv.Length}");
                if (bv.Target > 0)
                    sb.Append($",\"target\":{bv.Target}");
                sb.Append("}");
            }
            sb.Append("],");

            sb.Append($"\"buffers\":[{{\"byteLength\":{binSize}}}],");

            sb.Append("\"materials\":[");
            for (int i = 0; i < materials.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var mt = materials[i];
                sb.Append($"{{\"name\":\"{JsonEscape(mt.Name)}\",\"pbrMetallicRoughness\":{{");
                sb.Append($"\"baseColorFactor\":[{mt.Color.r:G6},{mt.Color.g:G6},{mt.Color.b:G6},{mt.Color.a:G6}],");
                sb.Append($"\"metallicFactor\":{mt.Metallic:G6},");
                sb.Append($"\"roughnessFactor\":{mt.Roughness:G6}");
                if (mt.TextureIndex >= 0)
                    sb.Append($",\"baseColorTexture\":{{\"index\":{mt.TextureIndex}}}");
                sb.Append("}");
                sb.Append(",\"doubleSided\":true");
                sb.Append("}");
            }
            sb.Append("]");

            if (textureViewIndices.Count > 0)
            {
                sb.Append(",\"textures\":[");
                for (int i = 0; i < textureViewIndices.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append($"{{\"source\":{i},\"sampler\":0}}");
                }
                sb.Append("],\"images\":[");
                for (int i = 0; i < textureViewIndices.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append($"{{\"bufferView\":{textureViewIndices[i]},\"mimeType\":\"image/png\"}}");
                }
                sb.Append("],\"samplers\":[{\"magFilter\":9729,\"minFilter\":9987,\"wrapS\":10497,\"wrapT\":10497}]");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static void WriteGlb(string jsonStr, byte[] binData, string outputPath)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonStr);
            int jsonPadding = (4 - (jsonBytes.Length % 4)) % 4;
            int paddedJsonLen = jsonBytes.Length + jsonPadding;
            int binPadding = (4 - (binData.Length % 4)) % 4;
            int paddedBinLen = binData.Length + binPadding;
            uint totalLength = (uint)(12 + 8 + paddedJsonLen + 8 + paddedBinLen);

            using (var fs = File.Create(outputPath))
            using (var w = new BinaryWriter(fs))
            {
                w.Write(GLB_MAGIC);
                w.Write(GLB_VERSION);
                w.Write(totalLength);

                w.Write((uint)paddedJsonLen);
                w.Write(CHUNK_TYPE_JSON);
                w.Write(jsonBytes);
                for (int i = 0; i < jsonPadding; i++) w.Write((byte)0x20);

                w.Write((uint)paddedBinLen);
                w.Write(CHUNK_TYPE_BIN);
                w.Write(binData);
                for (int i = 0; i < binPadding; i++) w.Write((byte)0x00);
            }
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        private class BufferViewInfo
        {
            public int Offset, Length, Target;
        }

        private class AccessorInfo
        {
            public int ViewIdx, CompType, Count;
            public string Type;
            public float[] Min, Max;
        }

        private class MeshInfo
        {
            public string Name;
            public int PosAcc, NormAcc, UvAcc, IdxAcc, MaterialIdx;
        }

        internal class MaterialInfo
        {
            public string Name;
            public Color Color = Color.white;
            public float Metallic;
            public float Roughness = 0.5f;
            public int TextureIndex = -1;
        }
    }
}
#endif
