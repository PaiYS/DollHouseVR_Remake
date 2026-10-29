using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Normal.Realtime;

namespace DollHouse.EditorSetup {
    /// <summary>
    /// Adds the networked sketch tool to the existing DollHouse scene without
    /// rebuilding it (preserves hand-tuned scene state and RealtimeView UUIDs):
    ///   - SketchLine material (URP/Unlit)
    ///   - Resources/SketchStroke.prefab (RealtimeView + SketchStroke + LineRenderer)
    ///   - SketchController on BirdsEyeRig + screen-space Sketch/Clear buttons
    /// Re-runnable: -executeMethod DollHouse.EditorSetup.SketchToolSetup.Run
    /// </summary>
    public static class SketchToolSetup {
        private const string kScenePath    = "Assets/DollHouse/Scenes/DollHouse.unity";
        private const string kMaterialPath = "Assets/DollHouse/Materials/SketchLine.mat";
        private const string kPrefabPath   = "Assets/Resources/SketchStroke.prefab";

        public static void Run() {
            try {
                Material lineMat = CreateLineMaterial();
                CreateStrokePrefab(lineMat);
                UpdateScene();
                AssetDatabase.SaveAssets();
                Debug.Log("SketchToolSetup: completed OK");
            } catch (Exception e) {
                Debug.LogError("SketchToolSetup: FAILED: " + e);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
            }
        }

        private static Material CreateLineMaterial() {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(kMaterialPath);
            if (existing != null)
                return existing;

            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) throw new Exception("URP Unlit shader not found");
            var mat = new Material(unlit);
            mat.SetColor("_BaseColor", new Color(1f, 0.3f, 0.05f)); // annotation orange-red
            AssetDatabase.CreateAsset(mat, kMaterialPath);
            return mat;
        }

        private static void CreateStrokePrefab(Material lineMat) {
            var root = new GameObject("SketchStroke");
            try {
                var line = root.AddComponent<LineRenderer>();
                line.useWorldSpace     = true;
                line.widthMultiplier   = 0.025f;
                line.numCapVertices    = 4;
                line.numCornerVertices = 4;
                line.alignment         = LineAlignment.View;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows    = false;
                line.sharedMaterial    = lineMat;
                line.positionCount     = 0;

                root.AddComponent<RealtimeView>();
                var stroke = root.AddComponent<SketchStroke>();
                var so = new SerializedObject(stroke);
                so.FindProperty("_line").objectReferenceValue = line;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath);
            } finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ----------------------------------------------------------------- scene

        private static void UpdateScene() {
            var scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject bevRig = GameObject.Find("BirdsEyeRig");
            if (bevRig == null) throw new Exception("BirdsEyeRig not found in scene");
            var birdsEye = bevRig.GetComponent<BirdsEyeController>();
            if (birdsEye == null) throw new Exception("BirdsEyeController not found on BirdsEyeRig");
            Camera bevCam = bevRig.GetComponentInChildren<Camera>(true);
            if (bevCam == null) throw new Exception("BEV Camera not found under BirdsEyeRig");
            var realtime = UnityEngine.Object.FindFirstObjectByType<Realtime>(FindObjectsInactive.Include);
            if (realtime == null) throw new Exception("Realtime not found in scene");

            // Rebuild the sketch UI from scratch each run.
            DestroyChild(bevRig.transform, "SketchCanvas");
            DestroyChild(bevRig.transform, "SketchEventSystem");

            // --- canvas + buttons
            var canvasGO = new GameObject("SketchCanvas");
            canvasGO.transform.SetParent(bevRig.transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            (Button sketchButton, Image sketchImage, RectTransform sketchRect) =
                CreateButton(canvasGO.transform, "SketchButton", "Sketch", new Vector2(24f, 24f));
            (Button clearButton, Image _, RectTransform clearRect) =
                CreateButton(canvasGO.transform, "ClearButton", "Clear", new Vector2(204f, 24f));

            // --- event system (lives under the rig: only active for the bird's-eye role)
            var eventSystemGO = new GameObject("SketchEventSystem");
            eventSystemGO.transform.SetParent(bevRig.transform, false);
            eventSystemGO.AddComponent<EventSystem>();
            eventSystemGO.AddComponent<InputSystemUIInputModule>();

            // --- sketch controller
            var sketch = bevRig.GetComponent<SketchController>();
            if (sketch == null) sketch = bevRig.AddComponent<SketchController>();

            var beSO = new SerializedObject(birdsEye);
            int surfaceMask = beSO.FindProperty("_fingerSurfaceMask").intValue;

            var sso = new SerializedObject(sketch);
            sso.FindProperty("_realtime").objectReferenceValue = realtime;
            sso.FindProperty("_camera").objectReferenceValue   = bevCam;
            sso.FindProperty("_surfaceMask").intValue          = surfaceMask;
            sso.FindProperty("_birdsEye").objectReferenceValue = birdsEye;
            sso.FindProperty("_sketchToggleButton").objectReferenceValue     = sketchButton;
            sso.FindProperty("_sketchToggleBackground").objectReferenceValue = sketchImage;
            sso.FindProperty("_clearButton").objectReferenceValue            = clearButton;
            SerializedProperty blockers = sso.FindProperty("_pointerBlockers");
            blockers.arraySize = 2;
            blockers.GetArrayElementAtIndex(0).objectReferenceValue = sketchRect;
            blockers.GetArrayElementAtIndex(1).objectReferenceValue = clearRect;
            sso.ApplyModifiedPropertiesWithoutUndo();

            beSO.FindProperty("_sketchController").objectReferenceValue = sketch;
            beSO.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("SketchToolSetup: scene updated and saved");
        }

        private static void DestroyChild(Transform parent, string name) {
            Transform child = parent.Find(name);
            if (child != null)
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        private static (Button, Image, RectTransform) CreateButton(
                Transform parent, string name, string label, Vector2 anchoredPosition) {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot     = Vector2.zero;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(160f, 72f);

            var image = go.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type   = Image.Type.Sliced;
            image.color  = new Color(1f, 1f, 1f, 0.85f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = labelGO.AddComponent<Text>();
            text.text      = label;
            text.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize  = 30;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color     = new Color(0.12f, 0.12f, 0.14f);

            return (button, image, rect);
        }
    }
}
