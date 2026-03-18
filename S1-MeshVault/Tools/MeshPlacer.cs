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
using Il2CppInterop.Runtime.Injection;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Police;
using Il2CppTMPro;
using GameCanvasScaler = Il2CppScheduleOne.UI.CanvasScaler;
using GameHUD = Il2CppScheduleOne.UI.HUD;
using GameInput = Il2CppScheduleOne.GameInput;
#else
using ScheduleOne.DevUtilities;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Police;
using TMPro;
using GameCanvasScaler = ScheduleOne.UI.CanvasScaler;
using GameHUD = ScheduleOne.UI.HUD;
using GameInput = ScheduleOne.GameInput;
#endif

namespace MeshVault.Tools
{
    public partial class MeshPlacer : MonoBehaviour
    {
        public MeshPlacer() : base() { }
#if IL2CPP
        public MeshPlacer(IntPtr ptr) : base(ptr) { }
#endif

        public static void Register()
        {
#if IL2CPP
            ClassInjector.RegisterTypeInIl2Cpp<MeshPlacer>();
#endif
        }

        internal static MeshPlacer Instance { get; private set; }

        private void Awake() => Instance = this;

        internal bool IsPositioning => _active && _preview != null;

        // Tool state
        private bool _active;

        // Editor mode callbacks (one-shot, cleared after firing)
        private Action _editorOnConfirm;
        private Action _editorOnCancel;

        // Hierarchy picker
        private GameObject _hierarchyPanel;
        private List<(Transform node, int depth, bool hasMesh)> _hierarchyNodes;
        private Dictionary<Transform, bool> _hierarchyChecked;
        private HashSet<Transform> _hierarchyExpanded;
        private List<Transform> _hierarchyAncestors;
        private Transform _hierarchyListContent;
        private Transform _highlightedNode;

        // Preview
        private GameObject _preview;
        private Transform _previewSourceTransform;
        private string _previewSourceName;
        private Vector3 _previewPosition;
        private Vector3 _previewRotation;
        private Vector3 _previewScale = Vector3.one;
        private bool _previewIsLiveObject;
        private string _previewDbId;

        // Edit mode
        private enum EditMode { Position, Rotation, Scale }
        private EditMode _mode = EditMode.Position;

        // Feedback
        private string _lastAction = "(none)";

        // GUI overlay
        private Texture2D _bgTex;

        // AABB Extract mode
        private bool _extractMode;
        private Vector3 _extractCenter;
        private Vector3 _extractSize = new Vector3(2.11f, 1.09f, 0.91f);
        private MeshFilter _extractSourceMF;
        private GameObject _extractBox;

        // StaticBatch extract
        private int _extractFirstSubMesh;
        private int _extractSubMeshCount;
        private List<MeshRenderer> _extractSourceRenderers;
        private string _extractNodeName;

        // Material preview panel
        private GameObject _materialPreviewPanel;
        private Camera _materialPreviewCamera;
        private RenderTexture _materialPreviewRT;
        private GameObject _materialPreviewMeshCopy;
        private string _materialPreviewEntryId;
        private string[] _materialPreviewOverrides;
        private Color?[] _colorPreviewOverrides;
        private Dictionary<int, GameObject> _selectedSwatchBorders;
        private GameObject _colorPickerOverlay;
        private Dictionary<string, float[]> _materialCatalog;
        private GameObject _previewBackdrop;
        private RawImage _previewRawImage;
        private Vector3 _previewOrbitCenter;
        private float _previewOrbitYaw;
        private float _previewOrbitPitch;
        private float _previewOrbitDist;
        private float _previewOrbitDistMin;
        private bool _previewDragging;
        private Vector3 _previewDragStart;

        // Positioner overrides (persist from preview through position/log cycle)
        private string[] _positionerMaterialOverrides;
        private Color?[] _positionerColorOverrides;

        // Combined Mesh Browser
        private GameObject _combinedMeshPanel;
        private Dictionary<Mesh, List<MeshRenderer>> _combinedMeshGroups;
        private Mesh _selectedCombinedMesh;
        private List<MeshRenderer> _selectedMeshObjects;
        private HashSet<int> _selectedForExport;
        private string _combinedSearchTerm = "";
        private float _objectListScrollPos;

        // Step sizes
        private static readonly float[] StepSizes = { 0.01f, 0.1f, 1f, 10f };
        private int _stepIndex = 1;

        // Health maintenance
        private float _healthTimer;

        // Camera control
        private float _camYaw;
        private float _camPitch;
        private float _camMoveSpeed = 10f;
        private bool _cursorFree;
        private bool _freecamActive;
        private bool _wasIsTyping;
        private const float CamLookSensitivity = 2f;

        // Crosshair highlight (freecam bounding box preview)
        private bool _crosshairHighlight;
        private GameObject _crosshairHighlightBox;
        private Transform _crosshairHighlightTarget;
        private MeshRenderer[] _cachedRenderers;
        private float _rendererCacheTime;
        private float _fallbackScanTimer;
        private const float RendererCacheInterval = 2f;
        private const float FallbackScanInterval = 0.15f;
        private static bool IsShiftHeld => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Constants
        private const float PreviewDistance = 3f;
        private const float PreviewYOffset = -1f;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (_active)
                    DeactivateTool();
                else
                    ActivateTool();
                return;
            }

            if (!_active) return;

            if (_freecamActive)
            {
                // Health maintenance — top up every 2 seconds
                _healthTimer += Time.deltaTime;
                if (_healthTimer >= 2f)
                {
                    _healthTimer = 0f;
                    if (Player.Local != null && Player.Local.Health.CurrentHealth < 100f)
                        Player.Local.Health.SetHealth(100f);
                }

                UpdateCameraControl();
                if (_crosshairHighlight && !_cursorFree)
                    UpdateCrosshairHighlight();
                else if (_crosshairHighlight && _cursorFree)
                    ClearCrosshairHighlight();
            }

            if (Input.GetKeyDown(KeyCode.Keypad5))
            {
                if (_extractMode)
                    _mode = _mode == EditMode.Position ? EditMode.Scale : EditMode.Position;
                else
                    _mode = (EditMode)(((int)_mode + 1) % 3);
                _lastAction = $"Mode: {_mode}";
                RefreshEditorPanel();
            }

            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                _stepIndex = Mathf.Min(_stepIndex + 1, StepSizes.Length - 1);
                _lastAction = $"Step: {StepSizes[_stepIndex]}";
                RefreshEditorPanel();
            }
            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                _stepIndex = Mathf.Max(_stepIndex - 1, 0);
                _lastAction = $"Step: {StepSizes[_stepIndex]}";
                RefreshEditorPanel();
            }

            // Shift + Numpad+/- to change step size
            if (IsShiftHeld)
            {
                if (Input.GetKeyDown(KeyCode.KeypadPlus))
                {
                    _stepIndex = Mathf.Min(_stepIndex + 1, StepSizes.Length - 1);
                    _lastAction = $"Step: {StepSizes[_stepIndex]}";
                    RefreshEditorPanel();
                }
                if (Input.GetKeyDown(KeyCode.KeypadMinus))
                {
                    _stepIndex = Mathf.Max(_stepIndex - 1, 0);
                    _lastAction = $"Step: {StepSizes[_stepIndex]}";
                    RefreshEditorPanel();
                }
            }

            if (Input.GetKeyDown(KeyCode.Keypad1))
            {
                if (_hierarchyPanel != null)
                {
                    CloseHierarchyPanel();
                    _lastAction = "Hierarchy closed";
                }
                else if (_extractMode)
                {
                    ExecuteExtract();
                }
                else if (_preview != null)
                {
                    if (_previewSourceTransform != null)
                        OnHierarchyExtract(_previewSourceTransform);
                    else
                        _lastAction = "Preview source lost — re-scan with Numpad1";
                }
                else
                {
                    var cam = PlayerSingleton<PlayerCamera>.Instance;
                    if (cam == null) { _lastAction = "No camera"; return; }

                    var ray = new Ray(cam.transform.position, cam.transform.forward);
                    int mask = ~(1 << LayerMask.NameToLayer("Player") | 1 << LayerMask.NameToLayer("NoCollide"));
                    if (Physics.Raycast(ray, out RaycastHit hit, 20f, mask))
                        ShowHierarchyPicker(hit);
                    else
                    {
                        // Fallback: find renderer without collider (e.g. overpass)
                        var rendererTarget = FindRendererAlongRay(ray, 100f);
                        if (rendererTarget != null)
                            ShowHierarchyPicker(rendererTarget);
                        else
                            _lastAction = "Nothing hit";
                    }
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.Keypad3))
            {
                if (_combinedMeshPanel != null)
                {
                    CloseCombinedMeshPanel();
                    _lastAction = "Combined mesh browser closed";
                }
                else
                    ShowCombinedMeshBrowser();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Keypad0))
            {
                if (_spawnPanel != null)
                {
                    CloseSpawnPanel();
                    _lastAction = "Spawn panel closed";
                }
                else
                    ShowSpawnPanel();
                return;
            }

            // Interactive preview: orbit + zoom
            if (_materialPreviewPanel != null && Cursor.lockState != CursorLockMode.Locked)
                UpdatePreviewInteraction();

            if (_extractMode)
            {
                if (Input.GetKeyDown(KeyCode.Keypad6)) AdjustExtract(0, +1);
                if (Input.GetKeyDown(KeyCode.Keypad4)) AdjustExtract(0, -1);
                if (Input.GetKeyDown(KeyCode.Keypad8)) AdjustExtract(2, +1);
                if (Input.GetKeyDown(KeyCode.Keypad2)) AdjustExtract(2, -1);
                if (Input.GetKeyDown(KeyCode.Keypad9) || (!IsShiftHeld && Input.GetKeyDown(KeyCode.KeypadPlus))) AdjustExtract(1, +1);
                if (Input.GetKeyDown(KeyCode.Keypad7) || (!IsShiftHeld && Input.GetKeyDown(KeyCode.KeypadMinus))) AdjustExtract(1, -1);
                UpdateExtractBox();
                return;
            }

            if (_preview != null)
            {
                bool adjusted = false;
                if (Input.GetKeyDown(KeyCode.Keypad6)) { Adjust(0, +1); adjusted = true; }
                if (Input.GetKeyDown(KeyCode.Keypad4)) { Adjust(0, -1); adjusted = true; }
                if (Input.GetKeyDown(KeyCode.Keypad8)) { Adjust(2, +1); adjusted = true; }
                if (Input.GetKeyDown(KeyCode.Keypad2)) { Adjust(2, -1); adjusted = true; }
                if (Input.GetKeyDown(KeyCode.Keypad9) || (!IsShiftHeld && Input.GetKeyDown(KeyCode.KeypadPlus))) { Adjust(1, +1); adjusted = true; }
                if (Input.GetKeyDown(KeyCode.Keypad7) || (!IsShiftHeld && Input.GetKeyDown(KeyCode.KeypadMinus))) { Adjust(1, -1); adjusted = true; }
                if (adjusted) RefreshEditorPanel();
                _preview.transform.position = _previewPosition;
                _preview.transform.eulerAngles = _previewRotation;
                _preview.transform.localScale = _previewScale;
            }

            if (Input.GetKeyDown(KeyCode.KeypadEnter) && _preview != null)
                LogPlacement();

            if (Input.GetKeyDown(KeyCode.Delete))
            {
                if (_materialPreviewPanel != null)
                {
                    ClosePreviewPanel();
                    _lastAction = "Preview panel closed";
                }
                else if (_spawnPanel != null)
                {
                    CloseSpawnPanel();
                    _lastAction = "Spawn panel closed";
                }
                else if (_combinedMeshPanel != null)
                {
                    CloseCombinedMeshPanel();
                    _lastAction = "Combined mesh browser closed";
                }
                else if (_hierarchyPanel != null)
                {
                    CloseHierarchyPanel();
                    _lastAction = "Hierarchy closed";
                }
                else if (_extractMode)
                {
                    StopExtractMode();
                    _lastAction = "Extract mode cancelled";
                }
                else if (_preview != null)
                {
                    DestroyPreview();
                    _lastAction = "Preview cleared";
                }
            }

            if (Input.GetKeyDown(KeyCode.KeypadPeriod))
                RaycastInspect();

            if (Input.GetKeyDown(KeyCode.KeypadDivide) && _preview == null)
                GrabObject();

            if (Input.GetKeyDown(KeyCode.Insert))
                CopyObject();

            if (Input.GetKeyDown(KeyCode.KeypadMultiply))
            {
                _crosshairHighlight = !_crosshairHighlight;
                if (!_crosshairHighlight) ClearCrosshairHighlight();
                _lastAction = _crosshairHighlight ? "Crosshair highlight ON" : "Crosshair highlight OFF";
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Shared helpers
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Enters editor mode: suppresses game input, optionally enables freecam
        /// (camera override, HUD hidden, movement disabled, police ignored).
        /// </summary>
        private void ActivateTool(bool useFreecam = true)
        {
            _active = true;
            _healthTimer = 0f;
            _cursorFree = false;
            _freecamActive = useFreecam;

            // Suppress all game input (prevents shooting, item use, hotbar switching)
            _wasIsTyping = GameInput.IsTyping;
            GameInput.IsTyping = true;

            if (useFreecam)
            {
                var cam = PlayerSingleton<PlayerCamera>.Instance;
                if (cam != null)
                {
                    cam.OverrideTransform(cam.transform.position, cam.transform.rotation, 0f);
                    cam.AddActiveUIElement("MeshPlacer");

                    var euler = cam.transform.eulerAngles;
                    _camYaw = euler.y;
                    _camPitch = euler.x;
                    if (_camPitch > 180f) _camPitch -= 360f;
                }

                var movement = PlayerSingleton<PlayerMovement>.Instance;
                if (movement != null)
                    movement.CanMove = false;

                var inv = PlayerSingleton<PlayerInventory>.Instance;
                if (inv != null)
                {
                    inv.SetViewmodelVisible(false);
                    inv.SetInventoryEnabled(false);
                }

                var hud = Singleton<GameHUD>.Instance;
                if (hud != null)
                    hud.canvas.enabled = false;

                if (Player.Local != null)
                {
                    Player.Local.SetVisibleToLocalPlayer(true);
                    Player.Local.Health.SetHealth(100f);
                }

                SetPoliceIgnore(true);

                // Cursor locked for camera look (Tab to toggle free)
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                // No freecam — just free the cursor for UI interaction
                _cursorFree = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            _lastAction = "Editor active — Numpad1 to scan";
        }

        /// <summary>
        /// Sets a consumer-provided GameObject as the active preview for positioning.
        /// Activates the tool if not already active, resets mouse state, and opens the editor panel.
        /// One-shot callbacks fire on confirm (Enter/Log) or cancel (Del/F9 exit).
        /// </summary>
        internal void OpenEditorForObject(GameObject target, string displayName,
            Action onConfirm, Action onCancel, bool useFreecam = true)
        {
            if (!_active) ActivateTool(useFreecam);
            DestroyPreview(); // clean up any existing preview (fires old cancel)

            // Force-reset mouse/cursor state in case a game UI (phone, etc.) left it dirty
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam != null)
                cam.LockMouse();
            if (useFreecam)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                _cursorFree = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _cursorFree = true;
            }

            _preview = target;
            _previewSourceName = displayName ?? target.name;
            _previewIsLiveObject = true;
            _previewPosition = new Vector3(
                Mathf.Round(target.transform.position.x * 100f) / 100f,
                Mathf.Round(target.transform.position.y * 100f) / 100f,
                Mathf.Round(target.transform.position.z * 100f) / 100f);
            _previewRotation = target.transform.eulerAngles;
            _previewScale = target.transform.localScale;
            _mode = EditMode.Position;

            _editorOnConfirm = onConfirm;
            _editorOnCancel = onCancel;

            ShowEditorPanel();
            _lastAction = $"Positioning \"{_previewSourceName}\"";
        }

        /// <summary>
        /// Exits editor mode: closes all panels, restores game input, and if freecam was
        /// active restores camera, movement, inventory, HUD, and police awareness.
        /// </summary>
        private void DeactivateTool()
        {
            _active = false;
            _cursorFree = false;

            CloseEditorPanel();
            CloseSpawnPanel();
            ClosePreviewPanel();
            CloseCombinedMeshPanel();
            CloseHierarchyPanel();
            DestroyPreview();
            StopExtractMode();
            ClearCrosshairHighlight();
            _crosshairHighlight = false;
            _cachedRenderers = null;

            // Restore game input
            GameInput.IsTyping = _wasIsTyping;

            if (_freecamActive)
            {
                var cam = PlayerSingleton<PlayerCamera>.Instance;
                if (cam != null)
                {
                    cam.RemoveActiveUIElement("MeshPlacer");
                    cam.StopTransformOverride(0f);
                }

                var movement = PlayerSingleton<PlayerMovement>.Instance;
                if (movement != null)
                    movement.CanMove = true;

                var inv = PlayerSingleton<PlayerInventory>.Instance;
                if (inv != null)
                {
                    inv.SetViewmodelVisible(true);
                    inv.SetInventoryEnabled(true);
                }

                var hud = Singleton<GameHUD>.Instance;
                if (hud != null)
                    hud.canvas.enabled = true;

                if (Player.Local != null)
                    Player.Local.SetVisibleToLocalPlayer(false);

                SetPoliceIgnore(false);

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            _freecamActive = false;
            _lastAction = "Editor deactivated";
        }

        /// <summary>
        /// Handles freecam input: Tab toggles cursor lock, mouse look when locked,
        /// WASD/Space/Ctrl movement, scroll wheel adjusts flight speed.
        /// </summary>
        private void UpdateCameraControl()
        {
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam == null) return;

            // Tab toggles cursor free/locked for UI interaction
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _cursorFree = !_cursorFree;
                if (_cursorFree)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            // Mouse look when cursor is locked (default state)
            if (!_cursorFree)
            {
                float mx = Input.GetAxis("Mouse X") * CamLookSensitivity;
                float my = Input.GetAxis("Mouse Y") * CamLookSensitivity;
                _camYaw += mx;
                _camPitch = Mathf.Clamp(_camPitch - my, -89f, 89f);
                cam.transform.rotation = Quaternion.Euler(_camPitch, _camYaw, 0f);
            }

            // WASD + Space/Ctrl movement (always available)
            float speed = _camMoveSpeed * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.LeftShift)) speed *= 3f;

            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += cam.transform.forward;
            if (Input.GetKey(KeyCode.S)) move -= cam.transform.forward;
            if (Input.GetKey(KeyCode.D)) move += cam.transform.right;
            if (Input.GetKey(KeyCode.A)) move -= cam.transform.right;
            if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
            if (Input.GetKey(KeyCode.LeftControl)) move -= Vector3.up;

            if (move.sqrMagnitude > 0.001f)
                cam.transform.position += move.normalized * speed;

            // Scroll wheel adjusts flight speed (only when cursor locked)
            if (!_cursorFree)
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f)
                    _camMoveSpeed = Mathf.Clamp(_camMoveSpeed + scroll * 0.5f, 1f, 50f);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Crosshair highlight
        // ═══════════════════════════════════════════════════════════════

        private void UpdateCrosshairHighlight()
        {
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam == null) return;

            var ray = new Ray(cam.transform.position, cam.transform.forward);
            int mask = ~(1 << LayerMask.NameToLayer("Player") | 1 << LayerMask.NameToLayer("NoCollide"));

            Transform target = null;

            // Try physics raycast first (objects with colliders — cheap)
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, mask))
            {
                target = hit.collider.transform;
            }
            else
            {
                // Throttle the expensive renderer scan
                _fallbackScanTimer += Time.unscaledDeltaTime;
                if (_fallbackScanTimer < FallbackScanInterval)
                    return; // Keep current highlight until next scan
                _fallbackScanTimer = 0f;

                target = FindRendererAlongRay(ray, 100f);
            }

            if (target == null)
            {
                ClearCrosshairHighlight();
                return;
            }

            if (target == _crosshairHighlightTarget) return;

            ClearCrosshairHighlight();

            try
            {
                _crosshairHighlightTarget = target;

                var renderers = target.GetComponentsInChildren<MeshRenderer>();
                if (renderers == null || renderers.Length == 0) return;

                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                _crosshairHighlightBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _crosshairHighlightBox.name = "MV_CrosshairHighlight";
                var col = _crosshairHighlightBox.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);
                _crosshairHighlightBox.transform.position = bounds.center;
                _crosshairHighlightBox.transform.localScale = bounds.size + Vector3.one * 0.02f;

                var boxMR = _crosshairHighlightBox.GetComponent<MeshRenderer>();
                boxMR.shadowCastingMode = ShadowCastingMode.Off;
                boxMR.receiveShadows = false;
                var mat = new Material(Shader.Find("Sprites/Default"));
                mat.color = new Color(0f, 1f, 1f, 0.15f);
                boxMR.material = mat;
            }
            catch (Exception ex)
            {
                Melon<MeshVaultPlugin>.Logger.Warning($"[MeshPlacer] Crosshair highlight failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Finds the nearest MeshRenderer whose bounding box intersects the ray.
        /// Uses a cached renderer list refreshed every <see cref="RendererCacheInterval"/> seconds.
        /// </summary>
        private Transform FindRendererAlongRay(Ray ray, float maxDist)
        {
            // Refresh cache periodically instead of every call
            float now = Time.unscaledTime;
            if (_cachedRenderers == null || now - _rendererCacheTime > RendererCacheInterval)
            {
                _cachedRenderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                _rendererCacheTime = now;
            }

            Transform best = null;
            float bestDist = maxDist;

            foreach (var mr in _cachedRenderers)
            {
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                if (mr.gameObject.name.StartsWith("MV_")) continue;

                var b = mr.bounds;
                if (b.IntersectRay(ray, out float dist) && dist > 0f && dist < bestDist)
                {
                    bestDist = dist;
                    best = mr.transform;
                }
            }

            return best;
        }

        private void ClearCrosshairHighlight()
        {
            if (_crosshairHighlightBox != null)
            {
                try { UnityEngine.Object.Destroy(_crosshairHighlightBox); } catch { }
                _crosshairHighlightBox = null;
            }
            _crosshairHighlightTarget = null;
        }

        private static void SetPoliceIgnore(bool ignore)
        {
            var officers = UnityEngine.Object.FindObjectsOfType<PoliceOfficer>(true);
            foreach (var officer in officers)
                officer.SetIgnorePlayers(ignore);
        }

        private void Adjust(int axis, int sign)
        {
            float step = StepSizes[_stepIndex];
            float delta = step * sign;
            string[] axisNames = { "X", "Y", "Z" };
            switch (_mode)
            {
                case EditMode.Position:
                    _previewPosition[axis] += delta;
                    _previewPosition[axis] = RoundToStep(_previewPosition[axis], step);
                    _lastAction = $"Pos {axisNames[axis]} {(delta >= 0 ? "+" : "")}{delta} -> {_previewPosition[axis]:F4}";
                    break;
                case EditMode.Rotation:
                    _previewRotation[axis] = (_previewRotation[axis] + delta) % 360f;
                    if (_previewRotation[axis] < 0f) _previewRotation[axis] += 360f;
                    _lastAction = $"Rot {axisNames[axis]} {(delta >= 0 ? "+" : "")}{delta} -> {_previewRotation[axis]:F2}";
                    break;
                case EditMode.Scale:
                    _previewScale[axis] = Mathf.Max(0.01f, _previewScale[axis] + delta);
                    _previewScale[axis] = RoundToStep(_previewScale[axis], step);
                    _lastAction = $"Scale {axisNames[axis]} {(delta >= 0 ? "+" : "")}{delta} -> {_previewScale[axis]:F4}";
                    break;
            }
        }

        private static float RoundToStep(float value, float step) =>
            Mathf.Round(value / step) * step;

        private void DestroyPreview()
        {
            // Fire cancel callback before clearing state
            var cancelCb = _editorOnCancel;
            _editorOnConfirm = null;
            _editorOnCancel = null;
            cancelCb?.Invoke();

            if (_preview != null && !_previewIsLiveObject)
                UnityEngine.Object.Destroy(_preview);
            _preview = null;
            _previewIsLiveObject = false;
            _previewSourceTransform = null;
            _previewSourceName = null;
            _previewDbId = null;
            _positionerMaterialOverrides = null;
            _positionerColorOverrides = null;
            CloseEditorPanel();
        }

        private void LogPlacement()
        {
            if (_preview == null) return;

            var p = _previewPosition;
            var r = _previewRotation;
            var s = _previewScale;

            string output =
                $"[MeshPlacer] Source: \"{_previewSourceName}\"\n" +
                $"[MeshPlacer] Position: new Vector3({p.x:F4}f, {p.y:F4}f, {p.z:F4}f)\n" +
                $"[MeshPlacer] Rotation: Quaternion.Euler({r.x:F2}f, {r.y:F2}f, {r.z:F2}f)\n" +
                $"[MeshPlacer] Scale:    new Vector3({s.x:F4}f, {s.y:F4}f, {s.z:F4}f)";

            if (_previewIsLiveObject && _preview.transform.parent != null)
            {
                var lp = _preview.transform.localPosition;
                var le = _preview.transform.localRotation.eulerAngles;
                output += $"\n[MeshPlacer] LocalPos: new Vector3({lp.x:F4}f, {lp.y:F4}f, {lp.z:F4}f)";
                output += $"\n[MeshPlacer] LocalRot: new Vector3({le.x:F2}f, {le.y:F2}f, {le.z:F2}f)";
                output += $"\n[MeshPlacer] FurnitureSlot: LocalPosition = new Vector3({lp.x:F4}f, {lp.y:F4}f, {lp.z:F4}f), EulerAngles = new Vector3({le.x:F2}f, {le.y:F2}f, {le.z:F2}f)";
            }

            if (_previewDbId != null)
            {
                output += $"\n[MeshPlacer] {FormatSpawnCall(_previewDbId, p, r, _positionerMaterialOverrides, _positionerColorOverrides)}";
            }

            Melon<MeshVaultPlugin>.Logger.Msg(output);
            GUIUtility.systemCopyBuffer = output;
            _lastAction = "Logged to clipboard";

            // Fire confirm callback
            var confirmCb = _editorOnConfirm;
            _editorOnConfirm = null;
            _editorOnCancel = null;
            confirmCb?.Invoke();
        }

        private static string FormatSpawnCall(string id, Vector3 pos, Vector3 rot,
            string[] materialOverrides, Color?[] colorOverrides)
        {
            var sb = new StringBuilder();
            sb.Append($"MeshVaultAPI.Spawn(\"{id}\", ");
            sb.Append($"new Vector3({pos.x:F4}f, {pos.y:F4}f, {pos.z:F4}f), ");
            sb.Append($"Quaternion.Euler({rot.x:F2}f, {rot.y:F2}f, {rot.z:F2}f)");

            if (materialOverrides != null)
            {
                sb.Append(", materialOverrides: new[] { ");
                for (int i = 0; i < materialOverrides.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(materialOverrides[i] != null ? $"\"{materialOverrides[i]}\"" : "null");
                }
                sb.Append(" }");
            }

            if (colorOverrides != null)
            {
                sb.Append(", colorOverrides: new Color?[] { ");
                for (int i = 0; i < colorOverrides.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    if (colorOverrides[i].HasValue)
                    {
                        var c = colorOverrides[i].Value;
                        sb.Append($"new Color({c.r:F2}f, {c.g:F2}f, {c.b:F2}f, {c.a:F2}f)");
                    }
                    else
                    {
                        sb.Append("null");
                    }
                }
                sb.Append(" }");
            }

            sb.Append(")");
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════
        // GUI overlay
        // ═══════════════════════════════════════════════════════════════

        private Texture2D _crosshairTex;

        private void DrawCrosshair()
        {
            if (_crosshairTex == null)
            {
                _crosshairTex = new Texture2D(1, 1);
                _crosshairTex.SetPixel(0, 0, Color.white);
                _crosshairTex.Apply();
            }

            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;
            const float len = 12f;
            const float thick = 2f;

            var oldColor = GUI.color;
            GUI.color = new Color(0f, 1f, 0f, 0.8f);
            // Horizontal line
            GUI.DrawTexture(new Rect(cx - len, cy - thick / 2f, len * 2f, thick), _crosshairTex);
            // Vertical line
            GUI.DrawTexture(new Rect(cx - thick / 2f, cy - len, thick, len * 2f), _crosshairTex);
            GUI.color = oldColor;
        }

        private void OnGUI()
        {
            if (!_active) return;

            // Always draw crosshair when tool is active
            DrawCrosshair();

            // Minimal status line when panels or editor are active
            if (_hierarchyPanel != null || _combinedMeshPanel != null || _spawnPanel != null
                || _materialPreviewPanel != null || _editorPanel != null)
            {
                GUI.Label(new Rect(10, 10, 400, 24), $"<b><color=#00ffff>MeshPlacer</color></b>  {_lastAction}");
                return;
            }

            if (_preview == null && !_extractMode)
            {
                GUI.Label(new Rect(10, 10, 700, 24),
                    "<b><color=#00ffff>MeshPlacer Active</color></b>  —  Numpad1: scan  |  Numpad3: meshes  |  Numpad0: spawn  |  Numpad/: grab  |  Ins: copy  |  F9: close");
                return;
            }

            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.85f));
                _bgTex.Apply();
            }
            var boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = _bgTex;

            if (_extractMode)
            {
                float w = 380f, h = 210f;
                float x = Screen.width - w - 10f;
                float y = Screen.height - h - 10f;
                GUI.Box(new Rect(x, y, w, h), "", boxStyle);
                GUILayout.BeginArea(new Rect(x + 10, y + 5, w - 20, h - 10));

                GUILayout.Label("<b><color=#00ff00>AABB EXTRACT MODE</color></b>");
                GUILayout.Space(4);

                string modeLabel = _mode == EditMode.Position ? "CENTER" : "SIZE";
                GUILayout.Label($"<size=16><b>>>> {modeLabel} <<<</b></size>");
                GUILayout.Label($"Step: {StepSizes[_stepIndex]}  (PgUp/PgDn to cycle)");
                GUILayout.Space(4);

                string cPrefix = _mode == EditMode.Position ? ">> " : "   ";
                string sPrefix = _mode == EditMode.Scale ? ">> " : "   ";
                GUILayout.Label($"{cPrefix}Center: ({_extractCenter.x:F2}, {_extractCenter.y:F2}, {_extractCenter.z:F2})");
                GUILayout.Label($"{sPrefix}Size:   ({_extractSize.x:F2}, {_extractSize.y:F2}, {_extractSize.z:F2})");
                GUILayout.Space(4);
                GUILayout.Label("Numpad5=mode  Numpad1=SAVE  Del=cancel");
                GUILayout.Label($"<b>Last:</b> {_lastAction}");

                GUILayout.EndArea();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Diagnostics
        // ═══════════════════════════════════════════════════════════════

        private static void DumpMaterialProperties(Material mat)
        {
            if (mat == null) { Melon<MeshVaultPlugin>.Logger.Msg("[MeshPlacer] DumpMat: null material"); return; }

            var shader = mat.shader;
            Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] === Material \"{mat.name}\" shader=\"{shader?.name}\" ===");

            int propCount = shader.GetPropertyCount();

            for (int i = 0; i < propCount; i++)
            {
                string pName = shader.GetPropertyName(i);
                var pType = shader.GetPropertyType(i);
                string desc = shader.GetPropertyDescription(i);

                switch (pType)
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        float f = mat.GetFloat(pName);
                        Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer]   [{pType}] {pName} (\"{desc}\") = {f}");
                        break;
                    case ShaderPropertyType.Color:
                        var c = mat.GetColor(pName);
                        Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer]   [{pType}] {pName} (\"{desc}\") = ({c.r}, {c.g}, {c.b}, {c.a})");
                        break;
                    case ShaderPropertyType.Vector:
                        var v = mat.GetVector(pName);
                        Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer]   [{pType}] {pName} (\"{desc}\") = ({v.x}, {v.y}, {v.z}, {v.w})");
                        break;
                    case ShaderPropertyType.Texture:
                        var tex = mat.GetTexture(pName);
                        var scale = mat.GetTextureScale(pName);
                        var offset = mat.GetTextureOffset(pName);
                        Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer]   [{pType}] {pName} (\"{desc}\") = \"{tex?.name}\" " +
                            $"tiling=({scale.x}, {scale.y}) offset=({offset.x}, {offset.y})");
                        break;
                    default:
                        Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer]   [{pType}] {pName} (\"{desc}\")");
                        break;
                }
            }
            Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] === End Material Dump ===");
        }

        private static int DestroyAllOrphanedTestSpawns()
        {
            int count = 0;
            var allGOs = UnityEngine.Object.FindObjectsOfType<MeshFilter>();
            foreach (var mf in allGOs)
            {
                if (mf != null && mf.gameObject != null &&
                    (mf.gameObject.name.StartsWith("MV_TestSpawn_") || mf.gameObject.name.StartsWith("MeshVault_")))
                {
                    UnityEngine.Object.Destroy(mf.gameObject);
                    count++;
                }
            }
            if (count > 0)
                Melon<MeshVaultPlugin>.Logger.Msg($"[MeshPlacer] Destroyed {count} orphaned test spawn(s)");
            return count;
        }

        private void OnDestroy()
        {
            DestroyPreview();
            StopExtractMode();
            CloseEditorPanel();
            CloseHierarchyPanel();
            CloseCombinedMeshPanel();
            CloseSpawnPanel();
            ClosePreviewPanel();
        }
    }
}
#endif
