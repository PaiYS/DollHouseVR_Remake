using System.Collections.Generic;
using UnityEngine;
using Normal.Realtime;

namespace DollHouse {
    /// <summary>
    /// A single annotation stroke drawn by the bird's-eye player, rendered with a
    /// LineRenderer on every client. Instantiated via Realtime.Instantiate (owned by
    /// the drawing client while in progress, ownership cleared when finished so any
    /// client can clear it later).
    /// </summary>
    public class SketchStroke : RealtimeComponent<SketchStrokeModel> {
        [SerializeField] private LineRenderer _line;

        public const int kMaxPoints      = 1024;
        private const int kBytesPerPoint = 12;

        private static readonly List<SketchStroke> _instances = new List<SketchStroke>();
        /// <summary>Every live stroke on this client (local and remote).</summary>
        public static IReadOnlyList<SketchStroke> instances => _instances;

        // Owner-side append buffer; mirrors the model while this client draws the stroke.
        private readonly List<Vector3> _points = new List<Vector3>();

        private void OnEnable()  => _instances.Add(this);
        private void OnDisable() => _instances.Remove(this);

        protected override void OnRealtimeModelReplaced(SketchStrokeModel previousModel, SketchStrokeModel currentModel) {
            if (previousModel != null)
                previousModel.pointDataDidChange -= PointDataDidChange;

            if (currentModel != null) {
                currentModel.pointDataDidChange += PointDataDidChange;
                ApplyPointData(currentModel.pointData);
            }
        }

        private void PointDataDidChange(SketchStrokeModel model, byte[] data) => ApplyPointData(data);

        /// <summary>
        /// Owner-side: append a world-space point (already offset from the surface)
        /// and sync the stroke. Points closer than minDistance to the last one are skipped.
        /// </summary>
        public bool TryAppendPoint(Vector3 worldPoint, float minDistance) {
            if (model == null || _points.Count >= kMaxPoints)
                return false;
            if (_points.Count > 0 && Vector3.Distance(_points[_points.Count - 1], worldPoint) < minDistance)
                return false;

            _points.Add(worldPoint);

            byte[] data = new byte[_points.Count * kBytesPerPoint];
            for (int i = 0; i < _points.Count; i++) {
                Vector3 p = _points[i];
                System.Buffer.BlockCopy(System.BitConverter.GetBytes(p.x), 0, data, i * kBytesPerPoint,     4);
                System.Buffer.BlockCopy(System.BitConverter.GetBytes(p.y), 0, data, i * kBytesPerPoint + 4, 4);
                System.Buffer.BlockCopy(System.BitConverter.GetBytes(p.z), 0, data, i * kBytesPerPoint + 8, 4);
            }
            model.pointData = data;
            return true;
        }

        /// <summary>
        /// Owner-side: finish the stroke. A single tap becomes a short dash so it still
        /// renders, and ownership is released so other clients may clear the stroke.
        /// </summary>
        public void Finish() {
            if (_points.Count == 1)
                TryAppendPoint(_points[0] + new Vector3(0.02f, 0f, 0f), 0f);

            RealtimeView view = GetComponent<RealtimeView>();
            if (view != null && view.isOwnedLocallySelf)
                view.ClearOwnership();
        }

        private void ApplyPointData(byte[] data) {
            if (_line == null)
                return;
            int count = (data == null) ? 0 : data.Length / kBytesPerPoint;
            _line.positionCount = count;
            for (int i = 0; i < count; i++) {
                _line.SetPosition(i, new Vector3(
                    System.BitConverter.ToSingle(data, i * kBytesPerPoint),
                    System.BitConverter.ToSingle(data, i * kBytesPerPoint + 4),
                    System.BitConverter.ToSingle(data, i * kBytesPerPoint + 8)));
            }
        }
    }
}
