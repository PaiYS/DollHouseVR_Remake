using UnityEngine;

namespace DollHouse {
    /// <summary>
    /// Drives the humanoid body (Bob) from the three network-synced targets
    /// (Head, LeftHand, RightHand). Runs identically on every client, so the
    /// remote side gets a full-body avatar without any extra networking:
    /// - body stands under the head and smoothly turns with it,
    /// - the neck bone mirrors the head target (look up in VR -> Bob looks up),
    /// - two-bone IK pulls each arm toward its hand target (point -> Bob points).
    /// Bind-pose local rotations are restored every frame before applying the
    /// world-space deltas, so the solve never accumulates drift.
    /// </summary>
    public class AvatarBodyDriver : MonoBehaviour {
        [Header("Synced targets (driven by RealtimeTransforms)")]
        [SerializeField] private Transform _headTarget;
        [SerializeField] private Transform _leftHandTarget;
        [SerializeField] private Transform _rightHandTarget;

        [Header("Body")]
        [SerializeField] private Transform _bodyRoot;   // Bob instance root
        [SerializeField] private Transform _neckBone;
        [SerializeField] private float _eyeHeightOfModel = 1.65f; // head-bone height in the model
        [SerializeField] private float _turnSpeed = 6f;

        [Header("Locomotion animation")]
        [SerializeField] private Animator _animator;    // ThirdPersonAnimatorController (Forward/Turn/OnGround)
        [SerializeField] private float _walkSpeedForFullForward = 2.2f; // m/s mapped to Forward = 1
        [SerializeField] private float _turnRateForFullTurn = 180f;     // deg/s mapped to Turn = 1

        [Header("Left arm chain")]
        [SerializeField] private Transform _leftUpperArm;
        [SerializeField] private Transform _leftForeArm;
        [SerializeField] private Transform _leftHandBone;

        [Header("Right arm chain")]
        [SerializeField] private Transform _rightUpperArm;
        [SerializeField] private Transform _rightForeArm;
        [SerializeField] private Transform _rightHandBone;

        private Quaternion _neckBindLocal;
        private Quaternion _lUpperBindLocal, _lForeBindLocal;
        private Quaternion _rUpperBindLocal, _rForeBindLocal;
        private Quaternion _neckBindWorldInverseTimesBody; // neck bind relative to body yaw
        private float _lUpperLen, _lLowerLen, _rUpperLen, _rLowerLen;
        private float _bodyYaw;
        private Vector3 _lastBodyPos;
        private float _smoothedSpeed;

        private void Start() {
            if (_animator != null) {
                _animator.applyRootMotion = false; // the driver positions the body
                _animator.SetBool("OnGround", true);
                _animator.SetBool("Crouch", false);
            }
            _lastBodyPos = _bodyRoot != null ? _bodyRoot.position : transform.position;
            if (_neckBone != null) {
                _neckBindLocal = _neckBone.localRotation;
                // Neck bind orientation relative to the body, captured in bind pose.
                _neckBindWorldInverseTimesBody = Quaternion.Inverse(_bodyRoot.rotation) * _neckBone.rotation;
            }
            CacheArm(_leftUpperArm, _leftForeArm, _leftHandBone, out _lUpperBindLocal, out _lForeBindLocal, out _lUpperLen, out _lLowerLen);
            CacheArm(_rightUpperArm, _rightForeArm, _rightHandBone, out _rUpperBindLocal, out _rForeBindLocal, out _rUpperLen, out _rLowerLen);
            _bodyYaw = transform.eulerAngles.y;
        }

        private static void CacheArm(Transform upper, Transform fore, Transform hand,
                                     out Quaternion upperBind, out Quaternion foreBind,
                                     out float upperLen, out float lowerLen) {
            upperBind = upper != null ? upper.localRotation : Quaternion.identity;
            foreBind  = fore  != null ? fore.localRotation  : Quaternion.identity;
            upperLen  = (upper != null && fore != null) ? Vector3.Distance(upper.position, fore.position) : 0.3f;
            lowerLen  = (fore  != null && hand != null) ? Vector3.Distance(fore.position, hand.position)  : 0.3f;
        }

        private void LateUpdate() {
            if (_headTarget == null || _bodyRoot == null)
                return;

            // --- body: stand under the head, turn smoothly toward head yaw
            Vector3 headPos = _headTarget.position;
            float floorY = transform.position.y; // avatar root rides the XR origin (floor level)
            _bodyRoot.position = new Vector3(headPos.x, floorY, headPos.z);

            Vector3 flatForward = _headTarget.forward; flatForward.y = 0f;
            float yawBefore = _bodyYaw;
            if (flatForward.sqrMagnitude > 0.001f) {
                float targetYaw = Quaternion.LookRotation(flatForward).eulerAngles.y;
                _bodyYaw = Mathf.LerpAngle(_bodyYaw, targetYaw, Time.deltaTime * _turnSpeed);
            }
            _bodyRoot.rotation = Quaternion.Euler(0f, _bodyYaw, 0f);

            // --- locomotion animation from actual body motion
            if (_animator != null && Time.deltaTime > 0.0001f) {
                Vector3 planarDelta = _bodyRoot.position - _lastBodyPos; planarDelta.y = 0f;
                float speed = planarDelta.magnitude / Time.deltaTime;
                _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, speed, Time.deltaTime * 8f);
                float forward = Mathf.Clamp01(_smoothedSpeed / _walkSpeedForFullForward);

                float yawRate = Mathf.DeltaAngle(yawBefore, _bodyYaw) / Time.deltaTime; // deg/s
                float turn = Mathf.Clamp(yawRate / _turnRateForFullTurn, -1f, 1f);

                _animator.SetFloat("Forward", forward, 0.1f, Time.deltaTime);
                _animator.SetFloat("Turn", turn, 0.1f, Time.deltaTime);
                _lastBodyPos = _bodyRoot.position;
            }

            // --- restore bind pose so the solves below are deterministic
            if (_neckBone != null) _neckBone.localRotation = _neckBindLocal;
            if (_leftUpperArm  != null) _leftUpperArm.localRotation  = _lUpperBindLocal;
            if (_leftForeArm   != null) _leftForeArm.localRotation   = _lForeBindLocal;
            if (_rightUpperArm != null) _rightUpperArm.localRotation = _rUpperBindLocal;
            if (_rightForeArm  != null) _rightForeArm.localRotation  = _rForeBindLocal;

            // --- neck: mirror the head target's rotation
            if (_neckBone != null)
                _neckBone.rotation = _headTarget.rotation * _neckBindWorldInverseTimesBody;

            // --- arms
            SolveArm(_leftUpperArm, _leftForeArm, _leftHandBone, _leftHandTarget, _lUpperLen, _lLowerLen, -_bodyRoot.right);
            SolveArm(_rightUpperArm, _rightForeArm, _rightHandBone, _rightHandTarget, _rUpperLen, _rLowerLen, _bodyRoot.right);
        }

        private void SolveArm(Transform upper, Transform fore, Transform hand, Transform target,
                              float a, float b, Vector3 outDir) {
            if (upper == null || fore == null || hand == null || target == null)
                return;

            Vector3 shoulder = upper.position;
            Vector3 goal = target.position;
            Vector3 toGoal = goal - shoulder;
            float dist = Mathf.Clamp(toGoal.magnitude, 0.05f, a + b - 0.001f);
            Vector3 dir = toGoal.normalized;

            // Elbow position via law of cosines; hint pushes the elbow down/outward.
            float cosAngle = Mathf.Clamp((a * a + dist * dist - b * b) / (2f * a * dist), -1f, 1f);
            float elbowAngle = Mathf.Acos(cosAngle);
            Vector3 hint = (Vector3.down + outDir * 0.5f - dir * 0.2f).normalized;
            Vector3 bendAxis = Vector3.Cross(dir, hint);
            if (bendAxis.sqrMagnitude < 0.0001f) bendAxis = Vector3.Cross(dir, Vector3.forward);
            bendAxis.Normalize();
            Vector3 elbowDir = Quaternion.AngleAxis(elbowAngle * Mathf.Rad2Deg, bendAxis) * dir;
            Vector3 elbow = shoulder + elbowDir * a;

            // Rotate upper arm toward the elbow, then forearm toward the goal.
            upper.rotation = Quaternion.FromToRotation(fore.position - upper.position, elbow - shoulder) * upper.rotation;
            fore.rotation  = Quaternion.FromToRotation(hand.position - fore.position, goal - fore.position) * fore.rotation;
        }
    }
}
