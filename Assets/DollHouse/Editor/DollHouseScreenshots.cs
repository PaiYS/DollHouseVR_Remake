using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DollHouse.EditorSetup {
    /// <summary>
    /// Headless README screenshot generator:
    ///   -batchmode -quit -executeMethod DollHouse.EditorSetup.DollHouseScreenshots.Run
    /// Renders 2x2 grids (isometric / front / top / right) of the room, the avatar
    /// and the giant hand into Docs/images. Requires the dollhouse walls (inner-face
    /// quads) so the side views can see into the room.
    /// </summary>
    public static class DollHouseScreenshots {
        private const int kCell = 960; // each grid cell is kCell x kCell/16*9? keep square cells
        private const int kCellW = 960, kCellH = 540;

        public static void Run() {
            try {
                EditorSceneManager.OpenScene("Assets/DollHouse/Scenes/DollHouse.unity", OpenSceneMode.Single);
                ConvertWallsToInnerFaces();
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

                Directory.CreateDirectory("Docs/images");

                CaptureGrid("Docs/images/room-views.png");

                // Avatar: temporary instance, head posed at standing height.
                var avatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/VRAvatar.prefab");
                var avatar = (GameObject)PrefabUtility.InstantiatePrefab(avatarPrefab);
                avatar.transform.position = new Vector3(0f, 0f, -0.8f);
                Transform head = avatar.transform.Find("Head");
                if (head != null) {
                    head.position = new Vector3(0f, 1.6f, -0.8f);
                    head.rotation = Quaternion.Euler(0f, 25f, 0f);
                }
                CaptureGrid("Docs/images/avatar-vision-cone.png");
                Object.DestroyImmediate(avatar);

                // Giant hand: temporary instance with renderers forced on.
                var fingerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/GiantFinger.prefab");
                var finger = (GameObject)PrefabUtility.InstantiatePrefab(fingerPrefab);
                finger.transform.position = new Vector3(0.9f, 0.75f, 0.6f); // pointing at the table
                foreach (var r in finger.GetComponentsInChildren<Renderer>()) r.enabled = true;
                CaptureGrid("Docs/images/giant-hand.png");
                Object.DestroyImmediate(finger);

                Debug.Log("DollHouseScreenshots: done");
            } catch (System.Exception e) {
                Debug.LogError("DollHouseScreenshots FAILED: " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Dollhouse walls: hide the cube slabs, add inward-facing quads.</summary>
        public static void ConvertWallsToInnerFaces() {
            var wallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/DollHouse/Materials/Wall.mat");
            var walls = new (string name, Vector3 outward, Vector2 size)[] {
                ("Wall N", Vector3.forward, new Vector2(8.2f, 2.7f)),
                ("Wall S", Vector3.back,    new Vector2(8.2f, 2.7f)),
                ("Wall E", Vector3.right,   new Vector2(6.2f, 2.7f)),
                ("Wall W", Vector3.left,    new Vector2(6.2f, 2.7f)),
            };
            foreach (var w in walls) {
                var go = GameObject.Find(w.name);
                if (go == null) { Debug.LogWarning("wall missing: " + w.name); continue; }
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false; // collider stays for pointing/raycasts

                var old = go.transform.Find("InnerFace");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "InnerFace";
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(go.transform, true);
                // Unity quad is visible from its -Z side; LookRotation(outward) points
                // +Z outward, so the visible face looks into the room.
                quad.transform.rotation = Quaternion.LookRotation(w.outward);
                quad.transform.position = go.transform.position - w.outward * 0.051f;
                Vector3 lossy = quad.transform.lossyScale;
                quad.transform.localScale = new Vector3(
                    quad.transform.localScale.x * w.size.x / lossy.x,
                    quad.transform.localScale.y * w.size.y / lossy.y,
                    quad.transform.localScale.z);
                var qmr = quad.GetComponent<MeshRenderer>();
                qmr.sharedMaterial = wallMat;
                // Dollhouse walls must not block the light pouring into the room.
                qmr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private static void CaptureGrid(string path) {
            Vector3 center = new Vector3(0f, 1.1f, 0f);

            // First URP render after a scene/content change can miss shader
            // variants (black or magenta output); render a throwaway frame so
            // everything currently in the scene is warmed up.
            Object.DestroyImmediate(RenderView(new Vector3(7f, 6.5f, -7f), center, false, 45f, 0f));

            var grid = new Texture2D(kCellW * 2, kCellH * 2, TextureFormat.RGB24, false);

            // iso (top-left), front (top-right), top (bottom-left), right (bottom-right)
            Blit(grid, 0, kCellH, RenderView(new Vector3(7f, 6.5f, -7f), center, false, 45f, 0f));
            Blit(grid, kCellW, kCellH, RenderView(new Vector3(0f, 1.35f, -9f), new Vector3(0f, 1.35f, 0f), true, 0f, 2.1f));
            Blit(grid, 0, 0, RenderView(new Vector3(0f, 11f, 0f), center, true, 0f, 3.4f));
            Blit(grid, kCellW, 0, RenderView(new Vector3(9f, 1.35f, 0f), new Vector3(0f, 1.35f, 0f), true, 0f, 2.1f));

            grid.Apply(false);
            File.WriteAllBytes(path, grid.EncodeToPNG());
            Object.DestroyImmediate(grid);
            Debug.Log("DollHouseScreenshots: wrote " + path);
        }

        private static Texture2D RenderView(Vector3 camPos, Vector3 lookAt, bool ortho, float fov, float orthoSize) {
            var go = new GameObject("__ShotCam");
            try {
                var cam = go.AddComponent<Camera>();
                go.transform.position = camPos;
                go.transform.rotation = Quaternion.LookRotation(lookAt - camPos);
                cam.orthographic = ortho;
                if (ortho) cam.orthographicSize = orthoSize; else cam.fieldOfView = fov;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 60f;
                int ceilingLayer = LayerMask.NameToLayer("CeilingOnly");
                if (ceilingLayer >= 0) cam.cullingMask = ~(1 << ceilingLayer);

                var rt = new RenderTexture(kCellW, kCellH, 24, RenderTextureFormat.ARGB32);
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                    RenderPipeline.SubmitRenderRequest(cam, request);
                else
                    Debug.LogWarning("render request unsupported; image will be empty");

                var tex = new Texture2D(kCellW, kCellH, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, kCellW, kCellH), 0, 0);
                tex.Apply(false);
                RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                return tex;
            } finally {
                Object.DestroyImmediate(go);
            }
        }

        private static void Blit(Texture2D grid, int x, int y, Texture2D cell) {
            grid.SetPixels(x, y, cell.width, cell.height, cell.GetPixels());
            Object.DestroyImmediate(cell);
        }
    }
}
