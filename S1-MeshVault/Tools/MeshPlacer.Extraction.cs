#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

#if IL2CPP
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
#else
using ScheduleOne.DevUtilities;
using ScheduleOne.PlayerScripts;
#endif

namespace MeshVault.Tools
{
    public partial class MeshPlacer
    {
        // ═══════════════════════════════════════════════════════════════
        // Extract mode
        // ═══════════════════════════════════════════════════════════════

        private void StartExtractMode()
        {
            try
            {
                var cam = PlayerSingleton<PlayerCamera>.Instance;
                if (cam == null) { _lastAction = "No camera"; return; }

                var ray = new Ray(cam.transform.position, cam.transform.forward);
                int mask = ~(1 << LayerMask.NameToLayer("Player") | 1 << LayerMask.NameToLayer("NoCollide"));
                if (!Physics.Raycast(ray, out RaycastHit hit, 20f, mask))
                {
                    _lastAction = "Extract: nothing hit";
                    return;
                }

                MeshFilter hitMF = null;
                var searchT = hit.collider.transform;
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Hit collider: \"{hit.collider.gameObject.name}\" hitPoint=({hit.point.x:F2},{hit.point.y:F2},{hit.point.z:F2})");

                for (int depth = 0; depth < 3 && searchT != null; depth++)
                {
                    var mf = searchT.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && !mf.sharedMesh.name.Contains("Combined Mesh"))
                    {
                        hitMF = mf;
                        break;
                    }
                    for (int c = 0; c < searchT.childCount && hitMF == null; c++)
                    {
                        var child = searchT.GetChild(c);
                        var cmf = child.GetComponent<MeshFilter>();
                        if (cmf != null && cmf.sharedMesh != null && !cmf.sharedMesh.name.Contains("Combined Mesh"))
                        {
                            hitMF = cmf;
                            break;
                        }
                    }
                    if (hitMF != null) break;
                    searchT = searchT.parent;
                }

                if (hitMF != null)
                {
                    _extractSourceMF = hitMF;
                    _extractFirstSubMesh = 0;
                    _extractSubMeshCount = hitMF.sharedMesh.subMeshCount;
                    _extractCenter = hitMF.GetComponent<Renderer>()?.bounds.center ?? hit.point;

                    Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Direct extract: \"{hitMF.gameObject.name}\" mesh=\"{hitMF.sharedMesh.name}\" " +
                        $"submeshes={_extractSubMeshCount} verts={hitMF.sharedMesh.vertexCount}");

                    ExecuteExtract();
                    return;
                }

                MeshFilter combinedMF = null;
                var t = hit.collider.transform;
                for (int depth = 0; depth < 20 && t != null; depth++)
                {
                    var mf = t.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.Contains("Combined Mesh"))
                    {
                        combinedMF = mf;
                        break;
                    }

                    for (int c = 0; c < t.childCount && combinedMF == null; c++)
                    {
                        var child = t.GetChild(c);
                        if (child == null) continue;
                        var cmf = child.GetComponent<MeshFilter>();
                        if (cmf != null && cmf.sharedMesh != null && cmf.sharedMesh.name.Contains("Combined Mesh"))
                        {
                            var cmr = child.GetComponent<MeshRenderer>();
                            if (cmr != null && cmr.bounds.Contains(hit.point))
                                combinedMF = cmf;
                            else if (combinedMF == null)
                                combinedMF = cmf;
                        }
                    }
                    if (combinedMF != null) break;
                    t = t.parent;
                }

                if (combinedMF == null)
                {
                    _lastAction = "Extract: no combined mesh found in hierarchy";
                    return;
                }

                _extractSourceMF = combinedMF;
                _extractFirstSubMesh = -1;
                _extractSubMeshCount = 0;
                _extractCenter = hit.point;
                _extractSize = new Vector3(2.11f, 1.09f, 0.91f);
                _extractMode = true;
                _mode = EditMode.Position;
                CreateExtractBox();

                _lastAction = $"AABB Extract: \"{combinedMF.sharedMesh.name}\" ({combinedMF.sharedMesh.vertexCount} verts)";
            }
            catch (Exception ex)
            {
                _lastAction = $"Extract start failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] Extract start failed: {ex}");
            }
        }

        private void StopExtractMode()
        {
            _extractMode = false;
            _extractSourceMF = null;
            _extractSourceRenderers = null;
            _extractNodeName = null;
            DestroyExtractBox();
        }

        private void AdjustExtract(int axis, int sign)
        {
            float step = StepSizes[_stepIndex];
            float delta = step * sign;
            string[] axisNames = { "X", "Y", "Z" };
            switch (_mode)
            {
                case EditMode.Position:
                    _extractCenter[axis] += delta;
                    _lastAction = $"Box Center {axisNames[axis]} {(delta >= 0 ? "+" : "")}{delta} -> {_extractCenter[axis]:F4}";
                    break;
                case EditMode.Scale:
                    _extractSize[axis] = Mathf.Max(0.1f, _extractSize[axis] + delta);
                    _lastAction = $"Box Size {axisNames[axis]} {(delta >= 0 ? "+" : "")}{delta} -> {_extractSize[axis]:F4}";
                    break;
            }
        }

        private void CreateExtractBox()
        {
            DestroyExtractBox();

            _extractBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _extractBox.name = "MV_ExtractBox";

            var col = _extractBox.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);

            var mr = _extractBox.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = new Color(0f, 1f, 0f, 0.2f);
            mr.material = mat;

            UpdateExtractBox();
        }

        private void UpdateExtractBox()
        {
            if (_extractBox == null) return;
            _extractBox.transform.position = _extractCenter;
            _extractBox.transform.localScale = _extractSize;
        }

        private void DestroyExtractBox()
        {
            if (_extractBox != null)
            {
                UnityEngine.Object.Destroy(_extractBox);
                _extractBox = null;
            }
        }

        private void ExecuteExtractBounds(Bounds rendererBounds)
        {
            _lastAction = "Extracting via bounds...";
            try
            {
                var sourceMesh = _extractSourceMF?.sharedMesh;
                if (sourceMesh == null)
                {
                    _lastAction = "Extract: no source mesh";
                    return;
                }

                var sourceMR = _extractSourceMF.GetComponent<MeshRenderer>();
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] BoundsExtract: mesh=\"{sourceMesh.name}\" verts={sourceMesh.vertexCount} submeshes={sourceMesh.subMeshCount}");

                var boundsMin = rendererBounds.min;
                var boundsMax = rendererBounds.max;

                Vector3[] srcVerts;
                Vector3[] srcNormals;
                Vector2[] srcUVs;
                List<int[]> allSubmeshTris;
#if IL2CPP
                if (!sourceMesh.isReadable)
                {
                    _lastAction = "Extract: mesh not readable";
                    return;
                }
                srcVerts = sourceMesh.vertices;
                srcNormals = sourceMesh.normals;
                srcUVs = sourceMesh.uv;
                allSubmeshTris = new List<int[]>();
                for (int s = 0; s < sourceMesh.subMeshCount; s++)
                    allSubmeshTris.Add(sourceMesh.GetTriangles(s));
#else
                if (sourceMesh.isReadable)
                {
                    srcVerts = sourceMesh.vertices;
                    srcNormals = sourceMesh.normals;
                    srcUVs = sourceMesh.uv;
                    allSubmeshTris = new List<int[]>();
                    for (int s = 0; s < sourceMesh.subMeshCount; s++)
                        allSubmeshTris.Add(sourceMesh.GetTriangles(s));
                }
                else
                {
                    if (!ReadMeshNative(sourceMesh, out srcVerts, out srcNormals, out srcUVs, out allSubmeshTris))
                    {
                        _lastAction = "Extract: couldn't read mesh";
                        return;
                    }
                }
#endif

                var meshTransform = _extractSourceMF.transform;
                int rawHits = 0, xfHits = 0;
                int step = Math.Max(1, srcVerts.Length / 500);
                for (int i = 0; i < srcVerts.Length; i += step)
                {
                    var v = srcVerts[i];
                    if (v.x >= boundsMin.x && v.x <= boundsMax.x &&
                        v.y >= boundsMin.y && v.y <= boundsMax.y &&
                        v.z >= boundsMin.z && v.z <= boundsMax.z)
                        rawHits++;
                    var tv = meshTransform.TransformPoint(v);
                    if (tv.x >= boundsMin.x && tv.x <= boundsMax.x &&
                        tv.y >= boundsMin.y && tv.y <= boundsMax.y &&
                        tv.z >= boundsMin.z && tv.z <= boundsMax.z)
                        xfHits++;
                }
                bool useRaw = rawHits >= xfHits;
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Vertex space: rawHits={rawHits} xfHits={xfHits} -> {(useRaw ? "raw" : "TransformPoint")}");

                var vertMap = new Dictionary<int, int>();
                var newVerts = new List<Vector3>();
                var newNormals = new List<Vector3>();
                var newUVs = new List<Vector2>();
                var subMeshTriLists = new List<List<int>>();
                var subMeshMats = new List<Material>();
                var matchedSubmeshIndices = new List<int>();

                var combinedMats = sourceMR?.sharedMaterials;
                int combinedMatCount = combinedMats?.Length ?? 0;

                for (int s = 0; s < allSubmeshTris.Count; s++)
                {
                    var subTris = allSubmeshTris[s];
                    var newSubTris = new List<int>();

                    for (int i = 0; i < subTris.Length; i += 3)
                    {
                        int i0 = subTris[i], i1 = subTris[i + 1], i2 = subTris[i + 2];
                        if (i0 >= srcVerts.Length || i1 >= srcVerts.Length || i2 >= srcVerts.Length) continue;

                        var rawCenter = (srcVerts[i0] + srcVerts[i1] + srcVerts[i2]) / 3f;
                        var worldCenter = useRaw ? rawCenter : meshTransform.TransformPoint(rawCenter);

                        if (worldCenter.x < boundsMin.x || worldCenter.x > boundsMax.x ||
                            worldCenter.y < boundsMin.y || worldCenter.y > boundsMax.y ||
                            worldCenter.z < boundsMin.z || worldCenter.z > boundsMax.z)
                            continue;

                        foreach (int idx in new[] { i0, i1, i2 })
                        {
                            if (!vertMap.ContainsKey(idx))
                            {
                                vertMap[idx] = newVerts.Count;
                                var worldVert = useRaw ? srcVerts[idx] : meshTransform.TransformPoint(srcVerts[idx]);
                                newVerts.Add(worldVert - rendererBounds.center);
                                newNormals.Add(srcNormals != null && idx < srcNormals.Length ? srcNormals[idx] : Vector3.up);
                                newUVs.Add(srcUVs != null && idx < srcUVs.Length ? srcUVs[idx] : Vector2.zero);
                            }
                            newSubTris.Add(vertMap[idx]);
                        }
                    }

                    if (newSubTris.Count > 0)
                    {
                        subMeshTriLists.Add(newSubTris);
                        matchedSubmeshIndices.Add(s);
                    }
                }

                int firstMatchIdx = matchedSubmeshIndices.Count > 0 ? matchedSubmeshIndices[0] : 0;
                for (int gs = 0; gs < matchedSubmeshIndices.Count; gs++)
                {
                    int localIdx = matchedSubmeshIndices[gs] - firstMatchIdx;
                    Material subMat = (localIdx >= 0 && localIdx < combinedMatCount)
                        ? combinedMats[localIdx] : null;
                    subMeshMats.Add(subMat);
                }

                int totalTris = 0;
                foreach (var st in subMeshTriLists) totalTris += st.Count;

                if (newVerts.Count == 0)
                {
                    _lastAction = "Extract: 0 triangles in bounds — try enlarging box";
                    return;
                }

                string extractName = _extractNodeName ?? _previewSourceName ?? "extracted";
                foreach (char c in Path.GetInvalidFileNameChars())
                    extractName = extractName.Replace(c, '_');
                if (extractName.Length > 50) extractName = extractName.Substring(0, 50);

                var perSubTexNames = new string[subMeshTriLists.Count];
                var perSubColorTints = new float[subMeshTriLists.Count][];
                string bakedTexName = null;
                float bakedTiling = 0.5f;

                var worldspaceVerts = new HashSet<int>();
                for (int gs = 0; gs < subMeshTriLists.Count; gs++)
                {
                    var mat = subMeshMats[gs];
                    if (mat != null && mat.shader != null && mat.shader.name.Contains("WorldspaceUV"))
                    {
                        var tex = mat.GetTexture("_DiffuseTexture");
                        if (tex != null)
                        {
                            perSubTexNames[gs] = tex.name;
                            if (bakedTexName == null)
                            {
                                bakedTexName = tex.name;
                                if (mat.HasProperty("_Tiling")) bakedTiling = mat.GetFloat("_Tiling");
                            }
                        }
                        var col = mat.HasProperty("_Color") ? mat.GetColor("_Color")
                            : new UnityEngine.Color(1, 1, 1, 1);
                        perSubColorTints[gs] = new[] { col.r, col.g, col.b, col.a };

                        foreach (int idx in subMeshTriLists[gs])
                            worldspaceVerts.Add(idx);
                    }
                }

                var finalUVs = new List<Vector2>(newUVs);
                if (bakedTexName != null && worldspaceVerts.Count > 0)
                {
                    var vertProj = new Dictionary<int, int>();
                    int origVertCount = newVerts.Count;

                    for (int gs = 0; gs < subMeshTriLists.Count; gs++)
                    {
                        if (gs >= subMeshMats.Count || subMeshMats[gs] == null ||
                            subMeshMats[gs].shader == null || !subMeshMats[gs].shader.name.Contains("WorldspaceUV"))
                            continue;

                        var tris = subMeshTriLists[gs];
                        for (int t = 0; t < tris.Count; t += 3)
                        {
                            int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];

                            var v0 = newVerts[i0] + rendererBounds.center;
                            var v1 = newVerts[i1] + rendererBounds.center;
                            var v2 = newVerts[i2] + rendererBounds.center;
                            var fn = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                            float fax = Mathf.Abs(fn.x), fay = Mathf.Abs(fn.y), faz = Mathf.Abs(fn.z);
                            int proj = (fay >= fax && fay >= faz) ? 1 : (faz >= fax) ? 2 : 3;

                            for (int vi = 0; vi < 3; vi++)
                            {
                                int idx = tris[t + vi];
                                int existingProj;
                                if (vertProj.TryGetValue(idx, out existingProj) && existingProj != proj)
                                {
                                    int newIdx = newVerts.Count;
                                    newVerts.Add(newVerts[idx]);
                                    newNormals.Add(newNormals[idx]);
                                    finalUVs.Add(Vector2.zero);
                                    tris[t + vi] = newIdx;
                                    idx = newIdx;
                                }
                                vertProj[idx] = proj;

                                var wp = newVerts[idx] + rendererBounds.center;
                                Vector2 uv;
                                if (proj == 1) uv = new Vector2(wp.x, wp.z);
                                else if (proj == 2) uv = new Vector2(wp.x, wp.y);
                                else uv = new Vector2(wp.z, wp.y);
                                finalUVs[idx] = uv * bakedTiling;
                            }
                        }
                    }
                }

                // Extract non-combined child meshes
                ChildMeshEntry[] childMeshEntries = null;
                if (_extractSourceRenderers != null)
                {
                    var childList = new List<ChildMeshEntry>();
                    foreach (var childMR in _extractSourceRenderers)
                    {
                        if (childMR == null) continue;
                        var childMF = childMR.GetComponent<MeshFilter>();
                        if (childMF == null || childMF.sharedMesh == null) continue;
                        var childMesh = childMF.sharedMesh;
                        if (childMesh.name.Contains("Combined Mesh")) continue;
                        if (childMesh.name == "Cube" && childMesh.vertexCount == 24) continue;

                        Vector3[] cVerts, cNormals;
                        Vector2[] cUVs;
                        int[] cTris;

                        if (childMesh.isReadable)
                        {
                            cVerts = childMesh.vertices;
                            cNormals = childMesh.normals;
                            cUVs = childMesh.uv;
                            cTris = childMesh.triangles;
                        }
                        else
                        {
#if IL2CPP
                            Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] Child mesh \"{childMesh.name}\" on \"{childMR.gameObject.name}\" not readable, skipping");
                            continue;
#else
                            List<int[]> childSubTris;
                            if (!ReadMeshNative(childMesh, out cVerts, out cNormals, out cUVs, out childSubTris))
                            {
                                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] Child mesh \"{childMesh.name}\" on \"{childMR.gameObject.name}\" read failed, skipping");
                                continue;
                            }
                            var flatChildTris = new List<int>();
                            foreach (var st in childSubTris) flatChildTris.AddRange(st);
                            cTris = flatChildTris.ToArray();
#endif
                        }

                        var worldVerts = new Vector3[cVerts.Length];
                        var worldNormals = new Vector3[cNormals != null ? cNormals.Length : cVerts.Length];
                        for (int i = 0; i < cVerts.Length; i++)
                            worldVerts[i] = childMR.transform.TransformPoint(cVerts[i]) - rendererBounds.center;
                        for (int i = 0; i < worldNormals.Length; i++)
                            worldNormals[i] = cNormals != null && i < cNormals.Length
                                ? childMR.transform.TransformDirection(cNormals[i]) : Vector3.up;

                        var cMat = childMR.sharedMaterial;
                        childList.Add(new ChildMeshEntry
                        {
                            Vertices = worldVerts,
                            Normals = worldNormals,
                            UVs = cUVs ?? new Vector2[cVerts.Length],
                            Triangles = cTris,
                            MaterialName = cMat?.name ?? "Standard",
                            ShaderName = cMat?.shader?.name ?? "Standard",
                            Color = cMat != null
                                ? new[] { cMat.color.r, cMat.color.g, cMat.color.b, cMat.color.a }
                                : new[] { 1f, 1f, 1f, 1f }
                        });
                    }
                    if (childList.Count > 0)
                        childMeshEntries = childList.ToArray();
                }

                string dbName = extractName.ToLowerInvariant().Replace(" ", "_");
                var subMeshTrisArr = new int[subMeshTriLists.Count][];
                for (int s = 0; s < subMeshTriLists.Count; s++)
                    subMeshTrisArr[s] = subMeshTriLists[s].ToArray();

                string savedId = MeshVaultAPI.SaveMesh(dbName, newVerts.ToArray(), newNormals.ToArray(),
                    finalUVs.ToArray(), subMeshTrisArr, subMeshMats.ToArray(), rendererBounds,
                    perSubTexNames, perSubColorTints, childMeshEntries);

                int triCount = totalTris / 3;
                _lastAction = $"Extracted {triCount} tris, {newVerts.Count} verts -> DB:\"{savedId}\"";
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Extracted: {triCount} tris, {newVerts.Count} verts -> DB:\"{savedId}\"");
                StopExtractMode();
            }
            catch (Exception ex)
            {
                _lastAction = $"Extract failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] BoundsExtract failed: {ex}");
            }
        }

        private void ExecuteExtract()
        {
            _lastAction = "Executing extract...";
            try
            {
                if (_extractSourceMF == null || _extractSourceMF.sharedMesh == null)
                {
                    _lastAction = "Extract: source mesh lost";
                    return;
                }

                var sourceMesh = _extractSourceMF.sharedMesh;

                bool useSubMeshSlice = _extractFirstSubMesh >= 0 && _extractSubMeshCount > 0;

                var newVerts = new List<Vector3>();
                var newNormals = new List<Vector3>();
                var newUVs = new List<Vector2>();
                var newTris = new List<int>();
                var worldPositions = new List<Vector3>();

                if (useSubMeshSlice)
                {
                    Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Submesh slice: first={_extractFirstSubMesh} count={_extractSubMeshCount}");

#if IL2CPP
                    if (!sourceMesh.isReadable)
                    {
                        _lastAction = "Extract: mesh not readable (use Mono build)";
                        return;
                    }
                    ExtractSubMeshSliceReadable(sourceMesh, newVerts, newNormals, newUVs, newTris, worldPositions);
#else
                    if (sourceMesh.isReadable)
                        ExtractSubMeshSliceReadable(sourceMesh, newVerts, newNormals, newUVs, newTris, worldPositions);
                    else
                        ExtractSubMeshSliceMeshData(sourceMesh, newVerts, newNormals, newUVs, newTris, worldPositions);
#endif
                }
                else if (_extractFirstSubMesh == -1)
                {
                    var aabbBounds = new Bounds(_extractCenter, _extractSize);
                    ExecuteExtractBounds(aabbBounds);
                    return;
                }
                else
                {
                    _lastAction = "Extract: no extraction mode set";
                    return;
                }

                if (newVerts.Count == 0)
                {
                    _lastAction = "Extract: 0 triangles — submesh range may be wrong";
                    return;
                }

                var sourceMR = _extractSourceMF.GetComponent<MeshRenderer>();
                var matArray = sourceMR != null ? sourceMR.sharedMaterials : null;

                string eName = _extractNodeName ?? _extractSourceMF.gameObject.name;
                foreach (char c in Path.GetInvalidFileNameChars())
                    eName = eName.Replace(c, '_');
                if (eName.Length > 50) eName = eName.Substring(0, 50);

                var subMeshTriLists = new List<int[]>();
                var subMeshMats = new List<Material>();
                int trisOffset = 0;
                var allTrisArr = newTris.ToArray();

                for (int s = _extractFirstSubMesh;
                     s < _extractFirstSubMesh + _extractSubMeshCount && s < sourceMesh.subMeshCount;
                     s++)
                {
                    int srcTriCount = sourceMesh.GetTriangles(s).Length;
                    if (trisOffset + srcTriCount > allTrisArr.Length) break;

                    var subArr = new int[srcTriCount];
                    Array.Copy(allTrisArr, trisOffset, subArr, 0, srcTriCount);
                    subMeshTriLists.Add(subArr);
                    trisOffset += srcTriCount;

                    Material subMat = (matArray != null && s < matArray.Length) ? matArray[s] : null;
                    subMeshMats.Add(subMat);
                }

                if (subMeshTriLists.Count == 0)
                {
                    subMeshTriLists.Add(allTrisArr);
                    Material fallbackMat = (matArray != null && matArray.Length > 0) ? matArray[0] : null;
                    subMeshMats.Add(fallbackMat);
                }

                var perSubTexNames2 = new string[subMeshMats.Count];
                var perSubColorTints2 = new float[subMeshMats.Count][];
                string bakedTexName = null;
                float bakedTiling = 0.5f;
                for (int gs = 0; gs < subMeshMats.Count; gs++)
                {
                    var m = subMeshMats[gs];
                    if (m != null && m.shader != null && m.shader.name.Contains("WorldspaceUV"))
                    {
                        if (bakedTexName == null) DumpMaterialProperties(m);
                        var tex = m.GetTexture("_DiffuseTexture");
                        if (tex != null)
                        {
                            perSubTexNames2[gs] = tex.name;
                            if (bakedTexName == null)
                            {
                                bakedTexName = tex.name;
                                if (m.HasProperty("_Tiling")) bakedTiling = m.GetFloat("_Tiling");
                            }
                        }
                        var col = m.HasProperty("_Color") ? m.GetColor("_Color")
                            : new UnityEngine.Color(1, 1, 1, 1);
                        perSubColorTints2[gs] = new[] { col.r, col.g, col.b, col.a };
                    }
                }

                var finalUVs = newUVs;
                if (bakedTexName != null && worldPositions.Count == newVerts.Count)
                {
                    finalUVs = new List<Vector2>(newVerts.Count);
                    for (int i = 0; i < newVerts.Count; i++)
                    {
                        var wp = worldPositions[i];
                        var n = i < newNormals.Count ? newNormals[i] : Vector3.up;
                        float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                        Vector2 uv;
                        if (ay >= ax && ay >= az)
                            uv = new Vector2(wp.x, wp.z);
                        else if (az >= ax)
                            uv = new Vector2(wp.x, wp.y);
                        else
                            uv = new Vector2(wp.z, wp.y);
                        finalUVs.Add(uv * bakedTiling);
                    }
                }

                var bounds = sourceMR != null ? sourceMR.bounds : new Bounds();
                if (sourceMR == null)
                {
                    var tempMesh = new Mesh { vertices = newVerts.ToArray() };
                    tempMesh.RecalculateBounds();
                    bounds = tempMesh.bounds;
                }
                string dbName = eName.ToLowerInvariant().Replace(" ", "_");
                string savedId = MeshVaultAPI.SaveMesh(dbName, newVerts.ToArray(), newNormals.ToArray(),
                    finalUVs.ToArray(), subMeshTriLists.ToArray(), subMeshMats.ToArray(), bounds,
                    perSubTexNames2, perSubColorTints2);

                StopExtractMode();

                int triCount = newTris.Count / 3;
                _lastAction = $"Extracted {triCount} tris, {newVerts.Count} verts -> DB:\"{savedId}\"";
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Extracted mesh: {triCount} tris, {newVerts.Count} verts -> DB:\"{savedId}\"");
            }
            catch (Exception ex)
            {
                _lastAction = $"Extract failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] Extract failed: {ex}");
            }
        }

        private void ExtractSubMeshSliceReadable(Mesh mesh,
            List<Vector3> outVerts, List<Vector3> outNormals, List<Vector2> outUVs, List<int> outTris,
            List<Vector3> outWorldPositions = null)
        {
            var srcVerts = mesh.vertices;
            var srcNormals = mesh.normals;
            var srcUVs = mesh.uv;
            var vertMap = new Dictionary<int, int>();

            var allExtractedIndices = new List<int>();
            for (int s = _extractFirstSubMesh; s < _extractFirstSubMesh + _extractSubMeshCount && s < mesh.subMeshCount; s++)
                allExtractedIndices.AddRange(mesh.GetTriangles(s));

            var centroid = Vector3.zero;
            var uniqueVerts = new HashSet<int>(allExtractedIndices);
            foreach (int idx in uniqueVerts)
                centroid += srcVerts[idx];
            if (uniqueVerts.Count > 0)
                centroid /= uniqueVerts.Count;

            _extractCenter = centroid;

            for (int s = _extractFirstSubMesh; s < _extractFirstSubMesh + _extractSubMeshCount && s < mesh.subMeshCount; s++)
            {
                int[] indices = mesh.GetTriangles(s);

                for (int i = 0; i < indices.Length; i++)
                {
                    int idx = indices[i];
                    if (!vertMap.ContainsKey(idx))
                    {
                        vertMap[idx] = outVerts.Count;
                        outVerts.Add(srcVerts[idx] - centroid);
                        outWorldPositions?.Add(srcVerts[idx]);
                        outNormals.Add(srcNormals != null && idx < srcNormals.Length ? srcNormals[idx] : Vector3.up);
                        outUVs.Add(srcUVs != null && idx < srcUVs.Length ? srcUVs[idx] : Vector2.zero);
                    }
                    outTris.Add(vertMap[idx]);
                }
            }
        }

#if !IL2CPP
        private static bool ReadMeshNative(Mesh mesh,
            out Vector3[] verts, out Vector3[] normals, out Vector2[] uvs, out List<int[]> submeshTris)
        {
            verts = null; normals = null; uvs = null; submeshTris = null;

            int vertCount = mesh.vertexCount;
            if (vertCount == 0) return false;

            int stride = mesh.GetVertexBufferStride(0);
            int posOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
            bool hasNormal = mesh.HasVertexAttribute(VertexAttribute.Normal);
            int normOffset = hasNormal ? mesh.GetVertexAttributeOffset(VertexAttribute.Normal) : 0;
            bool hasUV = mesh.HasVertexAttribute(VertexAttribute.TexCoord0);
            int uvOffset = hasUV ? mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0) : 0;

            GraphicsBuffer vb = mesh.GetVertexBuffer(0);
            if (vb == null) return false;
            byte[] rawVerts = new byte[vb.count * vb.stride];
            vb.GetData(rawVerts);
            vb.Release();

            verts = new Vector3[vertCount];
            normals = new Vector3[vertCount];
            uvs = new Vector2[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                int b = i * stride;
                verts[i] = new Vector3(
                    BitConverter.ToSingle(rawVerts, b + posOffset),
                    BitConverter.ToSingle(rawVerts, b + posOffset + 4),
                    BitConverter.ToSingle(rawVerts, b + posOffset + 8));
                normals[i] = hasNormal
                    ? new Vector3(
                        BitConverter.ToSingle(rawVerts, b + normOffset),
                        BitConverter.ToSingle(rawVerts, b + normOffset + 4),
                        BitConverter.ToSingle(rawVerts, b + normOffset + 8))
                    : Vector3.up;
                uvs[i] = hasUV
                    ? new Vector2(
                        BitConverter.ToSingle(rawVerts, b + uvOffset),
                        BitConverter.ToSingle(rawVerts, b + uvOffset + 4))
                    : Vector2.zero;
            }

            GraphicsBuffer ib = mesh.GetIndexBuffer();
            if (ib == null) return false;
            bool use32 = mesh.indexFormat == IndexFormat.UInt32;
            int bytesPerIdx = use32 ? 4 : 2;
            int totalIndices = ib.count;
            byte[] rawIdx = new byte[totalIndices * ib.stride];
            ib.GetData(rawIdx);
            ib.Release();

            int subCount = mesh.subMeshCount;
            var subDescs = new SubMeshDescriptor[subCount];
            for (int s = 0; s < subCount; s++)
                subDescs[s] = mesh.GetSubMesh(s);

            submeshTris = new List<int[]>(subCount);
            for (int s = 0; s < subCount; s++)
            {
                int count = (int)subDescs[s].indexCount;
                int start = (int)subDescs[s].indexStart;
                var tris = new int[count];
                for (int j = 0; j < count; j++)
                {
                    int byteOff = (start + j) * bytesPerIdx;
                    tris[j] = use32
                        ? BitConverter.ToInt32(rawIdx, byteOff)
                        : (int)BitConverter.ToUInt16(rawIdx, byteOff);
                }
                submeshTris.Add(tris);
            }

            return true;
        }

        private void ExtractSubMeshSliceMeshData(Mesh mesh,
            List<Vector3> outVerts, List<Vector3> outNormals, List<Vector2> outUVs, List<int> outTris,
            List<Vector3> outWorldPositions = null)
        {
            Vector3[] allVerts, allNormals;
            Vector2[] allUVs;
            List<int[]> allSubTris;
            if (!ReadMeshNative(mesh, out allVerts, out allNormals, out allUVs, out allSubTris))
                throw new Exception("ReadMeshNative failed for submesh slice");

            var allIndices = new List<int>();
            int endSub = Math.Min(_extractFirstSubMesh + _extractSubMeshCount, allSubTris.Count);
            for (int s = _extractFirstSubMesh; s < endSub; s++)
                allIndices.AddRange(allSubTris[s]);

            var uniqueVerts = new HashSet<int>(allIndices);
            var centroid = Vector3.zero;
            foreach (int idx in uniqueVerts)
                centroid += allVerts[idx];
            if (uniqueVerts.Count > 0)
                centroid /= uniqueVerts.Count;

            _extractCenter = centroid;

            var vertMap = new Dictionary<int, int>();
            for (int i = 0; i < allIndices.Count; i++)
            {
                int idx = allIndices[i];
                if (!vertMap.ContainsKey(idx))
                {
                    vertMap[idx] = outVerts.Count;
                    outVerts.Add(allVerts[idx] - centroid);
                    outWorldPositions?.Add(allVerts[idx]);
                    outNormals.Add(allNormals[idx]);
                    outUVs.Add(allUVs[idx]);
                }
                outTris.Add(vertMap[idx]);
            }
        }
#endif
    }
}
#endif
