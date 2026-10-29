using Normal.Realtime;

namespace DollHouse {
    /// <summary>Visibility state of the giant finger; its pose is synced by RealtimeTransform.</summary>
    [RealtimeModel]
    public partial class FingerModel {
        [RealtimeProperty(1, true, true)] private bool _visible;
    }
}
