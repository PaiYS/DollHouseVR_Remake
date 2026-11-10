using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Normal.Realtime;

namespace DollHouse.EditorSetup {
    /// <summary>
    /// Adds the Normcore app-key gate UI to the DollHouse scene (run once):
    ///   -batchmode -quit -executeMethod DollHouse.EditorSetup.AppKeyGateSetup.Run
    /// Builds a screen-space panel (desktop) and a world-space panel (VR), both
    /// with the key field + Edit + Enter, and wires AppKeyGate on the Normcore GO.
    /// </summary>
    public static class AppKeyGateSetup {
        private static Font _font;

        public static void Run() {
            try {
                EditorSceneManager.OpenScene("Assets/DollHouse/Scenes/DollHouse.unity", OpenSceneMode.Single);
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                // Event system: XRUIInputModule covers controller rays AND mouse/touch.
                var eventSystem = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
                if (eventSystem == null) {
                    var esGO = new GameObject("EventSystem");
                    eventSystem = esGO.AddComponent<EventSystem>();
                }
                var oldModule = eventSystem.GetComponent<BaseInputModule>();
                if (oldModule != null && !(oldModule is XRUIInputModule)) Object.DestroyImmediate(oldModule);
                var xrModule = eventSystem.GetComponent<XRUIInputModule>();
                if (xrModule == null) xrModule = eventSystem.gameObject.AddComponent<XRUIInputModule>();
                xrModule.enableMouseInput = true;
                xrModule.enableTouchInput = true;

                // --- Desktop panel (screen-space overlay)
                var desktopPanel = BuildPanel("AppKeyPanel_Desktop", out Canvas desktopCanvas);
                desktopCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                desktopCanvas.sortingOrder = 100;
                var scaler = desktopCanvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1600, 1000);

                // --- VR panel (world space, in front of the spawn point)
                var vrPanel = BuildPanel("AppKeyPanel_VR", out Canvas vrCanvas);
                vrCanvas.renderMode = RenderMode.WorldSpace;
                var vrRect = vrCanvas.GetComponent<RectTransform>();
                vrRect.sizeDelta = new Vector2(800, 500);
                Vector3 panelPos = new Vector3(0f, 1.5f, -0.9f);
                Vector3 playerHead = new Vector3(0f, 1.6f, -1.8f); // XR rig spawn
                vrRect.position = panelPos;
                vrRect.rotation = Quaternion.LookRotation(panelPos - playerHead);
                vrRect.localScale = Vector3.one * 0.0012f;
                vrCanvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

                // --- gate component on the Normcore object
                var normcoreGO = GameObject.Find("Normcore");
                var realtime = normcoreGO.GetComponent<Realtime>();
                var roleManager = normcoreGO.GetComponent<RoleManager>();
                var oldGate = normcoreGO.GetComponent<AppKeyGate>();
                if (oldGate != null) Object.DestroyImmediate(oldGate);
                var gate = normcoreGO.AddComponent<AppKeyGate>();
                var gso = new SerializedObject(gate);
                gso.FindProperty("_realtime").objectReferenceValue = realtime;
                gso.FindProperty("_roleManager").objectReferenceValue = roleManager;
                gso.FindProperty("_desktopPanel").objectReferenceValue = desktopPanel;
                gso.FindProperty("_vrPanel").objectReferenceValue = vrPanel;
                gso.ApplyModifiedPropertiesWithoutUndo();

                // Root-canvas X/Y live in anchoredPosition and override localPosition
                // on every scene load — pin the pose via anchoredPosition3D, last.
                Canvas.ForceUpdateCanvases();
                vrRect.anchoredPosition3D = panelPos;
                vrRect.rotation = Quaternion.LookRotation(panelPos - playerHead);
                vrRect.localScale = Vector3.one * 0.0012f;

                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                Debug.Log("AppKeyGateSetup: done, scene saved");
            } catch (System.Exception e) {
                Debug.LogError("AppKeyGateSetup FAILED: " + e);
                EditorApplication.Exit(1);
            }
        }

        private static AppKeyPanel BuildPanel(string name, out Canvas canvas) {
            var existing = GameObject.Find(name);
            if (existing != null) Object.DestroyImmediate(existing);

            var canvasGO = new GameObject(name);
            canvas = canvasGO.AddComponent<Canvas>();
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // backdrop
            var backdrop = MakeRect("Backdrop", canvasGO.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var backdropImage = backdrop.gameObject.AddComponent<Image>();
            backdropImage.color = new Color(0.07f, 0.07f, 0.09f, 0.96f);

            // card
            var card = MakeRect("Card", backdrop, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(720f, 320f), Vector2.zero);
            var cardImage = card.gameObject.AddComponent<Image>();
            cardImage.color = new Color(0.15f, 0.15f, 0.19f, 1f);

            MakeText("Title", card, "DollHouseVR — Normcore App Key",
                26, new Vector2(0.5f, 1f), new Vector2(0f, -45f), new Vector2(660f, 50f), TextAnchor.MiddleCenter, Color.white);
            MakeText("Hint", card, "Press Enter to use this key, or Edit to use your own (dashboard.normcore.io).",
                16, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(660f, 40f), TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.75f));

            // input field
            var inputRect = MakeRect("KeyInput", card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(620f, 56f), new Vector2(0f, -5f));
            var inputImage = inputRect.gameObject.AddComponent<Image>();
            inputImage.color = new Color(0.1f, 0.1f, 0.12f, 1f);
            var input = inputRect.gameObject.AddComponent<InputField>();
            var placeholder = MakeText("Placeholder", inputRect, "paste a Normcore app key...",
                20, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(590f, 48f), TextAnchor.MiddleLeft, new Color(0.5f, 0.5f, 0.55f));
            placeholder.fontStyle = FontStyle.Italic;
            var text = MakeText("Text", inputRect, "",
                20, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(590f, 48f), TextAnchor.MiddleLeft, Color.white);
            text.supportRichText = false;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 64;

            // buttons
            var editButton  = MakeButton("EditButton", card, "Edit",  new Vector2(-120f, -110f), new Color(0.35f, 0.35f, 0.4f));
            var enterButton = MakeButton("EnterButton", card, "Enter", new Vector2(120f, -110f), new Color(0.2f, 0.55f, 0.3f));

            var panel = canvasGO.AddComponent<AppKeyPanel>();
            var pso = new SerializedObject(panel);
            pso.FindProperty("_input").objectReferenceValue = input;
            pso.FindProperty("_editButton").objectReferenceValue = editButton;
            pso.FindProperty("_enterButton").objectReferenceValue = enterButton;
            pso.ApplyModifiedPropertiesWithoutUndo();
            return panel;
        }

        private static RectTransform MakeRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                              Vector2 size, Vector2 anchoredPos) {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            if (size != Vector2.zero) rect.sizeDelta = size;
            else { rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
            rect.anchoredPosition = anchoredPos;
            return rect;
        }

        private static Text MakeText(string name, Transform parent, string content, int fontSize,
                                     Vector2 anchor, Vector2 pos, Vector2 size, TextAnchor align, Color color) {
            var rect = MakeRect(name, parent, anchor, anchor, size, pos);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = align;
            text.color = color;
            return text;
        }

        private static Button MakeButton(string name, Transform parent, string label, Vector2 pos, Color color) {
            var rect = MakeRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(180f, 54f), pos);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            var button = rect.gameObject.AddComponent<Button>();
            MakeText("Label", rect, label, 22, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(170f, 48f), TextAnchor.MiddleCenter, Color.white);
            return button;
        }
    }
}
