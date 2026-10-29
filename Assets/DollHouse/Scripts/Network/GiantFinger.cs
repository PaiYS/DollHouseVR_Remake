using UnityEngine;
using Normal.Realtime;

namespace DollHouse {
    /// <summary>
    /// The giant finger the VR player sees when the bird's-eye player touches the room.
    /// Owned by the bird's-eye client; everyone toggles the renderers from the model.
    /// </summary>
    public class GiantFinger : RealtimeComponent<FingerModel> {
        [SerializeField] private Renderer[] _renderers;

        protected override void OnRealtimeModelReplaced(FingerModel previousModel, FingerModel currentModel) {
            if (previousModel != null)
                previousModel.visibleDidChange -= VisibleDidChange;

            if (currentModel != null) {
                if (currentModel.isFreshModel)
                    currentModel.visible = false;
                currentModel.visibleDidChange += VisibleDidChange;
                ApplyVisibility(currentModel.visible);
            }
        }

        private void VisibleDidChange(FingerModel model, bool visible) => ApplyVisibility(visible);

        /// <summary>Owner-side call (bird's-eye client).</summary>
        public void SetVisible(bool visible) {
            if (model != null && model.visible != visible)
                model.visible = visible;
        }

        private void ApplyVisibility(bool visible) {
            foreach (Renderer r in _renderers)
                if (r != null) r.enabled = visible;
        }
    }
}
