using UnityEngine;
using UnityEngine.InputSystem;
using Normal.Realtime;

namespace DollHouse {
    /// <summary>
    /// Input for User B on the flat touchscreen (touch or mouse, via the Input System):
    /// - Press anywhere: the giant finger appears at that spot in the room.
    /// - Press on furniture and drag: the furniture follows (ownership is requested first).
    /// </summary>
    public class BirdsEyeController : MonoBehaviour {
        [SerializeField] private Realtime _realtime;
        [SerializeField] private Camera   _camera;
        [SerializeField] private LayerMask _furnitureMask;
        [SerializeField] private LayerMask _fingerSurfaceMask; // floor + furniture
        [SerializeField] private string _fingerPrefabName = "GiantFinger";
        [Tooltip("Half extents of the room interior (x, z); dragged furniture is kept inside.")]
        [SerializeField] private Vector2 _roomHalfExtents = new Vector2(3.95f, 2.95f);
        [Tooltip("Optional: suspends this controller in sketch mode and blocks presses on its UI.")]
        [SerializeField] private SketchController _sketchController;
        [Tooltip("Degrees per mouse scroll notch while dragging furniture.")]
        [SerializeField] private float _rotateStepDegrees = 15f;

        private GiantFinger       _finger;
        private RealtimeTransform _fingerTransform;

        private RealtimeTransform _draggedItem;
        private Collider          _draggedCollider;
        private float             _dragPlaneY;
        private Vector3           _dragOffset;
        private Vector3           _dragHalfSize;     // world AABB extents of the dragged item
        private Vector3           _dragCenterOffset; // bounds center relative to transform position
        private bool              _wasPressed;
        private bool              _suspended;
        private bool              _pressBlockedByUI;
        private bool              _twisting;
        private float             _lastTwistAngle;

        private void OnEnable() {
            if (_realtime != null)
                _realtime.didConnectToRoom += DidConnectToRoom;
        }

        private void OnDisable() {
            if (_realtime != null)
                _realtime.didConnectToRoom -= DidConnectToRoom;
        }

        private void DidConnectToRoom(Realtime realtime) {
            GameObject fingerGO = Realtime.Instantiate(_fingerPrefabName, new Realtime.InstantiateOptions {
                ownedByClient               = true,
                preventOwnershipTakeover    = true,
                destroyWhenOwnerLeaves      = true,
                destroyWhenLastClientLeaves = true,
                useInstance                 = realtime,
            });
            _finger          = fingerGO.GetComponent<GiantFinger>();
            _fingerTransform = fingerGO.GetComponent<RealtimeTransform>();
        }

        /// <summary>While suspended (sketch mode) this controller ignores input.</summary>
        public void SetSuspended(bool suspended) {
            _suspended = suspended;
            if (!suspended)
                return;
            if (_draggedItem != null) {
                _draggedItem.ClearOwnership();
                _draggedItem = null;
                _draggedCollider = null;
            }
            _twisting = false;
            if (_finger != null)
                _finger.SetVisible(false);
            _wasPressed       = false;
            _pressBlockedByUI = false;
        }

        private void Update() {
            if (_suspended || _camera == null || _realtime == null || !_realtime.connected)
                return;

            bool pressed = TryGetPointer(out Vector2 screenPos);

            // A press that starts on the sketch UI is ignored for its whole duration.
            if (pressed && !_wasPressed && _sketchController != null &&
                _sketchController.IsPointerOverUI(screenPos))
                _pressBlockedByUI = true;
            if (!pressed)
                _pressBlockedByUI = false;
            if (_pressBlockedByUI)
                pressed = false;
            Ray ray = pressed ? _camera.ScreenPointToRay(screenPos) : default;

            // --- Furniture dragging -------------------------------------------------
            if (pressed && !_wasPressed) {
                if (Physics.Raycast(ray, out RaycastHit grabHit, 100f, _furnitureMask)) {
                    RealtimeTransform rt = grabHit.collider.GetComponentInParent<RealtimeTransform>();
                    if (rt != null) {
                        rt.RequestOwnership();
                        _draggedItem    = rt;
                        _draggedCollider = grabHit.collider;
                        _dragPlaneY  = rt.transform.position.y;
                        Vector3 planePoint = PointOnPlane(ray, _dragPlaneY);
                        _dragOffset  = rt.transform.position - planePoint;
                        Bounds itemBounds = grabHit.collider.bounds;
                        _dragHalfSize     = itemBounds.extents;
                        _dragCenterOffset = itemBounds.center - rt.transform.position;
                    }
                }
            } else if (pressed && _draggedItem != null) {
                Vector3 planePoint = PointOnPlane(ray, _dragPlaneY);

                // --- Rotation: two-finger twist, or mouse scroll while dragging.
                float rotationDelta = ReadRotationDelta();
                if (Mathf.Abs(rotationDelta) > 0.01f) {
                    Vector3 pivot = _draggedItem.transform.position + _dragCenterOffset;
                    _draggedItem.transform.RotateAround(pivot, Vector3.up, rotationDelta);
                    Physics.SyncTransforms(); // collider bounds must reflect the new yaw
                    Bounds b = _draggedCollider != null ? _draggedCollider.bounds
                        : new Bounds(_draggedItem.transform.position, _dragHalfSize * 2f);
                    _dragHalfSize     = b.extents;
                    _dragCenterOffset = b.center - _draggedItem.transform.position;
                    _dragOffset       = _draggedItem.transform.position - planePoint; // no jump
                }

                Vector3 target = planePoint + _dragOffset;

                // Keep the item's full bounds inside the room walls.
                Vector3 center = target + _dragCenterOffset;
                center.x = Mathf.Clamp(center.x,
                    -_roomHalfExtents.x + _dragHalfSize.x, _roomHalfExtents.x - _dragHalfSize.x);
                center.z = Mathf.Clamp(center.z,
                    -_roomHalfExtents.y + _dragHalfSize.z, _roomHalfExtents.y - _dragHalfSize.z);
                _draggedItem.transform.position = center - _dragCenterOffset;
            } else if (!pressed && _draggedItem != null) {
                _draggedItem.ClearOwnership();
                _draggedItem = null;
                _draggedCollider = null;
                _twisting = false;
            }

            // --- Giant finger -------------------------------------------------------
            if (_finger != null) {
                if (pressed && Physics.Raycast(ray, out RaycastHit fingerHit, 100f, _fingerSurfaceMask)) {
                    _fingerTransform.transform.position = fingerHit.point;
                    _finger.SetVisible(true);
                } else {
                    _finger.SetVisible(false);
                }
            }

            _wasPressed = pressed;
        }

        /// <summary>
        /// Yaw delta for the dragged item this frame: the angle change of the line
        /// between the first two touches (twist gesture), plus mouse-wheel steps.
        /// </summary>
        private float ReadRotationDelta() {
            float delta = 0f;

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null) {
                int count = 0;
                Vector2 p0 = default, p1 = default;
                foreach (var touch in touchscreen.touches) {
                    if (!touch.press.isPressed) continue;
                    if (count == 0) p0 = touch.position.ReadValue();
                    else if (count == 1) { p1 = touch.position.ReadValue(); }
                    if (++count >= 2) break;
                }
                if (count >= 2) {
                    float angle = Mathf.Atan2(p1.y - p0.y, p1.x - p0.x) * Mathf.Rad2Deg;
                    if (_twisting)
                        delta -= Mathf.DeltaAngle(_lastTwistAngle, angle); // screen CCW = world CW yaw
                    _lastTwistAngle = angle;
                    _twisting = true;
                } else {
                    _twisting = false;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null) {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) {
                    // Windows wheels report +-120 per notch; trackpads report small values.
                    float notches = Mathf.Clamp(Mathf.Abs(scroll) >= 60f ? scroll / 120f : Mathf.Sign(scroll), -3f, 3f);
                    delta += notches * _rotateStepDegrees;
                }
            }

            return Mathf.Clamp(delta, -45f, 45f);
        }

        private static Vector3 PointOnPlane(Ray ray, float y) {
            Plane plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            return plane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : ray.origin;
        }

        internal static bool TryGetPointer(out Vector2 position) {
            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed) {
                position = touch.primaryTouch.position.ReadValue();
                return true;
            }
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed) {
                position = mouse.position.ReadValue();
                return true;
            }
            position = default;
            return false;
        }
    }
}
