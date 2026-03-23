#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

#if IL2CPP
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppTMPro;
using GameCanvasScaler = Il2CppScheduleOne.UI.CanvasScaler;
#else
using ScheduleOne.DevUtilities;
using ScheduleOne.PlayerScripts;
using TMPro;
using GameCanvasScaler = ScheduleOne.UI.CanvasScaler;
#endif

namespace MeshVault.Tools
{
    public partial class MeshPlacer
    {
        // Decal panel state
        private GameObject _decalPanel;
        private List<Texture2D> _decalTextures;
        private List<GameObject> _decalSpawns = new List<GameObject>();
        private string _decalSearchTerm = "";
        private Color _decalTintColor = Color.white;
        private Material _decalBaseMaterial;
        private GameObject _decalColorPicker;
        private List<Image> _tintSwatchBorders = new List<Image>();
        private string _previewDecalTexName;
        private GameObject _decalImportDialog;
        private bool _importedDecalsLoaded;

        // Texture name prefixes to scan for
        private static readonly string[] _decalPrefixes = { "Graffiti_", "Decals ", "decals ", "SplatAlpha" };

        // Auxiliary map keywords to filter out
        private static readonly string[] _decalFilterKeywords =
            { "normal", "norm", "nor", "height", "_AO", "_ao", "MetallicSmoothness" };

        private const float DecalCellSize = 120f;
        private const float DecalCellSpacing = 4f;
        private const string DecalImportLogPrefix = "[DecalImport]";
        private const int HashByteCount = 4;

        private static readonly HashSet<string> _supportedImageExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };

        // Tint preset colors
        private static readonly (string name, Color color)[] _tintPresets =
        {
            ("White", Color.white),
            ("Red", new Color(0.9f, 0.15f, 0.15f)),
            ("Green", new Color(0.15f, 0.75f, 0.15f)),
            ("Blue", new Color(0.2f, 0.4f, 0.9f)),
            ("Yellow", new Color(0.95f, 0.9f, 0.1f)),
            ("Orange", new Color(0.95f, 0.55f, 0.1f)),
            ("Purple", new Color(0.6f, 0.2f, 0.8f)),
            ("Black", new Color(0.1f, 0.1f, 0.1f))
        };

        // ═══════════════════════════════════════════════════════════════
        // Decal Browser
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Scans for decal textures in the scene and shows a grid panel with texture previews.
        /// Clicking a texture spawns it as a DecalProjector on the aimed surface.
        /// </summary>
        private void ShowDecalPanel()
        {
            CloseDecalPanel();

            if (_decalTextures == null)
                ScanDecalTextures();

            _decalPanel = new GameObject("MV_DecalPanel");
            var rootCanvas = _decalPanel.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 94;

            var scaler = _decalPanel.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _decalPanel.AddComponent<GameCanvasScaler>();
            _decalPanel.AddComponent<GraphicRaycaster>();

            // Dark overlay
            var overlayObj = UIHelper.Panel("Overlay", _decalPanel.transform, new Color(0, 0, 0, 0.4f), fullAnchor: true);
            overlayObj.GetComponent<Image>().raycastTarget = true;

            // Panel
            var panelObj = UIHelper.Panel("DecalBrowser", _decalPanel.transform, new Color(0.1f, 0.12f, 0.1f));
            var panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.15f, 0.05f);
            panelRect.anchorMax = new Vector2(0.85f, 0.95f);

            // Title
            var titleText = UIHelper.Text("Title",
                $"<b>Decal Browser</b>  ({_decalTextures.Count} textures)",
                panelObj.transform, 17, TextAlignmentOptions.Center);
            var titleRect = titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.anchoredPosition = new Vector2(0, -5);
            titleRect.sizeDelta = new Vector2(0, 30);

            // Search bar
            var searchBarObj = new GameObject("SearchBar");
            searchBarObj.transform.SetParent(panelObj.transform, false);
            var searchBg = searchBarObj.AddComponent<Image>();
            searchBg.color = new Color(0.08f, 0.08f, 0.08f);
            var searchRect = searchBarObj.GetComponent<RectTransform>();
            searchRect.anchorMin = new Vector2(0, 1);
            searchRect.anchorMax = new Vector2(1, 1);
            searchRect.pivot = new Vector2(0.5f, 1);
            searchRect.anchoredPosition = new Vector2(0, -38);
            searchRect.sizeDelta = new Vector2(-12, 28);

            var searchTextObj = new GameObject("SearchText");
            searchTextObj.transform.SetParent(searchBarObj.transform, false);
            var searchTextRect = searchTextObj.AddComponent<RectTransform>();
            searchTextRect.anchorMin = Vector2.zero;
            searchTextRect.anchorMax = Vector2.one;
            searchTextRect.offsetMin = new Vector2(6, 2);
            searchTextRect.offsetMax = new Vector2(-6, -2);
            var searchTmp = searchTextObj.AddComponent<TextMeshProUGUI>();
            searchTmp.fontSize = 14;
            searchTmp.color = Color.white;
            searchTmp.alignment = TextAlignmentOptions.Left;
            searchTmp.richText = false;

            var placeholderObj = new GameObject("Placeholder");
            placeholderObj.transform.SetParent(searchBarObj.transform, false);
            var phRect = placeholderObj.AddComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = new Vector2(6, 2);
            phRect.offsetMax = new Vector2(-6, -2);
            var phTmp = placeholderObj.AddComponent<TextMeshProUGUI>();
            phTmp.fontSize = 14;
            phTmp.color = new Color(1f, 1f, 1f, 0.3f);
            phTmp.alignment = TextAlignmentOptions.Left;
            phTmp.text = "Search decals...";
            phTmp.fontStyle = FontStyles.Italic;

            var searchInput = searchBarObj.AddComponent<TMP_InputField>();
            searchInput.textComponent = searchTmp;
            searchInput.placeholder = phTmp;
            searchInput.text = _decalSearchTerm;

            // Tint color bar
            BuildTintBar(panelObj.transform);

            // Scrollable grid area
            var scrollGO = new GameObject("ScrollArea");
            scrollGO.transform.SetParent(panelObj.transform, false);
            var scrollRT = scrollGO.AddComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0, 0);
            scrollRT.anchorMax = new Vector2(1, 1);
            scrollRT.offsetMin = new Vector2(6, 44);
            scrollRT.offsetMax = new Vector2(-6, -102);

            var scrollRectComp = scrollGO.AddComponent<ScrollRect>();
            scrollRectComp.horizontal = false;
            scrollRectComp.movementType = ScrollRect.MovementType.Elastic;
            scrollRectComp.elasticity = 0.08f;
            scrollRectComp.inertia = true;
            scrollRectComp.decelerationRate = 0.06f;
            scrollRectComp.scrollSensitivity = 24f;

            // Viewport
            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);
            var viewportRT = viewportGO.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            var viewportImg = viewportGO.AddComponent<Image>();
            viewportImg.color = new Color(0, 0, 0, 0);
            viewportImg.raycastTarget = true;
            viewportGO.AddComponent<RectMask2D>();
            scrollRectComp.viewport = viewportRT;

            // Content with GridLayoutGroup
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0, 1);
            contentRT.sizeDelta = Vector2.zero;

            var grid = contentGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(DecalCellSize, DecalCellSize + 20f);
            grid.spacing = new Vector2(DecalCellSpacing, DecalCellSpacing);
            grid.padding = new RectOffset(6, 6, 6, 6);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.Flexible;

            var csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRectComp.content = contentRT;

            var gridContent = contentGO.transform;
            PopulateDecalGrid(gridContent, _decalSearchTerm);

            // Wire up search to rebuild grid on change
            var capturedGrid = gridContent;
            searchInput.onValueChanged.AddListener(new Action<string>(term =>
            {
                _decalSearchTerm = term;
                for (int i = capturedGrid.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(capturedGrid.GetChild(i).gameObject);
                PopulateDecalGrid(capturedGrid, term);
            }));

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

            var (clearMask, clearBtn, clearLabel) = UIHelper.RoundedButtonWithLabel(
                "ClearDecalsBtn", $"Clear Decals ({_decalSpawns.Count})", btnRowObj.transform,
                new Color(0.5f, 0.35f, 0.1f), 140, 28, 15, Color.white);
            clearBtn.onClick.AddListener(new Action(() =>
            {
                int destroyed = DestroyDecalSpawns();
                _lastAction = destroyed > 0 ? $"Cleared {destroyed} decal(s)" : "No decals to clear";
                ShowDecalPanel();
            }));

            var (importMask, importBtn, importLabel) = UIHelper.RoundedButtonWithLabel(
                "ImportBtn", "Import Custom", btnRowObj.transform,
                new Color(0.2f, 0.35f, 0.5f), 140, 28, 15, Color.white);
            importBtn.onClick.AddListener(new Action(() => ShowDecalImportDialog()));

            var (closeMask, closeBtn, closeLabel) = UIHelper.RoundedButtonWithLabel(
                "CloseBtn", "Close (Del)", btnRowObj.transform,
                new Color(0.5f, 0.2f, 0.2f), 140, 28, 15, Color.white);
            closeBtn.onClick.AddListener(new Action(() =>
            {
                CloseDecalPanel();
                _lastAction = "Decal panel closed";
            }));

            // Free cursor for panel interaction
            _cursorFree = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _lastAction = $"Decal browser: {_decalTextures.Count} textures";
        }

        // ═══════════════════════════════════════════════════════════════
        // Tint Color Bar
        // ═══════════════════════════════════════════════════════════════

        private void BuildTintBar(Transform panelParent)
        {
            _tintSwatchBorders.Clear();

            var tintBarObj = new GameObject("TintBar");
            tintBarObj.transform.SetParent(panelParent, false);
            var tintBarRect = tintBarObj.AddComponent<RectTransform>();
            tintBarRect.anchorMin = new Vector2(0, 1);
            tintBarRect.anchorMax = new Vector2(1, 1);
            tintBarRect.pivot = new Vector2(0.5f, 1);
            tintBarRect.anchoredPosition = new Vector2(0, -70);
            tintBarRect.sizeDelta = new Vector2(-12, 30);

            var tintHLG = tintBarObj.AddComponent<HorizontalLayoutGroup>();
            tintHLG.spacing = 4;
            tintHLG.padding = new RectOffset(4, 4, 2, 2);
            tintHLG.childControlWidth = false;
            tintHLG.childControlHeight = true;
            tintHLG.childForceExpandWidth = false;
            tintHLG.childForceExpandHeight = true;
            tintHLG.childAlignment = TextAnchor.MiddleLeft;

            // "Tint:" label
            var tintLabel = UIHelper.Text("TintLabel", "<b>Tint:</b>", tintBarObj.transform, 13, TextAlignmentOptions.MidlineLeft);
            var tintLabelLayout = tintLabel.gameObject.AddComponent<LayoutElement>();
            tintLabelLayout.preferredWidth = 40;

            // Preset color circles
            for (int i = 0; i < _tintPresets.Length; i++)
            {
                var preset = _tintPresets[i];
                CreateTintSwatch(tintBarObj.transform, preset.color, i);
            }

            // Custom "+" button
            var customObj = new GameObject("Custom");
            customObj.transform.SetParent(tintBarObj.transform, false);
            var customLayout = customObj.AddComponent<LayoutElement>();
            customLayout.preferredWidth = 26;
            var customBg = customObj.AddComponent<Image>();
            customBg.color = new Color(0.25f, 0.25f, 0.3f);
            var customBtn = customObj.AddComponent<Button>();
            customBtn.targetGraphic = customBg;
            customBtn.onClick.AddListener(new Action(() => ShowDecalColorPicker()));

            var customLabel = new GameObject("Label");
            customLabel.transform.SetParent(customObj.transform, false);
            var customLabelRT = customLabel.AddComponent<RectTransform>();
            customLabelRT.anchorMin = Vector2.zero;
            customLabelRT.anchorMax = Vector2.one;
            customLabelRT.offsetMin = Vector2.zero;
            customLabelRT.offsetMax = Vector2.zero;
            var customTmp = customLabel.AddComponent<TextMeshProUGUI>();
            customTmp.text = "+";
            customTmp.fontSize = 16;
            customTmp.color = Color.white;
            customTmp.alignment = TextAlignmentOptions.Center;
            customTmp.raycastTarget = false;

            UpdateTintHighlight();
        }

        private void CreateTintSwatch(Transform parent, Color color, int index)
        {
            // Border (highlight container)
            var borderObj = new GameObject($"Swatch_{index}");
            borderObj.transform.SetParent(parent, false);
            var borderLayout = borderObj.AddComponent<LayoutElement>();
            borderLayout.preferredWidth = 26;
            var borderImg = borderObj.AddComponent<Image>();
            borderImg.color = Color.clear;
            _tintSwatchBorders.Add(borderImg);

            // Inner color swatch
            var swatchObj = new GameObject("Color");
            swatchObj.transform.SetParent(borderObj.transform, false);
            var swatchRT = swatchObj.AddComponent<RectTransform>();
            swatchRT.anchorMin = Vector2.zero;
            swatchRT.anchorMax = Vector2.one;
            swatchRT.offsetMin = new Vector2(2, 2);
            swatchRT.offsetMax = new Vector2(-2, -2);
            var swatchImg = swatchObj.AddComponent<Image>();
            swatchImg.color = color;
            swatchImg.raycastTarget = false;

            var btn = borderObj.AddComponent<Button>();
            btn.targetGraphic = borderImg;
            var capturedColor = color;
            btn.onClick.AddListener(new Action(() =>
            {
                _decalTintColor = capturedColor;
                UpdateTintHighlight();
            }));
        }

        private void UpdateTintHighlight()
        {
            for (int i = 0; i < _tintSwatchBorders.Count && i < _tintPresets.Length; i++)
            {
                bool selected = ColorsApproxEqual(_decalTintColor, _tintPresets[i].color);
                _tintSwatchBorders[i].color = selected ? Color.white : Color.clear;
            }
        }

        private static bool ColorsApproxEqual(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

        // ═══════════════════════════════════════════════════════════════
        // Decal Color Picker
        // ═══════════════════════════════════════════════════════════════

        private void ShowDecalColorPicker()
        {
            if (_decalColorPicker != null)
                UnityEngine.Object.Destroy(_decalColorPicker);

            _decalColorPicker = new GameObject("DecalColorPicker");
            _decalColorPicker.transform.SetParent(_decalPanel.transform, false);
            var overlayImg = _decalColorPicker.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.6f);
            overlayImg.raycastTarget = true;
            var overlayRect = _decalColorPicker.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            var dialogObj = UIHelper.Panel("ColorDialog", _decalColorPicker.transform,
                new Color(0.12f, 0.12f, 0.15f));
            var dialogRect = dialogObj.GetComponent<RectTransform>();
            dialogRect.anchorMin = new Vector2(0.3f, 0.25f);
            dialogRect.anchorMax = new Vector2(0.7f, 0.75f);

            var title = UIHelper.Text("Title", "<b>Decal Tint Color</b>",
                dialogObj.transform, 16, TextAlignmentOptions.Center);
            var titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.88f);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(8, 0);
            titleRect.offsetMax = new Vector2(-8, -4);

            // Color preview swatch
            var previewObj = UIHelper.Panel("ColorPreview", dialogObj.transform, _decalTintColor);
            var previewRect = previewObj.GetComponent<RectTransform>();
            previewRect.anchorMin = new Vector2(0.35f, 0.72f);
            previewRect.anchorMax = new Vector2(0.65f, 0.86f);
            var previewImg = previewObj.GetComponent<Image>();

            // RGB Sliders (reusing same pattern as PreviewPanel)
            var rSlider = CreateDecalColorSlider(dialogObj.transform, "R", _decalTintColor.r,
                new Color(1, 0.3f, 0.3f), 0.52f, 0.68f);
            var gSlider = CreateDecalColorSlider(dialogObj.transform, "G", _decalTintColor.g,
                new Color(0.3f, 1, 0.3f), 0.34f, 0.50f);
            var bSlider = CreateDecalColorSlider(dialogObj.transform, "B", _decalTintColor.b,
                new Color(0.4f, 0.6f, 1), 0.16f, 0.32f);

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

            var (_, applyBtn, _) = UIHelper.RoundedButtonWithLabel(
                "ApplyBtn", "Apply", btnRow.transform,
                new Color(0.15f, 0.4f, 0.15f), 80, 24, 14, Color.white);
            applyBtn.onClick.AddListener(new Action(() =>
            {
                _decalTintColor = new Color(rSlider.value, gSlider.value, bSlider.value, 1f);
                UpdateTintHighlight();
                UnityEngine.Object.Destroy(_decalColorPicker);
                _decalColorPicker = null;
            }));

            var (_, cancelBtn, _) = UIHelper.RoundedButtonWithLabel(
                "CancelBtn", "Cancel", btnRow.transform,
                new Color(0.4f, 0.2f, 0.2f), 80, 24, 14, Color.white);
            cancelBtn.onClick.AddListener(new Action(() =>
            {
                UnityEngine.Object.Destroy(_decalColorPicker);
                _decalColorPicker = null;
            }));
        }

        private Slider CreateDecalColorSlider(Transform parent, string label, float initialValue,
            Color trackColor, float anchorYMin, float anchorYMax)
        {
            var rowObj = new GameObject($"Slider_{label}");
            rowObj.transform.SetParent(parent, false);
            var rowRect = rowObj.AddComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0, anchorYMin);
            rowRect.anchorMax = new Vector2(1, anchorYMax);
            rowRect.offsetMin = new Vector2(12, 2);
            rowRect.offsetMax = new Vector2(-12, -2);

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

            var sliderObj = new GameObject("Slider");
            sliderObj.transform.SetParent(rowObj.transform, false);
            var sliderRect = sliderObj.AddComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0.12f, 0.2f);
            sliderRect.anchorMax = new Vector2(0.84f, 0.8f);
            sliderRect.offsetMin = Vector2.zero;
            sliderRect.offsetMax = Vector2.zero;

            var bgObj = UIHelper.Panel("Background", sliderObj.transform, new Color(0.2f, 0.2f, 0.25f));
            var bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

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
        // Grid population
        // ═══════════════════════════════════════════════════════════════

        private void PopulateDecalGrid(Transform gridContent, string filter)
        {
            string lowerFilter = string.IsNullOrEmpty(filter) ? null : filter.ToLowerInvariant();
            int shown = 0;

            foreach (var tex in _decalTextures)
            {
                if (tex == null) continue;
                if (lowerFilter != null && !tex.name.ToLowerInvariant().Contains(lowerFilter))
                    continue;

                shown++;
                var capturedTex = tex;

                var cellObj = new GameObject($"Cell_{tex.name}");
                cellObj.transform.SetParent(gridContent, false);
                cellObj.AddComponent<RectTransform>();

                var cellBg = cellObj.AddComponent<Image>();
                cellBg.color = new Color(0.18f, 0.2f, 0.18f);
                var cellBtn = cellObj.AddComponent<Button>();
                cellBtn.targetGraphic = cellBg;
                cellBtn.onClick.AddListener(new Action(() => SpawnDecal(capturedTex)));

                var previewObj = new GameObject("Preview");
                previewObj.transform.SetParent(cellObj.transform, false);
                var previewRT = previewObj.AddComponent<RectTransform>();
                previewRT.anchorMin = new Vector2(0, 0.15f);
                previewRT.anchorMax = Vector2.one;
                previewRT.offsetMin = new Vector2(4, 0);
                previewRT.offsetMax = new Vector2(-4, -4);
                var rawImg = previewObj.AddComponent<RawImage>();
                rawImg.texture = tex;
                rawImg.raycastTarget = false;

                var labelObj = new GameObject("Label");
                labelObj.transform.SetParent(cellObj.transform, false);
                var labelRT = labelObj.AddComponent<RectTransform>();
                labelRT.anchorMin = Vector2.zero;
                labelRT.anchorMax = new Vector2(1, 0.15f);
                labelRT.offsetMin = new Vector2(2, 0);
                labelRT.offsetMax = new Vector2(-2, 0);
                var labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
                labelTmp.text = tex.name;
                labelTmp.fontSize = 10;
                labelTmp.color = new Color(0.8f, 0.8f, 0.8f);
                labelTmp.alignment = TextAlignmentOptions.Center;
                UIHelper.SetWrapping(labelTmp, false);
                labelTmp.overflowMode = TextOverflowModes.Ellipsis;
                labelTmp.raycastTarget = false;
            }

            if (shown == 0)
            {
                var emptyObj = new GameObject("Empty");
                emptyObj.transform.SetParent(gridContent, false);
                var emptyRT = emptyObj.AddComponent<RectTransform>();
                emptyRT.sizeDelta = new Vector2(400, 40);
                var emptyTmp = emptyObj.AddComponent<TextMeshProUGUI>();
                emptyTmp.text = lowerFilter != null ? "<i>No matches</i>" : "<i>No decal textures found</i>";
                emptyTmp.fontSize = 15;
                emptyTmp.color = Color.white;
                emptyTmp.alignment = TextAlignmentOptions.Center;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Texture scanning
        // ═══════════════════════════════════════════════════════════════

        private void ScanDecalTextures()
        {
            // Load previously imported custom decals from disk (once per session)
            if (!_importedDecalsLoaded)
            {
                LoadImportedDecals();
                _importedDecalsLoaded = true;
            }

            _decalTextures = new List<Texture2D>();
            var allTextures = Resources.FindObjectsOfTypeAll<Texture2D>();

            foreach (var tex in allTextures)
            {
                if (tex == null || string.IsNullOrEmpty(tex.name)) continue;

                bool prefixMatch = false;
                foreach (var prefix in _decalPrefixes)
                {
                    if (tex.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        prefixMatch = true;
                        break;
                    }
                }
                if (!prefixMatch) continue;

                string nameLower = tex.name.ToLowerInvariant();
                bool isAux = false;
                foreach (var keyword in _decalFilterKeywords)
                {
                    if (nameLower.Contains(keyword.ToLowerInvariant()))
                    {
                        isAux = true;
                        break;
                    }
                }
                if (isAux) continue;

                _decalTextures.Add(tex);
            }

            // Include registered decals from mods
            var registered = MeshVaultAPI.ListRegisteredDecals();
            foreach (var id in registered)
            {
                var regTex = MeshVaultAPI.GetRegisteredDecal(id);
                if (regTex != null && !_decalTextures.Contains(regTex))
                    _decalTextures.Add(regTex);
            }

            _decalTextures.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            Melon<MeshVaultPlugin>.Logger.Msg($"[DecalBrowser] Found {_decalTextures.Count} decal textures ({registered.Length} registered)");
        }

        // ═══════════════════════════════════════════════════════════════
        // Spawning
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Finds and caches a base decal material by cloning one from an existing scene DecalProjector.
        /// Cloning the full material preserves shader keywords and render settings that are
        /// lost when creating a Material from just the shader.
        /// </summary>
        private Material FindDecalBaseMaterial()
        {
            if (_decalBaseMaterial != null) return _decalBaseMaterial;

            // Clone from an existing scene DecalProjector (most reliable — gets all shader keywords)
            var existingProjectors = UnityEngine.Object.FindObjectsOfType<DecalProjector>(true);
            foreach (var p in existingProjectors)
            {
                if (p != null && p.material != null && p.material.shader != null)
                {
                    _decalBaseMaterial = new Material(p.material);
                    Melon<MeshVaultPlugin>.Logger.Msg($"[DecalBrowser] Cloned decal material, shader: {_decalBaseMaterial.shader.name}");
                    return _decalBaseMaterial;
                }
            }

            // Fallback: create from shader name (may lack keywords)
            var shader = Shader.Find("Shader Graphs/Decal")
                      ?? Shader.Find("Universal Render Pipeline/Decal");
            if (shader != null)
            {
                _decalBaseMaterial = new Material(shader);
                Melon<MeshVaultPlugin>.Logger.Msg($"[DecalBrowser] Created decal material from shader: {shader.name}");
            }
            else
            {
                Melon<MeshVaultPlugin>.Logger.Warning("[DecalBrowser] Could not find a decal shader");
            }

            return _decalBaseMaterial;
        }

        /// <summary>
        /// Spawns a DecalProjector with the given texture, aligned to the surface the camera is looking at.
        /// Naturally clips to surface geometry boundaries.
        /// </summary>
        private void SpawnDecal(Texture2D tex)
        {
            if (tex == null) return;

            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam == null) { _lastAction = "No camera"; return; }

            var baseMat = FindDecalBaseMaterial();
            if (baseMat == null)
            {
                _lastAction = "No decal shader found";
                return;
            }

            var go = new GameObject($"MV_Decal_{tex.name}");
            var projector = go.AddComponent<DecalProjector>();

            // Clone the base material so each decal gets its own texture/tint
            var mat = new Material(baseMat);
            mat.SetTexture(MeshVaultAPI.DecalPropBaseMap, tex);
            mat.SetColor(MeshVaultAPI.DecalPropColor, _decalTintColor);

            projector.material = mat;
            projector.size = new Vector3(1f, 1f, MeshVaultAPI.DecalDepth);
            projector.pivot = new Vector3(0f, 0f, MeshVaultAPI.DecalDepth * 0.5f);
            projector.fadeFactor = 1f;
            projector.renderingLayerMask = uint.MaxValue;

            // Wall-align on spawn: raycast from camera forward
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            int mask = ~(1 << LayerMask.NameToLayer("Player") | 1 << LayerMask.NameToLayer("NoCollide"));
            Vector3 spawnPos;
            Quaternion spawnRot;

            if (Physics.Raycast(ray, out RaycastHit hit, 100f, mask))
            {
                // Position slightly off the wall — small offset to avoid z-fighting
                spawnPos = hit.point + hit.normal * 0.01f;
                Vector3 up = Mathf.Abs(Vector3.Dot(hit.normal, Vector3.up)) > 0.99f
                    ? Vector3.forward : Vector3.up;
                spawnRot = Quaternion.LookRotation(-hit.normal, up);
            }
            else
            {
                spawnPos = cam.transform.position + cam.transform.forward * 5f;
                spawnRot = Quaternion.LookRotation(cam.transform.forward);
            }

            go.transform.position = spawnPos;
            go.transform.rotation = spawnRot;

            _decalSpawns.Add(go);

            // Enter positioner for fine-tuning
            CloseDecalPanel();

            _preview = go;
            _previewSourceName = $"Decal: {tex.name}";
            _previewDbId = null;
            _previewDecalTexName = tex.name;
            _previewPosition = new Vector3(
                Mathf.Round(spawnPos.x * 100f) / 100f,
                Mathf.Round(spawnPos.y * 100f) / 100f,
                Mathf.Round(spawnPos.z * 100f) / 100f);
            _previewRotation = spawnRot.eulerAngles;
            _previewScale = Vector3.one;
            _previewIsLiveObject = false;
            _mode = EditMode.Position;
            _positionerMaterialOverrides = null;
            _positionerColorOverrides = null;

            ShowEditorPanel();
            _lastAction = $"Positioning decal \"{tex.name}\" — numpad to adjust, Enter to log, Del to cancel";
        }

        // ═══════════════════════════════════════════════════════════════
        // Cleanup
        // ═══════════════════════════════════════════════════════════════

        private int DestroyDecalSpawns()
        {
            int count = 0;
            foreach (var go in _decalSpawns)
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                    count++;
                }
            }
            _decalSpawns.Clear();
            return count;
        }

        private void CloseDecalPanel()
        {
            if (_decalImportDialog != null)
            {
                UnityEngine.Object.Destroy(_decalImportDialog);
                _decalImportDialog = null;
            }
            if (_decalColorPicker != null)
            {
                UnityEngine.Object.Destroy(_decalColorPicker);
                _decalColorPicker = null;
            }
            if (_decalPanel != null)
            {
                UnityEngine.Object.Destroy(_decalPanel);
                _decalPanel = null;
            }
            _tintSwatchBorders.Clear();
            if (_active && _editorPanel == null)
            {
                _cursorFree = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Import Custom Decal
        // ═══════════════════════════════════════════════════════════════

        private static string _decalImportDir =>
            Path.Combine(Application.dataPath, "..", "UserData", "MeshVault", "Decals");

        /// <summary>
        /// Shows a dialog for importing a custom decal texture from a file path.
        /// The file is copied to UserData/MeshVault/Decals/ with a content-based hash prefix.
        /// </summary>
        private void ShowDecalImportDialog()
        {
            if (_decalImportDialog != null)
                UnityEngine.Object.Destroy(_decalImportDialog);

            _decalImportDialog = new GameObject("DecalImportDialog");
            _decalImportDialog.transform.SetParent(_decalPanel.transform, false);
            var overlayImg = _decalImportDialog.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.6f);
            overlayImg.raycastTarget = true;
            var overlayRect = _decalImportDialog.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            var dialogObj = UIHelper.Panel("ImportDialog", _decalImportDialog.transform,
                new Color(0.12f, 0.12f, 0.15f));
            var dialogRect = dialogObj.GetComponent<RectTransform>();
            dialogRect.anchorMin = new Vector2(0.2f, 0.35f);
            dialogRect.anchorMax = new Vector2(0.8f, 0.65f);

            var title = UIHelper.Text("Title", "<b>Import Custom Decal</b>",
                dialogObj.transform, 16, TextAlignmentOptions.Center);
            var titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.82f);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(8, 0);
            titleRect.offsetMax = new Vector2(-8, -4);

            var hint = UIHelper.Text("Hint",
                "Paste the full path to a PNG or JPG file:",
                dialogObj.transform, 13, TextAlignmentOptions.MidlineLeft);
            var hintRect = hint.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0, 0.62f);
            hintRect.anchorMax = new Vector2(1, 0.80f);
            hintRect.offsetMin = new Vector2(12, 0);
            hintRect.offsetMax = new Vector2(-12, 0);

            // Path input
            var inputBarObj = new GameObject("PathInput");
            inputBarObj.transform.SetParent(dialogObj.transform, false);
            var inputBg = inputBarObj.AddComponent<Image>();
            inputBg.color = new Color(0.08f, 0.08f, 0.08f);
            var inputRect = inputBarObj.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0, 0.38f);
            inputRect.anchorMax = new Vector2(1, 0.60f);
            inputRect.offsetMin = new Vector2(12, 4);
            inputRect.offsetMax = new Vector2(-12, -4);

            var inputTextObj = new GameObject("InputText");
            inputTextObj.transform.SetParent(inputBarObj.transform, false);
            var inputTextRect = inputTextObj.AddComponent<RectTransform>();
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = new Vector2(6, 2);
            inputTextRect.offsetMax = new Vector2(-6, -2);
            var inputTmp = inputTextObj.AddComponent<TextMeshProUGUI>();
            inputTmp.fontSize = 13;
            inputTmp.color = Color.white;
            inputTmp.alignment = TextAlignmentOptions.MidlineLeft;
            inputTmp.richText = false;

            var phObj = new GameObject("Placeholder");
            phObj.transform.SetParent(inputBarObj.transform, false);
            var phRect = phObj.AddComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = new Vector2(6, 2);
            phRect.offsetMax = new Vector2(-6, -2);
            var phTmp = phObj.AddComponent<TextMeshProUGUI>();
            phTmp.fontSize = 13;
            phTmp.color = new Color(1f, 1f, 1f, 0.3f);
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;
            phTmp.text = "C:\\path\\to\\decal.png";
            phTmp.fontStyle = FontStyles.Italic;

            var pathInput = inputBarObj.AddComponent<TMP_InputField>();
            pathInput.textComponent = inputTmp;
            pathInput.placeholder = phTmp;

            // Status label
            var statusLabel = UIHelper.Text("Status", "",
                dialogObj.transform, 12, TextAlignmentOptions.Center);
            var statusRect = statusLabel.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0, 0.22f);
            statusRect.anchorMax = new Vector2(1, 0.38f);
            statusRect.offsetMin = new Vector2(12, 0);
            statusRect.offsetMax = new Vector2(-12, 0);
            statusLabel.color = new Color(1f, 0.8f, 0.3f);

            // Button row
            var btnRow = new GameObject("BtnRow");
            btnRow.transform.SetParent(dialogObj.transform, false);
            var btnRowRect = btnRow.AddComponent<RectTransform>();
            btnRowRect.anchorMin = new Vector2(0, 0);
            btnRowRect.anchorMax = new Vector2(1, 0.22f);
            btnRowRect.offsetMin = new Vector2(8, 4);
            btnRowRect.offsetMax = new Vector2(-8, -2);
            var btnRowHLG = btnRow.AddComponent<HorizontalLayoutGroup>();
            btnRowHLG.spacing = 6;
            btnRowHLG.childControlWidth = true;
            btnRowHLG.childControlHeight = true;
            btnRowHLG.childForceExpandWidth = true;
            btnRowHLG.childForceExpandHeight = true;

            var (_, importBtn, _) = UIHelper.RoundedButtonWithLabel(
                "ImportBtn", "Import", btnRow.transform,
                new Color(0.15f, 0.4f, 0.15f), 80, 24, 14, Color.white);
            importBtn.onClick.AddListener(new Action(() =>
            {
                string result = ImportDecalFromFile(pathInput.text);
                if (result != null)
                {
                    statusLabel.text = $"<color=#88ff88>{result}</color>";
                    // Rebuild the decal list so the new texture appears
                    _decalTextures = null;
                    ShowDecalPanel();
                }
                else
                {
                    statusLabel.text = "<color=#ff8888>Import failed — check log</color>";
                }
            }));

            var (_, cancelBtn, _) = UIHelper.RoundedButtonWithLabel(
                "CancelBtn", "Cancel", btnRow.transform,
                new Color(0.4f, 0.2f, 0.2f), 80, 24, 14, Color.white);
            cancelBtn.onClick.AddListener(new Action(() =>
            {
                UnityEngine.Object.Destroy(_decalImportDialog);
                _decalImportDialog = null;
            }));
        }

        /// <summary>
        /// Imports a decal texture from a file path. Copies the file to UserData/MeshVault/Decals/
        /// with a content-based SHA256 hash prefix to prevent duplicates.
        /// </summary>
        /// <returns>A success message, or null on failure.</returns>
        private string ImportDecalFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Melon<MeshVaultPlugin>.Logger.Warning($"{DecalImportLogPrefix} No file path provided");
                return null;
            }

            filePath = filePath.Trim().Trim('"');
            if (!File.Exists(filePath))
            {
                Melon<MeshVaultPlugin>.Logger.Warning($"{DecalImportLogPrefix} File not found: {filePath}");
                return null;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (!_supportedImageExtensions.Contains(ext))
            {
                Melon<MeshVaultPlugin>.Logger.Warning($"{DecalImportLogPrefix} Unsupported format: {ext} (use PNG or JPG)");
                return null;
            }

            byte[] fileData;
            try
            {
                fileData = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                Melon<MeshVaultPlugin>.Logger.Error($"{DecalImportLogPrefix} Failed to read file: {ex.Message}");
                return null;
            }

            // Compute content-based hash for deduplication
            string hashPrefix;
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(fileData);
                hashPrefix = BitConverter.ToString(hash, 0, HashByteCount).Replace("-", "").ToLowerInvariant();
            }

            string originalName = Path.GetFileNameWithoutExtension(filePath);
            string destName = $"{hashPrefix}_{originalName}{ext}";
            string destDir = Path.GetFullPath(_decalImportDir);
            string destPath = Path.Combine(destDir, destName);

            // Copy to userdata if not already there
            if (!File.Exists(destPath))
            {
                try
                {
                    Directory.CreateDirectory(destDir);
                    File.Copy(filePath, destPath);
                    Melon<MeshVaultPlugin>.Logger.Msg($"{DecalImportLogPrefix} Copied to: {destPath}");
                }
                catch (Exception ex)
                {
                    Melon<MeshVaultPlugin>.Logger.Error($"{DecalImportLogPrefix} Failed to copy file: {ex.Message}");
                    return null;
                }
            }
            else
            {
                Melon<MeshVaultPlugin>.Logger.Msg($"{DecalImportLogPrefix} File already exists: {destPath}");
            }

            // Load as texture and register directly (bypasses prefix system)
            string texId = $"{hashPrefix}_{originalName}";
            if (MeshVaultAPI.GetRegisteredDecal(texId) != null)
            {
                Melon<MeshVaultPlugin>.Logger.Msg($"{DecalImportLogPrefix} Decal \"{texId}\" already registered");
                return $"Already imported: {texId}";
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, fileData))
            {
                Melon<MeshVaultPlugin>.Logger.Warning($"{DecalImportLogPrefix} Failed to load image data from {filePath}");
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            tex.name = texId;
            tex.filterMode = FilterMode.Bilinear;

            MeshVaultAPI.RegisterDecalDirect(texId, tex);
            Melon<MeshVaultPlugin>.Logger.Msg($"{DecalImportLogPrefix} Registered decal: {texId} ({tex.width}x{tex.height})");
            return $"Imported: {texId}";
        }

        /// <summary>
        /// Loads any previously imported custom decal files from UserData/MeshVault/Decals/.
        /// Uses RegisterDecalDirect to bypass the prefix system.
        /// </summary>
        private void LoadImportedDecals()
        {
            string dir = Path.GetFullPath(_decalImportDir);
            if (!Directory.Exists(dir)) return;

            string[] files = Directory.GetFiles(dir, "*.*");
            int loaded = 0;

            foreach (string file in files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (!_supportedImageExtensions.Contains(ext)) continue;

                string texId = Path.GetFileNameWithoutExtension(file);
                if (MeshVaultAPI.GetRegisteredDecal(texId) != null) continue;

                byte[] data;
                try
                {
                    data = File.ReadAllBytes(file);
                }
                catch (Exception ex)
                {
                    Melon<MeshVaultPlugin>.Logger.Warning($"{DecalImportLogPrefix} Failed to read {file}: {ex.Message}");
                    continue;
                }

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, data))
                {
                    Melon<MeshVaultPlugin>.Logger.Warning($"{DecalImportLogPrefix} Failed to load image: {file}");
                    UnityEngine.Object.Destroy(tex);
                    continue;
                }

                tex.name = texId;
                tex.filterMode = FilterMode.Bilinear;

                if (MeshVaultAPI.RegisterDecalDirect(texId, tex))
                    loaded++;
                else
                    UnityEngine.Object.Destroy(tex);
            }

            if (loaded > 0)
                Melon<MeshVaultPlugin>.Logger.Msg($"{DecalImportLogPrefix} Loaded {loaded} imported decal(s) from disk");
        }
    }
}
#endif
