using System;
using UnityEngine;
using UnityEngine.UI;

namespace DollHouse {
    /// <summary>
    /// One app-key entry panel (there is a screen-space one for desktop and a
    /// world-space one for VR). The field starts locked with the default key;
    /// Edit unlocks it, Enter submits.
    /// </summary>
    public class AppKeyPanel : MonoBehaviour {
        [SerializeField] private InputField _input;
        [SerializeField] private Button _editButton;
        [SerializeField] private Button _enterButton;

        public event Action<string> submitted;

        private void Awake() {
            if (_editButton != null)
                _editButton.onClick.AddListener(() => {
                    _input.interactable = true;
                    _input.Select();
                    _input.ActivateInputField(); // opens the system keyboard on Quest
                });
            if (_enterButton != null)
                _enterButton.onClick.AddListener(() => submitted?.Invoke(_input != null ? _input.text : ""));
        }

        public void Show(string prefill) {
            gameObject.SetActive(true);
            if (_input != null) {
                _input.text = prefill;
                _input.interactable = false; // read-only until Edit
            }
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
