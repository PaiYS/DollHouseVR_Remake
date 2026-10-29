using Normal.Realtime;

namespace DollHouse {
    /// <summary>
    /// One sketch stroke: world-space points packed as consecutive float32 x,y,z
    /// triplets (12 bytes per point). The drawing client replaces the array on every
    /// appended point; the fresh array fails the reference-equality check, so each
    /// append syncs (same trick as WebcamStreamModel frames).
    /// </summary>
    [RealtimeModel]
    public partial class SketchStrokeModel {
        [RealtimeProperty(1, true, true)] private byte[] _pointData;
    }
}
