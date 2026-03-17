#if DEBUG
using System;
using UnityEngine;
using UnityEngine.UI;

#if IL2CPP
using Il2CppTMPro;
using GameCanvasScaler = Il2CppScheduleOne.UI.CanvasScaler;
#else
using TMPro;
using GameCanvasScaler = ScheduleOne.UI.CanvasScaler;
#endif

namespace MeshVault.Tools
{
    public partial class MeshPlacer
    {
        // Editor panel state
        private GameObject _editorPanel;
        private TextMeshProUGUI[] _editorAxisLabels;
        private TextMeshProUGUI _editorStepLabel;
        private TextMeshProUGUI _editorSourceLabel;
        private Image[] _editorModeTabImages;

        // ═══════════════════════════════════════════════════════════════
        // Editor Panel
        // ═══════════════════════════════════════════════════════════════

        private static readonly Color EditorBg = new Color(0.08f, 0.1f, 0.08f, 0.92f);
        private static readonly Color TabActive = new Color(0.15f, 0.4f, 0.15f);
        private static readonly Color TabInactive = new Color(0.2f, 0.22f, 0.2f);
        private static readonly Color AxisBtnColor = new Color(0.25f, 0.28f, 0.25f);
        private static readonly Color LogBtnColor = new Color(0.15f, 0.4f, 0.15f);
        private static readonly Color DelBtnColor = new Color(0.5f, 0.15f, 0.15f);

        private const float PanelW = 280f;
        private const float PanelH = 210f;

        private void ShowEditorPanel()
        {
            CloseEditorPanel();

            _editorPanel = new GameObject("MV_EditorPanel");
            var canvas = _editorPanel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 91;

            var scaler = _editorPanel.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _editorPanel.AddComponent<GameCanvasScaler>();
            _editorPanel.AddComponent<GraphicRaycaster>();

            // Main panel — bottom-right, draggable
            var panelObj = UIHelper.Panel("Panel", _editorPanel.transform, EditorBg);
            var panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1, 0);
            panelRect.anchorMax = new Vector2(1, 0);
            panelRect.pivot = new Vector2(1, 0);
            panelRect.anchoredPosition = new Vector2(-10, 10);
            panelRect.sizeDelta = new Vector2(PanelW, PanelH);

            panelObj.GetComponent<Image>().raycastTarget = true;
            UIHelper.MakeDraggable(panelObj);

            // All children use absolute anchor positions within the panel.
            // Y coordinates: top=PanelH, bottom=0.

            // ── Row 1: Mode tabs (Pos, Rot, Scl) + Step label ──
            float y = PanelH - 8;
            float tabW = 55f, tabH = 24f, tabGap = 4f;
            _editorModeTabImages = new Image[3];
            string[] tabLabels = { "Pos", "Rot", "Scl" };
            EditMode[] tabModes = { EditMode.Position, EditMode.Rotation, EditMode.Scale };
            for (int i = 0; i < 3; i++)
            {
                float tx = 8 + i * (tabW + tabGap);
                var capturedMode = tabModes[i];
                var (maskGO, btn, _) = UIHelper.RoundedButtonWithLabel(
                    $"Tab_{tabLabels[i]}", tabLabels[i], panelObj.transform,
                    _mode == capturedMode ? TabActive : TabInactive,
                    tabW, tabH, 13, Color.white);
                SetAnchored(maskGO, panelRect, tx, y - tabH, tabW, tabH);
                _editorModeTabImages[i] = maskGO.transform.GetChild(0).GetComponent<Image>();
                btn.onClick.AddListener(new Action(() => SetEditMode(capturedMode)));
            }

            // Step label (right side of row 1)
            _editorStepLabel = UIHelper.Text("StepLabel", "", panelObj.transform, 13, TextAlignmentOptions.MidlineRight);
            UIHelper.SetWrapping(_editorStepLabel, false);
            SetAnchored(_editorStepLabel.gameObject, panelRect, PanelW - 108, y - tabH, 100, tabH);

            // ── Row 2: PgDn / PgUp buttons (right-aligned) ──
            y -= tabH + 6;
            float pgBtnW = 48f, pgBtnH = 22f;

            var (pgUpMask, pgUpBtn, _) = UIHelper.RoundedButtonWithLabel(
                "PgUp", "PgUp", panelObj.transform, AxisBtnColor, pgBtnW, pgBtnH, 12, Color.white);
            SetAnchored(pgUpMask, panelRect, PanelW - 8 - pgBtnW, y - pgBtnH, pgBtnW, pgBtnH);
            pgUpBtn.onClick.AddListener(new Action(() =>
            {
                _stepIndex = Mathf.Min(_stepIndex + 1, StepSizes.Length - 1);
                _lastAction = $"Step: {StepSizes[_stepIndex]}";
                RefreshEditorPanel();
            }));

            var (pgDnMask, pgDnBtn, _) = UIHelper.RoundedButtonWithLabel(
                "PgDn", "PgDn", panelObj.transform, AxisBtnColor, pgBtnW, pgBtnH, 12, Color.white);
            SetAnchored(pgDnMask, panelRect, PanelW - 8 - pgBtnW - tabGap - pgBtnW, y - pgBtnH, pgBtnW, pgBtnH);
            pgDnBtn.onClick.AddListener(new Action(() =>
            {
                _stepIndex = Mathf.Max(_stepIndex - 1, 0);
                _lastAction = $"Step: {StepSizes[_stepIndex]}";
                RefreshEditorPanel();
            }));

            // ── Rows 3-5: Axis rows (X, Y, Z) ──
            y -= pgBtnH + 8;
            float axisRowH = 28f, axisBtnW = 36f;
            _editorAxisLabels = new TextMeshProUGUI[3];
            string[] axisNames = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                float rowY = y - i * (axisRowH + 4);

                // Value label — fills left side
                var label = UIHelper.Text($"Label_{axisNames[i]}", "", panelObj.transform, 14, TextAlignmentOptions.MidlineLeft);
                UIHelper.SetWrapping(label, false);
                SetAnchored(label.gameObject, panelRect, 8, rowY - axisRowH, PanelW - 8 - axisBtnW * 2 - tabGap - 12, axisRowH);
                _editorAxisLabels[i] = label;

                // − button
                float minusX = PanelW - 8 - axisBtnW * 2 - tabGap;
                var (minusMask, minusBtn, _) = UIHelper.RoundedButtonWithLabel(
                    $"Minus_{axisNames[i]}", "\u2212", panelObj.transform,
                    AxisBtnColor, axisBtnW, axisRowH - 2, 16, Color.white);
                SetAnchored(minusMask, panelRect, minusX, rowY - axisRowH + 1, axisBtnW, axisRowH - 2);
                minusBtn.onClick.AddListener(new Action(() =>
                {
                    Adjust(axis, -1);
                    ApplyPreviewTransform();
                    RefreshEditorPanel();
                }));

                // + button
                float plusX = PanelW - 8 - axisBtnW;
                var (plusMask, plusBtn, _) = UIHelper.RoundedButtonWithLabel(
                    $"Plus_{axisNames[i]}", "+", panelObj.transform,
                    AxisBtnColor, axisBtnW, axisRowH - 2, 16, Color.white);
                SetAnchored(plusMask, panelRect, plusX, rowY - axisRowH + 1, axisBtnW, axisRowH - 2);
                plusBtn.onClick.AddListener(new Action(() =>
                {
                    Adjust(axis, +1);
                    ApplyPreviewTransform();
                    RefreshEditorPanel();
                }));
            }

            // ── Bottom row: Source name + Log/Del buttons ──
            float logW = 44f, delW = 44f, bottomH = 26f;
            _editorSourceLabel = UIHelper.Text("Source", "", panelObj.transform, 12, TextAlignmentOptions.MidlineLeft);
            UIHelper.SetWrapping(_editorSourceLabel, false);
            _editorSourceLabel.overflowMode = TextOverflowModes.Ellipsis;
            SetAnchored(_editorSourceLabel.gameObject, panelRect, 8, 6, PanelW - logW - delW - 24, bottomH);

            var (logMask, logBtn, _) = UIHelper.RoundedButtonWithLabel(
                "LogBtn", "Log", panelObj.transform, LogBtnColor, logW, bottomH, 13, Color.white);
            SetAnchored(logMask, panelRect, PanelW - 8 - delW - tabGap - logW, 6, logW, bottomH);
            logBtn.onClick.AddListener(new Action(() => LogPlacement()));

            var (delMask, delBtn, _) = UIHelper.RoundedButtonWithLabel(
                "DelBtn", "Del", panelObj.transform, DelBtnColor, delW, bottomH, 13, Color.white);
            SetAnchored(delMask, panelRect, PanelW - 8 - delW, 6, delW, bottomH);
            delBtn.onClick.AddListener(new Action(() =>
            {
                DestroyPreview();
                _lastAction = "Preview cleared";
            }));

            RefreshEditorPanel();
        }

        /// <summary>
        /// Positions a child element at absolute pixel coordinates within a parent panel.
        /// Origin is bottom-left of parent. (x, y) is the bottom-left corner of the child.
        /// </summary>
        private static void SetAnchored(GameObject go, RectTransform parent, float x, float y, float w, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private void CloseEditorPanel()
        {
            if (_editorPanel != null)
            {
                UnityEngine.Object.Destroy(_editorPanel);
                _editorPanel = null;
            }
            _editorAxisLabels = null;
            _editorStepLabel = null;
            _editorSourceLabel = null;
            _editorModeTabImages = null;
        }

        private void RefreshEditorPanel()
        {
            if (_editorPanel == null) return;

            // Update mode tab highlights
            if (_editorModeTabImages != null)
            {
                _editorModeTabImages[0].color = _mode == EditMode.Position ? TabActive : TabInactive;
                _editorModeTabImages[1].color = _mode == EditMode.Rotation ? TabActive : TabInactive;
                _editorModeTabImages[2].color = _mode == EditMode.Scale ? TabActive : TabInactive;
            }

            // Update step label
            if (_editorStepLabel != null)
                _editorStepLabel.text = $"Step: {StepSizes[_stepIndex]}";

            // Update axis values
            if (_editorAxisLabels != null)
            {
                string[] axisNames = { "X", "Y", "Z" };
                for (int i = 0; i < 3; i++)
                {
                    if (_editorAxisLabels[i] == null) continue;
                    float val = _mode switch
                    {
                        EditMode.Position => _previewPosition[i],
                        EditMode.Rotation => _previewRotation[i],
                        EditMode.Scale => _previewScale[i],
                        _ => 0f
                    };
                    string fmt = _mode == EditMode.Rotation ? "F1" : "F2";
                    _editorAxisLabels[i].text = $"  {axisNames[i]}:  <b>{val.ToString(fmt)}</b>";
                }
            }

            // Update source label
            if (_editorSourceLabel != null)
                _editorSourceLabel.text = _previewSourceName ?? "";
        }

        private void SetEditMode(EditMode mode)
        {
            _mode = mode;
            _lastAction = $"Mode: {_mode}";
            RefreshEditorPanel();
        }

        private void ApplyPreviewTransform()
        {
            if (_preview == null) return;
            _preview.transform.position = _previewPosition;
            _preview.transform.eulerAngles = _previewRotation;
            _preview.transform.localScale = _previewScale;
        }
    }
}
#endif
