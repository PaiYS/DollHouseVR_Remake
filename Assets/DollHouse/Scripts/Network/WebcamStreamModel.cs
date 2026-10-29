using Normal.Realtime;

namespace DollHouse {
    /// <summary>
    /// Carries JPEG-compressed webcam frames from the bird's-eye client to everyone else.
    /// EncodeToJPG allocates a fresh array per frame, so the default reference-equality
    /// check detects every assignment as a change.
    /// </summary>
    [RealtimeModel]
    public partial class WebcamStreamModel {
        [RealtimeProperty(1, true, true)] private byte[] _frameData;
    }
}
