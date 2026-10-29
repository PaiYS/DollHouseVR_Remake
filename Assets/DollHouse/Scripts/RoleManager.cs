using UnityEngine;
using Normal.Realtime;

namespace DollHouse {
    /// <summary>
    /// Decides which side of the asymmetric experience this instance plays:
    /// - VRPlayer  (User A): Quest headset, walks the room at human scale.
    /// - BirdsEye  (User B): flat touchscreen, looks down into the room.
    /// On Android (Quest) the role defaults to VRPlayer; everywhere else to BirdsEye.
    /// </summary>
    public class RoleManager : MonoBehaviour {
        public enum Role { Auto, VRPlayer, BirdsEye }

        [Header("Role")]
        [Tooltip("Auto = VR on Android (Quest), Bird's-eye on desktop/editor.")]
        [SerializeField] private Role _roleOverride = Role.Auto;

        [Header("Networking")]
        [SerializeField] private Realtime _realtime;
        [SerializeField] private RealtimeAvatarManager _avatarManager;
        [SerializeField] private GameObject _vrAvatarPrefab;
        [SerializeField] private string _roomName = "DollHouse";

        [Header("Rigs")]
        [SerializeField] private GameObject _xrRig;
        [SerializeField] private GameObject _birdsEyeRig;

        [Header("Features")]
        [SerializeField] private WebcamCeilingStream _ceilingStream;

        public Role activeRole { get; private set; }

        private void Awake() {
            Role role = _roleOverride;
            if (role == Role.Auto) {
#if UNITY_ANDROID && !UNITY_EDITOR
                role = Role.VRPlayer;
#else
                role = Role.BirdsEye;
#endif
            }
            activeRole = role;

            bool isVR = role == Role.VRPlayer;
            if (_xrRig       != null) _xrRig.SetActive(isVR);
            if (_birdsEyeRig != null) _birdsEyeRig.SetActive(!isVR);

            // Only the VR player embodies an avatar in the room.
            if (_avatarManager != null)
                _avatarManager.localAvatarPrefab = isVR ? _vrAvatarPrefab : null;

            // Only the bird's-eye player streams their webcam to the ceiling.
            if (!isVR && _ceilingStream != null)
                _ceilingStream.StartSending();
        }

        private void Start() {
            if (_realtime != null && !string.IsNullOrEmpty(_roomName))
                _realtime.Connect(_roomName);
        }
    }
}
