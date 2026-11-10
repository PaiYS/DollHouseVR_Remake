using UnityEngine;
using Normal;
using Normal.Realtime;

namespace DollHouse {
    /// <summary>
    /// Startup screen shown before connecting: the Normcore app key, pre-filled
    /// with the build's default (or the last key used on this device), editable
    /// if someone wants to run the experience on their own Normcore app.
    /// Connects via RoleManager once Enter is pressed.
    /// </summary>
    public class AppKeyGate : MonoBehaviour {
        [SerializeField] private Realtime _realtime;
        [SerializeField] private RoleManager _roleManager;
        [SerializeField] private AppKeyPanel _desktopPanel;
        [SerializeField] private AppKeyPanel _vrPanel;

        private const string kPrefsKey = "normcore_app_key";
        private AppKeyPanel _activePanel;

        private void Start() {
            string defaultKey = _realtime != null && _realtime.normcoreAppSettings != null
                ? _realtime.normcoreAppSettings.normcoreAppKey : "";
            string prefill = PlayerPrefs.GetString(kPrefsKey, defaultKey);
            if (string.IsNullOrWhiteSpace(prefill)) prefill = defaultKey;

            bool isVR = _roleManager != null && _roleManager.activeRole == RoleManager.Role.VRPlayer;
            _activePanel = isVR ? _vrPanel : _desktopPanel;
            AppKeyPanel otherPanel = isVR ? _desktopPanel : _vrPanel;
            if (otherPanel != null) otherPanel.Hide();

            if (_activePanel == null) { // misconfigured: fall back to direct connect
                if (_roleManager != null) _roleManager.ConnectToRoom();
                return;
            }
            _activePanel.submitted += OnSubmit;
            _activePanel.Show(prefill);
        }

        private void OnSubmit(string key) {
            key = (key ?? "").Trim();
            if (key.Length == 0) return;

            string currentKey = _realtime.normcoreAppSettings != null
                ? _realtime.normcoreAppSettings.normcoreAppKey : "";
            if (key != currentKey)
                _realtime.normcoreAppSettings = NormcoreAppSettings.CreateInstance(key);

            PlayerPrefs.SetString(kPrefsKey, key);
            PlayerPrefs.Save();

            _activePanel.Hide();
            _roleManager.ConnectToRoom();
        }
    }
}
