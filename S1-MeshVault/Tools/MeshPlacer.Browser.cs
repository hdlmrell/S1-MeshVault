#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
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
        // Database naming
        private string _dbEntryName = "";
        private TMP_InputField _dbNameInputField;

        // ═══════════════════════════════════════════════════════════════
        // Combined Mesh Browser
        // ═══════════════════════════════════════════════════════════════

        private Dictionary<Mesh, List<MeshRenderer>> ScanCombinedMeshes()
        {
            var result = new Dictionary<Mesh, List<MeshRenderer>>();
            var allRenderers = FindObjectsOfType<MeshRenderer>();
            for (int i = 0; i < allRenderers.Length; i++)
            {
                var mr = allRenderers[i];
                if (mr == null) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (!mf.sharedMesh.name.Contains("Combined Mesh")) continue;

                if (!result.ContainsKey(mf.sharedMesh))
                    result[mf.sharedMesh] = new List<MeshRenderer>();
                result[mf.sharedMesh].Add(mr);
            }
            return result;
        }

        private void ShowCombinedMeshBrowser()
        {
            try
            {
                CloseCombinedMeshPanel(fullClose: false);
                CloseHierarchyPanel();
                DestroyPreview();
                StopExtractMode();

                _combinedMeshGroups = ScanCombinedMeshes();
                _selectedCombinedMesh = null;
                _selectedMeshObjects = null;
                _selectedForExport = null;

                // Build UI
                _combinedMeshPanel = new GameObject("MeshPlacerCombinedBrowser");
                var rootCanvas = _combinedMeshPanel.AddComponent<Canvas>();
                rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                rootCanvas.sortingOrder = 92;

                var scaler = _combinedMeshPanel.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
                _combinedMeshPanel.AddComponent<GameCanvasScaler>();
                _combinedMeshPanel.AddComponent<GraphicRaycaster>();

                // Panel — left 35%
                var panelObj = UIHelper.Panel("CombinedPanel", _combinedMeshPanel.transform, new Color(0.1f, 0.1f, 0.12f));
                var panelRect = panelObj.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.01f, 0.05f);
                panelRect.anchorMax = new Vector2(0.36f, 0.95f);
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;

                // Title
                var titleText = UIHelper.Text("Title", $"<b>Combined Meshes</b> ({_combinedMeshGroups.Count} found)",
                    panelObj.transform, 16, TextAlignmentOptions.Center);
                var titleRect = titleText.GetComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0, 1);
                titleRect.anchorMax = new Vector2(1, 1);
                titleRect.pivot = new Vector2(0.5f, 1);
                titleRect.anchoredPosition = new Vector2(0, -5);
                titleRect.sizeDelta = new Vector2(0, 28);

                // Search field
                var searchObj = new GameObject("SearchField");
                searchObj.transform.SetParent(panelObj.transform, false);
                var searchRect = searchObj.AddComponent<RectTransform>();
                searchRect.anchorMin = new Vector2(0, 1);
                searchRect.anchorMax = new Vector2(1, 1);
                searchRect.pivot = new Vector2(0.5f, 1);
                searchRect.anchoredPosition = new Vector2(0, -35);
                searchRect.sizeDelta = new Vector2(-16, 26);

                var searchBg = searchObj.AddComponent<Image>();
                searchBg.color = new Color(0.18f, 0.18f, 0.22f);

                var placeholderObj = new GameObject("Placeholder");
                placeholderObj.transform.SetParent(searchObj.transform, false);
                var phRect = placeholderObj.AddComponent<RectTransform>();
                phRect.anchorMin = Vector2.zero;
                phRect.anchorMax = Vector2.one;
                phRect.offsetMin = new Vector2(6, 0);
                phRect.offsetMax = new Vector2(-6, 0);
                var phText = placeholderObj.AddComponent<TextMeshProUGUI>();
                phText.text = "Search objects...";
                phText.fontSize = 15;
                phText.fontStyle = FontStyles.Italic;
                phText.color = new Color(0.5f, 0.5f, 0.5f);
                phText.alignment = TextAlignmentOptions.Left;

                var inputTextObj = new GameObject("Text");
                inputTextObj.transform.SetParent(searchObj.transform, false);
                var itRect = inputTextObj.AddComponent<RectTransform>();
                itRect.anchorMin = Vector2.zero;
                itRect.anchorMax = Vector2.one;
                itRect.offsetMin = new Vector2(6, 0);
                itRect.offsetMax = new Vector2(-6, 0);
                var inputText = inputTextObj.AddComponent<TextMeshProUGUI>();
                inputText.fontSize = 15;
                inputText.color = Color.white;
                inputText.alignment = TextAlignmentOptions.Left;
                inputText.richText = false;

                var searchField = searchObj.AddComponent<TMP_InputField>();
                searchField.textComponent = inputText;
                searchField.placeholder = phText;
                searchField.text = _combinedSearchTerm;

                // Scrollable list
                var listContent = UIHelper.ScrollableVerticalList("CombinedList", panelObj.transform, out ScrollRect scrollRect);
                var scrollRectTransform = scrollRect.GetComponent<RectTransform>();
                scrollRectTransform.anchorMin = new Vector2(0, 0);
                scrollRectTransform.anchorMax = new Vector2(1, 1);
                scrollRectTransform.offsetMin = new Vector2(8, 40);
                scrollRectTransform.offsetMax = new Vector2(-8, -64);

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

                // Compute distance from player to closest renderer in each group, sort closest first
                var playerPos = Vector3.zero;
                var player = PlayerSingleton<PlayerMovement>.Instance;
                if (player != null) playerPos = player.transform.position;

                var sortedGroups = new List<KeyValuePair<Mesh, List<MeshRenderer>>>(_combinedMeshGroups);
                var groupDistances = new Dictionary<Mesh, float>();
                foreach (var kvp in sortedGroups)
                {
                    float closestDist = float.MaxValue;
                    for (int ri = 0; ri < kvp.Value.Count; ri++)
                    {
                        // Use closest point on bounds to player for accurate distance
                        var closest = kvp.Value[ri].bounds.ClosestPoint(playerPos);
                        float d = Vector3.Distance(playerPos, closest);
                        if (d < closestDist) closestDist = d;
                    }
                    groupDistances[kvp.Key] = closestDist;
                }
                sortedGroups.Sort((a, b) => groupDistances[a.Key].CompareTo(groupDistances[b.Key]));

                // Pre-compute ALL unique object names per group (for search + display)
                var groupUniqueNames = new Dictionary<Mesh, List<string>>();
                foreach (var kvp in sortedGroups)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var names = new List<string>();
                    for (int ri = 0; ri < kvp.Value.Count; ri++)
                    {
                        string n = GetMeaningfulName(kvp.Value[ri]);
                        if (seen.Add(n)) names.Add(n);
                    }
                    groupUniqueNames[kvp.Key] = names;
                }

                // Populate mesh list rows and track for search filtering
                // Each entry: rowObj, searchable string, unique names list, Text component, header prefix
                var searchRows = new List<GameObject>();
                var searchStrs = new List<string>();
                var searchNameLists = new List<List<string>>();
                var searchTextComponents = new List<TextMeshProUGUI>();
                var searchHeaders = new List<string>();

                foreach (var kvp in sortedGroups)
                {
                    var mesh = kvp.Key;
                    var renderers = kvp.Value;
                    float dist = groupDistances[mesh];

                    string meshName = mesh.name;
                    if (meshName.Length > 35) meshName = meshName.Substring(0, 32) + "...";

                    var uniqueNames = groupUniqueNames[mesh];
                    string headerPrefix = $"<b>{meshName}</b>  <color=#5af><size=80%>{dist:F0}m</size></color>\n<color=#888><size=80%>{mesh.vertexCount} verts, {renderers.Count} objects</size></color>\n<color=#aa8><size=75%>";
                    // Default sample: first 4 names
                    string samplesStr = BuildSampleStr(uniqueNames, "", 4);

                    var rowObj = new GameObject($"Row_{meshName}");
                    rowObj.transform.SetParent(listContent, false);

                    var rowRT = rowObj.AddComponent<RectTransform>();
                    var rowLayout = rowObj.AddComponent<LayoutElement>();
                    rowLayout.minHeight = 62f;
                    rowLayout.preferredHeight = 62f;
                    rowLayout.flexibleWidth = 1;

                    var rowBg = rowObj.AddComponent<Image>();
                    rowBg.color = new Color(0.16f, 0.16f, 0.2f);

                    // Name text
                    var nameObj = new GameObject("Name");
                    nameObj.transform.SetParent(rowObj.transform, false);
                    var nameRect = nameObj.AddComponent<RectTransform>();
                    nameRect.anchorMin = Vector2.zero;
                    nameRect.anchorMax = Vector2.one;
                    nameRect.offsetMin = new Vector2(8, 2);
                    nameRect.offsetMax = new Vector2(-8, -2);
                    var nameText = nameObj.AddComponent<TextMeshProUGUI>();
                    nameText.text = headerPrefix + samplesStr + "</size></color>";
                    nameText.fontSize = 15;
                    nameText.color = Color.white;
                    nameText.alignment = TextAlignmentOptions.Left;
                    nameText.richText = true;
                    UIHelper.SetWrapping(nameText, true);
                    nameText.overflowMode = TextOverflowModes.Truncate;

                    // Make entire row clickable
                    var rowBtn = rowObj.AddComponent<Button>();
                    var bc = rowBtn.colors;
                    bc.normalColor = new Color(0.16f, 0.16f, 0.2f);
                    bc.highlightedColor = new Color(0.22f, 0.22f, 0.3f);
                    bc.pressedColor = new Color(0.12f, 0.12f, 0.16f);
                    rowBtn.colors = bc;

                    Mesh capturedMesh = mesh;
                    rowBtn.onClick.AddListener(new Action(() => ShowObjectList(capturedMesh)));

                    // Hover highlight — highlight first renderer in this group
                    List<MeshRenderer> capturedRenderers = renderers;
                    var meshRowTrigger = rowObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                    var meshEnter = new UnityEngine.EventSystems.EventTrigger.Entry();
                    meshEnter.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
                    meshEnter.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(_ =>
                    {
                        if (capturedRenderers.Count > 0 && capturedRenderers[0] != null)
                            HighlightObject(capturedRenderers[0].transform);
                    }));
                    meshRowTrigger.triggers.Add(meshEnter);
                    var meshExit = new UnityEngine.EventSystems.EventTrigger.Entry();
                    meshExit.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
                    meshExit.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(_ => ClearHighlight()));
                    meshRowTrigger.triggers.Add(meshExit);

                    // Track for search filtering + dynamic sample text update
                    string searchStr = (meshName + " " + string.Join(" ", uniqueNames)).ToLowerInvariant();
                    searchRows.Add(rowObj);
                    searchStrs.Add(searchStr);
                    searchNameLists.Add(uniqueNames);
                    searchTextComponents.Add(nameText);
                    searchHeaders.Add(headerPrefix);
                }

                var rebuildRT = listContent.GetComponent<RectTransform>();
                if (rebuildRT != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rebuildRT);

                // Wire search filter — also update sample names to show matches first
                var filterAction = new Action<string>(filter =>
                {
                    _combinedSearchTerm = filter ?? "";
                    string lower = _combinedSearchTerm.ToLowerInvariant().Trim();
                    for (int i = 0; i < searchRows.Count; i++)
                    {
                        bool show = string.IsNullOrEmpty(lower) || searchStrs[i].Contains(lower);
                        searchRows[i].SetActive(show);
                        // Update sample text to prioritize matching names
                        string samples = BuildSampleStr(searchNameLists[i], lower, 4);
                        searchTextComponents[i].text = searchHeaders[i] + samples + "</size></color>";
                    }
                });
                searchField.onValueChanged.AddListener(filterAction);

                // Apply existing search term on load
                if (!string.IsNullOrEmpty(_combinedSearchTerm))
                    filterAction.Invoke(_combinedSearchTerm);

                // Close button
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
                    CloseCombinedMeshPanel();
                    _lastAction = "Combined mesh browser closed";
                }));

                // Unlock cursor
                var cam = PlayerSingleton<PlayerCamera>.Instance;
                if (cam != null)
                {
                    cam.AddActiveUIElement("MV_CombinedBrowser");
                    cam.SetCanLook(false);
                    cam.FreeMouse();
                }
                GameInput.IsTyping = true;
                _wasTypingFromUs = true;

                _combinedMeshPanel.SetActive(true);
                _lastAction = $"Combined meshes: {_combinedMeshGroups.Count} found";
            }
            catch (Exception ex)
            {
                _lastAction = $"Combined browser failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] ShowCombinedMeshBrowser failed: {ex}");
                CloseCombinedMeshPanel();
            }
        }

        private void ShowObjectList(Mesh mesh)
        {
            try
            {
                if (_combinedMeshGroups == null || !_combinedMeshGroups.ContainsKey(mesh))
                {
                    _lastAction = "Mesh group not found";
                    return;
                }

                bool isSameMesh = _selectedCombinedMesh == mesh;
                _selectedCombinedMesh = mesh;
                _selectedMeshObjects = _combinedMeshGroups[mesh];

                // Only init selection when navigating to a NEW mesh (not when refreshing checkboxes)
                if (!isSameMesh)
                {
                    _selectedForExport = new HashSet<int>();
                    for (int i = 0; i < _selectedMeshObjects.Count; i++)
                        _selectedForExport.Add(i);
                }

                ClearHighlight();

                // Destroy old panel GO (keep cursor unlocked, keep _combinedMeshGroups)
                if (_combinedMeshPanel != null)
                {
                    UnityEngine.Object.Destroy(_combinedMeshPanel);
                    _combinedMeshPanel = null;
                }

                // Build new panel
                _combinedMeshPanel = new GameObject("MeshPlacerObjectList");
                var rootCanvas = _combinedMeshPanel.AddComponent<Canvas>();
                rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                rootCanvas.sortingOrder = 92;

                var scaler = _combinedMeshPanel.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
                _combinedMeshPanel.AddComponent<GameCanvasScaler>();
                _combinedMeshPanel.AddComponent<GraphicRaycaster>();

                var panelObj = UIHelper.Panel("ObjectPanel", _combinedMeshPanel.transform, new Color(0.1f, 0.1f, 0.12f));
                var panelRect = panelObj.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.01f, 0.05f);
                panelRect.anchorMax = new Vector2(0.36f, 0.95f);
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;

                // Title
                string meshDisplayName = mesh.name;
                if (meshDisplayName.Length > 30) meshDisplayName = meshDisplayName.Substring(0, 27) + "...";
                var titleText = UIHelper.Text("Title",
                    $"<b>{meshDisplayName}</b> ({_selectedMeshObjects.Count} objects)",
                    panelObj.transform, 16, TextAlignmentOptions.Center);
                var titleRect = titleText.GetComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0, 1);
                titleRect.anchorMax = new Vector2(1, 1);
                titleRect.pivot = new Vector2(0.5f, 1);
                titleRect.anchoredPosition = new Vector2(0, -5);
                titleRect.sizeDelta = new Vector2(0, 28);

                // Search field (same term as mesh list)
                var searchObj = new GameObject("SearchField");
                searchObj.transform.SetParent(panelObj.transform, false);
                var searchRect = searchObj.AddComponent<RectTransform>();
                searchRect.anchorMin = new Vector2(0, 1);
                searchRect.anchorMax = new Vector2(1, 1);
                searchRect.pivot = new Vector2(0.5f, 1);
                searchRect.anchoredPosition = new Vector2(0, -35);
                searchRect.sizeDelta = new Vector2(-16, 26);

                var searchBg2 = searchObj.AddComponent<Image>();
                searchBg2.color = new Color(0.18f, 0.18f, 0.22f);

                var phObj2 = new GameObject("Placeholder");
                phObj2.transform.SetParent(searchObj.transform, false);
                var phRect2 = phObj2.AddComponent<RectTransform>();
                phRect2.anchorMin = Vector2.zero;
                phRect2.anchorMax = Vector2.one;
                phRect2.offsetMin = new Vector2(6, 0);
                phRect2.offsetMax = new Vector2(-6, 0);
                var phText2 = phObj2.AddComponent<TextMeshProUGUI>();
                phText2.text = "Search objects...";
                phText2.fontSize = 15;
                phText2.fontStyle = FontStyles.Italic;
                phText2.color = new Color(0.5f, 0.5f, 0.5f);
                phText2.alignment = TextAlignmentOptions.Left;

                var itObj2 = new GameObject("Text");
                itObj2.transform.SetParent(searchObj.transform, false);
                var itRect2 = itObj2.AddComponent<RectTransform>();
                itRect2.anchorMin = Vector2.zero;
                itRect2.anchorMax = Vector2.one;
                itRect2.offsetMin = new Vector2(6, 0);
                itRect2.offsetMax = new Vector2(-6, 0);
                var itText2 = itObj2.AddComponent<TextMeshProUGUI>();
                itText2.fontSize = 15;
                itText2.color = Color.white;
                itText2.alignment = TextAlignmentOptions.Left;
                itText2.richText = false;

                var objSearchField = searchObj.AddComponent<TMP_InputField>();
                objSearchField.textComponent = itText2;
                objSearchField.placeholder = phText2;
                objSearchField.text = _combinedSearchTerm;

                // Button row: Back | Select All | Deselect All
                var btnRowObj = new GameObject("ButtonRow");
                btnRowObj.transform.SetParent(panelObj.transform, false);
                var btnRowRect = btnRowObj.AddComponent<RectTransform>();
                btnRowRect.anchorMin = new Vector2(0, 1);
                btnRowRect.anchorMax = new Vector2(1, 1);
                btnRowRect.pivot = new Vector2(0.5f, 1);
                btnRowRect.anchoredPosition = new Vector2(0, -63);
                btnRowRect.sizeDelta = new Vector2(-16, 28);

                var btnRowHLG = btnRowObj.AddComponent<HorizontalLayoutGroup>();
                btnRowHLG.spacing = 4;
                btnRowHLG.padding = new RectOffset(0, 0, 0, 0);
                btnRowHLG.childControlWidth = true;
                btnRowHLG.childControlHeight = true;
                btnRowHLG.childForceExpandWidth = true;
                btnRowHLG.childForceExpandHeight = true;

                var (backMask, backBtn, backLabel) = UIHelper.RoundedButtonWithLabel(
                    "BackBtn", "< Back", btnRowObj.transform,
                    new Color(0.3f, 0.3f, 0.35f), 80, 26, 15, Color.white);
                backBtn.onClick.AddListener(new Action(() => ShowCombinedMeshBrowser()));

                var (selAllMask, selAllBtn, selAllLabel) = UIHelper.RoundedButtonWithLabel(
                    "SelectAllBtn", "Select All", btnRowObj.transform,
                    new Color(0.2f, 0.35f, 0.2f), 80, 26, 15, Color.white);
                selAllBtn.onClick.AddListener(new Action(() =>
                {
                    if (_selectedMeshObjects == null) return;
                    for (int i = 0; i < _selectedMeshObjects.Count; i++)
                        _selectedForExport.Add(i);
                    ShowObjectList(_selectedCombinedMesh);
                }));

                var (deselMask, deselBtn, deselLabel) = UIHelper.RoundedButtonWithLabel(
                    "DeselectAllBtn", "Deselect All", btnRowObj.transform,
                    new Color(0.35f, 0.25f, 0.2f), 80, 26, 15, Color.white);
                deselBtn.onClick.AddListener(new Action(() =>
                {
                    if (_selectedForExport != null) _selectedForExport.Clear();
                    ShowObjectList(_selectedCombinedMesh);
                }));

                // Scrollable object list
                var listContent = UIHelper.ScrollableVerticalList("ObjectList", panelObj.transform, out ScrollRect scrollRect);
                var scrollRectTransform = scrollRect.GetComponent<RectTransform>();
                scrollRectTransform.anchorMin = new Vector2(0, 0);
                scrollRectTransform.anchorMax = new Vector2(1, 1);
                scrollRectTransform.offsetMin = new Vector2(8, 76);
                scrollRectTransform.offsetMax = new Vector2(-8, -95);

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

                // Populate object rows — track for search filtering
                var objSearchRows = new List<KeyValuePair<GameObject, string>>();
                float savedScrollPos = _objectListScrollPos;

                for (int i = 0; i < _selectedMeshObjects.Count; i++)
                {
                    var mr = _selectedMeshObjects[i];
                    if (mr == null) continue;

                    int idx = i;
                    string objName = GetMeaningfulName(mr);
                    bool selected = _selectedForExport.Contains(i);
                    var bounds = mr.bounds;
                    string sizeStr = $"{bounds.size.x:F1}x{bounds.size.y:F1}x{bounds.size.z:F1}";

                    var rowObj = new GameObject($"Row_{i}");
                    rowObj.transform.SetParent(listContent, false);

                    var rowRT = rowObj.AddComponent<RectTransform>();
                    var rowLayout = rowObj.AddComponent<LayoutElement>();
                    rowLayout.minHeight = 48f;
                    rowLayout.preferredHeight = 48f;
                    rowLayout.flexibleWidth = 1;

                    var rowBg = rowObj.AddComponent<Image>();
                    rowBg.color = selected ? new Color(0.15f, 0.22f, 0.15f) : new Color(0.14f, 0.14f, 0.16f);

                    int cbSize = 28;

                    // Checkbox — left side
                    var cbObj = new GameObject("Checkbox");
                    cbObj.transform.SetParent(rowObj.transform, false);
                    var cbRect = cbObj.AddComponent<RectTransform>();
                    cbRect.anchorMin = new Vector2(0, 0.5f);
                    cbRect.anchorMax = new Vector2(0, 0.5f);
                    cbRect.pivot = new Vector2(0, 0.5f);
                    cbRect.anchoredPosition = new Vector2(6, 0);
                    cbRect.sizeDelta = new Vector2(cbSize, cbSize);

                    var cbBg = cbObj.AddComponent<Image>();
                    cbBg.color = selected ? new Color(0.2f, 0.7f, 0.2f) : new Color(0.35f, 0.35f, 0.4f);

                    var cbBtn = cbObj.AddComponent<Button>();
                    var cbc = cbBtn.colors;
                    cbc.normalColor = cbBg.color;
                    cbc.highlightedColor = selected ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.45f, 0.45f, 0.5f);
                    cbc.pressedColor = selected ? new Color(0.15f, 0.5f, 0.15f) : new Color(0.25f, 0.25f, 0.3f);
                    cbBtn.colors = cbc;

                    // Checkmark
                    var checkObj = new GameObject("Check");
                    checkObj.transform.SetParent(cbObj.transform, false);
                    var checkRect = checkObj.AddComponent<RectTransform>();
                    checkRect.anchorMin = Vector2.zero;
                    checkRect.anchorMax = Vector2.one;
                    checkRect.offsetMin = Vector2.zero;
                    checkRect.offsetMax = Vector2.zero;
                    var checkText = checkObj.AddComponent<TextMeshProUGUI>();
                    checkText.text = selected ? "X" : "";
                    checkText.fontSize = 18;
                    checkText.color = Color.white;
                    checkText.alignment = TextAlignmentOptions.Center;

                    cbBtn.onClick.AddListener(new Action(() =>
                    {
                        if (_selectedForExport.Contains(idx))
                            _selectedForExport.Remove(idx);
                        else
                            _selectedForExport.Add(idx);
                        ShowObjectList(_selectedCombinedMesh);
                    }));

                    // Name text — between checkbox and highlight button
                    var nameObj = new GameObject("Name");
                    nameObj.transform.SetParent(rowObj.transform, false);
                    var nameRect = nameObj.AddComponent<RectTransform>();
                    nameRect.anchorMin = Vector2.zero;
                    nameRect.anchorMax = Vector2.one;
                    nameRect.offsetMin = new Vector2(6 + cbSize + 6, 2);
                    nameRect.offsetMax = new Vector2(-8, -2);
                    var nameText = nameObj.AddComponent<TextMeshProUGUI>();
                    nameText.text = $"<b>{objName}</b>\n<color=#888><size=80%>{sizeStr}</size></color>";
                    nameText.fontSize = 15;
                    nameText.color = Color.white;
                    nameText.alignment = TextAlignmentOptions.Left;
                    nameText.richText = true;
                    UIHelper.SetWrapping(nameText, true);
                    nameText.overflowMode = TextOverflowModes.Truncate;

                    // Hover highlight — highlight this renderer on pointer enter
                    MeshRenderer capturedMR = mr;
                    var objRowTrigger = rowObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                    var objEnter = new UnityEngine.EventSystems.EventTrigger.Entry();
                    objEnter.eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter;
                    objEnter.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(_ =>
                        HighlightObject(capturedMR.transform)));
                    objRowTrigger.triggers.Add(objEnter);
                    var objExit = new UnityEngine.EventSystems.EventTrigger.Entry();
                    objExit.eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit;
                    objExit.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(_ =>
                        ClearHighlight()));
                    objRowTrigger.triggers.Add(objExit);

                    objSearchRows.Add(new KeyValuePair<GameObject, string>(rowObj, objName.ToLowerInvariant()));
                }

                var rebuildRT = listContent.GetComponent<RectTransform>();
                if (rebuildRT != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rebuildRT);

                // Apply search filter and wire search field
                var objFilterAction = new Action<string>(filter =>
                {
                    _combinedSearchTerm = filter ?? "";
                    string lower = _combinedSearchTerm.ToLowerInvariant().Trim();
                    for (int fi = 0; fi < objSearchRows.Count; fi++)
                    {
                        bool show = string.IsNullOrEmpty(lower) || objSearchRows[fi].Value.Contains(lower);
                        objSearchRows[fi].Key.SetActive(show);
                    }
                });
                objSearchField.onValueChanged.AddListener(objFilterAction);

                // Apply existing search term
                if (!string.IsNullOrEmpty(_combinedSearchTerm))
                {
                    string initLower = _combinedSearchTerm.ToLowerInvariant().Trim();
                    for (int fi = 0; fi < objSearchRows.Count; fi++)
                    {
                        bool show = string.IsNullOrEmpty(initLower) || objSearchRows[fi].Value.Contains(initLower);
                        objSearchRows[fi].Key.SetActive(show);
                    }
                }

                // Restore scroll position (after layout rebuild, schedule for next frame)
                ScrollRect capturedScroll = scrollRect;
                float capturedPos = savedScrollPos;
                scrollRect.onValueChanged.AddListener(new Action<Vector2>(v => _objectListScrollPos = v.y));
                MelonCoroutines.Start(RestoreScrollNextFrame(capturedScroll, capturedPos));

                // ── Database save row ──
                var dbRowObj = new GameObject("DbRow");
                dbRowObj.transform.SetParent(panelObj.transform, false);
                var dbRowRect = dbRowObj.AddComponent<RectTransform>();
                dbRowRect.anchorMin = new Vector2(0, 0);
                dbRowRect.anchorMax = new Vector2(1, 0);
                dbRowRect.pivot = new Vector2(0.5f, 0);
                dbRowRect.anchoredPosition = new Vector2(0, 36);
                dbRowRect.sizeDelta = new Vector2(-16, 32);

                var dbRowHLG = dbRowObj.AddComponent<HorizontalLayoutGroup>();
                dbRowHLG.spacing = 4;
                dbRowHLG.childControlWidth = true;
                dbRowHLG.childControlHeight = true;
                dbRowHLG.childForceExpandWidth = false;
                dbRowHLG.childForceExpandHeight = true;

                // Name input field
                var nameFieldObj = new GameObject("DbNameField");
                nameFieldObj.transform.SetParent(dbRowObj.transform, false);
                nameFieldObj.AddComponent<RectTransform>();
                var nameFieldLayout = nameFieldObj.AddComponent<LayoutElement>();
                nameFieldLayout.flexibleWidth = 1;
                nameFieldLayout.preferredHeight = 30;
                var nameFieldBg = nameFieldObj.AddComponent<Image>();
                nameFieldBg.color = new Color(0.18f, 0.18f, 0.22f);

                var dbTextObj = new GameObject("Text");
                dbTextObj.transform.SetParent(nameFieldObj.transform, false);
                var dbTextRect = dbTextObj.AddComponent<RectTransform>();
                dbTextRect.anchorMin = Vector2.zero;
                dbTextRect.anchorMax = Vector2.one;
                dbTextRect.offsetMin = new Vector2(6, 0);
                dbTextRect.offsetMax = new Vector2(-6, 0);
                var dbInputText = dbTextObj.AddComponent<TextMeshProUGUI>();
                dbInputText.fontSize = 15;
                dbInputText.color = Color.white;
                dbInputText.alignment = TextAlignmentOptions.Left;
                dbInputText.richText = false;

                var namePlaceholderObj = new GameObject("Placeholder");
                namePlaceholderObj.transform.SetParent(nameFieldObj.transform, false);
                var namePHRect = namePlaceholderObj.AddComponent<RectTransform>();
                namePHRect.anchorMin = Vector2.zero;
                namePHRect.anchorMax = Vector2.one;
                namePHRect.offsetMin = new Vector2(6, 0);
                namePHRect.offsetMax = new Vector2(-6, 0);
                var namePHText = namePlaceholderObj.AddComponent<TextMeshProUGUI>();
                namePHText.text = "entry name...";
                namePHText.fontSize = 15;
                namePHText.fontStyle = FontStyles.Italic;
                namePHText.color = new Color(0.5f, 0.5f, 0.5f);
                namePHText.alignment = TextAlignmentOptions.Left;

                _dbNameInputField = nameFieldObj.AddComponent<TMP_InputField>();
                _dbNameInputField.textComponent = dbInputText;
                _dbNameInputField.placeholder = namePHText;
                // Pre-fill with first selected object's name
                if (_selectedForExport.Count > 0)
                {
                    foreach (int idx in _selectedForExport)
                    {
                        if (idx < _selectedMeshObjects.Count && _selectedMeshObjects[idx] != null)
                        {
                            _dbEntryName = GetMeaningfulName(_selectedMeshObjects[idx])
                                .ToLowerInvariant().Replace(" ", "_").Replace("(", "").Replace(")", "");
                            break;
                        }
                    }
                }
                _dbNameInputField.text = _dbEntryName;
                _dbNameInputField.onValueChanged.AddListener(new Action<string>(v => _dbEntryName = v));

                // Save to DB button
                var (saveMask, saveBtn, saveLabel) = UIHelper.RoundedButtonWithLabel(
                    "SaveDbBtn", "Save to DB", dbRowObj.transform,
                    new Color(0.15f, 0.45f, 0.15f), 110, 30, 15, Color.white);
                var saveBtnLayout = saveMask.AddComponent<LayoutElement>();
                saveBtnLayout.preferredWidth = 110;
                saveBtn.onClick.AddListener(new Action(SaveSelectedToDatabase));

                // Close button
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
                    CloseCombinedMeshPanel();
                    _lastAction = "Combined mesh browser closed";
                }));

                // Cursor already unlocked from ShowCombinedMeshBrowser, but handle direct navigation
                if (!_wasTypingFromUs)
                {
                    var cam = PlayerSingleton<PlayerCamera>.Instance;
                    if (cam != null)
                    {
                        cam.AddActiveUIElement("MV_CombinedBrowser");
                        cam.SetCanLook(false);
                        cam.FreeMouse();
                    }
                    GameInput.IsTyping = true;
                    _wasTypingFromUs = true;
                }

                _combinedMeshPanel.SetActive(true);
                _lastAction = $"{meshDisplayName}: {_selectedMeshObjects.Count} objects, {_selectedForExport.Count} selected";
            }
            catch (Exception ex)
            {
                _lastAction = $"Object list failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] ShowObjectList failed: {ex}");
            }
        }

        private static string GetMeaningfulName(MeshRenderer mr)
        {
            var genericNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Cube", "Sphere", "Cylinder", "Capsule", "Plane", "Quad",
                "MeshRenderer", "LOD0", "LOD1", "LOD2", "mesh", "model",
                "default", "GameObject"
            };

            var t = mr.transform;
            for (int i = 0; i < 5 && t != null; i++)
            {
                string name = t.gameObject.name;
                if (!string.IsNullOrEmpty(name) && name.Length > 2 && !genericNames.Contains(name))
                    return name;
                t = t.parent;
            }
            return mr.gameObject.name;
        }

        private static string BuildSampleStr(List<string> allNames, string searchLower, int max)
        {
            var result = new List<string>();
            // If searching, show matching names first
            if (!string.IsNullOrEmpty(searchLower))
            {
                for (int i = 0; i < allNames.Count && result.Count < max; i++)
                {
                    if (allNames[i].ToLowerInvariant().Contains(searchLower))
                        result.Add(allNames[i]);
                }
            }
            // Fill remaining slots with non-matching names
            for (int i = 0; i < allNames.Count && result.Count < max; i++)
            {
                if (!result.Contains(allNames[i]))
                    result.Add(allNames[i]);
            }
            string s = string.Join(", ", result);
            if (allNames.Count > result.Count) s += ", ...";
            return s;
        }

        private static System.Collections.IEnumerator RestoreScrollNextFrame(ScrollRect sr, float pos)
        {
            yield return null;
            if (sr != null) sr.verticalNormalizedPosition = pos;
        }

        private void CloseCombinedMeshPanel(bool fullClose = true)
        {
            if (_combinedMeshPanel != null)
            {
                UnityEngine.Object.Destroy(_combinedMeshPanel);
                _combinedMeshPanel = null;
            }
            _combinedMeshGroups = null;
            _selectedCombinedMesh = null;
            _selectedMeshObjects = null;
            _selectedForExport = null;
            if (fullClose)
            {
                _combinedSearchTerm = "";
                _objectListScrollPos = 0f;
            }
            _tabLooking = false;
            ClearHighlight();

            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam != null)
            {
                cam.RemoveActiveUIElement("MV_CombinedBrowser");
                cam.SetCanLook(true);
                cam.LockMouse();
            }

            if (_wasTypingFromUs)
            {
                GameInput.IsTyping = false;
                _wasTypingFromUs = false;
            }
        }

        private void SaveSelectedToDatabase()
        {
            if (string.IsNullOrWhiteSpace(_dbEntryName))
            {
                _lastAction = "Enter a name for the DB entry";
                return;
            }
            if (_selectedForExport == null || _selectedForExport.Count == 0)
            {
                _lastAction = "No objects selected";
                return;
            }
            if (_selectedCombinedMesh == null)
            {
                _lastAction = "No combined mesh selected";
                return;
            }

            try
            {
                int savedCount = 0;
                foreach (int idx in _selectedForExport)
                {
                    if (idx >= _selectedMeshObjects.Count) continue;
                    var mr = _selectedMeshObjects[idx];
                    if (mr == null) continue;

                    var rendererBounds = mr.bounds;
                    var sourceMesh = _selectedCombinedMesh;

                    // Read mesh data
                    Vector3[] allVerts, allNormals;
                    Vector2[] allUVs;
                    List<int[]> allSubmeshTris;

                    if (sourceMesh.isReadable)
                    {
                        allVerts = sourceMesh.vertices;
                        allNormals = sourceMesh.normals;
                        allUVs = sourceMesh.uv;
                        allSubmeshTris = new List<int[]>();
                        for (int s = 0; s < sourceMesh.subMeshCount; s++)
                            allSubmeshTris.Add(sourceMesh.GetTriangles(s));
                    }
                    else
                    {
#if IL2CPP
                        _lastAction = "Mesh not readable — Mono only";
                        return;
#else
                        if (!ReadMeshNative(sourceMesh, out allVerts, out allNormals, out allUVs, out allSubmeshTris))
                        {
                            _lastAction = "Failed to read mesh data";
                            return;
                        }
#endif
                    }

                    // Per-submesh extraction: filter each submesh's triangles by bounds,
                    // preserving the material-per-submesh mapping from the combined mesh.
                    bool isSceneRoot = sourceMesh.name.Contains("(root: scene)");
                    var meshTransform = mr.GetComponent<MeshFilter>() != null
                        ? mr.GetComponent<MeshFilter>().transform
                        : mr.transform;

                    // Vertex space detection (same logic as extraction)
                    int rawHits = 0, xfHits = 0;
                    int sampleCount = Math.Min(allVerts.Length, 200);
                    int sampleStep = Math.Max(1, allVerts.Length / sampleCount);
                    for (int i = 0; i < allVerts.Length && i < sampleCount * sampleStep; i += sampleStep)
                    {
                        if (rendererBounds.Contains(allVerts[i])) rawHits++;
                        if (rendererBounds.Contains(meshTransform.TransformPoint(allVerts[i]))) xfHits++;
                    }
                    bool useRaw = isSceneRoot || rawHits >= xfHits;

                    // Shared vertex buffer across all submeshes
                    var newVerts = new List<Vector3>();
                    var newNormals = new List<Vector3>();
                    var newUVs = new List<Vector2>();
                    var vertMap = new Dictionary<int, int>();

                    var boundsMin = rendererBounds.min;
                    var boundsMax = rendererBounds.max;

                    // Per-submesh: filter triangles and track their material
                    var subMeshTriLists = new List<List<int>>();
                    var subMeshMats = new List<Material>();
                    var matArray = mr.sharedMaterials;

                    for (int s = 0; s < allSubmeshTris.Count; s++)
                    {
                        var subTris = allSubmeshTris[s];
                        var newSubTris = new List<int>();

                        for (int i = 0; i < subTris.Length; i += 3)
                        {
                            int i0 = subTris[i], i1 = subTris[i + 1], i2 = subTris[i + 2];
                            if (i0 >= allVerts.Length || i1 >= allVerts.Length || i2 >= allVerts.Length) continue;

                            Vector3 v0 = useRaw ? allVerts[i0] : meshTransform.TransformPoint(allVerts[i0]);
                            Vector3 v1 = useRaw ? allVerts[i1] : meshTransform.TransformPoint(allVerts[i1]);
                            Vector3 v2 = useRaw ? allVerts[i2] : meshTransform.TransformPoint(allVerts[i2]);

                            Vector3 centroid = (v0 + v1 + v2) / 3f;
                            if (centroid.x < boundsMin.x || centroid.x > boundsMax.x ||
                                centroid.y < boundsMin.y || centroid.y > boundsMax.y ||
                                centroid.z < boundsMin.z || centroid.z > boundsMax.z)
                                continue;

                            foreach (int origIdx in new[] { i0, i1, i2 })
                            {
                                if (!vertMap.ContainsKey(origIdx))
                                {
                                    vertMap[origIdx] = newVerts.Count;
                                    newVerts.Add(allVerts[origIdx]);
                                    newNormals.Add(origIdx < allNormals.Length ? allNormals[origIdx] : Vector3.up);
                                    newUVs.Add(origIdx < allUVs.Length ? allUVs[origIdx] : Vector2.zero);
                                }
                                newSubTris.Add(vertMap[origIdx]);
                            }
                        }

                        if (newSubTris.Count > 0)
                        {
                            subMeshTriLists.Add(newSubTris);
                            subMeshMats.Add(null); // placeholder — resolved below
                        }
                    }

                    if (newVerts.Count == 0) continue;

                    // Determine material for each extracted submesh by sampling triangle
                    // centroids against ALL renderers sharing this combined mesh.
                    // After static batching, each renderer retains only its own material,
                    // so mr.sharedMaterials[s] doesn't give us the correct material per submesh.
                    for (int gs = 0; gs < subMeshTriLists.Count; gs++)
                    {
                        var sTris = subMeshTriLists[gs];
                        if (sTris.Count < 3) continue;

                        int sampleTris = Math.Min(sTris.Count / 3, 5);
                        int bestHits = 0;
                        float bestVol = float.MaxValue;
                        Material bestMat = null;

                        for (int j = 0; j < _selectedMeshObjects.Count; j++)
                        {
                            var omr = _selectedMeshObjects[j];
                            if (omr == null) continue;
                            var omrMat = omr.sharedMaterial;
                            if (omrMat == null) continue;

                            int hits = 0;
                            for (int t = 0; t < sampleTris; t++)
                            {
                                int ti = t * 3;
                                var rawC = (newVerts[sTris[ti]] + newVerts[sTris[ti + 1]] + newVerts[sTris[ti + 2]]) / 3f;
                                var worldC = useRaw ? rawC : meshTransform.TransformPoint(rawC);
                                if (omr.bounds.Contains(worldC)) hits++;
                            }

                            if (hits > 0)
                            {
                                float vol = omr.bounds.size.x * omr.bounds.size.y * omr.bounds.size.z;
                                // Prefer most hits, then smallest bounds (most specific renderer)
                                if (hits > bestHits || (hits == bestHits && vol < bestVol))
                                {
                                    bestHits = hits;
                                    bestVol = vol;
                                    bestMat = omrMat;
                                }
                            }
                        }

                        if (bestMat != null)
                        {
                            subMeshMats[gs] = bestMat;
                            Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Submesh {gs}: matched to \"{bestMat.name}\" ({bestHits}/{sampleTris} hits)");
                        }
                        else
                        {
                            Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] Submesh {gs}: no matching renderer found");
                        }
                    }

                    // Per-submesh texture names and color tints for baked materials
                    var perSubTexNames = new string[subMeshMats.Count];
                    var perSubColorTints = new float[subMeshMats.Count][];
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
                                perSubTexNames[gs] = tex.name;
                                if (bakedTexName == null)
                                {
                                    bakedTexName = tex.name;
                                    if (m.HasProperty("_Tiling")) bakedTiling = m.GetFloat("_Tiling");
                                }
                            }
                            var col = m.HasProperty("_Color") ? m.GetColor("_Color")
                                : new UnityEngine.Color(1, 1, 1, 1);
                            perSubColorTints[gs] = new[] { col.r, col.g, col.b, col.a };
                        }
                    }

                    var finalUVs = newUVs;
                    if (bakedTexName != null)
                    {
                        finalUVs = new List<Vector2>(newVerts.Count);
                        for (int i = 0; i < newVerts.Count; i++)
                        {
                            var worldPos = useRaw ? newVerts[i] : meshTransform.TransformPoint(newVerts[i]);
                            var n = i < newNormals.Count ? newNormals[i] : Vector3.up;
                            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                            Vector2 uv;
                            if (ay >= ax && ay >= az)
                                uv = new Vector2(worldPos.x, worldPos.z);
                            else if (az >= ax)
                                uv = new Vector2(worldPos.x, worldPos.y);
                            else
                                uv = new Vector2(worldPos.z, worldPos.y);
                            finalUVs.Add(uv * bakedTiling);
                        }
                        Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Baked {finalUVs.Count} UVs (triplanar, tiling={bakedTiling}), texture=\"{bakedTexName}\"");
                    }

                    // Generate unique name for multi-select
                    string entryName = _selectedForExport.Count == 1
                        ? _dbEntryName
                        : $"{_dbEntryName}_{savedCount}";

                    // Convert to arrays for Save
                    var subMeshTrisArr = new int[subMeshTriLists.Count][];
                    for (int s = 0; s < subMeshTriLists.Count; s++)
                        subMeshTrisArr[s] = subMeshTriLists[s].ToArray();

                    entryName = MeshVaultAPI.SaveMesh(entryName, newVerts.ToArray(), newNormals.ToArray(),
                        finalUVs.ToArray(), subMeshTrisArr, subMeshMats.ToArray(), rendererBounds,
                        perSubTexNames, perSubColorTints);
                    savedCount++;
                }

                _lastAction = $"Saved {savedCount} entries to DB";
            }
            catch (Exception ex)
            {
                _lastAction = $"DB save failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] SaveSelectedToDatabase failed: {ex}");
            }
        }

        private void ExportSelectedAsGlb(bool combine = false)
        {
            try
            {
                if (_selectedCombinedMesh == null || _selectedMeshObjects == null ||
                    _selectedForExport == null || _selectedForExport.Count == 0)
                {
                    _lastAction = "Nothing selected for export";
                    return;
                }

                _lastAction = $"Exporting {_selectedForExport.Count} objects...";
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] GLB export: {_selectedForExport.Count} objects from \"{_selectedCombinedMesh.name}\"");

                // Read mesh data once — keep triangles per-submesh for multi-material support
                Vector3[] srcVerts;
                Vector3[] srcNormals;
                Vector2[] srcUVs;
                List<int[]> srcSubmeshTris;

#if IL2CPP
                if (!_selectedCombinedMesh.isReadable)
                {
                    _lastAction = "Export: mesh not readable (use Mono build)";
                    return;
                }
                srcVerts = _selectedCombinedMesh.vertices;
                srcNormals = _selectedCombinedMesh.normals;
                srcUVs = _selectedCombinedMesh.uv;
                srcSubmeshTris = new List<int[]>();
                for (int s = 0; s < _selectedCombinedMesh.subMeshCount; s++)
                    srcSubmeshTris.Add(_selectedCombinedMesh.GetTriangles(s));
#else
                if (_selectedCombinedMesh.isReadable)
                {
                    srcVerts = _selectedCombinedMesh.vertices;
                    srcNormals = _selectedCombinedMesh.normals;
                    srcUVs = _selectedCombinedMesh.uv;
                    srcSubmeshTris = new List<int[]>();
                    for (int s = 0; s < _selectedCombinedMesh.subMeshCount; s++)
                        srcSubmeshTris.Add(_selectedCombinedMesh.GetTriangles(s));
                }
                else if (!ReadMeshNative(_selectedCombinedMesh, out srcVerts, out srcNormals, out srcUVs, out srcSubmeshTris))
                {
                    _lastAction = "Export: couldn't read mesh data";
                    return;
                }
#endif

                int totalTris = 0;
                foreach (var st in srcSubmeshTris) totalTris += st.Length / 3;
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Mesh data: {srcVerts.Length} verts, {totalTris} tris, {srcSubmeshTris.Count} submeshes");

                // Determine vertex space once (probe with first selected renderer)
                MeshRenderer probeRenderer = null;
                foreach (int idx in _selectedForExport)
                {
                    if (idx < _selectedMeshObjects.Count && _selectedMeshObjects[idx] != null)
                    {
                        probeRenderer = _selectedMeshObjects[idx];
                        break;
                    }
                }

                if (probeRenderer == null)
                {
                    _lastAction = "Export: no valid renderers";
                    return;
                }

                var probeMF = probeRenderer.GetComponent<MeshFilter>();
                Transform meshTransform = probeMF != null ? probeMF.transform : probeRenderer.transform;

                var probeBounds = probeRenderer.bounds;
                var pMin = probeBounds.min;
                var pMax = probeBounds.max;
                int rawHits = 0, xfHits = 0;
                int probeStep = Math.Max(1, srcVerts.Length / 500);
                for (int i = 0; i < srcVerts.Length; i += probeStep)
                {
                    var v = srcVerts[i];
                    if (v.x >= pMin.x && v.x <= pMax.x && v.y >= pMin.y && v.y <= pMax.y && v.z >= pMin.z && v.z <= pMax.z)
                        rawHits++;
                    var tv = meshTransform.TransformPoint(v);
                    if (tv.x >= pMin.x && tv.x <= pMax.x && tv.y >= pMin.y && tv.y <= pMax.y && tv.z >= pMin.z && tv.z <= pMax.z)
                        xfHits++;
                }
                bool useRaw = rawHits >= xfHits;
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Vertex space probe: rawHits={rawHits} xfHits={xfHits} -> {(useRaw ? "raw" : "TransformPoint")}");

                // Extract each selected object — per-submesh for multi-material support
                var exportObjects = new List<ExportObject>();
                int exportedCount = 0;
                int skippedCount = 0;

                foreach (int idx in _selectedForExport)
                {
                    if (idx >= _selectedMeshObjects.Count) continue;
                    var mr = _selectedMeshObjects[idx];
                    if (mr == null) continue;

                    var bounds = mr.bounds;
                    var bMin = bounds.min;
                    var bMax = bounds.max;
                    var materials = mr.sharedMaterials;
                    string objName = GetMeaningfulName(mr);
                    Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] BEFORE extract \"{objName}\": {materials.Length} materials, shader={mr.sharedMaterials[0]?.shader?.name}");
                    for (int mi = 0; mi < materials.Length; mi++)
                        Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer]   mat[{mi}] = \"{materials[mi]?.name}\"");
                    bool anyGeometry = false;
                    int localMatIdx = 0;

                    // Try each submesh — matching submeshes map to renderer's materials in order
                    for (int s = 0; s < srcSubmeshTris.Count; s++)
                    {
                        var subTris = srcSubmeshTris[s];
                        var newVerts = new List<Vector3>();
                        var newNormals = new List<Vector3>();
                        var newUVs = new List<Vector2>();
                        var newTris = new List<int>();
                        var vertMap = new Dictionary<int, int>();

                        for (int i = 0; i < subTris.Length; i += 3)
                        {
                            int i0 = subTris[i], i1 = subTris[i + 1], i2 = subTris[i + 2];
                            var rawCenter = (srcVerts[i0] + srcVerts[i1] + srcVerts[i2]) / 3f;
                            var worldCenter = useRaw ? rawCenter : meshTransform.TransformPoint(rawCenter);

                            if (worldCenter.x < bMin.x || worldCenter.x > bMax.x ||
                                worldCenter.y < bMin.y || worldCenter.y > bMax.y ||
                                worldCenter.z < bMin.z || worldCenter.z > bMax.z)
                                continue;

                            int[] tri = { i0, i1, i2 };
                            for (int ti = 0; ti < 3; ti++)
                            {
                                int vidx = tri[ti];
                                if (!vertMap.ContainsKey(vidx))
                                {
                                    vertMap[vidx] = newVerts.Count;
                                    var worldVert = useRaw ? srcVerts[vidx] : meshTransform.TransformPoint(srcVerts[vidx]);
                                    newVerts.Add(worldVert - bounds.center);
                                    newNormals.Add(srcNormals != null && vidx < srcNormals.Length ? srcNormals[vidx] : Vector3.up);
                                    newUVs.Add(srcUVs != null && vidx < srcUVs.Length ? srcUVs[vidx] : Vector2.zero);
                                }
                                newTris.Add(vertMap[vidx]);
                            }
                        }

                        if (newTris.Count == 0) continue;

                        // This submesh has geometry for this renderer — map to next material
                        Material mat = (materials != null && localMatIdx < materials.Length)
                            ? materials[localMatIdx] : null;
                        localMatIdx++;

                        string partName = (materials != null && materials.Length > 1 && mat != null)
                            ? $"{objName}_{mat.name}" : objName;

                        exportObjects.Add(new ExportObject
                        {
                            Name = partName,
                            Vertices = newVerts.ToArray(),
                            Normals = newNormals.ToArray(),
                            UVs = newUVs.ToArray(),
                            Triangles = newTris.ToArray(),
                            Material = mat
                        });
                        anyGeometry = true;
                    }

                    // Check if materials changed after extraction
                    var matsAfter = mr.sharedMaterials;
                    Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] AFTER extract \"{objName}\": {matsAfter.Length} materials");
                    for (int mi = 0; mi < matsAfter.Length; mi++)
                        Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer]   mat[{mi}] = \"{matsAfter[mi]?.name}\"");

                    if (anyGeometry) exportedCount++;
                    else skippedCount++;
                }

                if (exportObjects.Count == 0)
                {
                    _lastAction = $"Export: 0 objects had geometry (skipped {skippedCount})";
                    Melon<MeshVaultPlugin>.Logger.Warning("[MeshPlacer] No geometry found for any selected objects");
                    return;
                }

                // Combine mode: merge all export objects into one mesh (no material separation)
                if (combine && exportObjects.Count > 1)
                {
                    var allVerts = new List<Vector3>();
                    var allNormals = new List<Vector3>();
                    var allUVs = new List<Vector2>();
                    var allTris = new List<int>();
                    Material firstMat = null;
                    foreach (var eo in exportObjects)
                    {
                        int baseVert = allVerts.Count;
                        allVerts.AddRange(eo.Vertices);
                        allNormals.AddRange(eo.Normals);
                        allUVs.AddRange(eo.UVs);
                        for (int ti = 0; ti < eo.Triangles.Length; ti++)
                            allTris.Add(eo.Triangles[ti] + baseVert);
                        if (firstMat == null) firstMat = eo.Material;
                    }
                    string combinedName = exportObjects[0].Name + "_combined";
                    exportObjects.Clear();
                    exportObjects.Add(new ExportObject
                    {
                        Name = combinedName,
                        Vertices = allVerts.ToArray(),
                        Normals = allNormals.ToArray(),
                        UVs = allUVs.ToArray(),
                        Triangles = allTris.ToArray(),
                        Material = firstMat
                    });
                    Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Combined into single mesh: {allVerts.Count} verts, {allTris.Count / 3} tris");
                }

                string exportDir = Path.Combine(Application.dataPath, "..", "UserData", "MeshVault", "Exports");
                Directory.CreateDirectory(exportDir);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // Build filename from exported object names
                var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var eo in exportObjects)
                    fileNames.Add(eo.Name);
                string baseName = string.Join("_", fileNames);
                // Sanitize for filesystem
                foreach (char c in Path.GetInvalidFileNameChars())
                    baseName = baseName.Replace(c, '_');
                if (baseName.Length > 60) baseName = baseName.Substring(0, 60);
                string glbPath = Path.Combine(exportDir, $"{baseName}_{timestamp}.glb");

                string result = GlbExporter.Export(exportObjects, glbPath);
                if (result != null)
                {
                    string mode = combine ? "combined" : "separate";
                    _lastAction = $"Exported {exportedCount} objects ({mode}) to GLB";
                    Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] GLB export ({mode}): {exportedCount} objects, {skippedCount} skipped -> {glbPath}");
                }
                else
                {
                    _lastAction = "GLB export failed -- check log";
                }
            }
            catch (Exception ex)
            {
                _lastAction = $"Export failed: {ex.Message}";
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] ExportSelectedAsGlb failed: {ex}");
            }
        }
    }
}
#endif
