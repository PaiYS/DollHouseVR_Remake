using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DollHouse.EditorSetup {
    /// <summary>
    /// Marker-driven play-mode test for the sketch tool (for automation when the
    /// editor can't be driven interactively):
    ///   Temp/run_sketch_play_test.marker      enter play mode and run the test
    ///   Temp/continue_sketch_play_test.marker proceed to the clear-all check
    ///   Temp/stop_sketch_play_test.marker     exit play mode
    /// Simulates mouse input through the Input System so the real
    /// SketchController raycast/draw path is exercised. Results are logged with
    /// the SKETCHTEST prefix.
    /// </summary>
    [InitializeOnLoad]
    internal static class SketchPlayTest {
        private const string kStartMarker    = "Temp/run_sketch_play_test.marker";
        private const string kContinueMarker = "Temp/continue_sketch_play_test.marker";
        private const string kStopMarker     = "Temp/stop_sketch_play_test.marker";
        private const string kActiveFlag     = "DollHouse.SketchPlayTest.Active";

        // World-space segments to draw (floor, then across the table group).
        private static readonly Vector3[,] kSegments = {
            { new Vector3(-2f, 0f, -1.2f), new Vector3(1.5f, 0f, -1.2f) },
            { new Vector3(0.2f, 0f, 0.6f), new Vector3(1.6f, 0f, 0.6f) },
        };
        private const float kDrawSeconds = 1.2f;
        private const float kGapSeconds  = 0.4f;

        private static int    _phase;
        private static int    _segment;
        private static double _phaseStart;
        private static SketchController _sketch;
        private static Camera _camera;

        static SketchPlayTest() {
            EditorApplication.update += Tick;
        }

        private static void Tick() {
            if (!EditorApplication.isPlaying) {
                if (File.Exists(kStartMarker)) {
                    File.Delete(kStartMarker);
                    SessionState.SetBool(kActiveFlag, true);
                    _phase = 0;
                    EditorApplication.isPlaying = true;
                }
                return;
            }

            if (File.Exists(kStopMarker)) {
                File.Delete(kStopMarker);
                SessionState.SetBool(kActiveFlag, false);
                EditorApplication.isPlaying = false;
                return;
            }

            if (!SessionState.GetBool(kActiveFlag, false))
                return;

            RunStateMachine();
        }

        private static double now => EditorApplication.timeSinceStartup;

        private static void RunStateMachine() {
            switch (_phase) {
                case 0: { // wait for Normcore connection
                    var realtime = Object.FindFirstObjectByType<Normal.Realtime.Realtime>();
                    if (realtime != null && realtime.connected) {
                        _sketch = Object.FindFirstObjectByType<SketchController>();
                        if (_sketch == null) { Fail("no SketchController in scene"); return; }
                        var so = new SerializedObject(_sketch);
                        _camera = so.FindProperty("_camera").objectReferenceValue as Camera;
                        if (_camera == null) { Fail("SketchController has no camera"); return; }
                        if (!_sketch.sketchMode) _sketch.ToggleSketchMode();
                        Debug.Log("SKETCHTEST: connected, sketch mode = " + _sketch.sketchMode);
                        _segment = 0;
                        NextPhase(1);
                    } else if (_phaseStart == 0) {
                        _phaseStart = now;
                    } else if (now - _phaseStart > 30.0) {
                        Fail("Normcore did not connect within 30 s");
                    }
                    break;
                }
                case 1: { // press-drag one segment
                    float t = (float)((now - _phaseStart) / kDrawSeconds);
                    Vector3 world = Vector3.Lerp(kSegments[_segment, 0], kSegments[_segment, 1], Mathf.Clamp01(t));
                    QueueMouse(world, true);
                    if (t >= 1f) {
                        QueueMouse(kSegments[_segment, 1], false);
                        NextPhase(2);
                    }
                    break;
                }
                case 2: { // gap between segments
                    if (now - _phaseStart < kGapSeconds) break;
                    _segment++;
                    if (_segment < kSegments.GetLength(0)) { NextPhase(1); break; }
                    // verify
                    int strokes = SketchStroke.instances.Count;
                    int points  = 0;
                    foreach (var s in SketchStroke.instances) {
                        var line = s.GetComponent<LineRenderer>();
                        if (line != null) points += line.positionCount;
                    }
                    if (strokes == kSegments.GetLength(0) && points >= 2 * strokes)
                        Debug.Log($"SKETCHTEST: DRAW PASS — {strokes} strokes, {points} line points total. " +
                                  "Waiting for continue marker (clear test).");
                    else
                        Fail($"draw check — {strokes} strokes, {points} line points");
                    NextPhase(3);
                    break;
                }
                case 3: { // hold for screenshot until continue marker
                    if (!File.Exists(kContinueMarker)) break;
                    File.Delete(kContinueMarker);
                    _sketch.ClearAll();
                    NextPhase(4);
                    break;
                }
                case 4: { // verify clear
                    if (now - _phaseStart < 0.5) break;
                    int strokes = SketchStroke.instances.Count;
                    if (strokes == 0) Debug.Log("SKETCHTEST: CLEAR PASS — all strokes removed. DONE");
                    else              Fail($"clear check — {strokes} strokes remain");
                    SessionState.SetBool(kActiveFlag, false);
                    EditorApplication.isPlaying = false;
                    break;
                }
            }
        }

        private static void NextPhase(int phase) {
            _phase = phase;
            _phaseStart = now;
        }

        private static void QueueMouse(Vector3 worldPoint, bool leftDown) {
            Mouse mouse = Mouse.current;
            if (mouse == null) mouse = InputSystem.AddDevice<Mouse>();
            Vector3 screen = _camera.WorldToScreenPoint(worldPoint);
            var state = new MouseState { position = new Vector2(screen.x, screen.y) };
            state = state.WithButton(MouseButton.Left, leftDown);
            InputSystem.QueueStateEvent(mouse, state);
        }

        private static void Fail(string message) {
            Debug.LogError("SKETCHTEST: FAIL — " + message);
            SessionState.SetBool(kActiveFlag, false);
            EditorApplication.isPlaying = false;
        }
    }
}
