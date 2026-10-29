using System.IO;
using UnityEditor;

namespace DollHouse.EditorSetup {
    /// <summary>
    /// Runs SketchToolSetup.Run once after a domain reload when the marker file
    /// exists (written by automation when the AI-assistant RunCommand execute path
    /// is unavailable). Uses EditorApplication.update, not delayCall — delayCall
    /// does not fire in an unfocused editor.
    /// </summary>
    [InitializeOnLoad]
    internal static class SketchToolAutoRun {
        private const string kMarkerPath = "Temp/run_sketch_tool_setup.marker";

        static SketchToolAutoRun() {
            EditorApplication.update += RunOnce;
        }

        private static void RunOnce() {
            EditorApplication.update -= RunOnce;
            if (!File.Exists(kMarkerPath))
                return;
            File.Delete(kMarkerPath);
            SketchToolSetup.Run();
        }
    }
}
