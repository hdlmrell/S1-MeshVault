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

        // Tool state
        private bool _active;

        // Hierarchy picker
        private GameObject _hierarchyPanel;
        private List<(Transform node, int depth, bool hasMesh)> _hierarchyNodes;
        private Dictionary<Transform, bool> _hierarchyChecked;
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

        // Input
        private bool _wasTypingFromUs;

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
        private bool _tabLooking;
        private float _objectListScrollPos;

        // Step sizes
        private static readonly float[] StepSizes = { 0.01f, 0.1f, 1f, 10f };
        private int _stepIndex = 1;

        // Constants
        private const float PreviewDistance = 3f;
        private const float PreviewYOffset = -1f;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                _active = !_active;
                if (_active)
                {
                    _lastAction = "Tool activated — Numpad1 to scan";
                }
                else
                {
                    CloseSpawnPanel();
                    ClosePreviewPanel();
                    CloseCombinedMeshPanel();
                    CloseHierarchyPanel();
                    DestroyPreview();
                    StopExtractMode();
                    _lastAction = "Tool deactivated";
                }
                return;
            }

            if (!_active) return;

            if (Input.GetKeyDown(KeyCode.Keypad5))
            {
                if (_extractMode)
                    _mode = _mode == EditMode.Position ? EditMode.Scale : EditMode.Position;
                else
                    _mode = (EditMode)(((int)_mode + 1) % 3);
                _lastAction = $"Mode: {_mode}";
            }

            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
            {
                _stepIndex = (_stepIndex + 1) % StepSizes.Length;
                _lastAction = $"Step: {StepSizes[_stepIndex]}";
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
                        _lastAction = "Nothing hit";
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

            if ((_combinedMeshPanel != null || _hierarchyPanel != null || _spawnPanel != null || _materialPreviewPanel != null))
            {
                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    _tabLooking = true;
                    var cam = PlayerSingleton<PlayerCamera>.Instance;
                    if (cam != null) cam.SetCanLook(true);
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                if (Input.GetKeyUp(KeyCode.Tab) && _tabLooking)
                {
                    _tabLooking = false;
                    var cam = PlayerSingleton<PlayerCamera>.Instance;
                    if (cam != null)
                    {
                        cam.SetCanLook(false);
                        cam.FreeMouse();
                    }
                }
            }

            // Interactive preview: orbit + zoom
            if (_materialPreviewPanel != null && !_tabLooking)
                UpdatePreviewInteraction();

            if (_extractMode)
            {
                if (Input.GetKeyDown(KeyCode.Keypad6)) AdjustExtract(0, +1);
                if (Input.GetKeyDown(KeyCode.Keypad4)) AdjustExtract(0, -1);
                if (Input.GetKeyDown(KeyCode.Keypad8)) AdjustExtract(2, +1);
                if (Input.GetKeyDown(KeyCode.Keypad2)) AdjustExtract(2, -1);
                if (Input.GetKeyDown(KeyCode.Keypad9) || Input.GetKeyDown(KeyCode.KeypadPlus)) AdjustExtract(1, +1);
                if (Input.GetKeyDown(KeyCode.Keypad7) || Input.GetKeyDown(KeyCode.KeypadMinus)) AdjustExtract(1, -1);
                UpdateExtractBox();
                return;
            }

            if (_preview != null)
            {
                if (Input.GetKeyDown(KeyCode.Keypad6)) Adjust(0, +1);
                if (Input.GetKeyDown(KeyCode.Keypad4)) Adjust(0, -1);
                if (Input.GetKeyDown(KeyCode.Keypad8)) Adjust(2, +1);
                if (Input.GetKeyDown(KeyCode.Keypad2)) Adjust(2, -1);
                if (Input.GetKeyDown(KeyCode.Keypad9) || Input.GetKeyDown(KeyCode.KeypadPlus)) Adjust(1, +1);
                if (Input.GetKeyDown(KeyCode.Keypad7) || Input.GetKeyDown(KeyCode.KeypadMinus)) Adjust(1, -1);
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

            if (Input.GetKeyDown(KeyCode.Insert) && _preview == null)
                CopyObject();
        }

        // ═══════════════════════════════════════════════════════════════
        // Shared helpers
        // ═══════════════════════════════════════════════════════════════

        private void Adjust(int axis, int sign)
        {
            float step = StepSizes[_stepIndex];
            float delta = step * sign;
            string[] axisNames = { "X", "Y", "Z" };
            switch (_mode)
            {
                case EditMode.Position:
                    _previewPosition[axis] += delta;
                    _lastAction = $"Pos {axisNames[axis]} {(delta >= 0 ? "+" : "")}{delta} -> {_previewPosition[axis]:F4}";
                    break;
                case EditMode.Rotation:
                    _previewRotation[axis] += delta;
                    _lastAction = $"Rot {axisNames[axis]} {(delta >= 0 ? "+" : "")}{delta} -> {_previewRotation[axis]:F2}";
                    break;
                case EditMode.Scale:
                    _previewScale[axis] = Mathf.Max(0.01f, _previewScale[axis] + delta);
                    _lastAction = $"Scale {axisNames[axis]} {(delta >= 0 ? "+" : "")}{delta} -> {_previewScale[axis]:F4}";
                    break;
            }
        }

        private void DestroyPreview()
        {
            if (_preview != null && !_previewIsLiveObject)
                UnityEngine.Object.Destroy(_preview);
            _preview = null;
            _previewIsLiveObject = false;
            _previewSourceTransform = null;
            _previewSourceName = null;
            _previewDbId = null;
            _positionerMaterialOverrides = null;
            _positionerColorOverrides = null;
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

        private void OnGUI()
        {
            if (!_active) return;

            if (_hierarchyPanel != null || _combinedMeshPanel != null || _spawnPanel != null || _materialPreviewPanel != null)
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
                GUILayout.Label($"Step: {StepSizes[_stepIndex]}  (Shift to cycle)");
                GUILayout.Space(4);

                string cPrefix = _mode == EditMode.Position ? ">> " : "   ";
                string sPrefix = _mode == EditMode.Scale ? ">> " : "   ";
                GUILayout.Label($"{cPrefix}Center: ({_extractCenter.x:F2}, {_extractCenter.y:F2}, {_extractCenter.z:F2})");
                GUILayout.Label($"{sPrefix}Size:   ({_extractSize.x:F2}, {_extractSize.y:F2}, {_extractSize.z:F2})");
                GUILayout.Space(4);
                GUILayout.Label("Numpad5=mode  Numpad1=SAVE  Del=cancel");
                GUILayout.Label($"<b>Last:</b> {_lastAction}");

                GUILayout.EndArea();
                return;
            }

            float pw = 360f, ph = 200f;
            float px = Screen.width - pw - 10f;
            float py = Screen.height - ph - 10f;
            GUI.Box(new Rect(px, py, pw, ph), "", boxStyle);

            GUILayout.BeginArea(new Rect(px + 10, py + 5, pw - 20, ph - 10));

            GUILayout.Label($"<b>Selected:</b> {_previewSourceName}");
            GUILayout.Space(4);

            string previewModeLabel = _mode switch
            {
                EditMode.Position => "POSITION",
                EditMode.Rotation => "ROTATION",
                EditMode.Scale => "SCALE",
                _ => "?"
            };
            GUILayout.Label($"<size=16><b>>>> {previewModeLabel} <<<</b></size>");
            GUILayout.Label($"Step: {StepSizes[_stepIndex]}  (Shift to cycle)");
            GUILayout.Space(4);

            string posPrefix = _mode == EditMode.Position ? ">> " : "   ";
            string rotPrefix = _mode == EditMode.Rotation ? ">> " : "   ";
            string sclPrefix = _mode == EditMode.Scale ? ">> " : "   ";

            GUILayout.Label($"{posPrefix}Position: ({_previewPosition.x:F2}, {_previewPosition.y:F2}, {_previewPosition.z:F2})");
            GUILayout.Label($"{rotPrefix}Rotation: ({_previewRotation.x:F1}, {_previewRotation.y:F1}, {_previewRotation.z:F1})");
            GUILayout.Label($"{sclPrefix}Scale:    ({_previewScale.x:F2}, {_previewScale.y:F2}, {_previewScale.z:F2})");

            GUILayout.Space(4);
            GUILayout.Label($"<b>Last:</b> {_lastAction}");

            GUILayout.EndArea();
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
                    (mf.gameObject.name.StartsWith("MV_TestSpawn_") || mf.gameObject.name.StartsWith("OTC_TestSpawn_")))
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
            CloseHierarchyPanel();
            CloseCombinedMeshPanel();
            CloseSpawnPanel();
            ClosePreviewPanel();
        }
    }
}
#endif
