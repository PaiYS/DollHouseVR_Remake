using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DollHouse.EditorSetup {
    /// <summary>
    /// Renders README screenshots of the app-key gate:
    ///   -batchmode -quit -executeMethod DollHouse.EditorSetup.AppKeyGateShots.Run
    /// Desktop panel composited over the bird's-eye view (canvas temporarily in
    /// camera space, since overlay canvases don't render into camera captures);
    /// VR card shot from the player's spawn eye point.
    /// </summary>
    public static class AppKeyGateShots {
        private const int kW = 1920, kH = 1080;

        public static void Run() {
            try {
                EditorSceneManager.OpenScene("Assets/DollHouse/Scenes/DollHouse.unity", OpenSceneMode.Single);
                Directory.CreateDirectory("Docs/images");

                Canvas desktopCanvas = null, vrCanvas = null;
                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                    if (c.name == "AppKeyPanel_Desktop") desktopCanvas = c;
                    else if (c.name == "AppKeyPanel_VR") vrCanvas = c;
                }
                if (desktopCanvas == null || vrCanvas == null)
                    throw new System.Exception("panels not found: desktop=" + (desktopCanvas != null) + " vr=" + (vrCanvas != null));
                int ceilingLayer = LayerMask.NameToLayer("CeilingOnly");

                // Make sure both panels are active in the saved scene (an earlier
                // tool pass accidentally saved the desktop one inactive).
                desktopCanvas.gameObject.SetActive(true);
                vrCanvas.gameObject.SetActive(true);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

                // --- VR view. Root-canvas Y lives in anchoredPosition (2D), which
                // overrides localPosition on every scene load — so pin the card's
                // height via anchoredPosition3D and save; a transform change here
                // also forces the CanvasRenderer rebuild that batch renders need.
                var vrRect = (RectTransform)vrCanvas.transform;
                vrRect.anchoredPosition3D = new Vector3(0f, 1.5f, -0.9f);
                vrRect.rotation = Quaternion.LookRotation(
                    new Vector3(0f, 1.5f, -0.9f) - new Vector3(0f, 1.6f, -1.8f));
                vrRect.localScale = Vector3.one * 0.0012f;
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                desktopCanvas.gameObject.SetActive(false);
                var vrCamGO = new GameObject("__GateShotCamVR");
                var vrCam = vrCamGO.AddComponent<Camera>();
                vrCamGO.transform.position = new Vector3(0f, 1.62f, -1.85f);
                vrCamGO.transform.rotation = Quaternion.LookRotation(
                    new Vector3(0f, 1.45f, -0.9f) - vrCamGO.transform.position);
                vrCam.fieldOfView = 70f;
                vrCam.clearFlags = CameraClearFlags.SolidColor;
                vrCam.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
                if (ceilingLayer >= 0) vrCam.cullingMask = ~(1 << ceilingLayer);

                DirtyCanvas(vrCanvas);
                Render(vrCam, null);                                 // warm-up
                Render(vrCam, "Docs/images/appkey-vr.png");
                Object.DestroyImmediate(vrCamGO);
                desktopCanvas.gameObject.SetActive(true);

                // --- Desktop: panel over the top-down designer view.
                vrCanvas.gameObject.SetActive(false);
                var camGO = new GameObject("__GateShotCam");
                var cam = camGO.AddComponent<Camera>();
                camGO.transform.position = new Vector3(0f, 9f, 0f);
                camGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                cam.orthographic = true;
                cam.orthographicSize = 3.4f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
                if (ceilingLayer >= 0) cam.cullingMask = ~(1 << ceilingLayer);

                var previousMode = desktopCanvas.renderMode;
                desktopCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                desktopCanvas.worldCamera = cam;
                desktopCanvas.planeDistance = 1f;

                DirtyCanvas(desktopCanvas); // batch renders need explicit UI geometry rebuilds
                Render(cam, null);                                   // warm-up
                Render(cam, "Docs/images/appkey-desktop.png");
                desktopCanvas.renderMode = previousMode;
                desktopCanvas.worldCamera = null;
                Object.DestroyImmediate(camGO);

                // restore both panels active (scene already saved in that state).
                vrCanvas.gameObject.SetActive(true);

                Debug.Log("AppKeyGateShots: done");
                // Scene intentionally NOT saved: captures must not alter it.
            } catch (System.Exception e) {
                Debug.LogError("AppKeyGateShots FAILED: " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// A canvas loaded fresh from disk has no rebuild queued, so
        /// ForceUpdateCanvases alone produces no geometry in batch mode;
        /// dirty every Graphic first, then force the rebuild.
        /// </summary>
        private static void DirtyCanvas(Canvas canvas) {
            foreach (var graphic in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                graphic.SetAllDirty();
            Canvas.ForceUpdateCanvases();
        }

        private static void Render(Camera cam, string path) {
            var rt = new RenderTexture(kW, kH, 24, RenderTextureFormat.ARGB32);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
                RenderPipeline.SubmitRenderRequest(cam, request);
            if (path != null) {
                var tex = new Texture2D(kW, kH, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, kW, kH), 0, 0);
                tex.Apply(false);
                RenderTexture.active = null;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("AppKeyGateShots: wrote " + path);
            }
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
