using UnityEngine;
using UnityEngine.UI;
using Normal.Realtime;

namespace DollHouse {
    /// <summary>
    /// Sketch/annotation tool for User B (bird's-eye touchscreen), the modern
    /// replacement for the 2016 sketch.cs. A screen-space button toggles between
    /// Move mode (BirdsEyeController: drag furniture, giant finger) and Sketch mode:
    /// press-drag raycasts the room and draws a stroke slightly above the hit
    /// surface. Strokes are Realtime.Instantiated so every client sees them;
    /// Clear removes all strokes for everyone.
    /// </summary>
    public class SketchController : MonoBehaviour {
        [Header("Scene")]
        [SerializeField] private Realtime  _realtime;
        [SerializeField] private Camera    _camera;
        [SerializeField] private LayerMask _surfaceMask;      // same surfaces the giant finger uses
        [SerializeField] private BirdsEyeController _birdsEye;

        [Header("Stroke")]
        [SerializeField] private string _strokePrefabName = "SketchStroke";
        [SerializeField] private float  _surfaceOffset    = 0.015f;
        [SerializeField] private float  _minPointDistance = 0.03f;

        [Header("UI")]
        [SerializeField] private Button  _sketchToggleButton;
        [SerializeField] private Graphic _sketchToggleBackground;
        [SerializeField] private Button  _clearButton;
        [SerializeField] private RectTransform[] _pointerBlockers; // screen-space UI that eats touches
        [SerializeField] private Color _modeOffColor = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField] private Color _modeOnColor  = new Color(1f, 0.55f, 0.15f, 1f);

        public bool sketchMode { get; private set; }

        private SketchStroke _activeStroke;
        private bool _pressBlockedByUI;

        private void Awake() {
            if (_sketchToggleButton != null) _sketchToggleButton.onClick.AddListener(ToggleSketchMode);
            if (_clearButton        != null) _clearButton.onClick.AddListener(ClearAll);
            ApplyModeVisuals();
        }

        private void Update() {
            if (!sketchMode || _camera == null || _realtime == null || !_realtime.connected)
                return;

            bool pressed = BirdsEyeController.TryGetPointer(out Vector2 screenPos);

            // A press that starts on the UI never draws, for its whole duration.
            if (pressed && _activeStroke == null && !_pressBlockedByUI && IsPointerOverUI(screenPos))
                _pressBlockedByUI = true;
            if (!pressed)
                _pressBlockedByUI = false;

            if (pressed && !_pressBlockedByUI) {
                Ray ray = _camera.ScreenPointToRay(screenPos);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f, _surfaceMask)) {
                    if (_activeStroke == null)
                        BeginStroke();
                    if (_activeStroke != null)
                        _activeStroke.TryAppendPoint(hit.point + hit.normal * _surfaceOffset, _minPointDistance);
                }
            } else if (_activeStroke != null) {
                EndStroke();
            }
        }

        private void OnDisable() {
            if (_activeStroke != null)
                EndStroke();
        }

        private void BeginStroke() {
            GameObject strokeGO = Realtime.Instantiate(_strokePrefabName, new Realtime.InstantiateOptions {
                ownedByClient               = true,
                preventOwnershipTakeover    = false,
                destroyWhenOwnerLeaves      = false, // strokes persist if the sketcher leaves
                destroyWhenLastClientLeaves = true,
                useInstance                 = _realtime,
            });
            _activeStroke = strokeGO.GetComponent<SketchStroke>();
        }

        private void EndStroke() {
            _activeStroke.Finish();
            _activeStroke = null;
        }

        /// <summary>UI button: switch between Move/Point mode and Sketch mode.</summary>
        public void ToggleSketchMode() {
            sketchMode = !sketchMode;
            if (!sketchMode && _activeStroke != null)
                EndStroke();
            _pressBlockedByUI = false;
            if (_birdsEye != null)
                _birdsEye.SetSuspended(sketchMode);
            ApplyModeVisuals();
        }

        /// <summary>UI button: delete every stroke, synced to all clients.</summary>
        public void ClearAll() {
            if (_realtime == null || !_realtime.connected)
                return;
            if (_activeStroke != null)
                EndStroke();
            for (int i = SketchStroke.instances.Count - 1; i >= 0; i--)
                Realtime.Destroy(SketchStroke.instances[i].gameObject);
        }

        /// <summary>True when the screen position is over one of the sketch UI controls.</summary>
        public bool IsPointerOverUI(Vector2 screenPos) {
            if (_pointerBlockers == null)
                return false;
            foreach (RectTransform rect in _pointerBlockers)
                if (rect != null && rect.gameObject.activeInHierarchy &&
                    RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos))
                    return true;
            return false;
        }

        private void ApplyModeVisuals() {
            if (_sketchToggleBackground != null)
                _sketchToggleBackground.color = sketchMode ? _modeOnColor : _modeOffColor;
        }
    }
}
