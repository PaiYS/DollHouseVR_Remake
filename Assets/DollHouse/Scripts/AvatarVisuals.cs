using UnityEngine;
using Normal.Realtime;

namespace DollHouse {
    /// <summary>
    /// Hides the avatar's own visuals for the local player (a head box in front of the
    /// camera would block the view); remote clients see everything, including the
    /// vision cone that shows where the VR player is looking.
    /// </summary>
    public class AvatarVisuals : MonoBehaviour {
        [SerializeField] private GameObject[] _hideForLocalPlayer;

        private void Start() {
            RealtimeView view = GetComponent<RealtimeView>();
            if (view != null && view.isOwnedLocallyInHierarchy) {
                foreach (GameObject go in _hideForLocalPlayer)
                    if (go != null) go.SetActive(false);
            }
        }
    }
}
