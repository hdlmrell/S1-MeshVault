using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using MelonLoader;
using UnityEngine;

namespace MeshVault
{
    /// <summary>
    /// Public API for MeshVault — query, spawn, and manage mesh entries from a shared JSON database.
    /// </summary>
    public static class MeshVaultAPI
    {
        private const string EmbeddedResourceName = "MeshVault.Resources.MeshDatabase.json";
        private const string URPLitShader = "Universal Render Pipeline/Lit";
        private const string URPSimpleLitShader = "Universal Render Pipeline/Simple Lit";
        private const string StandardShader = "Standard";
        private const string WorldspaceUVShaderTag = "WorldspaceUV";
        private const string ShaderPropBaseMap = "_BaseMap";
        private const string ShaderPropBaseColor = "_BaseColor";
        private const string ShaderPropMetallic = "_Metallic";
        private const string ShaderPropSmoothness = "_Smoothness";
        private const float DefaultMetallic = 0f;
        private const float DefaultSmoothness = 0.1f;
        private const float BakedSmoothness = 0f;

        internal static readonly float[] DefaultColor = { 1f, 1f, 1f, 1f };

        private static Dictionary<string, MeshEntry> _cache;
        private static bool _loaded;

        private static readonly Dictionary<string, Material> _materialCache = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Texture> _textureCache = new Dictionary<string, Texture>();

        private static readonly HashSet<string> _materialBlacklist = new HashSet<string>
        {
            "SM_AC_Impostor",
            "filing cabinet mat",
            "mastermat1",
            "pallet rack mat",
            "pallet mat",
            "PolygonGangWarfare_Material_01_A",
            "PropsMat",
            "small bin mat",
            "rubbish bin mat"
        };

        private static string DiskPath =>
            Path.Combine(Application.dataPath, "..", "UserData", "MeshVault", "MeshDatabase.json");

        private static string DataDir =>
            Path.Combine(Application.dataPath, "..", "UserData", "MeshVault");

        // ═══════════════════════════════════════════════════════════════
        // Initialization
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes the mesh database. Idempotent — safe to call multiple times.
        /// Loads from disk file first, falls back to embedded resource.
        /// </summary>
        public static void Init()
        {
            if (_loaded) return;
            _cache = new Dictionary<string, MeshEntry>();

            // Ensure data directory exists
            try
            {
                string dir = Path.GetFullPath(DataDir);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Melon<MeshVaultPlugin>.Logger.Error($"Failed to create data directory: {ex.Message}");
            }

            // Try disk file first
            string diskPath = Path.GetFullPath(DiskPath);
            if (File.Exists(diskPath))
            {
                try
                {
                    string json = File.ReadAllText(diskPath);
                    JsonParser.ParseDatabase(json, _cache);
                    Melon<MeshVaultPlugin>.Logger.Msg(
                        $"Loaded {_cache.Count} mesh entries from disk: {diskPath}");
                    _loaded = true;
                    return;
                }
                catch (Exception ex)
                {
                    Melon<MeshVaultPlugin>.Logger.Error(
                        $"Failed to load from disk: {ex.Message}");
                }
            }

            // Embedded resource fallback
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (stream != null)
                    {
                        byte[] data = new byte[stream.Length];
                        stream.Read(data, 0, data.Length);
                        string json = Encoding.UTF8.GetString(data);
                        JsonParser.ParseDatabase(json, _cache);
                        Melon<MeshVaultPlugin>.Logger.Msg(
                            $"Loaded {_cache.Count} mesh entries from embedded resource");
                    }
                    else
                    {
                        Melon<MeshVaultPlugin>.Logger.Msg(
                            "No MeshDatabase.json found (disk or embedded) — starting with empty cache");
                    }
                }
            }
            catch (Exception ex)
            {
                Melon<MeshVaultPlugin>.Logger.Error(
                    $"Failed to load from embedded resource: {ex.Message}");
            }
            _loaded = true;
        }

        // ═══════════════════════════════════════════════════════════════
        // Query API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns true if a mesh entry with the given ID exists in the database.
        /// </summary>
        public static bool HasMesh(string id)
        {
            EnsureLoaded();
            return _cache.ContainsKey(id);
        }

        /// <summary>
        /// Returns the mesh entry for the given ID, or null if not found.
        /// </summary>
        public static MeshEntry GetMesh(string id)
        {
            EnsureLoaded();
            return _cache.TryGetValue(id, out var entry) ? entry : null;
        }

        /// <summary>
        /// Returns an array of all mesh IDs in the database.
        /// </summary>
        public static string[] ListMeshes()
        {
            EnsureLoaded();
            var keys = new string[_cache.Count];
            _cache.Keys.CopyTo(keys, 0);
            return keys;
        }

        /// <summary>
        /// Returns the full mesh entry dictionary. Intended for debug/extraction tools.
        /// </summary>
        public static Dictionary<string, MeshEntry> GetAllEntries()
        {
            EnsureLoaded();
            return _cache;
        }

        /// <summary>
        /// Scans all database entries and returns a dictionary mapping every unique material name
        /// to its representative RGBA color, excluding blacklisted mesh-specific materials.
        /// </summary>
        public static Dictionary<string, float[]> BuildMaterialCatalog()
        {
            EnsureLoaded();
            var catalog = new Dictionary<string, float[]>();

            foreach (var kvp in _cache)
            {
                var entry = kvp.Value;

                if (!string.IsNullOrEmpty(entry.MaterialName)
                    && !catalog.ContainsKey(entry.MaterialName)
                    && !_materialBlacklist.Contains(entry.MaterialName))
                {
                    catalog[entry.MaterialName] = entry.Color ?? DefaultColor;
                }

                if (entry.SubMeshMaterialNames != null)
                {
                    for (int i = 0; i < entry.SubMeshMaterialNames.Length; i++)
                    {
                        string matName = entry.SubMeshMaterialNames[i];
                        if (string.IsNullOrEmpty(matName) || catalog.ContainsKey(matName)
                            || _materialBlacklist.Contains(matName))
                            continue;

                        float[] color = (entry.SubMeshColorTints != null && i < entry.SubMeshColorTints.Length
                                         && entry.SubMeshColorTints[i] != null)
                            ? entry.SubMeshColorTints[i]
                            : entry.Color ?? DefaultColor;
                        catalog[matName] = color;
                    }
                }

                if (entry.ChildMeshes != null)
                {
                    foreach (var child in entry.ChildMeshes)
                    {
                        if (!string.IsNullOrEmpty(child.MaterialName)
                            && !catalog.ContainsKey(child.MaterialName)
                            && !_materialBlacklist.Contains(child.MaterialName))
                        {
                            catalog[child.MaterialName] = child.Color ?? DefaultColor;
                        }
                    }
                }
            }

            return catalog;
        }

        // ═══════════════════════════════════════════════════════════════
        // Debug tool support
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Adds or replaces a mesh entry in the runtime cache. Used by extraction tools to inject entries.
        /// </summary>
        public static void SetEntry(string id, MeshEntry entry)
        {
            EnsureLoaded();
            _cache[id] = entry;
        }

        /// <summary>
        /// Clears the mesh cache and all material/texture caches, forcing a reload on next access.
        /// </summary>
        public static void InvalidateCache()
        {
            _cache = null;
            _loaded = false;
            _materialCache.Clear();
            _textureCache.Clear();
        }

        private static void EnsureLoaded()
        {
            if (!_loaded) Init();
        }

        // ═══════════════════════════════════════════════════════════════
        // Spawning
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Spawns a mesh entry as a GameObject with MeshFilter, MeshRenderer, MeshCollider, and child meshes.
        /// </summary>
        /// <param name="id">The mesh entry ID from the database.</param>
        /// <param name="position">World position for the spawned object.</param>
        /// <param name="rotation">World rotation for the spawned object.</param>
        /// <param name="parent">Optional parent transform.</param>
        /// <param name="namePrefix">Prefix for the GameObject name.</param>
        /// <param name="materialOverrides">Optional per-slot material name overrides.
        /// Indices 0..N-1 map to submeshes, N..N+M-1 to child meshes.
        /// Null entries keep the default material.</param>
        /// <returns>The spawned GameObject, or null if the entry was not found.</returns>
        public static GameObject Spawn(string id, Vector3 position, Quaternion rotation,
            Transform parent = null, string namePrefix = "MeshVault",
            string[] materialOverrides = null, Color?[] colorOverrides = null)
        {
            var entry = GetMesh(id);
            if (entry == null)
            {
                Melon<MeshVaultPlugin>.Logger.Warning($"Entry \"{id}\" not found");
                return null;
            }

            // Build mesh
            var mesh = new Mesh();
            mesh.name = $"MV_{id}";
            mesh.vertices = entry.Vertices;
            mesh.normals = entry.Normals;
            mesh.uv = entry.UVs;

            // Multi-submesh or single?
            Material[] materials;
            if (entry.SubMeshTriCounts != null && entry.SubMeshTriCounts.Length > 0)
            {
                int subMeshCount = entry.SubMeshTriCounts.Length;
                mesh.subMeshCount = subMeshCount;

                int offset = 0;
                for (int s = 0; s < subMeshCount; s++)
                {
                    int count = entry.SubMeshTriCounts[s];
                    var subTris = new int[count];
                    Array.Copy(entry.Triangles, offset, subTris, 0, count);
                    mesh.SetTriangles(subTris, s);
                    offset += count;
                }

                materials = new Material[subMeshCount];
                for (int s = 0; s < subMeshCount; s++)
                {
                    // Material override takes priority
                    if (materialOverrides != null && s < materialOverrides.Length
                        && materialOverrides[s] != null)
                    {
                        var overrideMat = FindSceneMaterial(materialOverrides[s]);
                        if (overrideMat != null)
                        {
                            materials[s] = overrideMat;
                            continue;
                        }
                    }

                    string shaderName = (entry.SubMeshShaderNames != null && s < entry.SubMeshShaderNames.Length)
                        ? entry.SubMeshShaderNames[s] : "";
                    bool isWorldspaceUV = shaderName.Contains(WorldspaceUVShaderTag);

                    if (isWorldspaceUV)
                    {
                        string texName = (entry.SubMeshTextureNames != null && s < entry.SubMeshTextureNames.Length
                            && !string.IsNullOrEmpty(entry.SubMeshTextureNames[s]))
                            ? entry.SubMeshTextureNames[s] : entry.TextureName;
                        float[] colorTint = (entry.SubMeshColorTints != null && s < entry.SubMeshColorTints.Length
                            && entry.SubMeshColorTints[s] != null)
                            ? entry.SubMeshColorTints[s] : entry.BakedColorTint;

                        if (!string.IsNullOrEmpty(texName))
                        {
                            var bakedMat = CreateBakedMaterial(texName, colorTint);
                            if (bakedMat != null)
                            {
                                materials[s] = bakedMat;
                                continue;
                            }
                        }
                    }

                    string matName = (entry.SubMeshMaterialNames != null && s < entry.SubMeshMaterialNames.Length)
                        ? entry.SubMeshMaterialNames[s] : entry.MaterialName;
                    materials[s] = FindSceneMaterial(matName) ?? CreateFallbackMaterial(entry);
                }
            }
            else
            {
                // Legacy single-material path
                mesh.triangles = entry.Triangles;

                // Material override for single-material entries (index 0)
                Material singleOverride = null;
                if (materialOverrides != null && materialOverrides.Length > 0
                    && materialOverrides[0] != null)
                {
                    singleOverride = FindSceneMaterial(materialOverrides[0]);
                }

                if (singleOverride != null)
                {
                    materials = new[] { singleOverride };
                }
                else if (!string.IsNullOrEmpty(entry.TextureName))
                {
                    var bakedMat = CreateBakedMaterial(entry.TextureName, entry.BakedColorTint);
                    materials = new[] { bakedMat ?? CreateFallbackMaterial(entry) };
                }
                else
                {
                    var sceneMat = FindSceneMaterial(entry.MaterialName);
                    materials = new[] { sceneMat ?? CreateFallbackMaterial(entry) };
                }
            }

            mesh.RecalculateBounds();

            // Create GameObject
            var go = new GameObject($"{namePrefix}_{id}");
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;

            go.transform.position = position;
            go.transform.rotation = rotation;

            if (parent != null)
                go.transform.SetParent(parent, true);

            // Spawn child meshes
            if (entry.ChildMeshes != null)
            {
                for (int c = 0; c < entry.ChildMeshes.Length; c++)
                {
                    var child = entry.ChildMeshes[c];
                    if (child.Vertices == null || child.Vertices.Length == 0) continue;

                    var childMesh = new Mesh();
                    childMesh.name = $"MV_{id}_child{c}";
                    childMesh.vertices = child.Vertices;
                    childMesh.normals = child.Normals;
                    childMesh.uv = child.UVs;
                    childMesh.triangles = child.Triangles;
                    childMesh.RecalculateBounds();

                    var childGO = new GameObject($"child_{c}");
                    childGO.transform.SetParent(go.transform, false);
                    var childMF = childGO.AddComponent<MeshFilter>();
                    childMF.sharedMesh = childMesh;
                    var childMC = childGO.AddComponent<MeshCollider>();
                    childMC.sharedMesh = childMesh;
                    var childMR = childGO.AddComponent<MeshRenderer>();

                    // Child mesh material override (index continues after submeshes)
                    int overrideIdx = (entry.SubMeshTriCounts != null && entry.SubMeshTriCounts.Length > 0)
                        ? entry.SubMeshTriCounts.Length + c
                        : 1 + c;
                    Material childOverride = null;
                    if (materialOverrides != null && overrideIdx < materialOverrides.Length
                        && materialOverrides[overrideIdx] != null)
                    {
                        childOverride = FindSceneMaterial(materialOverrides[overrideIdx]);
                    }

                    if (childOverride != null)
                    {
                        childMR.sharedMaterial = childOverride;
                    }
                    else if (FindSceneMaterial(child.MaterialName) is Material sceneMat)
                    {
                        childMR.sharedMaterial = sceneMat;
                    }
                    else
                    {
                        var fallbackShader = Shader.Find(URPLitShader)
                                          ?? Shader.Find(StandardShader);
                        var fallbackMat = new Material(fallbackShader);
                        var col = child.Color ?? DefaultColor;
                        fallbackMat.color = new UnityEngine.Color(col[0], col[1], col[2], col[3]);
                        if (fallbackMat.HasProperty(ShaderPropBaseColor))
                            fallbackMat.SetColor(ShaderPropBaseColor, fallbackMat.color);
                        if (fallbackMat.HasProperty(ShaderPropMetallic))
                            fallbackMat.SetFloat(ShaderPropMetallic, DefaultMetallic);
                        if (fallbackMat.HasProperty(ShaderPropSmoothness))
                            fallbackMat.SetFloat(ShaderPropSmoothness, DefaultSmoothness);
                        childMR.sharedMaterial = fallbackMat;
                    }
                }
            }

            // Apply color overrides — instantiate material per slot to avoid mutating shared materials
            if (colorOverrides != null)
            {
                var mainMR = go.GetComponent<MeshRenderer>();
                if (mainMR != null)
                {
                    var mats = mainMR.sharedMaterials;
                    for (int i = 0; i < mats.Length && i < colorOverrides.Length; i++)
                    {
                        if (!colorOverrides[i].HasValue) continue;
                        mats[i] = new Material(mats[i]);
                        mats[i].color = colorOverrides[i].Value;
                        if (mats[i].HasProperty(ShaderPropBaseColor))
                            mats[i].SetColor(ShaderPropBaseColor, colorOverrides[i].Value);
                    }
                    mainMR.sharedMaterials = mats;
                }

                int mainSlots = (entry.SubMeshTriCounts != null && entry.SubMeshTriCounts.Length > 0)
                    ? entry.SubMeshTriCounts.Length : 1;
                if (entry.ChildMeshes != null)
                {
                    for (int c = 0; c < entry.ChildMeshes.Length; c++)
                    {
                        int idx = mainSlots + c;
                        if (idx >= colorOverrides.Length || !colorOverrides[idx].HasValue) continue;
                        var childMR = go.transform.GetChild(c)?.GetComponent<MeshRenderer>();
                        if (childMR == null) continue;
                        var mat = new Material(childMR.sharedMaterial);
                        mat.color = colorOverrides[idx].Value;
                        if (mat.HasProperty(ShaderPropBaseColor))
                            mat.SetColor(ShaderPropBaseColor, colorOverrides[idx].Value);
                        childMR.sharedMaterial = mat;
                    }
                }
            }

#if DEBUG
            Melon<MeshVaultPlugin>.Logger.Msg(
                $"Spawned \"{id}\" at {position} ({materials.Length} material(s))");
#endif
            return go;
        }

        // ═══════════════════════════════════════════════════════════════
        // Material helpers
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Searches all loaded MeshRenderers for a material with the given name. Results are cached.
        /// </summary>
        public static Material FindSceneMaterial(string matName)
        {
            if (string.IsNullOrEmpty(matName)) return null;

            if (_materialCache.TryGetValue(matName, out var cached) && cached != null)
                return cached;

            var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                foreach (var m in mats)
                {
                    if (m != null && m.name == matName)
                    {
                        _materialCache[matName] = m;
                        return m;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Searches all loaded Texture2D assets for a texture with the given name. Results are cached.
        /// </summary>
        public static Texture FindSceneTexture(string texName)
        {
            if (string.IsNullOrEmpty(texName)) return null;

            if (_textureCache.TryGetValue(texName, out var cached) && cached != null)
                return cached;

            var allTextures = Resources.FindObjectsOfTypeAll<Texture2D>();
            foreach (var t in allTextures)
            {
                if (t != null && t.name == texName)
                {
                    _textureCache[texName] = t;
                    return t;
                }
            }
            return null;
        }

        private static Material CreateBakedMaterial(string textureName, float[] colorTint)
        {
            var tex = FindSceneTexture(textureName);
            if (tex == null) return null;

            var shader = Shader.Find(URPLitShader)
                      ?? Shader.Find(URPSimpleLitShader)
                      ?? Shader.Find(StandardShader);
            var mat = new Material(shader);

            if (mat.HasProperty(ShaderPropBaseMap))
                mat.SetTexture(ShaderPropBaseMap, tex);
            mat.mainTexture = tex;

            if (colorTint != null && colorTint.Length >= 4)
            {
                var tint = new UnityEngine.Color(colorTint[0], colorTint[1], colorTint[2], colorTint[3]);
                mat.color = tint;
                if (mat.HasProperty(ShaderPropBaseColor))
                    mat.SetColor(ShaderPropBaseColor, tint);
            }

            if (mat.HasProperty(ShaderPropMetallic))
                mat.SetFloat(ShaderPropMetallic, DefaultMetallic);
            if (mat.HasProperty(ShaderPropSmoothness))
                mat.SetFloat(ShaderPropSmoothness, BakedSmoothness);

            return mat;
        }

        private static Material CreateFallbackMaterial(MeshEntry entry)
        {
            var shader = Shader.Find(URPLitShader)
                      ?? Shader.Find(URPSimpleLitShader)
                      ?? Shader.Find(StandardShader);
            var mat = new Material(shader);

            var c = entry.Color ?? DefaultColor;
            mat.color = new UnityEngine.Color(c[0], c[1], c[2], c[3]);
            if (mat.HasProperty(ShaderPropBaseColor))
                mat.SetColor(ShaderPropBaseColor, mat.color);
            if (mat.HasProperty(ShaderPropMetallic))
                mat.SetFloat(ShaderPropMetallic, DefaultMetallic);
            if (mat.HasProperty(ShaderPropSmoothness))
                mat.SetFloat(ShaderPropSmoothness, DefaultSmoothness);

            return mat;
        }

#if DEBUG
        // ═══════════════════════════════════════════════════════════════
        // Write / CRUD API (Debug builds only)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Serializes the entire in-memory database to <c>UserData/MeshVault/MeshDatabase.json</c>.
        /// </summary>
        public static void WriteDatabase()
        {
            EnsureLoaded();
            var db = _cache;
            var savedCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                string path = Path.GetFullPath(DiskPath);
                string dir = Path.GetDirectoryName(path);
                Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("{");

                int entryIdx = 0;
                foreach (var kvp in db)
                {
                    string id = kvp.Key;
                    var e = kvp.Value;

                    sb.Append($"  \"{JsonEscape(id)}\": {{");
                    sb.AppendLine();

                    // Vertices
                    sb.Append("    \"vertices\": [");
                    for (int i = 0; i < e.Vertices.Length; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append($"[{e.Vertices[i].x},{e.Vertices[i].y},{e.Vertices[i].z}]");
                    }
                    sb.AppendLine("],");

                    // Normals
                    sb.Append("    \"normals\": [");
                    for (int i = 0; i < e.Normals.Length; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append($"[{e.Normals[i].x},{e.Normals[i].y},{e.Normals[i].z}]");
                    }
                    sb.AppendLine("],");

                    // UVs
                    sb.Append("    \"uvs\": [");
                    for (int i = 0; i < e.UVs.Length; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append($"[{e.UVs[i].x},{e.UVs[i].y}]");
                    }
                    sb.AppendLine("],");

                    // Triangles
                    sb.Append("    \"triangles\": [");
                    for (int i = 0; i < e.Triangles.Length; i++)
                    {
                        if (i > 0) sb.Append(",");
                        sb.Append(e.Triangles[i]);
                    }
                    sb.AppendLine("],");

                    sb.AppendLine($"    \"materialName\": \"{JsonEscape(e.MaterialName)}\",");
                    sb.AppendLine($"    \"shaderName\": \"{JsonEscape(e.ShaderName)}\",");
                    sb.AppendLine($"    \"color\": [{e.Color[0]},{e.Color[1]},{e.Color[2]},{e.Color[3]}],");
                    sb.AppendLine($"    \"boundsCenter\": [{e.BoundsCenter[0]},{e.BoundsCenter[1]},{e.BoundsCenter[2]}],");
                    sb.AppendLine($"    \"boundsSize\": [{e.BoundsSize[0]},{e.BoundsSize[1]},{e.BoundsSize[2]}],");

                    // Multi-submesh data
                    if (e.SubMeshTriCounts != null && e.SubMeshTriCounts.Length > 0)
                    {
                        sb.Append("    \"subMeshTriCounts\": [");
                        for (int i = 0; i < e.SubMeshTriCounts.Length; i++)
                        {
                            if (i > 0) sb.Append(",");
                            sb.Append(e.SubMeshTriCounts[i]);
                        }
                        sb.AppendLine("],");

                        sb.Append("    \"subMeshMaterialNames\": [");
                        for (int i = 0; i < e.SubMeshMaterialNames.Length; i++)
                        {
                            if (i > 0) sb.Append(",");
                            sb.Append($"\"{JsonEscape(e.SubMeshMaterialNames[i])}\"");
                        }
                        sb.AppendLine("],");

                        sb.Append("    \"subMeshShaderNames\": [");
                        for (int i = 0; i < e.SubMeshShaderNames.Length; i++)
                        {
                            if (i > 0) sb.Append(",");
                            sb.Append($"\"{JsonEscape(e.SubMeshShaderNames[i])}\"");
                        }
                        sb.AppendLine("],");

                        if (e.SubMeshTextureNames != null)
                        {
                            sb.Append("    \"subMeshTextureNames\": [");
                            for (int i = 0; i < e.SubMeshTextureNames.Length; i++)
                            {
                                if (i > 0) sb.Append(",");
                                sb.Append($"\"{JsonEscape(e.SubMeshTextureNames[i] ?? "")}\"");
                            }
                            sb.AppendLine("],");
                        }

                        if (e.SubMeshColorTints != null)
                        {
                            sb.Append("    \"subMeshColorTints\": [");
                            for (int i = 0; i < e.SubMeshColorTints.Length; i++)
                            {
                                if (i > 0) sb.Append(",");
                                var t = e.SubMeshColorTints[i];
                                if (t != null && t.Length >= 4)
                                    sb.Append($"[{t[0]},{t[1]},{t[2]},{t[3]}]");
                                else
                                    sb.Append("[1,1,1,1]");
                            }
                            sb.AppendLine("],");
                        }
                    }
                    else
                    {
                        sb.AppendLine("    \"subMeshTriCounts\": [],");
                    }

                    // Legacy baked texture name
                    bool hasColorTint = e.BakedColorTint != null && e.BakedColorTint.Length >= 4;
                    bool hasChildren = e.ChildMeshes != null && e.ChildMeshes.Length > 0;
                    sb.Append($"    \"textureName\": \"{JsonEscape(e.TextureName ?? "")}\"");
                    if (hasColorTint || hasChildren) sb.AppendLine(","); else sb.AppendLine();

                    if (hasColorTint)
                    {
                        sb.Append($"    \"bakedColorTint\": [{e.BakedColorTint[0]},{e.BakedColorTint[1]},{e.BakedColorTint[2]},{e.BakedColorTint[3]}]");
                        if (hasChildren) sb.AppendLine(","); else sb.AppendLine();
                    }

                    // Child meshes
                    if (hasChildren)
                    {
                        sb.AppendLine("    \"childMeshes\": [");
                        for (int cm = 0; cm < e.ChildMeshes.Length; cm++)
                        {
                            var child = e.ChildMeshes[cm];
                            sb.AppendLine("      {");

                            sb.Append("        \"vertices\": [");
                            for (int i = 0; i < child.Vertices.Length; i++)
                            {
                                if (i > 0) sb.Append(",");
                                sb.Append($"[{child.Vertices[i].x},{child.Vertices[i].y},{child.Vertices[i].z}]");
                            }
                            sb.AppendLine("],");

                            sb.Append("        \"normals\": [");
                            for (int i = 0; i < child.Normals.Length; i++)
                            {
                                if (i > 0) sb.Append(",");
                                sb.Append($"[{child.Normals[i].x},{child.Normals[i].y},{child.Normals[i].z}]");
                            }
                            sb.AppendLine("],");

                            sb.Append("        \"uvs\": [");
                            for (int i = 0; i < child.UVs.Length; i++)
                            {
                                if (i > 0) sb.Append(",");
                                sb.Append($"[{child.UVs[i].x},{child.UVs[i].y}]");
                            }
                            sb.AppendLine("],");

                            sb.Append("        \"triangles\": [");
                            for (int i = 0; i < child.Triangles.Length; i++)
                            {
                                if (i > 0) sb.Append(",");
                                sb.Append(child.Triangles[i]);
                            }
                            sb.AppendLine("],");

                            sb.AppendLine($"        \"materialName\": \"{JsonEscape(child.MaterialName)}\",");
                            sb.AppendLine($"        \"shaderName\": \"{JsonEscape(child.ShaderName)}\",");
                            sb.AppendLine($"        \"color\": [{child.Color[0]},{child.Color[1]},{child.Color[2]},{child.Color[3]}]");

                            sb.AppendLine(cm < e.ChildMeshes.Length - 1 ? "      }," : "      }");
                        }
                        sb.AppendLine("    ]");
                    }

                    entryIdx++;
                    sb.Append(entryIdx < db.Count ? "  }," : "  }");
                    sb.AppendLine();
                }

                sb.AppendLine("}");

                File.WriteAllText(path, sb.ToString());
                Melon<MeshVaultPlugin>.Logger.Msg($"Wrote {db.Count} entries to {path}");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = savedCulture;
            }
        }

        /// <summary>
        /// High-level save: builds a <see cref="MeshEntry"/> from raw Unity data, adds it to the cache,
        /// and writes the database to disk. If an entry with the same ID already exists, a numeric
        /// suffix is appended (e.g. "bench_1", "bench_2") to avoid overwriting.
        /// </summary>
        /// <returns>The actual ID the entry was saved under (may differ from <paramref name="id"/> if deduplicated).</returns>
        public static string SaveMesh(string id, Vector3[] verts, Vector3[] normals,
            Vector2[] uvs, int[][] subMeshTris, Material[] subMeshMats, Bounds bounds,
            string[] perSubTexNames = null, float[][] perSubColorTints = null,
            ChildMeshEntry[] childMeshes = null)
        {
            EnsureLoaded();
            id = DeduplicateId(id);

            var flatTris = new List<int>();
            var triCounts = new int[subMeshTris.Length];
            var matNames = new string[subMeshMats.Length];
            var shaderNames = new string[subMeshMats.Length];

            for (int s = 0; s < subMeshTris.Length; s++)
            {
                flatTris.AddRange(subMeshTris[s]);
                triCounts[s] = subMeshTris[s].Length;
                matNames[s] = (subMeshMats[s] != null) ? subMeshMats[s].name : StandardShader;
                shaderNames[s] = (subMeshMats[s] != null && subMeshMats[s].shader != null)
                    ? subMeshMats[s].shader.name : StandardShader;
            }

            var firstMat = subMeshMats.Length > 0 ? subMeshMats[0] : null;

            var entry = new MeshEntry
            {
                Vertices = verts,
                Normals = normals,
                UVs = uvs,
                Triangles = flatTris.ToArray(),
                MaterialName = firstMat != null ? firstMat.name : StandardShader,
                ShaderName = firstMat != null && firstMat.shader != null ? firstMat.shader.name : StandardShader,
                Color = firstMat != null
                    ? new[] { firstMat.color.r, firstMat.color.g, firstMat.color.b, firstMat.color.a }
                    : new[] { 1f, 1f, 1f, 1f },
                BoundsCenter = new[] { bounds.center.x, bounds.center.y, bounds.center.z },
                BoundsSize = new[] { bounds.size.x, bounds.size.y, bounds.size.z },
                SubMeshTriCounts = triCounts,
                SubMeshMaterialNames = matNames,
                SubMeshShaderNames = shaderNames,
                SubMeshTextureNames = perSubTexNames,
                SubMeshColorTints = perSubColorTints,
                TextureName = FirstNonNull(perSubTexNames),
                BakedColorTint = FirstNonNull(perSubColorTints),
                ChildMeshes = childMeshes
            };

            SetEntry(id, entry);
            WriteDatabase();
            return id;
        }

        /// <summary>
        /// Returns a unique ID based on the given candidate. If the ID is already taken,
        /// appends _1, _2, etc. until a free slot is found.
        /// </summary>
        private static string DeduplicateId(string id)
        {
            if (!_cache.ContainsKey(id)) return id;

            int suffix = 1;
            string candidate;
            do
            {
                candidate = $"{id}_{suffix}";
                suffix++;
            } while (_cache.ContainsKey(candidate));

            Melon<MeshVaultPlugin>.Logger.Warning(
                $"ID \"{id}\" already exists — saving as \"{candidate}\"");
            return candidate;
        }

        /// <summary>
        /// Removes a mesh entry from the in-memory cache. Call <see cref="WriteDatabase"/> to persist.
        /// </summary>
        /// <returns>True if the entry was found and removed.</returns>
        public static bool RemoveEntry(string id)
        {
            EnsureLoaded();
            return _cache.Remove(id);
        }

        /// <summary>
        /// Renames a mesh entry in the in-memory cache. Call <see cref="WriteDatabase"/> to persist.
        /// </summary>
        /// <returns>True if the rename succeeded. False if oldId doesn't exist or newId is already taken.</returns>
        public static bool RenameEntry(string oldId, string newId)
        {
            EnsureLoaded();
            if (!_cache.TryGetValue(oldId, out var entry)) return false;
            if (_cache.ContainsKey(newId)) return false;
            _cache.Remove(oldId);
            _cache[newId] = entry;
            return true;
        }

        /// <summary>
        /// Escapes a string for safe inclusion in JSON output.
        /// </summary>
        public static string JsonEscape(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>
        /// Returns the first non-null, non-empty string in the array, or null.
        /// </summary>
        public static string FirstNonNull(string[] arr)
        {
            if (arr == null) return null;
            foreach (var s in arr)
                if (!string.IsNullOrEmpty(s)) return s;
            return null;
        }

        /// <summary>
        /// Returns the first non-null float array in the array-of-arrays, or null.
        /// </summary>
        public static float[] FirstNonNull(float[][] arr)
        {
            if (arr == null) return null;
            foreach (var a in arr)
                if (a != null) return a;
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        // Editor Mode API (Debug builds only)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Opens F9 editor mode to position a specific GameObject.
        /// The object moves in realtime — read its transform directly.
        /// <paramref name="onConfirm"/> fires when user presses Enter/Log.
        /// <paramref name="onCancel"/> fires on Del or F9 exit.
        /// Callbacks are one-shot (cleared after firing).
        /// Set <paramref name="useFreecam"/> to false for UI elements or when
        /// the player camera should stay attached (e.g., positioning screen-space objects).
        /// </summary>
        public static void EnterEditorMode(GameObject target, string displayName = null,
            Action onConfirm = null, Action onCancel = null, bool useFreecam = true)
        {
            var placer = Tools.MeshPlacer.Instance;
            if (placer == null || target == null) return;
            placer.OpenEditorForObject(target, displayName, onConfirm, onCancel, useFreecam);
        }

        /// <summary>
        /// Returns true when the F9 editor is active and has an object being positioned.
        /// </summary>
        public static bool IsEditorModeActive =>
            Tools.MeshPlacer.Instance != null &&
            Tools.MeshPlacer.Instance.IsPositioning;
#endif
    }
}
