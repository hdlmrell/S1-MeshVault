#if DEBUG
using System;
using System.Collections.Generic;
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
        // Spawn panel state
        private GameObject _spawnPanel;
        private List<GameObject> _testSpawns = new List<GameObject>();

        // ═══════════════════════════════════════════════════════════════
        // Spawn Panel (Numpad0)
        // ═══════════════════════════════════════════════════════════════

        private void ShowSpawnPanel()
        {
            CloseSpawnPanel();

            var entries = MeshVaultAPI.ListMeshes();

            _spawnPanel = new GameObject("MeshPlacerSpawnPanel");
            var rootCanvas = _spawnPanel.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 93;

            var scaler = _spawnPanel.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _spawnPanel.AddComponent<GameCanvasScaler>();
            _spawnPanel.AddComponent<GraphicRaycaster>();

            // Dark overlay
            var overlayObj = UIHelper.Panel("Overlay", _spawnPanel.transform, new Color(0, 0, 0, 0.4f), fullAnchor: true);
            overlayObj.GetComponent<Image>().raycastTarget = true;

            // Panel — right side
            var panelObj = UIHelper.Panel("SpawnPanel", _spawnPanel.transform, new Color(0.1f, 0.12f, 0.1f));
            var panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.3f, 0.1f);
            panelRect.anchorMax = new Vector2(0.7f, 0.9f);

            // Title
            var titleText = UIHelper.Text("Title", $"<b>Mesh Database</b>  ({entries.Length} entries)", panelObj.transform, 17, TextAlignmentOptions.Center);
            var titleRect = titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.anchoredPosition = new Vector2(0, -5);
            titleRect.sizeDelta = new Vector2(0, 30);

            // Scrollable list
            var listContent = UIHelper.ScrollableVerticalList("SpawnList", panelObj.transform, out ScrollRect scrollRect);
            var scrollRectTransform = scrollRect.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0, 0);
            scrollRectTransform.anchorMax = new Vector2(1, 1);
            scrollRectTransform.offsetMin = new Vector2(6, 80);
            scrollRectTransform.offsetMax = new Vector2(-6, -40);

            // Fix content rect — same pattern as Browser.cs to prevent left-side clipping.
            // UIHelper's default pivot (0.5, 1) causes content to overflow both sides of the viewport.
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
                contentLayout.spacing = 3;
                contentLayout.padding = new RectOffset(4, 4, 4, 4);
                contentLayout.childControlHeight = true;
                contentLayout.childControlWidth = true;
                contentLayout.childForceExpandHeight = false;
                contentLayout.childForceExpandWidth = true;
            }

            if (entries.Length == 0)
            {
                var emptyText = UIHelper.Text("Empty", "<i>No entries — extract objects first</i>", listContent, 15, TextAlignmentOptions.Center);
                var emptyLayout = emptyText.gameObject.AddComponent<LayoutElement>();
                emptyLayout.preferredHeight = 40;
            }
            else
            {
                foreach (string entryId in entries)
                {
                    var entry = MeshVaultAPI.GetMesh(entryId);
                    if (entry == null) continue;

                    var rowObj = new GameObject($"Row_{entryId}");
                    rowObj.transform.SetParent(listContent, false);
                    rowObj.AddComponent<RectTransform>();
                    var rowBg = rowObj.AddComponent<Image>();
                    rowBg.color = new Color(0.15f, 0.18f, 0.15f);
                    var rowLayout = rowObj.AddComponent<LayoutElement>();
                    rowLayout.preferredHeight = 48;
                    rowLayout.flexibleWidth = 1;

                    // No HLG — manual anchor layout to avoid LayoutElement fighting

                    // Entry info — fills left side, leaves 228px for buttons on right
                    var infoObj = new GameObject("Info");
                    infoObj.transform.SetParent(rowObj.transform, false);
                    var infoRect = infoObj.AddComponent<RectTransform>();
                    infoRect.anchorMin = Vector2.zero;
                    infoRect.anchorMax = Vector2.one;
                    infoRect.offsetMin = new Vector2(6, 2);
                    infoRect.offsetMax = new Vector2(-228, -2);
                    var infoText = infoObj.AddComponent<TextMeshProUGUI>();
                    infoText.text = $"<b>{entryId}</b>\n<color=#888><size=80%>{entry.Vertices.Length} verts, {entry.Triangles.Length / 3} tris  mat=\"{entry.MaterialName}\"</size></color>";
                    infoText.fontSize = 15;
                    infoText.color = Color.white;
                    UIHelper.SetWrapping(infoText, true);
                    infoText.overflowMode = TextOverflowModes.Overflow;

                    // Buttons — anchored to right edge of row
                    string capturedId = entryId;
                    CreateRowButton(rowObj.transform, "Place", -224, -170, new Color(0.15f, 0.4f, 0.15f),
                        new Action(() => SpawnFromDatabase(capturedId)));
                    CreateRowButton(rowObj.transform, "Preview", -166, -106, new Color(0.3f, 0.2f, 0.5f),
                        new Action(() => ShowPreviewPanel(capturedId)));
                    CreateRowButton(rowObj.transform, "Ren", -102, -62, new Color(0.2f, 0.3f, 0.5f),
                        new Action(() => ShowRenameInput(capturedId)));
                    CreateRowButton(rowObj.transform, "X", -58, -6, new Color(0.5f, 0.15f, 0.15f),
                        new Action(() => { MeshVaultAPI.RemoveEntry(capturedId); MeshVaultAPI.WriteDatabase(); ShowSpawnPanel(); }));
                }
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

            // Clear test spawns button
            var (clearMask, clearBtn, clearLabel) = UIHelper.RoundedButtonWithLabel(
                "ClearSpawnsBtn", $"Clear Spawns ({_testSpawns.Count})", btnRowObj.transform,
                new Color(0.5f, 0.35f, 0.1f), 140, 28, 15, Color.white);
            clearBtn.onClick.AddListener(new Action(() =>
            {
                int destroyed = DestroyTestSpawns();
                _lastAction = destroyed > 0 ? $"Cleared {destroyed} test spawn(s)" : "No test spawns to clear";
                ShowSpawnPanel(); // refresh count
            }));

            // Close button
            var (closeMask, closeBtn, closeLabel) = UIHelper.RoundedButtonWithLabel(
                "CloseBtn", "Close (Del)", btnRowObj.transform,
                new Color(0.5f, 0.2f, 0.2f), 140, 28, 15, Color.white);
            closeBtn.onClick.AddListener(new Action(() =>
            {
                CloseSpawnPanel();
                _lastAction = "Spawn panel closed";
            }));

            // Unlock cursor
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam != null)
            {
                cam.SetCanLook(false);
                cam.FreeMouse();
            }

            _lastAction = $"Spawn panel: {entries.Length} entries";
        }

        private void SpawnFromDatabase(string id)
        {
            var player = PlayerSingleton<PlayerMovement>.Instance;
            if (player == null) { _lastAction = "No player"; return; }

            var forward = player.transform.forward;
            forward.y = 0;
            forward.Normalize();
            var spawnPos = player.transform.position + forward * 3f;

            var go = MeshVaultAPI.Spawn(id, spawnPos, Quaternion.identity, namePrefix: "MV_TestSpawn");
            if (go != null)
            {
                _testSpawns.Add(go);
                EnterPositioner(go, id, spawnPos);
            }
            else
            {
                _lastAction = $"Failed to spawn \"{id}\"";
            }
        }

        private void SpawnFromDatabaseAtOrigin(string id)
        {
            var entry = MeshVaultAPI.GetMesh(id);
            if (entry == null) { _lastAction = $"Entry \"{id}\" not found"; return; }

            var originPos = new Vector3(entry.BoundsCenter[0], entry.BoundsCenter[1], entry.BoundsCenter[2]);
            var go = MeshVaultAPI.Spawn(id, originPos, Quaternion.identity, namePrefix: "MV_TestSpawn");
            if (go != null)
            {
                _testSpawns.Add(go);
                EnterPositioner(go, id, originPos);
            }
            else
            {
                _lastAction = $"Failed to spawn \"{id}\"";
            }
        }

        private void EnterPositioner(GameObject go, string dbId, Vector3 pos,
            string[] materialOverrides = null, Color?[] colorOverrides = null)
        {
            CloseSpawnPanel();

            // Re-lock cursor for numpad positioning
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam != null)
            {
                cam.SetCanLook(true);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            // Set as preview so numpad adjust + OnGUI overlay work
            _preview = go;
            _previewSourceName = dbId;
            _previewDbId = dbId;
            _previewPosition = pos;
            _previewRotation = Vector3.zero;
            _previewScale = Vector3.one;
            _previewIsLiveObject = false;
            _mode = EditMode.Position;
            _positionerMaterialOverrides = materialOverrides;
            _positionerColorOverrides = colorOverrides;

            _lastAction = $"Positioning \"{dbId}\" — numpad to adjust, Enter to log, Del to cancel";
        }

        private int DestroyTestSpawns()
        {
            int count = 0;
            foreach (var go in _testSpawns)
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                    count++;
                }
            }
            _testSpawns.Clear();

            // Also clean up any orphaned test spawns
            count += DestroyAllOrphanedTestSpawns();
            return count;
        }

        private void CreateRowButton(Transform parent, string label, float rightMin, float rightMax, Color bgColor, Action onClick)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.1f);
            rt.anchorMax = new Vector2(1, 0.9f);
            rt.offsetMin = new Vector2(rightMin, 0);
            rt.offsetMax = new Vector2(rightMax, 0);

            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var txtObj = new GameObject("Label");
            txtObj.transform.SetParent(go.transform, false);
            var txtRt = txtObj.AddComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
            var txt = txtObj.AddComponent<TextMeshProUGUI>();
            txt.text = label;
            txt.fontSize = 15;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.Center;
        }

        private GameObject _renameOverlay;

        private bool _renameWasTyping;

        private void ShowRenameInput(string oldId)
        {
            if (_renameOverlay != null)
                UnityEngine.Object.Destroy(_renameOverlay);

            // Suppress game input while typing in the rename field
            _renameWasTyping = GameInput.IsTyping;
            GameInput.IsTyping = true;

            _renameOverlay = new GameObject("RenameOverlay");
            _renameOverlay.transform.SetParent(_spawnPanel.transform, false);
            var overlayImg = _renameOverlay.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.7f);
            overlayImg.raycastTarget = true;
            var overlayRect = _renameOverlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            // Dialog box
            var dialogObj = new GameObject("Dialog");
            dialogObj.transform.SetParent(_renameOverlay.transform, false);
            var dialogBg = dialogObj.AddComponent<Image>();
            dialogBg.color = new Color(0.12f, 0.14f, 0.12f);
            var dialogRect = dialogObj.GetComponent<RectTransform>();
            dialogRect.anchorMin = new Vector2(0.2f, 0.4f);
            dialogRect.anchorMax = new Vector2(0.8f, 0.6f);
            dialogRect.offsetMin = Vector2.zero;
            dialogRect.offsetMax = Vector2.zero;

            // Label
            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(dialogObj.transform, false);
            var labelRect = labelObj.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0.6f);
            labelRect.anchorMax = new Vector2(1, 1);
            labelRect.offsetMin = new Vector2(10, 0);
            labelRect.offsetMax = new Vector2(-10, -4);
            var labelText = labelObj.AddComponent<TextMeshProUGUI>();
            labelText.text = $"Rename <b>{oldId}</b>:";
            labelText.fontSize = 16;
            labelText.color = Color.white;
            labelText.alignment = TextAlignmentOptions.Left;

            // Input field
            var inputObj = new GameObject("InputField");
            inputObj.transform.SetParent(dialogObj.transform, false);
            var inputBg = inputObj.AddComponent<Image>();
            inputBg.color = new Color(0.08f, 0.08f, 0.08f);
            var inputRect = inputObj.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0, 0.1f);
            inputRect.anchorMax = new Vector2(0.7f, 0.55f);
            inputRect.offsetMin = new Vector2(10, 0);
            inputRect.offsetMax = new Vector2(0, 0);

            var inputTextObj = new GameObject("Text");
            inputTextObj.transform.SetParent(inputObj.transform, false);
            var inputTextRect = inputTextObj.AddComponent<RectTransform>();
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = new Vector2(6, 2);
            inputTextRect.offsetMax = new Vector2(-6, -2);
            var inputText = inputTextObj.AddComponent<TextMeshProUGUI>();
            inputText.fontSize = 15;
            inputText.color = Color.white;
            inputText.alignment = TextAlignmentOptions.Left;
            inputText.richText = false;

            var inputField = inputObj.AddComponent<TMP_InputField>();
            inputField.textComponent = inputText;
            inputField.text = oldId;
            inputField.characterValidation = TMP_InputField.CharacterValidation.None;

            // OK button
            string capturedOldId = oldId;
            CreateRowButton(dialogObj.transform, "OK", -90, -10, new Color(0.15f, 0.4f, 0.15f),
                new Action(() =>
                {
                    string newId = inputField.text.Trim().ToLowerInvariant().Replace(" ", "_");
                    if (string.IsNullOrEmpty(newId) || newId == capturedOldId)
                    {
                        UnityEngine.Object.Destroy(_renameOverlay);
                        _renameOverlay = null;
                        if (!_renameWasTyping) GameInput.IsTyping = false;
                        return;
                    }
                    if (MeshVaultAPI.RenameEntry(capturedOldId, newId))
                    {
                        MeshVaultAPI.WriteDatabase();
                        _lastAction = $"Renamed \"{capturedOldId}\" -> \"{newId}\"";
                        UnityEngine.Object.Destroy(_renameOverlay);
                        _renameOverlay = null;
                        if (!_renameWasTyping) GameInput.IsTyping = false;
                        ShowSpawnPanel();
                    }
                    else
                    {
                        labelText.text = $"<color=red>Failed</color> — \"{newId}\" may already exist";
                    }
                }));

            // Cancel button
            CreateRowButton(dialogObj.transform, "Cancel", -200, -100, new Color(0.4f, 0.2f, 0.2f),
                new Action(() =>
                {
                    UnityEngine.Object.Destroy(_renameOverlay);
                    _renameOverlay = null;
                    if (!_renameWasTyping) GameInput.IsTyping = false;
                }));

            // Focus the input
            inputField.Select();
            inputField.ActivateInputField();
        }

        private void CloseSpawnPanel()
        {
            if (_renameOverlay != null)
            {
                UnityEngine.Object.Destroy(_renameOverlay);
                _renameOverlay = null;
                if (!_renameWasTyping) GameInput.IsTyping = false;
            }
            if (_spawnPanel != null)
            {
                UnityEngine.Object.Destroy(_spawnPanel);
                _spawnPanel = null;
            }
        }
    }
}
#endif
