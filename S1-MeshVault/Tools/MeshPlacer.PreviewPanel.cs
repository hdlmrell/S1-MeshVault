#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
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
        private const int PreviewLayer = 31;
        private static readonly Vector3 PreviewSpawnPos = new Vector3(0, 5000, 0);

        // ═══════════════════════════════════════════════════════════════
        // Preview Panel
        // ═══════════════════════════════════════════════════════════════

        private void ShowPreviewPanel(string entryId)
        {
            ClosePreviewPanel();

            var entry = MeshVaultAPI.GetMesh(entryId);
            if (entry == null)
            {
                _lastAction = $"Entry \"{entryId}\" not found";
                return;
            }

            _materialPreviewEntryId = entryId;
            _materialCatalog = MeshVaultAPI.BuildMaterialCatalog();

            // Count material slots: submeshes + child meshes
            int submeshCount = (entry.SubMeshTriCounts != null && entry.SubMeshTriCounts.Length > 0)
                ? entry.SubMeshTriCounts.Length : 1;
            int childCount = entry.ChildMeshes?.Length ?? 0;
            int slotCount = submeshCount + childCount;
            _materialPreviewOverrides = new string[slotCount];
            _colorPreviewOverrides = new Color?[slotCount];
            _selectedSwatchBorders = new Dictionary<int, GameObject>();

            // Spawn preview mesh off-screen
            _materialPreviewMeshCopy = MeshVaultAPI.Spawn(entryId, PreviewSpawnPos, Quaternion.identity,
                namePrefix: "MV_Preview");
            if (_materialPreviewMeshCopy == null)
            {
                _lastAction = $"Failed to create preview for \"{entryId}\"";
                return;
            }
            SetLayerRecursive(_materialPreviewMeshCopy, PreviewLayer);

            // Create preview camera + render texture
            _materialPreviewRT = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32);

            var camGO = new GameObject("MV_PreviewCamera");
            _materialPreviewCamera = camGO.AddComponent<Camera>();
            _materialPreviewCamera.targetTexture = _materialPreviewRT;
            var bgColor = new Color(0.12f, 0.12f, 0.15f, 1f);
            _materialPreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            _materialPreviewCamera.backgroundColor = bgColor;
            _materialPreviewCamera.cullingMask = 1 << PreviewLayer;
            _materialPreviewCamera.nearClipPlane = 0.01f;
            _materialPreviewCamera.farClipPlane = 500f;
            _materialPreviewCamera.depth = -100;
            _materialPreviewCamera.useOcclusionCulling = false;
            _materialPreviewCamera.allowHDR = false;
            _materialPreviewCamera.allowMSAA = false;

            // Disable post-processing and shadows via URP camera data
            foreach (var comp in camGO.GetComponents<Component>())
            {
                if (comp != null && comp.GetType().Name == "UniversalAdditionalCameraData")
                {
                    var t = comp.GetType();
                    t.GetProperty("renderPostProcessing")?.SetValue(comp, false);
                    t.GetProperty("renderShadows")?.SetValue(comp, false);
                    break;
                }
            }

            // Set up orbit camera around the mesh center
            var previewBounds = GetCombinedBounds(_materialPreviewMeshCopy);
            _previewOrbitCenter = previewBounds.center;
            float maxDim = Mathf.Max(previewBounds.size.x,
                Mathf.Max(previewBounds.size.y, previewBounds.size.z));
            _previewOrbitDist = Mathf.Max(maxDim * 1.2f, 0.5f);
            _previewOrbitDistMin = _previewOrbitDist * 0.3f;
            _previewOrbitYaw = 0f;
            _previewOrbitPitch = 15f;
            _previewDragging = false;
            UpdatePreviewCamera();

            // Create an opaque backdrop box around the mesh so the skybox is fully occluded
            _previewBackdrop = CreatePreviewBackdrop(_previewOrbitCenter);

            RenderPreview();

            // Build UI
            BuildPreviewUI(entry, submeshCount, childCount);

            // Free cursor for panel interaction
            _cursorFree = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _lastAction = $"Preview: {entryId} ({slotCount} material slot(s))";
        }

        private void BuildPreviewUI(MeshEntry entry, int submeshCount, int childCount)
        {
            _materialPreviewPanel = new GameObject("MeshPlacerPreviewPanel");
            var rootCanvas = _materialPreviewPanel.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 94;

            var scaler = _materialPreviewPanel.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _materialPreviewPanel.AddComponent<GameCanvasScaler>();
            _materialPreviewPanel.AddComponent<GraphicRaycaster>();

            // Dark overlay
            var overlayObj = UIHelper.Panel("Overlay", _materialPreviewPanel.transform,
                new Color(0, 0, 0, 0.5f), fullAnchor: true);
            overlayObj.GetComponent<Image>().raycastTarget = true;

            // Main panel
            var panelObj = UIHelper.Panel("PreviewPanel", _materialPreviewPanel.transform,
                new Color(0.1f, 0.1f, 0.13f));
            var panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.15f, 0.08f);
            panelRect.anchorMax = new Vector2(0.85f, 0.92f);

            // Title
            var titleText = UIHelper.Text("Title",
                $"<b>Material Preview</b>  —  {_materialPreviewEntryId}",
                panelObj.transform, 17, TextAlignmentOptions.Center);
            var titleRect = titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.anchoredPosition = new Vector2(0, -5);
            titleRect.sizeDelta = new Vector2(0, 30);

            // Left side: 3D preview image
            var rawImageGO = new GameObject("PreviewImage");
            rawImageGO.transform.SetParent(panelObj.transform, false);
            _previewRawImage = rawImageGO.AddComponent<RawImage>();
            _previewRawImage.texture = _materialPreviewRT;
            var rawImage = _previewRawImage;
            var rawImageRect = rawImageGO.GetComponent<RectTransform>();
            rawImageRect.anchorMin = new Vector2(0, 0.08f);
            rawImageRect.anchorMax = new Vector2(0.45f, 0.95f);
            rawImageRect.offsetMin = new Vector2(8, 0);
            rawImageRect.offsetMax = new Vector2(-4, -36);

            // Right side: material slots
            var slotsArea = UIHelper.Panel("SlotsArea", panelObj.transform, new Color(0.08f, 0.08f, 0.1f));
            var slotsRect = slotsArea.GetComponent<RectTransform>();
            slotsRect.anchorMin = new Vector2(0.45f, 0.08f);
            slotsRect.anchorMax = new Vector2(1, 0.95f);
            slotsRect.offsetMin = new Vector2(4, 0);
            slotsRect.offsetMax = new Vector2(-8, -36);

            var slotsContent = UIHelper.ScrollableVerticalList("SlotsList", slotsArea.transform,
                out ScrollRect slotsScroll);
            var slotsScrollRT = slotsScroll.GetComponent<RectTransform>();
            slotsScrollRT.anchorMin = Vector2.zero;
            slotsScrollRT.anchorMax = Vector2.one;
            slotsScrollRT.offsetMin = new Vector2(4, 4);
            slotsScrollRT.offsetMax = new Vector2(-4, -4);

            var contentLayout = slotsContent.GetComponent<VerticalLayoutGroup>();
            if (contentLayout != null)
            {
                contentLayout.spacing = 6;
                contentLayout.padding = new RectOffset(4, 4, 4, 4);
            }

            // Fix content rect pivot
            var contentRect = slotsContent.GetComponent<RectTransform>();
            if (contentRect != null)
            {
                contentRect.anchorMin = new Vector2(0, 1);
                contentRect.anchorMax = new Vector2(1, 1);
                contentRect.pivot = new Vector2(0, 1);
                contentRect.sizeDelta = new Vector2(0, contentRect.sizeDelta.y);
                contentRect.offsetMin = new Vector2(0, contentRect.offsetMin.y);
                contentRect.offsetMax = new Vector2(0, contentRect.offsetMax.y);
            }

            slotsScroll.horizontal = false;

            // Build slot rows
            var sortedMaterials = _materialCatalog.Keys.OrderBy(k => k).ToArray();

            for (int i = 0; i < submeshCount; i++)
            {
                string defaultMat = (entry.SubMeshMaterialNames != null && i < entry.SubMeshMaterialNames.Length)
                    ? entry.SubMeshMaterialNames[i] : entry.MaterialName;
                CreateMaterialSlotRow(slotsContent, i, $"Submesh {i}", defaultMat, sortedMaterials);
            }

            for (int i = 0; i < childCount; i++)
            {
                int slotIdx = submeshCount + i;
                string defaultMat = entry.ChildMeshes[i].MaterialName;
                CreateMaterialSlotRow(slotsContent, slotIdx, $"Child {i}", defaultMat, sortedMaterials);
            }

            // Bottom button row
            var btnRowObj = new GameObject("BtnRow");
            btnRowObj.transform.SetParent(panelObj.transform, false);
            var btnRowRect = btnRowObj.AddComponent<RectTransform>();
            btnRowRect.anchorMin = new Vector2(0, 0);
            btnRowRect.anchorMax = new Vector2(1, 0);
            btnRowRect.pivot = new Vector2(0.5f, 0);
            btnRowRect.anchoredPosition = new Vector2(0, 6);
            btnRowRect.sizeDelta = new Vector2(-16, 32);

            var btnRowHLG = btnRowObj.AddComponent<HorizontalLayoutGroup>();
            btnRowHLG.spacing = 6;
            btnRowHLG.childControlWidth = true;
            btnRowHLG.childControlHeight = true;
            btnRowHLG.childForceExpandWidth = true;
            btnRowHLG.childForceExpandHeight = true;

            var (_, hereBtn, _) = UIHelper.RoundedButtonWithLabel(
                "SpawnHereBtn", "Spawn Here", btnRowObj.transform,
                new Color(0.15f, 0.4f, 0.15f), 140, 28, 15, Color.white);
            hereBtn.onClick.AddListener(new Action(() => SpawnFromPreview(false)));

            var (_, originBtn, _) = UIHelper.RoundedButtonWithLabel(
                "SpawnOriginBtn", "Spawn Origin", btnRowObj.transform,
                new Color(0.35f, 0.3f, 0.15f), 140, 28, 15, Color.white);
            originBtn.onClick.AddListener(new Action(() => SpawnFromPreview(true)));

            var (_, cancelBtn, _) = UIHelper.RoundedButtonWithLabel(
                "CancelBtn", "Cancel (Del)", btnRowObj.transform,
                new Color(0.5f, 0.2f, 0.2f), 140, 28, 15, Color.white);
            cancelBtn.onClick.AddListener(new Action(ClosePreviewPanel));
        }

        private void CreateMaterialSlotRow(Transform parent, int slotIndex, string slotLabel,
            string defaultMat, string[] sortedMaterials)
        {
            var slotObj = new GameObject($"Slot_{slotIndex}");
            slotObj.transform.SetParent(parent, false);
            slotObj.AddComponent<RectTransform>();
            var slotBg = slotObj.AddComponent<Image>();
            slotBg.color = new Color(0.14f, 0.14f, 0.18f);
            var slotLayout = slotObj.AddComponent<LayoutElement>();
            slotLayout.preferredHeight = 72;
            slotLayout.flexibleWidth = 1;

            var vlg = slotObj.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2;
            vlg.padding = new RectOffset(6, 6, 4, 4);
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            // Label row (with Tint button)
            var labelRow = new GameObject("LabelRow");
            labelRow.transform.SetParent(slotObj.transform, false);
            labelRow.AddComponent<RectTransform>();
            var labelRowLE = labelRow.AddComponent<LayoutElement>();
            labelRowLE.preferredHeight = 20;

            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(labelRow.transform, false);
            var labelRect = labelObj.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = new Vector2(-44, 0);

            var labelTMP = labelObj.AddComponent<TextMeshProUGUI>();
            labelTMP.text = $"<b>{slotLabel}:</b> <color=#aaa>{defaultMat ?? "none"}</color>";
            labelTMP.fontSize = 13;
            labelTMP.color = Color.white;
            UIHelper.SetWrapping(labelTMP, false);

            // Tint button (right-aligned in label row)
            var tintBtnObj = new GameObject("TintBtn");
            tintBtnObj.transform.SetParent(labelRow.transform, false);
            var tintBtnRect = tintBtnObj.AddComponent<RectTransform>();
            tintBtnRect.anchorMin = new Vector2(1, 0);
            tintBtnRect.anchorMax = new Vector2(1, 1);
            tintBtnRect.pivot = new Vector2(1, 0.5f);
            tintBtnRect.anchoredPosition = Vector2.zero;
            tintBtnRect.sizeDelta = new Vector2(40, 0);

            var tintBtnImg = tintBtnObj.AddComponent<Image>();
            tintBtnImg.color = new Color(0.3f, 0.25f, 0.4f);
            var tintBtn = tintBtnObj.AddComponent<Button>();
            tintBtn.targetGraphic = tintBtnImg;

            var tintLabelObj = new GameObject("Label");
            tintLabelObj.transform.SetParent(tintBtnObj.transform, false);
            var tintLabelRect = tintLabelObj.AddComponent<RectTransform>();
            tintLabelRect.anchorMin = Vector2.zero;
            tintLabelRect.anchorMax = Vector2.one;
            tintLabelRect.offsetMin = Vector2.zero;
            tintLabelRect.offsetMax = Vector2.zero;
            var tintLabelTMP = tintLabelObj.AddComponent<TextMeshProUGUI>();
            tintLabelTMP.text = "Tint";
            tintLabelTMP.fontSize = 11;
            tintLabelTMP.color = Color.white;
            tintLabelTMP.alignment = TextAlignmentOptions.Center;

            int capturedSlotForTint = slotIndex;
            tintBtn.onClick.AddListener(new Action(() => ShowColorPicker(capturedSlotForTint)));

            // Swatch scroll area
            var swatchArea = new GameObject("SwatchArea");
            swatchArea.transform.SetParent(slotObj.transform, false);
            swatchArea.AddComponent<RectTransform>();
            var swatchLE = swatchArea.AddComponent<LayoutElement>();
            swatchLE.preferredHeight = 36;
            swatchLE.flexibleWidth = 1;

            var swatchScroll = swatchArea.AddComponent<ScrollRect>();
            swatchScroll.horizontal = true;
            swatchScroll.vertical = false;
            swatchScroll.movementType = ScrollRect.MovementType.Elastic;
            swatchScroll.elasticity = 0.08f;
            swatchScroll.scrollSensitivity = 12f;

            // Viewport for horizontal swatch scroll
            var swatchViewport = new GameObject("Viewport");
            swatchViewport.transform.SetParent(swatchArea.transform, false);
            var svpRect = swatchViewport.AddComponent<RectTransform>();
            svpRect.anchorMin = Vector2.zero;
            svpRect.anchorMax = Vector2.one;
            svpRect.offsetMin = Vector2.zero;
            svpRect.offsetMax = Vector2.zero;
            var svpImg = swatchViewport.AddComponent<Image>();
            svpImg.color = new Color(0, 0, 0, 0);
            svpImg.raycastTarget = true;
            swatchViewport.AddComponent<RectMask2D>();
            swatchScroll.viewport = svpRect;

            // Content for swatches
            var swatchContent = new GameObject("Content");
            swatchContent.transform.SetParent(swatchViewport.transform, false);
            var scRect = swatchContent.AddComponent<RectTransform>();
            scRect.anchorMin = new Vector2(0, 0);
            scRect.anchorMax = new Vector2(0, 1);
            scRect.pivot = new Vector2(0, 0.5f);
            scRect.sizeDelta = new Vector2(0, 0);

            var hlg = swatchContent.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.padding = new RectOffset(2, 2, 2, 2);
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            var csf = swatchContent.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            swatchScroll.content = scRect;

            // "Default" swatch (first, with border indicator)
            int capturedSlot = slotIndex;
            CreateSwatch(swatchContent.transform, capturedSlot, defaultMat, defaultMat, labelTMP, true);

            // All other materials from catalog
            foreach (string matName in sortedMaterials)
            {
                if (matName == defaultMat) continue;
                CreateSwatch(swatchContent.transform, capturedSlot, matName, defaultMat, labelTMP, false);
            }
        }

        private void CreateSwatch(Transform parent, int slotIndex, string materialName,
            string defaultMat, TextMeshProUGUI slotLabel, bool isDefault)
        {
            const int swatchSize = 28;

            var swatchObj = new GameObject($"Swatch_{materialName}");
            swatchObj.transform.SetParent(parent, false);

            var swatchRect = swatchObj.AddComponent<RectTransform>();
            swatchRect.sizeDelta = new Vector2(swatchSize, swatchSize);

            var swatchLE = swatchObj.AddComponent<LayoutElement>();
            swatchLE.preferredWidth = swatchSize;
            swatchLE.preferredHeight = swatchSize;

            var swatchImg = swatchObj.AddComponent<Image>();

            // Get color from catalog
            float[] rgba = _materialCatalog.TryGetValue(materialName, out var col) ? col : MeshVaultAPI.DefaultColor;
            var swatchColor = new Color(rgba[0], rgba[1], rgba[2], 1f);
            swatchImg.color = swatchColor;

            // Border on every swatch (only active on currently selected)
            var borderObj = new GameObject("Border");
            borderObj.transform.SetParent(swatchObj.transform, false);
            var borderRect = borderObj.AddComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = new Vector2(-2, -2);
            borderRect.offsetMax = new Vector2(2, 2);
            borderObj.transform.SetAsFirstSibling();
            var borderImg = borderObj.AddComponent<Image>();
            borderImg.color = Color.white;
            borderImg.raycastTarget = false;

            borderObj.SetActive(isDefault);
            if (isDefault)
                _selectedSwatchBorders[slotIndex] = borderObj;

            var btn = swatchObj.AddComponent<Button>();
            btn.targetGraphic = swatchImg;
#if !IL2CPP
            swatchObj.AddComponent<ScrollForwarder>();
#endif

            string capturedName = isDefault ? null : materialName;
            string capturedDefault = defaultMat;
            int capturedSlot = slotIndex;
            GameObject capturedBorder = borderObj;

            btn.onClick.AddListener(new Action(() =>
            {
                _materialPreviewOverrides[capturedSlot] = capturedName;
                ApplyOverrideToPreview(capturedSlot);

                // Update swatch selection indicator
                if (_selectedSwatchBorders.TryGetValue(capturedSlot, out var prevBorder))
                    prevBorder.SetActive(false);
                capturedBorder.SetActive(true);
                _selectedSwatchBorders[capturedSlot] = capturedBorder;

                string current = capturedName ?? capturedDefault;
                string suffix = capturedName == null ? " <color=#0f0>(default)</color>" : "";
                slotLabel.text = $"<b>Submesh {capturedSlot}:</b> <color=#aaa>{current}</color>{suffix}";
            }));
        }

        private void ApplyOverrideToPreview(int slotIndex)
        {
            RespawnPreviewMesh();
            RenderPreview();
        }

        private void RespawnPreviewMesh()
        {
            if (_materialPreviewMeshCopy != null)
                UnityEngine.Object.DestroyImmediate(_materialPreviewMeshCopy);

            // Pass null when all entries are default — Spawn treats null as "no overrides"
            string[] matOverrides = null;
            if (_materialPreviewOverrides != null)
            {
                foreach (var o in _materialPreviewOverrides)
                    if (o != null) { matOverrides = _materialPreviewOverrides; break; }
            }

            Color?[] colorOverrides = null;
            if (_colorPreviewOverrides != null)
            {
                foreach (var c in _colorPreviewOverrides)
                    if (c.HasValue) { colorOverrides = _colorPreviewOverrides; break; }
            }

            _materialPreviewMeshCopy = MeshVaultAPI.Spawn(_materialPreviewEntryId, PreviewSpawnPos,
                Quaternion.identity, namePrefix: "MV_Preview",
                materialOverrides: matOverrides, colorOverrides: colorOverrides);

            if (_materialPreviewMeshCopy != null)
            {
                SetLayerRecursive(_materialPreviewMeshCopy, PreviewLayer);

                // Debug: log materials on the respawned mesh
                var mr = _materialPreviewMeshCopy.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var mats = mr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        Melon<MeshVaultPlugin>.Logger.Msg(
                            $"[Preview] slot {i}: {(m != null ? m.name : "NULL")} " +
                            $"shader={m?.shader?.name ?? "?"} color={m?.color}");
                    }
                }
            }
            else
            {
                Melon<MeshVaultPlugin>.Logger.Warning("[Preview] RespawnPreviewMesh: Spawn returned null");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Color Picker
        // ═══════════════════════════════════════════════════════════════

        private void ShowColorPicker(int slotIndex)
        {
            if (_colorPickerOverlay != null)
                UnityEngine.Object.Destroy(_colorPickerOverlay);

            Color currentColor = (_colorPreviewOverrides != null && slotIndex < _colorPreviewOverrides.Length
                && _colorPreviewOverrides[slotIndex].HasValue)
                ? _colorPreviewOverrides[slotIndex].Value : Color.white;

            _colorPickerOverlay = new GameObject("ColorPickerOverlay");
            _colorPickerOverlay.transform.SetParent(_materialPreviewPanel.transform, false);
            var overlayImg = _colorPickerOverlay.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.6f);
            overlayImg.raycastTarget = true;
            var overlayRect = _colorPickerOverlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            // Dialog panel
            var dialogObj = UIHelper.Panel("ColorPickerDialog", _colorPickerOverlay.transform,
                new Color(0.12f, 0.12f, 0.15f));
            var dialogRect = dialogObj.GetComponent<RectTransform>();
            dialogRect.anchorMin = new Vector2(0.3f, 0.25f);
            dialogRect.anchorMax = new Vector2(0.7f, 0.75f);

            // Title
            var title = UIHelper.Text("Title", $"<b>Color Tint — Slot {slotIndex}</b>",
                dialogObj.transform, 16, TextAlignmentOptions.Center);
            var titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.88f);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(8, 0);
            titleRect.offsetMax = new Vector2(-8, -4);

            // Color preview swatch
            var previewObj = UIHelper.Panel("ColorPreview", dialogObj.transform, currentColor);
            var previewRect = previewObj.GetComponent<RectTransform>();
            previewRect.anchorMin = new Vector2(0.35f, 0.72f);
            previewRect.anchorMax = new Vector2(0.65f, 0.86f);
            var previewImg = previewObj.GetComponent<Image>();

            // RGB Sliders
            var rSlider = CreateColorSlider(dialogObj.transform, "R", currentColor.r,
                new Color(1, 0.3f, 0.3f), 0.52f, 0.68f);
            var gSlider = CreateColorSlider(dialogObj.transform, "G", currentColor.g,
                new Color(0.3f, 1, 0.3f), 0.34f, 0.50f);
            var bSlider = CreateColorSlider(dialogObj.transform, "B", currentColor.b,
                new Color(0.4f, 0.6f, 1), 0.16f, 0.32f);

            // Live preview update
            Action updatePreview = () =>
            {
                previewImg.color = new Color(rSlider.value, gSlider.value, bSlider.value, 1f);
            };
            rSlider.onValueChanged.AddListener(new Action<float>(_ => updatePreview()));
            gSlider.onValueChanged.AddListener(new Action<float>(_ => updatePreview()));
            bSlider.onValueChanged.AddListener(new Action<float>(_ => updatePreview()));

            // Button row
            var btnRow = new GameObject("BtnRow");
            btnRow.transform.SetParent(dialogObj.transform, false);
            var btnRowRect = btnRow.AddComponent<RectTransform>();
            btnRowRect.anchorMin = new Vector2(0, 0);
            btnRowRect.anchorMax = new Vector2(1, 0.14f);
            btnRowRect.offsetMin = new Vector2(8, 4);
            btnRowRect.offsetMax = new Vector2(-8, -2);
            var btnRowHLG = btnRow.AddComponent<HorizontalLayoutGroup>();
            btnRowHLG.spacing = 6;
            btnRowHLG.childControlWidth = true;
            btnRowHLG.childControlHeight = true;
            btnRowHLG.childForceExpandWidth = true;
            btnRowHLG.childForceExpandHeight = true;

            int capturedSlot = slotIndex;

            var (_, applyBtn, _) = UIHelper.RoundedButtonWithLabel(
                "ApplyBtn", "Apply", btnRow.transform,
                new Color(0.15f, 0.4f, 0.15f), 80, 24, 14, Color.white);
            applyBtn.onClick.AddListener(new Action(() =>
            {
                var newColor = new Color(rSlider.value, gSlider.value, bSlider.value, 1f);
                _colorPreviewOverrides[capturedSlot] = newColor;
                ApplyOverrideToPreview(capturedSlot);
                UnityEngine.Object.Destroy(_colorPickerOverlay);
                _colorPickerOverlay = null;
            }));

            var (_, resetBtn, _) = UIHelper.RoundedButtonWithLabel(
                "ResetBtn", "Reset", btnRow.transform,
                new Color(0.5f, 0.35f, 0.1f), 80, 24, 14, Color.white);
            resetBtn.onClick.AddListener(new Action(() =>
            {
                _colorPreviewOverrides[capturedSlot] = null;
                ApplyOverrideToPreview(capturedSlot);
                UnityEngine.Object.Destroy(_colorPickerOverlay);
                _colorPickerOverlay = null;
            }));

            var (_, cancelBtn, _) = UIHelper.RoundedButtonWithLabel(
                "CancelBtn", "Cancel", btnRow.transform,
                new Color(0.4f, 0.2f, 0.2f), 80, 24, 14, Color.white);
            cancelBtn.onClick.AddListener(new Action(() =>
            {
                UnityEngine.Object.Destroy(_colorPickerOverlay);
                _colorPickerOverlay = null;
            }));
        }

        private Slider CreateColorSlider(Transform parent, string label, float initialValue,
            Color trackColor, float anchorYMin, float anchorYMax)
        {
            var rowObj = new GameObject($"Slider_{label}");
            rowObj.transform.SetParent(parent, false);
            var rowRect = rowObj.AddComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0, anchorYMin);
            rowRect.anchorMax = new Vector2(1, anchorYMax);
            rowRect.offsetMin = new Vector2(12, 2);
            rowRect.offsetMax = new Vector2(-12, -2);

            // Label
            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(rowObj.transform, false);
            var labelRect = labelObj.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0);
            labelRect.anchorMax = new Vector2(0.1f, 1);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var labelTMP = labelObj.AddComponent<TextMeshProUGUI>();
            labelTMP.text = $"<b>{label}:</b>";
            labelTMP.fontSize = 14;
            labelTMP.color = trackColor;
            labelTMP.alignment = TextAlignmentOptions.MidlineLeft;

            // Value label
            var valueObj = new GameObject("Value");
            valueObj.transform.SetParent(rowObj.transform, false);
            var valueRect = valueObj.AddComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.85f, 0);
            valueRect.anchorMax = new Vector2(1, 1);
            valueRect.offsetMin = Vector2.zero;
            valueRect.offsetMax = Vector2.zero;
            var valueTMP = valueObj.AddComponent<TextMeshProUGUI>();
            valueTMP.text = $"{initialValue:F2}";
            valueTMP.fontSize = 13;
            valueTMP.color = Color.white;
            valueTMP.alignment = TextAlignmentOptions.MidlineRight;

            // Slider
            var sliderObj = new GameObject("Slider");
            sliderObj.transform.SetParent(rowObj.transform, false);
            var sliderRect = sliderObj.AddComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0.12f, 0.2f);
            sliderRect.anchorMax = new Vector2(0.84f, 0.8f);
            sliderRect.offsetMin = Vector2.zero;
            sliderRect.offsetMax = Vector2.zero;

            // Background track
            var bgObj = UIHelper.Panel("Background", sliderObj.transform, new Color(0.2f, 0.2f, 0.25f));
            var bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Fill area
            var fillAreaObj = new GameObject("Fill Area");
            fillAreaObj.transform.SetParent(sliderObj.transform, false);
            var fillAreaRect = fillAreaObj.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = Vector2.zero;
            fillAreaRect.offsetMax = Vector2.zero;

            var fillObj = UIHelper.Panel("Fill", fillAreaObj.transform, trackColor);
            var fillRect = fillObj.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            // Handle slide area
            var handleAreaObj = new GameObject("Handle Slide Area");
            handleAreaObj.transform.SetParent(sliderObj.transform, false);
            var handleAreaRect = handleAreaObj.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = Vector2.zero;
            handleAreaRect.offsetMax = Vector2.zero;

            var handleObj = UIHelper.Panel("Handle", handleAreaObj.transform, Color.white);
            var handleRect = handleObj.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(10, 0);

            var slider = sliderObj.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.value = initialValue;
            slider.direction = Slider.Direction.LeftToRight;

            slider.onValueChanged.AddListener(new Action<float>(v =>
            {
                valueTMP.text = $"{v:F2}";
            }));

            return slider;
        }

        // ═══════════════════════════════════════════════════════════════
        // Preview rendering + interaction
        // ═══════════════════════════════════════════════════════════════

        private void UpdatePreviewCamera()
        {
            if (_materialPreviewCamera == null) return;
            var rot = Quaternion.Euler(_previewOrbitPitch, _previewOrbitYaw, 0);
            var offset = rot * new Vector3(0, 0, -_previewOrbitDist);
            _materialPreviewCamera.transform.position = _previewOrbitCenter + offset;
            _materialPreviewCamera.transform.LookAt(_previewOrbitCenter);
        }

        private void UpdatePreviewInteraction()
        {
            if (_previewRawImage == null || _materialPreviewCamera == null) return;

            var rt = _previewRawImage.GetComponent<RectTransform>();
            bool hovering = RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition);
            bool changed = false;

            // Scroll to zoom (only when hovering)
            if (hovering)
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _previewOrbitDist = Mathf.Clamp(
                        _previewOrbitDist * (1f - scroll * 0.12f),
                        _previewOrbitDistMin, 150f);
                    changed = true;
                }
            }

            // Click+drag to rotate
            if (hovering && Input.GetMouseButtonDown(0))
            {
                _previewDragging = true;
                _previewDragStart = Input.mousePosition;
            }
            if (_previewDragging && Input.GetMouseButton(0))
            {
                Vector3 delta = Input.mousePosition - _previewDragStart;
                if (delta.sqrMagnitude > 0.01f)
                {
                    _previewOrbitYaw += delta.x * 0.4f;
                    _previewOrbitPitch = Mathf.Clamp(
                        _previewOrbitPitch - delta.y * 0.4f, -85f, 85f);
                    _previewDragStart = Input.mousePosition;
                    changed = true;
                }
            }
            if (Input.GetMouseButtonUp(0))
                _previewDragging = false;

            if (changed)
            {
                UpdatePreviewCamera();
                RenderPreview();
            }
        }

        private void RenderPreview()
        {
            if (_materialPreviewCamera == null || _materialPreviewRT == null) return;

            // Temporarily null the global skybox so URP skips its DrawSkyboxPass.
            var savedSkybox = RenderSettings.skybox;
            RenderSettings.skybox = null;
            try
            {
                _materialPreviewCamera.Render();
            }
            finally
            {
                RenderSettings.skybox = savedSkybox;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Spawn from preview
        // ═══════════════════════════════════════════════════════════════

        private void SpawnFromPreview(bool atOrigin)
        {
            if (_materialPreviewEntryId == null) return;

            var entry = MeshVaultAPI.GetMesh(_materialPreviewEntryId);
            if (entry == null) return;

            Vector3 spawnPos;
            if (atOrigin)
            {
                spawnPos = new Vector3(entry.BoundsCenter[0], entry.BoundsCenter[1], entry.BoundsCenter[2]);
            }
            else
            {
                var player = PlayerSingleton<PlayerMovement>.Instance;
                if (player == null) { _lastAction = "No player"; return; }
                var forward = player.transform.forward;
                forward.y = 0;
                forward.Normalize();
                spawnPos = player.transform.position + forward * 3f;
            }

            // Check material overrides
            bool hasMaterialOverrides = false;
            foreach (var o in _materialPreviewOverrides)
                if (o != null) { hasMaterialOverrides = true; break; }

            // Check color overrides
            bool hasColorOverrides = false;
            if (_colorPreviewOverrides != null)
                foreach (var c in _colorPreviewOverrides)
                    if (c.HasValue) { hasColorOverrides = true; break; }

            string capturedId = _materialPreviewEntryId;
            string[] matOverrides = hasMaterialOverrides ? (string[])_materialPreviewOverrides.Clone() : null;
            Color?[] colOverrides = hasColorOverrides ? (Color?[])_colorPreviewOverrides.Clone() : null;

            ClosePreviewPanel();

            var go = MeshVaultAPI.Spawn(capturedId, spawnPos, Quaternion.identity,
                namePrefix: "MV_TestSpawn", materialOverrides: matOverrides,
                colorOverrides: colOverrides);
            if (go != null)
            {
                _testSpawns.Add(go);
                EnterPositioner(go, capturedId, spawnPos,
                    materialOverrides: matOverrides, colorOverrides: colOverrides);
            }
            else
            {
                _lastAction = $"Failed to spawn \"{capturedId}\"";
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Cleanup
        // ═══════════════════════════════════════════════════════════════

        private void ClosePreviewPanel()
        {
            if (_colorPickerOverlay != null)
            {
                UnityEngine.Object.Destroy(_colorPickerOverlay);
                _colorPickerOverlay = null;
            }
            if (_previewBackdrop != null)
            {
                UnityEngine.Object.Destroy(_previewBackdrop);
                _previewBackdrop = null;
            }
            if (_materialPreviewMeshCopy != null)
            {
                UnityEngine.Object.Destroy(_materialPreviewMeshCopy);
                _materialPreviewMeshCopy = null;
            }
            if (_materialPreviewCamera != null)
            {
                UnityEngine.Object.Destroy(_materialPreviewCamera.gameObject);
                _materialPreviewCamera = null;
            }
            if (_materialPreviewRT != null)
            {
                _materialPreviewRT.Release();
                UnityEngine.Object.Destroy(_materialPreviewRT);
                _materialPreviewRT = null;
            }
            if (_materialPreviewPanel != null)
            {
                UnityEngine.Object.Destroy(_materialPreviewPanel);
                _materialPreviewPanel = null;
            }
            _materialPreviewEntryId = null;
            _materialPreviewOverrides = null;
            _colorPreviewOverrides = null;
            _selectedSwatchBorders = null;
            _materialCatalog = null;
            _previewRawImage = null;
            _previewDragging = false;

            // Re-lock cursor if tool is still active
            if (_active)
            {
                _cursorFree = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        /// <summary>
        /// Creates a large inverted cube around the mesh to occlude the skybox.
        /// Physically flips the mesh normals + triangle winding so the inside faces
        /// become "front" faces under default culling — no shader _Cull property needed.
        /// </summary>
        private GameObject CreatePreviewBackdrop(Vector3 center)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "MV_PreviewBackdrop";
            cube.transform.position = center;
            cube.transform.localScale = Vector3.one * 400f;
            cube.layer = PreviewLayer;

            var col = cube.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);

            // Flip normals and triangle winding so inside faces are "front" faces
            var mf = cube.GetComponent<MeshFilter>();
            if (mf != null)
            {
                var mesh = mf.mesh; // creates an instance copy
                var normals = mesh.normals;
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = -normals[i];
                mesh.normals = normals;

                var tris = mesh.triangles;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int tmp = tris[i];
                    tris[i] = tris[i + 2];
                    tris[i + 2] = tmp;
                }
                mesh.triangles = tris;
            }

            // Build material: black base + emission = flat solid color
            var bgColor = new Color(0.12f, 0.12f, 0.15f, 1f);
            Material mat = null;

            if (_materialPreviewMeshCopy != null)
            {
                var meshMR = _materialPreviewMeshCopy.GetComponent<MeshRenderer>();
                if (meshMR != null && meshMR.sharedMaterial != null)
                {
                    mat = new Material(meshMR.sharedMaterial.shader);
                    mat.color = Color.black;
                    mat.SetColor("_BaseColor", Color.black);
                    mat.SetTexture("_MainTex", Texture2D.whiteTexture);
                    mat.SetFloat("_Smoothness", 0f);
                    mat.SetFloat("_Metallic", 0f);
                    mat.SetFloat("_SpecularHighlights", 0f);
                    mat.SetFloat("_EnvironmentReflections", 0f);
                    mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", bgColor);
                }
            }
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Standard");
                if (shader != null)
                {
                    mat = new Material(shader);
                    mat.color = Color.black;
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", Color.black);
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", bgColor);
                }
            }

            var mr = cube.GetComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return cube;
        }

        private static Bounds GetCombinedBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<MeshRenderer>();
            if (renderers == null || renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }
    }
}
#endif
