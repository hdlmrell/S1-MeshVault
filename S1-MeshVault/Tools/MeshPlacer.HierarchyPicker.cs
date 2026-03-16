#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

#if IL2CPP
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppTMPro;
using GameCanvasScaler = Il2CppScheduleOne.UI.CanvasScaler;
using GameInput = Il2CppScheduleOne.GameInput;
#else
using ScheduleOne.DevUtilities;
using ScheduleOne.PlayerScripts;
using TMPro;
using GameCanvasScaler = ScheduleOne.UI.CanvasScaler;
using GameInput = ScheduleOne.GameInput;
#endif

namespace MeshVault.Tools
{
    public partial class MeshPlacer
    {
        private void RaycastInspect()
        {
            try
            {
                var cam = PlayerSingleton<PlayerCamera>.Instance;
                if (cam == null) { _lastAction = "No camera"; return; }

                var ray = new Ray(cam.transform.position, cam.transform.forward);
                int mask = ~(1 << LayerMask.NameToLayer("Player") | 1 << LayerMask.NameToLayer("NoCollide"));
                if (!Physics.Raycast(ray, out RaycastHit hit, 20f, mask))
                {
                    _lastAction = "Raycast: nothing hit";
                    return;
                }

                var hitGo = hit.collider.gameObject;
                var output = $"[RAYCAST] Hit: \"{hitGo.name}\" layer={LayerMask.LayerToName(hitGo.layer)} pos={hit.point:F2}\n";

                var t = hitGo.transform;
                int depth = 0;
                string hierarchy = "";
                while (t != null && depth < 8)
                {
                    string indent = new string(' ', depth * 2);
                    string flags = "";
                    if (t.gameObject.isStatic) flags += " [Static]";

                    var mf = t.GetComponent<MeshFilter>();
                    var mr = t.GetComponent<MeshRenderer>();

                    if (mf != null && mf.sharedMesh != null)
                        flags += $" MeshFilter={{mesh=\"{mf.sharedMesh.name}\" verts={mf.sharedMesh.vertexCount}}}";
                    if (mr != null)
                    {
                        var mats = mr.sharedMaterials;
                        if (mats != null && mats.Length > 0)
                        {
                            var matNames = new List<string>();
                            for (int i = 0; i < mats.Length; i++)
                                if (mats[i] != null) matNames.Add(mats[i].name);
                            flags += $" Materials=[{string.Join(", ", matNames)}]";
                        }
                    }

                    hierarchy += $"{indent}{t.gameObject.name}{flags}\n";
                    t = t.parent;
                    depth++;
                }

                output += hierarchy;

                var parentObj = hitGo.transform.parent;
                if (parentObj != null)
                {
                    output += $"[RAYCAST] Children of \"{parentObj.gameObject.name}\":\n";
                    for (int c = 0; c < parentObj.childCount; c++)
                    {
                        var child = parentObj.GetChild(c);
                        if (child == null) continue;
                        string cFlags = "";
                        if (child.gameObject.isStatic) cFlags += " [Static]";

                        var cmf = child.GetComponent<MeshFilter>();
                        var cmr = child.GetComponent<MeshRenderer>();
                        if (cmf != null && cmf.sharedMesh != null)
                            cFlags += $" MeshFilter={{mesh=\"{cmf.sharedMesh.name}\" verts={cmf.sharedMesh.vertexCount}}}";
                        if (cmr != null)
                        {
                            var mats = cmr.sharedMaterials;
                            if (mats != null && mats.Length > 0)
                            {
                                var matNames = new List<string>();
                                for (int m = 0; m < mats.Length; m++)
                                    if (mats[m] != null) matNames.Add(mats[m].name);
                                cFlags += $" Materials=[{string.Join(", ", matNames)}]";
                            }
                        }
                        output += $"  [{c}] \"{child.gameObject.name}\"{cFlags}\n";
                    }
                }

                Melon<MeshVaultPlugin>.Logger.Msg(output);
                GUIUtility.systemCopyBuffer = output;
                _lastAction = $"Inspect: {hitGo.name} (logged + copied)";
            }
            catch (Exception ex)
            {
                _lastAction = $"Inspect failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] Inspect failed: {ex}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Grab — raycast and reposition an existing object
        // ═══════════════════════════════════════════════════════════════

        private void GrabObject()
        {
            try
            {
                var cam = PlayerSingleton<PlayerCamera>.Instance;
                if (cam == null) { _lastAction = "No camera"; return; }

                var ray = new Ray(cam.transform.position, cam.transform.forward);
                int mask = ~(1 << LayerMask.NameToLayer("Player") | 1 << LayerMask.NameToLayer("NoCollide"));
                if (!Physics.Raycast(ray, out RaycastHit hit, 20f, mask))
                {
                    _lastAction = "Grab: nothing hit";
                    return;
                }

                var t = hit.collider.transform;
                GameObject target = null;
                for (int i = 0; i < 10 && t != null; i++)
                {
                    if (t.gameObject.name.StartsWith("OTC_") || t.gameObject.name.StartsWith("MV_"))
                    {
                        target = t.gameObject;
                        break;
                    }
                    t = t.parent;
                }

                if (target == null)
                    target = hit.collider.gameObject;

                _preview = target;
                _previewSourceName = target.name;
                _previewDbId = null;
                _previewPosition = target.transform.position;
                _previewRotation = target.transform.rotation.eulerAngles;
                _previewScale = target.transform.localScale;
                _previewIsLiveObject = true;
                _mode = EditMode.Position;

                string parentInfo = target.transform.parent != null
                    ? $" parent=\"{target.transform.parent.gameObject.name}\""
                    : "";
                _lastAction = $"Grabbed \"{target.name}\"{parentInfo} — numpad to adjust, Enter to log";
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Grabbed \"{target.name}\" at {target.transform.position}{parentInfo}");
            }
            catch (Exception ex)
            {
                _lastAction = $"Grab failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] Grab failed: {ex}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Copy — clone a mesh object and enter positioner
        // ═══════════════════════════════════════════════════════════════

        private void CopyObject()
        {
            try
            {
                var cam = PlayerSingleton<PlayerCamera>.Instance;
                if (cam == null) { _lastAction = "No camera"; return; }

                var ray = new Ray(cam.transform.position, cam.transform.forward);
                int mask = ~(1 << LayerMask.NameToLayer("Player") | 1 << LayerMask.NameToLayer("NoCollide"));
                if (!Physics.Raycast(ray, out RaycastHit hit, 20f, mask))
                {
                    _lastAction = "Copy: nothing hit";
                    return;
                }

                var t = hit.collider.transform;
                GameObject target = null;
                for (int i = 0; i < 10 && t != null; i++)
                {
                    if (t.gameObject.name.StartsWith("OTC_") || t.gameObject.name.StartsWith("MV_"))
                    {
                        target = t.gameObject;
                        break;
                    }
                    t = t.parent;
                }

                if (target == null)
                {
                    _lastAction = "Copy: no OTC_/MV_ object found";
                    return;
                }

                string meshId = null;
                string name = target.name;
                foreach (var prefix in new[] { "OTC_Furniture_", "OTC_TestSpawn_", "MV_TestSpawn_", "MeshVault_" })
                {
                    if (name.StartsWith(prefix))
                    {
                        meshId = name.Substring(prefix.Length);
                        break;
                    }
                }

                if (meshId == null)
                {
                    _lastAction = $"Copy: can't parse mesh ID from \"{name}\"";
                    return;
                }

                var player = PlayerSingleton<PlayerMovement>.Instance;
                var forward = player != null ? player.transform.forward : Vector3.forward;
                forward.y = 0;
                forward.Normalize();
                var spawnPos = target.transform.position + forward * 1f;
                var spawnRot = target.transform.rotation;

                var go = MeshVaultAPI.Spawn(meshId, spawnPos, spawnRot, namePrefix: "MV_TestSpawn");
                if (go == null)
                {
                    _lastAction = $"Copy: failed to spawn \"{meshId}\"";
                    return;
                }

                _testSpawns.Add(go);
                _preview = go;
                _previewSourceName = meshId;
                _previewDbId = meshId;
                _previewPosition = spawnPos;
                _previewRotation = spawnRot.eulerAngles;
                _previewScale = Vector3.one;
                _previewIsLiveObject = false;
                _mode = EditMode.Position;

                _lastAction = $"Copied \"{meshId}\" — numpad to adjust, Enter to log, Del to cancel";
            }
            catch (Exception ex)
            {
                _lastAction = $"Copy failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] Copy failed: {ex}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Hierarchy picker
        // ═══════════════════════════════════════════════════════════════

        private void ShowHierarchyPicker(RaycastHit hit)
        {
            try
            {
            CloseHierarchyPanel();

            var hitT = hit.collider.transform;

            var ancestors = new List<Transform>();
            var t = hitT;
            for (int i = 0; i < 3 && t != null; i++)
            {
                if (t != hitT && t.childCount > 50)
                    break;
                ancestors.Add(t);
                t = t.parent;
            }
            ancestors.Reverse();

            const int maxTotalNodes = 30;
            _hierarchyNodes = new List<(Transform, int, bool)>();
            int baseDepth = 0;
            foreach (var ancestor in ancestors)
            {
                if (_hierarchyNodes.Count >= maxTotalNodes) break;

                bool hasMesh = HasUsableMesh(ancestor);
                _hierarchyNodes.Add((ancestor, baseDepth, hasMesh));

                int childCount = Mathf.Min(ancestor.childCount, 15);
                for (int c = 0; c < childCount && _hierarchyNodes.Count < maxTotalNodes; c++)
                {
                    var child = ancestor.GetChild(c);
                    if (child == null || ancestors.Contains(child)) continue;
                    bool childHasMesh = HasUsableMesh(child);
                    _hierarchyNodes.Add((child, baseDepth + 1, childHasMesh));
                }
                baseDepth++;
            }

            // Build UI panel
            _hierarchyPanel = new GameObject("MV_HierarchyPanel");
            var rootCanvas = _hierarchyPanel.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 91;

            var scaler = _hierarchyPanel.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _hierarchyPanel.AddComponent<GameCanvasScaler>();
            _hierarchyPanel.AddComponent<GraphicRaycaster>();

            var panelObj = UIHelper.Panel("HierarchyPanel", _hierarchyPanel.transform, new Color(0.1f, 0.1f, 0.12f));
            var panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.01f, 0.05f);
            panelRect.anchorMax = new Vector2(0.36f, 0.95f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            string hitName = hitT.gameObject.name;
            if (hitName.Length > 30) hitName = hitName.Substring(0, 27) + "...";
            var titleTmp = UIHelper.Text("Title", $"<b>Hierarchy</b> — {hitName}", panelObj.transform, 16, TextAlignmentOptions.Center);
            var titleRect = titleTmp.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.anchoredPosition = new Vector2(0, -5);
            titleRect.sizeDelta = new Vector2(0, 28);

            var listContent = UIHelper.ScrollableVerticalList("HierarchyList", panelObj.transform, out ScrollRect scrollRect);
            var scrollRectTransform = scrollRect.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0, 0);
            scrollRectTransform.anchorMax = new Vector2(1, 1);
            scrollRectTransform.offsetMin = new Vector2(8, 40);
            scrollRectTransform.offsetMax = new Vector2(-8, -38);

            var csf = listContent.GetComponent<ContentSizeFitter>();
            if (csf != null)
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var contentRect = listContent.GetComponent<RectTransform>();
            if (contentRect != null)
            {
                contentRect.anchorMin = new Vector2(0, 1);
                contentRect.anchorMax = new Vector2(1, 1);
                contentRect.pivot = new Vector2(0, 1);
                contentRect.sizeDelta = new Vector2(0, contentRect.sizeDelta.y);
                contentRect.offsetMin = new Vector2(0, contentRect.offsetMin.y);
                contentRect.offsetMax = new Vector2(0, contentRect.offsetMax.y);
            }

            scrollRect.horizontal = false;

            var contentLayout = listContent.GetComponent<VerticalLayoutGroup>();
            if (contentLayout != null)
            {
                contentLayout.spacing = 2;
                contentLayout.padding = new RectOffset(4, 4, 2, 2);
                contentLayout.childControlHeight = true;
                contentLayout.childControlWidth = true;
                contentLayout.childForceExpandHeight = false;
                contentLayout.childForceExpandWidth = true;
            }

            for (int i = 0; i < _hierarchyNodes.Count; i++)
            {
                var (node, depth, hasMesh) = _hierarchyNodes[i];
                CreateHierarchyRow(listContent, node, depth, hasMesh);
            }

            var contentRT = listContent.GetComponent<RectTransform>();
            if (contentRT != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);

            var (closeMask, closeBtn, closeLabel) = UIHelper.RoundedButtonWithLabel(
                "CloseBtn", "Close (Del)", panelObj.transform,
                new Color(0.5f, 0.2f, 0.2f), 300, 28, 15, Color.white);
            var closeRect = closeMask.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(0.5f, 0);
            closeRect.anchorMax = new Vector2(0.5f, 0);
            closeRect.pivot = new Vector2(0.5f, 0);
            closeRect.anchoredPosition = new Vector2(0, 6);

            closeBtn.onClick.AddListener(new Action(() =>
            {
                CloseHierarchyPanel();
                _lastAction = "Hierarchy closed";
            }));

            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam != null)
            {
                cam.AddActiveUIElement("MV_MeshPlacer");
                cam.SetCanLook(false);
                cam.FreeMouse();
            }
            GameInput.IsTyping = true;
            _wasTypingFromUs = true;

            _hierarchyPanel.SetActive(true);
            _lastAction = $"Hierarchy: {_hierarchyNodes.Count} nodes";
            }
            catch (Exception ex)
            {
                _lastAction = $"Hierarchy failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] ShowHierarchyPicker failed: {ex}");
                CloseHierarchyPanel();
            }
        }

        private bool HasUsableMesh(Transform t)
        {
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) return true;
            int checkCount = Mathf.Min(t.childCount, 50);
            for (int c = 0; c < checkCount; c++)
            {
                var cmf = t.GetChild(c).GetComponent<MeshFilter>();
                if (cmf != null && cmf.sharedMesh != null) return true;
            }
            return false;
        }

        private bool IsValidMesh(Mesh m) =>
            m != null && m.vertexCount > 24
            && m.name != "Cube"
            && !m.name.Contains("Combined Mesh");

        private bool IsExtractableChildMesh(Mesh m) =>
            m != null && m.vertexCount > 24
            && m.name != "Cube";

        private bool HasValidChildMeshes(Transform t)
        {
            int checkCount = Mathf.Min(t.childCount, 50);
            for (int c = 0; c < checkCount; c++)
            {
                var cmf = t.GetChild(c).GetComponent<MeshFilter>();
                if (cmf != null && IsExtractableChildMesh(cmf.sharedMesh)) return true;
            }
            return false;
        }

        private void CreateHierarchyRow(Transform parent, Transform node, int depth, bool hasMesh)
        {
            var rowObj = new GameObject($"Row_{node.gameObject.name}");
            rowObj.transform.SetParent(parent, false);

            bool validChildren = HasValidChildMeshes(node);
            bool showSave = hasMesh;
            bool showSaveAll = validChildren;
            int btnCount = (showSave ? 1 : 0) + (showSaveAll ? 1 : 0) + (hasMesh ? 1 : 0); // +1 for View

            var rowRT = rowObj.AddComponent<RectTransform>();
            var rowLayout = rowObj.AddComponent<LayoutElement>();
            rowLayout.minHeight = hasMesh ? 44f : 26f;
            rowLayout.preferredHeight = hasMesh ? 44f : 26f;
            rowLayout.flexibleWidth = 1;

            var rowBg = rowObj.AddComponent<Image>();
            rowBg.color = hasMesh ? new Color(0.22f, 0.22f, 0.28f) : new Color(0.16f, 0.16f, 0.2f);

            int leftPad = 8 + depth * 10;
            int btnW = 72;
            int btnGap = 4;
            int rightZone = hasMesh ? (btnW * btnCount + btnGap * (btnCount - 1) + 8) : 4;

            string goName = node.gameObject.name;
            string labelText;
            if (hasMesh)
            {
                var mf = node.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    string meshName = mf.sharedMesh.name;
                    if (meshName.Length > 30) meshName = meshName.Substring(0, 27) + "...";
                    labelText = $"<b>{goName}</b>\n<color=#888><size=80%>{meshName} ({mf.sharedMesh.vertexCount} v)</size></color>";
                }
                else
                {
                    int childMeshes = 0;
                    int checkCount = Mathf.Min(node.childCount, 50);
                    for (int c = 0; c < checkCount; c++)
                    {
                        var cmf = node.GetChild(c).GetComponent<MeshFilter>();
                        if (cmf != null && cmf.sharedMesh != null) childMeshes++;
                    }
                    labelText = $"<b>{goName}</b>\n<color=#888><size=80%>{childMeshes} child mesh{(childMeshes != 1 ? "es" : "")}</size></color>";
                }
            }
            else
            {
                labelText = $"<color=#666>{goName}</color>";
            }

            var nameObj = new GameObject("Name");
            nameObj.transform.SetParent(rowObj.transform, false);
            var nameRect = nameObj.AddComponent<RectTransform>();
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = new Vector2(leftPad, 2);
            nameRect.offsetMax = new Vector2(-rightZone, -2);
            var nameTmp = nameObj.AddComponent<TextMeshProUGUI>();
            nameTmp.text = labelText;
            nameTmp.fontSize = 15;
            nameTmp.color = Color.white;
            nameTmp.alignment = TextAlignmentOptions.Left;
            nameTmp.richText = true;
            UIHelper.SetWrapping(nameTmp, true);
            nameTmp.overflowMode = TextOverflowModes.Truncate;

            if (!hasMesh) return;

            Transform capturedNode = node;
            int btnIndex = 0; // tracks right-to-left button position

            // Save All button (rightmost when present)
            if (showSaveAll)
            {
                int offset = 3 + btnIndex * (btnW + btnGap);
                CreateRowButton(rowObj.transform, "SaveAllBtn", "Save All", btnW,
                    new Color(0.2f, 0.35f, 0.55f), new Color(0.25f, 0.42f, 0.65f), new Color(0.15f, 0.28f, 0.45f),
                    offset, () => OnHierarchyExtractAll(capturedNode));
                btnIndex++;
            }

            // Save button (single mesh)
            if (showSave)
            {
                int offset = 3 + btnIndex * (btnW + btnGap);
                CreateRowButton(rowObj.transform, "ExtractBtn", "Save", btnW,
                    new Color(0.2f, 0.45f, 0.25f), new Color(0.25f, 0.55f, 0.3f), new Color(0.15f, 0.35f, 0.18f),
                    offset, () => OnHierarchyExtract(capturedNode));
                btnIndex++;
            }

            // View button (leftmost)
            {
                int offset = 3 + btnIndex * (btnW + btnGap);
                CreateRowButton(rowObj.transform, "PreviewBtn", "View", btnW,
                    new Color(0.25f, 0.45f, 0.3f), new Color(0.3f, 0.55f, 0.35f), new Color(0.18f, 0.35f, 0.22f),
                    offset, () => OnHierarchyPreview(capturedNode));
            }

            // Hover highlight
            var eventTrigger = rowObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();

            var enterEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            enterEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
            Transform hoverNode = node;
            enterEntry.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(_ => HighlightObject(hoverNode)));
            eventTrigger.triggers.Add(enterEntry);

            var exitEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            exitEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
            exitEntry.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(_ => ClearHighlight()));
            eventTrigger.triggers.Add(exitEntry);
        }

        private void CreateRowButton(Transform parent, string name, string label, int width,
            Color normal, Color highlighted, Color pressed, int rightOffset, Action onClick)
        {
            var btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            var rect = btnObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(1, 0);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 0.5f);
            rect.sizeDelta = new Vector2(width, -4);
            rect.anchoredPosition = new Vector2(-rightOffset, 0);
            var bg = btnObj.AddComponent<Image>();
            bg.color = normal;
            var btn = btnObj.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = pressed;
            btn.colors = colors;

            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(btnObj.transform, false);
            var labelRect = labelObj.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var tmp = labelObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 14;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            btn.onClick.AddListener(onClick);
        }

        private void CloseHierarchyPanel()
        {
            if (_hierarchyPanel != null)
            {
                UnityEngine.Object.Destroy(_hierarchyPanel);
                _hierarchyPanel = null;
            }
            _hierarchyNodes = null;
            ClearHighlight();

            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam != null)
            {
                cam.RemoveActiveUIElement("MV_MeshPlacer");
                cam.SetCanLook(true);
                cam.LockMouse();
            }

            if (_wasTypingFromUs)
            {
                GameInput.IsTyping = false;
                _wasTypingFromUs = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Highlighting
        // ═══════════════════════════════════════════════════════════════

        private GameObject _highlightBox;

        private void HighlightObject(Transform target)
        {
            if (target == null || _highlightedNode == target) return;
            ClearHighlight();

            try
            {
                _highlightedNode = target;

                var renderers = target.GetComponentsInChildren<MeshRenderer>();
                if (renderers == null || renderers.Length == 0) return;

                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                _highlightBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _highlightBox.name = "MV_HighlightBox";
                var col = _highlightBox.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);
                _highlightBox.transform.position = bounds.center;
                _highlightBox.transform.localScale = bounds.size + Vector3.one * 0.02f;

                var boxMR = _highlightBox.GetComponent<MeshRenderer>();
                boxMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                boxMR.receiveShadows = false;
                var mat = new Material(Shader.Find("Sprites/Default"));
                mat.color = new Color(0f, 1f, 1f, 0.15f);
                boxMR.material = mat;
            }
            catch (Exception ex)
            {
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] Highlight failed: {ex.Message}");
            }
        }

        private void ClearHighlight()
        {
            if (_highlightBox != null)
            {
                try { UnityEngine.Object.Destroy(_highlightBox); } catch { }
                _highlightBox = null;
            }
            _highlightedNode = null;
        }

        // ═══════════════════════════════════════════════════════════════
        // Hierarchy actions
        // ═══════════════════════════════════════════════════════════════

        private void OnHierarchyPreview(Transform node)
        {
            try
            {
                CloseHierarchyPanel();
                DestroyPreview();

                var source = node.gameObject;
                _previewSourceTransform = node;
                _previewSourceName = source.name;

                _preview = GameObject.Instantiate(source);
                _preview.name = "MV_MeshPlacer_Preview";

                foreach (var comp in _preview.GetComponentsInChildren<MonoBehaviour>(true))
                    UnityEngine.Object.Destroy(comp);
                foreach (var rb in _preview.GetComponentsInChildren<Rigidbody>(true))
                    UnityEngine.Object.Destroy(rb);
                foreach (var col in _preview.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.Destroy(col);

                _preview.SetActive(true);
                foreach (var child in _preview.GetComponentsInChildren<Transform>(true))
                    child.gameObject.SetActive(true);
                foreach (var renderer in _preview.GetComponentsInChildren<MeshRenderer>(true))
                    renderer.enabled = true;

                var player = PlayerSingleton<PlayerMovement>.Instance;
                if (player != null)
                {
                    var forward = player.transform.forward;
                    forward.y = 0;
                    forward.Normalize();
                    _previewPosition = player.transform.position + forward * PreviewDistance;
                    _previewPosition.y += PreviewYOffset;
                }

                _previewRotation = Vector3.zero;
                _previewScale = Vector3.one;
                _preview.transform.position = _previewPosition;
                _preview.transform.eulerAngles = _previewRotation;
                _preview.transform.localScale = _previewScale;

                int meshCount = _preview.GetComponentsInChildren<MeshRenderer>(true).Length;
                _lastAction = $"Preview: {_previewSourceName} ({meshCount} mesh{(meshCount != 1 ? "es" : "")})";
            }
            catch (Exception ex)
            {
                _lastAction = $"Preview failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] Preview failed: {ex}");
                DestroyPreview();
            }
        }

        /// <summary>
        /// Extract the selected node from the hierarchy.
        /// FIX: Only check the node DIRECTLY for a MeshFilter — do NOT search descendants
        /// for individual meshes. If the node has no direct mesh, fall through to combined
        /// bounds extraction which correctly handles the entire subtree.
        /// </summary>
        private void OnHierarchyExtract(Transform node)
        {
            try
            {
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] OnHierarchyExtract: \"{node.gameObject.name}\"");

                // Only check this node directly — don't step down to children
                var mf = node.GetComponent<MeshFilter>();

                bool hasRealMesh = mf != null && mf.sharedMesh != null
                    && mf.sharedMesh.vertexCount > 24
                    && mf.sharedMesh.name != "Cube"
                    && !mf.sharedMesh.name.Contains("Combined Mesh");

                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] hasRealMesh={hasRealMesh} (direct only)");

                if (hasRealMesh)
                {
                    CloseHierarchyPanel();

                    var mesh = mf.sharedMesh;
                    bool isCombined = mesh.name.Contains("Combined Mesh");

                    if (isCombined)
                    {
                        var mr = mf.GetComponent<MeshRenderer>();
                        if (mr != null)
                        {
                            ExecuteExtractBounds(mr.bounds);
                        }
                        else
                        {
                            _extractSourceMF = mf;
                            _extractFirstSubMesh = -1;
                            _extractSubMeshCount = 0;
                            _extractCenter = node.position;
                            _extractSize = new Vector3(2.11f, 1.09f, 0.91f);
                            _extractMode = true;
                            _mode = EditMode.Position;
                            CreateExtractBox();
                            _lastAction = $"AABB mode: {mesh.name}";
                        }
                    }
                    else
                    {
                        _extractSourceMF = mf;
                        _extractFirstSubMesh = 0;
                        _extractSubMeshCount = mesh.subMeshCount;
                        _extractCenter = mf.GetComponent<Renderer>()?.bounds.center ?? node.position;

                        Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Direct extract: \"{node.gameObject.name}\" mesh=\"{mesh.name}\" " +
                            $"submeshes={mesh.subMeshCount} verts={mesh.vertexCount}");

                        ExecuteExtract();
                    }
                }
                else
                {
                    // No real mesh on this node — search children for combined mesh renderers
                    MeshFilter combinedMF = null;
                    Bounds? combinedBounds = null;

                    var childRenderers = node.GetComponentsInChildren<MeshRenderer>(true);
                    foreach (var childMR in childRenderers)
                    {
                        var childMF = childMR.GetComponent<MeshFilter>();
                        if (childMF == null || childMF.sharedMesh == null) continue;

                        if (combinedMF == null)
                        {
                            combinedMF = childMF;
                            combinedBounds = childMR.bounds;
                        }
                        else
                        {
                            var b = combinedBounds.Value;
                            b.Encapsulate(childMR.bounds);
                            combinedBounds = b;
                        }
                    }

                    if (combinedMF == null)
                    {
                        var searchT = node.parent;
                        for (int d = 0; d < 10 && searchT != null; d++)
                        {
                            var cmf = searchT.GetComponent<MeshFilter>();
                            if (cmf != null && cmf.sharedMesh != null && cmf.sharedMesh.name.Contains("Combined Mesh"))
                            {
                                combinedMF = cmf;
                                break;
                            }
                            searchT = searchT.parent;
                        }
                    }

                    if (combinedMF == null)
                    {
                        _lastAction = "No extractable mesh found";
                        return;
                    }

                    CloseHierarchyPanel();

                    Bounds bounds;
                    if (combinedBounds.HasValue)
                    {
                        bounds = combinedBounds.Value;
                        bounds.size += new Vector3(0.1f, 0.1f, 0.1f);
                    }
                    else
                    {
                        var colliders = node.GetComponentsInChildren<Collider>();
                        if (colliders.Length == 0)
                        {
                            _lastAction = "No bounds to compute extraction region";
                            return;
                        }
                        bounds = colliders[0].bounds;
                        for (int i = 1; i < colliders.Length; i++)
                            bounds.Encapsulate(colliders[i].bounds);
                        bounds.size += new Vector3(0.1f, 0.1f, 0.1f);
                    }

                    _extractSourceMF = combinedMF;
                    _extractFirstSubMesh = -1;
                    _extractSubMeshCount = 0;
                    _extractSourceRenderers = new List<MeshRenderer>(childRenderers);
                    _extractNodeName = node.gameObject.name;
                    _extractCenter = bounds.center;
                    _extractSize = bounds.size;
                    _extractMode = true;
                    _mode = EditMode.Position;
                    CreateExtractBox();

                    _lastAction = $"AABB mode: {node.gameObject.name} — adjust box, Numpad1 to save";
                }
            }
            catch (Exception ex)
            {
                _lastAction = $"Extract failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] OnHierarchyExtract failed: {ex}");
            }
        }

        /// <summary>
        /// Extract a node and all its valid child meshes as a single MeshEntry with ChildMeshEntries.
        /// Handles both direct meshes and Combined Meshes (via bounds-based AABB clipping).
        /// Filters out Cube colliders (24v).
        /// </summary>
        private void OnHierarchyExtractAll(Transform node)
        {
            try
            {
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] OnHierarchyExtractAll: \"{node.gameObject.name}\"");

                // Collect all extractable (MeshFilter, MeshRenderer) pairs from node + direct children
                var candidates = new List<(MeshFilter mf, MeshRenderer mr, bool isCombined)>();

                var directMF = node.GetComponent<MeshFilter>();
                var directMR = node.GetComponent<MeshRenderer>();
                if (directMF != null && directMR != null && IsExtractableChildMesh(directMF.sharedMesh))
                    candidates.Add((directMF, directMR, directMF.sharedMesh.name.Contains("Combined Mesh")));

                int childCount = Mathf.Min(node.childCount, 50);
                for (int c = 0; c < childCount; c++)
                {
                    var child = node.GetChild(c);
                    var cmf = child.GetComponent<MeshFilter>();
                    var cmr = child.GetComponent<MeshRenderer>();
                    if (cmf == null || cmr == null || !IsExtractableChildMesh(cmf.sharedMesh)) continue;
                    candidates.Add((cmf, cmr, cmf.sharedMesh.name.Contains("Combined Mesh")));
                }

                if (candidates.Count == 0)
                {
                    _lastAction = "Save All: no valid meshes found";
                    return;
                }

                CloseHierarchyPanel();

                // Compute combined bounds across all children for world-space centering
                Bounds combinedBounds = candidates[0].mr.bounds;
                for (int i = 1; i < candidates.Count; i++)
                    combinedBounds.Encapsulate(candidates[i].mr.bounds);

                // Extract geometry for each candidate
                var parts = new List<(Vector3[] verts, Vector3[] normals, Vector2[] uvs, int[] tris, Material mat, int vertCount)>();

                foreach (var (mf, mr, isCombined) in candidates)
                {
                    if (isCombined)
                    {
                        // AABB clip from combined mesh using this renderer's bounds (padded to catch edge triangles)
                        var padded = mr.bounds;
                        padded.Expand(0.1f);
                        var clipped = ClipMeshByBounds(mf, mr, padded, combinedBounds.center);
                        if (clipped.HasValue)
                            parts.Add(clipped.Value);
                    }
                    else
                    {
                        // Direct extraction
                        var extracted = ExtractDirectMesh(mf, mr, combinedBounds.center);
                        if (extracted.HasValue)
                            parts.Add(extracted.Value);
                    }
                }

                if (parts.Count == 0)
                {
                    _lastAction = "Save All: no geometry extracted";
                    return;
                }

                // Pick the part with the most vertices as primary
                int primaryIdx = 0;
                int maxVerts = 0;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i].vertCount > maxVerts) { maxVerts = parts[i].vertCount; primaryIdx = i; }
                }

                var primary = parts[primaryIdx];

                // Build child mesh entries from remaining parts
                var childList = new List<ChildMeshEntry>();
                for (int i = 0; i < parts.Count; i++)
                {
                    if (i == primaryIdx) continue;
                    var p = parts[i];
                    childList.Add(new ChildMeshEntry
                    {
                        Vertices = p.verts,
                        Normals = p.normals,
                        UVs = p.uvs,
                        Triangles = p.tris,
                        MaterialName = p.mat?.name ?? "Standard",
                        ShaderName = p.mat?.shader?.name ?? "Standard",
                        Color = p.mat != null
                            ? new[] { p.mat.color.r, p.mat.color.g, p.mat.color.b, p.mat.color.a }
                            : new[] { 1f, 1f, 1f, 1f }
                    });
                }

                ChildMeshEntry[] childMeshEntries = childList.Count > 0 ? childList.ToArray() : null;

                // Name derivation
                string eName = node.gameObject.name;
                foreach (char ch in Path.GetInvalidFileNameChars())
                    eName = eName.Replace(ch, '_');
                if (eName.Length > 50) eName = eName.Substring(0, 50);
                string dbName = eName.ToLowerInvariant().Replace(" ", "_");

                // Primary mesh: wrap tris in a single submesh
                var subMeshTris = new int[][] { primary.tris };
                var subMeshMats = new Material[] { primary.mat };

                string savedId = MeshVaultAPI.SaveMesh(dbName, primary.verts, primary.normals, primary.uvs,
                    subMeshTris, subMeshMats, combinedBounds,
                    childMeshes: childMeshEntries);

                int childCount2 = childMeshEntries?.Length ?? 0;
                _lastAction = $"Saved \"{savedId}\" ({primary.vertCount} v, {childCount2} children)";
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Save All: \"{savedId}\" primary={primary.vertCount}v children={childCount2}");
            }
            catch (Exception ex)
            {
                _lastAction = $"Save All failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] OnHierarchyExtractAll failed: {ex}");
            }
        }

        /// <summary>
        /// Extract a direct (non-combined) mesh, transforming vertices relative to the given center.
        /// </summary>
        private (Vector3[] verts, Vector3[] normals, Vector2[] uvs, int[] tris, Material mat, int vertCount)?
            ExtractDirectMesh(MeshFilter mf, MeshRenderer mr, Vector3 boundsCenter)
        {
            Vector3[] verts, normals;
            Vector2[] uvs;
            int[][] subTris;
            if (!ReadMeshData(mf.sharedMesh, mf, out verts, out normals, out uvs, out subTris))
                return null;

            for (int i = 0; i < verts.Length; i++)
                verts[i] = mr.transform.TransformPoint(verts[i]) - boundsCenter;
            for (int i = 0; i < normals.Length; i++)
                normals[i] = mr.transform.TransformDirection(normals[i]);

            int totalTris = 0;
            foreach (var st in subTris) totalTris += st.Length;
            var flatTris = new int[totalTris];
            int offset = 0;
            foreach (var st in subTris) { Array.Copy(st, 0, flatTris, offset, st.Length); offset += st.Length; }

            return (verts, normals, uvs ?? new Vector2[verts.Length], flatTris, mr.sharedMaterial, verts.Length);
        }

        /// <summary>
        /// AABB-clip triangles from a Combined Mesh using the renderer's bounds.
        /// Returns geometry relative to the given center, or null if nothing was extracted.
        /// </summary>
        private (Vector3[] verts, Vector3[] normals, Vector2[] uvs, int[] tris, Material mat, int vertCount)?
            ClipMeshByBounds(MeshFilter mf, MeshRenderer mr, Bounds clipBounds, Vector3 centerOffset)
        {
            var mesh = mf.sharedMesh;
            Vector3[] srcVerts, srcNormals;
            Vector2[] srcUVs;
            int[][] srcSubTris;
            if (!ReadMeshData(mesh, mf, out srcVerts, out srcNormals, out srcUVs, out srcSubTris))
                return null;

            var meshTransform = mf.transform;
            var bMin = clipBounds.min;
            var bMax = clipBounds.max;

            // Detect vertex space (raw vs transformed) using sampling
            int rawHits = 0, xfHits = 0;
            int step = Math.Max(1, srcVerts.Length / 500);
            for (int i = 0; i < srcVerts.Length; i += step)
            {
                var v = srcVerts[i];
                if (v.x >= bMin.x && v.x <= bMax.x && v.y >= bMin.y && v.y <= bMax.y && v.z >= bMin.z && v.z <= bMax.z)
                    rawHits++;
                var tv = meshTransform.TransformPoint(v);
                if (tv.x >= bMin.x && tv.x <= bMax.x && tv.y >= bMin.y && tv.y <= bMax.y && tv.z >= bMin.z && tv.z <= bMax.z)
                    xfHits++;
            }
            bool useRaw = rawHits >= xfHits;

            var vertMap = new Dictionary<int, int>();
            var newVerts = new List<Vector3>();
            var newNormals = new List<Vector3>();
            var newUVs = new List<Vector2>();
            var newTris = new List<int>();

            foreach (var subTris in srcSubTris)
            {
                for (int i = 0; i < subTris.Length; i += 3)
                {
                    int i0 = subTris[i], i1 = subTris[i + 1], i2 = subTris[i + 2];
                    if (i0 >= srcVerts.Length || i1 >= srcVerts.Length || i2 >= srcVerts.Length) continue;

                    var rawCenter = (srcVerts[i0] + srcVerts[i1] + srcVerts[i2]) / 3f;
                    var worldCenter = useRaw ? rawCenter : meshTransform.TransformPoint(rawCenter);

                    if (worldCenter.x < bMin.x || worldCenter.x > bMax.x ||
                        worldCenter.y < bMin.y || worldCenter.y > bMax.y ||
                        worldCenter.z < bMin.z || worldCenter.z > bMax.z)
                        continue;

                    foreach (int idx in new[] { i0, i1, i2 })
                    {
                        if (!vertMap.ContainsKey(idx))
                        {
                            vertMap[idx] = newVerts.Count;
                            var worldVert = useRaw ? srcVerts[idx] : meshTransform.TransformPoint(srcVerts[idx]);
                            newVerts.Add(worldVert - centerOffset);
                            newNormals.Add(srcNormals != null && idx < srcNormals.Length ? srcNormals[idx] : Vector3.up);
                            newUVs.Add(srcUVs != null && idx < srcUVs.Length ? srcUVs[idx] : Vector2.zero);
                        }
                        newTris.Add(vertMap[idx]);
                    }
                }
            }

            if (newVerts.Count == 0) return null;

            return (newVerts.ToArray(), newNormals.ToArray(), newUVs.ToArray(), newTris.ToArray(),
                mr.sharedMaterial, newVerts.Count);
        }

        /// <summary>
        /// Reads mesh geometry data, handling non-readable meshes on Mono via ReadMeshNative.
        /// </summary>
        private bool ReadMeshData(Mesh mesh, MeshFilter mf,
            out Vector3[] verts, out Vector3[] normals, out Vector2[] uvs, out int[][] subTris)
        {
            verts = null; normals = null; uvs = null; subTris = null;
            if (mesh == null) return false;

            if (mesh.isReadable)
            {
                verts = mesh.vertices;
                normals = mesh.normals ?? new Vector3[mesh.vertexCount];
                uvs = mesh.uv;
                subTris = new int[mesh.subMeshCount][];
                for (int s = 0; s < mesh.subMeshCount; s++)
                    subTris[s] = mesh.GetTriangles(s);
                return true;
            }

#if IL2CPP
            Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] Mesh \"{mesh.name}\" on \"{mf.gameObject.name}\" not readable (use Mono build)");
            return false;
#else
            List<int[]> nativeSubTris;
            if (!ReadMeshNative(mesh, out verts, out normals, out uvs, out nativeSubTris))
                return false;
            subTris = nativeSubTris.ToArray();
            return true;
#endif
        }
    }
}
#endif
