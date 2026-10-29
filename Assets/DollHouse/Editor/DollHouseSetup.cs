using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering.Universal;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using Normal;
using Normal.Realtime;

namespace DollHouse.EditorSetup {
    /// <summary>
    /// Headless project bootstrap, run in two batchmode passes:
    ///   Pass 1: -executeMethod DollHouse.EditorSetup.DollHouseSetup.ConfigureProject
    ///           (input system, layers, URP, player settings, XR plug-in management)
    ///   Pass 2: -executeMethod DollHouse.EditorSetup.DollHouseSetup.BuildContent
    ///           (material conversion, prefabs, scene)
    /// </summary>
    public static class DollHouseSetup {
        private const string kSettingsDir   = "Assets/DollHouse/Settings";
        private const string kModelsDir     = "Assets/DollHouse/Models";
        private const string kMaterialsDir  = "Assets/DollHouse/Materials";
        private const string kResourcesDir  = "Assets/Resources";
        private const string kScenePath     = "Assets/DollHouse/Scenes/DollHouse.unity";
        private const string kFurnitureLayerName = "Furniture";
        private const string kCeilingLayerName   = "CeilingOnly";

        // ================================================================= PASS 1

        public static void ConfigureProject() {
            try {
                EnsureFolders();
                SetActiveInputHandler();
                EnsureLayers();
                SetupURP();
                SetupPlayerSettings();
                SetupXR();
                AssetDatabase.SaveAssets();
                Debug.Log("DollHouseSetup: ConfigureProject completed OK");
            } catch (Exception e) {
                Debug.LogError("DollHouseSetup: ConfigureProject FAILED: " + e);
                EditorApplication.Exit(1);
            }
        }

        private static void EnsureFolders() {
            foreach (string dir in new[] {
                         "Assets/DollHouse", kSettingsDir, kModelsDir, kMaterialsDir,
                         "Assets/DollHouse/Scenes", kResourcesDir, "Assets/XR" }) {
                if (!AssetDatabase.IsValidFolder(dir)) {
                    string parent = dir.Substring(0, dir.LastIndexOf('/'));
                    string leaf   = dir.Substring(dir.LastIndexOf('/') + 1);
                    AssetDatabase.CreateFolder(parent, leaf);
                }
            }
        }

        private static void SetActiveInputHandler() {
            // 0 = old Input Manager, 1 = Input System package, 2 = both
            var playerSettings = new SerializedObject(
                Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            SerializedProperty prop = playerSettings.FindProperty("activeInputHandler");
            if (prop != null && prop.intValue != 1) {
                prop.intValue = 1;
                playerSettings.ApplyModifiedProperties();
                Debug.Log("DollHouseSetup: activeInputHandler set to Input System (restart applies it)");
            }
        }

        private static void EnsureLayers() {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            AddLayer(layers, kFurnitureLayerName);
            AddLayer(layers, kCeilingLayerName);
            tagManager.ApplyModifiedProperties();
        }

        private static void AddLayer(SerializedProperty layers, string name) {
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name)
                    return;
            for (int i = 8; i < layers.arraySize; i++) {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue)) {
                    slot.stringValue = name;
                    return;
                }
            }
            throw new Exception("No free layer slot for " + name);
        }

        private static void SetupURP() {
            string rendererPath = kSettingsDir + "/DollHouse_URP_Renderer.asset";
            string pipelinePath = kSettingsDir + "/DollHouse_URP_Pipeline.asset";

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (rendererData == null) {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, rendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null) {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }

            pipeline.msaaSampleCount = 4;   // Quest-friendly
            pipeline.supportsHDR    = false;
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int currentLevel = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++) {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(currentLevel, false);
            Debug.Log("DollHouseSetup: URP assigned");
        }

        private static void SetupPlayerSettings() {
            PlayerSettings.companyName = "ProtoOwl";
            PlayerSettings.productName = "DollHouseVR";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.protoowl.dollhousevr");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)29;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                        UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            Debug.Log("DollHouseSetup: player settings applied");
        }

        private static void SetupXR() {
            // Android (Quest): OpenXR loader, auto-init.
            ConfigureXRForTarget(BuildTargetGroup.Android, initOnStart: true);
            // Standalone: loader available for Link testing, but never auto-init
            // (the bird's-eye build must not try to start VR).
            ConfigureXRForTarget(BuildTargetGroup.Standalone, initOnStart: false);

            EnableOpenXRFeatures(BuildTargetGroup.Android, new[] {
                "MetaQuestFeature",
                "OculusTouchControllerProfile",
                "MetaQuestTouchProControllerProfile",
                "MetaQuestTouchPlusControllerProfile",
            });
            EnableOpenXRFeatures(BuildTargetGroup.Standalone, new[] {
                "OculusTouchControllerProfile",
                "MetaQuestTouchProControllerProfile",
                "MetaQuestTouchPlusControllerProfile",
            });
            Debug.Log("DollHouseSetup: XR configured");
        }

        private static void ConfigureXRForTarget(BuildTargetGroup group, bool initOnStart) {
            XRGeneralSettingsPerBuildTarget perBuildTarget;
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out perBuildTarget);
            if (perBuildTarget == null) {
                perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perBuildTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuildTarget, true);
            }

            XRGeneralSettings settings = perBuildTarget.SettingsForBuildTarget(group);
            if (settings == null) {
                settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                settings.name = group + " Settings";
                perBuildTarget.SetSettingsForBuildTarget(group, settings);
                AssetDatabase.AddObjectToAsset(settings, perBuildTarget);

                var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = group + " Providers";
                AssetDatabase.AddObjectToAsset(manager, perBuildTarget);
                settings.Manager = manager;
            }

            settings.InitManagerOnStart = initOnStart;

            bool assigned = XRPackageMetadataStore.AssignLoader(
                settings.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
            Debug.Log($"DollHouseSetup: OpenXR loader for {group}: {(assigned ? "assigned" : "FAILED")}");
            EditorUtility.SetDirty(perBuildTarget);
        }

        private static void EnableOpenXRFeatures(BuildTargetGroup group, string[] featureTypeNames) {
            FeatureHelpers.RefreshFeatures(group);
            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null) {
                Debug.LogError("DollHouseSetup: no OpenXRSettings for " + group);
                return;
            }
            foreach (var feature in settings.GetFeatures()) {
                if (featureTypeNames.Contains(feature.GetType().Name)) {
                    feature.enabled = true;
                    EditorUtility.SetDirty(feature);
                    Debug.Log($"DollHouseSetup: enabled OpenXR feature {feature.GetType().Name} ({group})");
                }
            }
            EditorUtility.SetDirty(settings);
        }

        // ================================================================= PASS 2

        public static void BuildContent() {
            try {
                EnsureFolders();
                ConvertLegacyMaterials();
                Material[] mats = CreateMaterials();
                GameObject avatarPrefab = BuildAvatarPrefab(mats);
                GameObject fingerPrefab = BuildFingerPrefab(mats);
                BuildScene(avatarPrefab, fingerPrefab, mats);
                AssetDatabase.SaveAssets();
                Debug.Log("DollHouseSetup: BuildContent completed OK");
            } catch (Exception e) {
                Debug.LogError("DollHouseSetup: BuildContent FAILED: " + e);
                EditorApplication.Exit(1);
            }
        }

        private static void ConvertLegacyMaterials() {
            // URP's Converters.RunInBatchMode throws (Base2DMaterialUpgrader has no
            // default ctor in 17.3), so convert the simple legacy materials directly.
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) throw new Exception("URP Lit shader not found");

            int converted = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Legacy" })) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat")) continue; // skip materials embedded in models
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                if (mat.shader.name.StartsWith("Universal Render Pipeline")) continue;

                Texture baseMap = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Vector2 scale   = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
                Vector2 offset  = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
                Color   color   = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                Texture bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;

                mat.shader = urpLit;
                mat.SetTexture("_BaseMap", baseMap);
                mat.SetTextureScale("_BaseMap", scale);
                mat.SetTextureOffset("_BaseMap", offset);
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", 0.15f);
                if (bumpMap != null) {
                    mat.SetTexture("_BumpMap", bumpMap);
                    mat.EnableKeyword("_NORMALMAP");
                }
                EditorUtility.SetDirty(mat);
                converted++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"DollHouseSetup: converted {converted} legacy materials to URP/Lit");
        }

        // --------------------------------------------------------------- materials

        private struct Mats {
            public const int Floor = 0, Wall = 1, Ceiling = 2, Cone = 3, Skin = 4, Head = 5, Visor = 6;
        }

        private static Material[] CreateMaterials() {
            var mats = new Material[7];

            mats[Mats.Floor] = CreateLitMaterial("Floor", new Color(0.85f, 0.85f, 0.85f));
            var tiles = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Legacy/Textures/Tiles_01/Tiles_01.png");
            if (tiles != null) {
                mats[Mats.Floor].SetTexture("_BaseMap", tiles);
                mats[Mats.Floor].SetTextureScale("_BaseMap", new Vector2(4f, 3f));
            }

            mats[Mats.Wall]    = CreateLitMaterial("Wall",  new Color(0.93f, 0.91f, 0.86f));
            mats[Mats.Skin]    = CreateLitMaterial("Skin",  new Color(0.94f, 0.76f, 0.62f));
            mats[Mats.Head]    = CreateLitMaterial("Head",  new Color(0.95f, 0.85f, 0.4f));
            mats[Mats.Visor]   = CreateLitMaterial("Visor", new Color(0.1f, 0.1f, 0.12f));

            // Ceiling: unlit so the webcam video reads like a screen.
            var ceiling = new Material(Shader.Find("Universal Render Pipeline/Unlit")) {
                color = new Color(0.25f, 0.25f, 0.28f)
            };
            mats[Mats.Ceiling] = SaveMaterial(ceiling, "Ceiling");

            // Vision cone: transparent unlit yellow.
            var cone = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            cone.SetFloat("_Surface", 1f); // transparent
            cone.SetOverrideTag("RenderType", "Transparent");
            cone.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            cone.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            cone.SetFloat("_ZWrite", 0f);
            cone.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            cone.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            cone.SetColor("_BaseColor", new Color(1f, 0.92f, 0.2f, 0.25f));
            mats[Mats.Cone] = SaveMaterial(cone, "VisionCone");

            return mats;
        }

        private static Material CreateLitMaterial(string name, Color color) {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            return SaveMaterial(mat, name);
        }

        private static Material SaveMaterial(Material mat, string name) {
            string path = kMaterialsDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) {
                existing.CopyPropertiesFromMaterial(mat);
                existing.shader = mat.shader;
                existing.renderQueue = mat.renderQueue;
                UnityEngine.Object.DestroyImmediate(mat);
                return existing;
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // --------------------------------------------------------------- prefabs

        private static GameObject BuildAvatarPrefab(Material[] mats) {
            var root = new GameObject("VRAvatar");
            try {
                var head = new GameObject("Head");
                head.transform.SetParent(root.transform, false);

                var headMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                headMesh.name = "HeadMesh";
                headMesh.transform.SetParent(head.transform, false);
                headMesh.transform.localScale = new Vector3(0.22f, 0.26f, 0.24f);
                headMesh.GetComponent<MeshRenderer>().sharedMaterial = mats[Mats.Head];
                UnityEngine.Object.DestroyImmediate(headMesh.GetComponent<Collider>());

                var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visor.name = "Visor";
                visor.transform.SetParent(head.transform, false);
                visor.transform.localPosition = new Vector3(0f, 0.03f, 0.12f);
                visor.transform.localScale = new Vector3(0.18f, 0.07f, 0.03f);
                visor.GetComponent<MeshRenderer>().sharedMaterial = mats[Mats.Visor];
                UnityEngine.Object.DestroyImmediate(visor.GetComponent<Collider>());

                var cone = new GameObject("VisionCone");
                cone.transform.SetParent(head.transform, false);
                var coneFilter = cone.AddComponent<MeshFilter>();
                coneFilter.sharedMesh = GetOrCreateVisionConeMesh();
                var coneRenderer = cone.AddComponent<MeshRenderer>();
                coneRenderer.sharedMaterial = mats[Mats.Cone];
                coneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var leftHand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                leftHand.name = "LeftHand";
                leftHand.transform.SetParent(root.transform, false);
                leftHand.transform.localScale = Vector3.one * 0.12f;
                leftHand.GetComponent<MeshRenderer>().sharedMaterial = mats[Mats.Skin];
                UnityEngine.Object.DestroyImmediate(leftHand.GetComponent<Collider>());

                var rightHand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rightHand.name = "RightHand";
                rightHand.transform.SetParent(root.transform, false);
                rightHand.transform.localScale = Vector3.one * 0.12f;
                rightHand.GetComponent<MeshRenderer>().sharedMaterial = mats[Mats.Skin];
                UnityEngine.Object.DestroyImmediate(rightHand.GetComponent<Collider>());

                root.AddComponent<RealtimeView>();
                var avatar = root.AddComponent<RealtimeAvatar>();
                var so = new SerializedObject(avatar);
                so.FindProperty("_head").objectReferenceValue      = head.transform;
                so.FindProperty("_leftHand").objectReferenceValue  = leftHand.transform;
                so.FindProperty("_rightHand").objectReferenceValue = rightHand.transform;
                so.ApplyModifiedPropertiesWithoutUndo();

                var visuals = root.AddComponent<AvatarVisuals>();
                var vso = new SerializedObject(visuals);
                SerializedProperty hideList = vso.FindProperty("_hideForLocalPlayer");
                hideList.arraySize = 4;
                hideList.GetArrayElementAtIndex(0).objectReferenceValue = headMesh;
                hideList.GetArrayElementAtIndex(1).objectReferenceValue = visor;
                hideList.GetArrayElementAtIndex(2).objectReferenceValue = cone;
                hideList.GetArrayElementAtIndex(3).objectReferenceValue = leftHand;
                vso.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, kResourcesDir + "/VRAvatar.prefab");
            } finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Mesh GetOrCreateVisionConeMesh() {
            string path = kModelsDir + "/VisionCone.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            // Four-sided pyramid: apex at the head, opening forward (+Z).
            float length = 2.0f, halfW = 0.9f, halfH = 0.65f;
            var mesh = new Mesh { name = "VisionCone" };
            var v = new Vector3[] {
                Vector3.zero,
                new Vector3(-halfW,  halfH, length),
                new Vector3( halfW,  halfH, length),
                new Vector3( halfW, -halfH, length),
                new Vector3(-halfW, -halfH, length),
            };
            mesh.vertices = v;
            mesh.triangles = new[] {
                0, 1, 2,   0, 2, 3,   0, 3, 4,   0, 4, 1, // sides
                1, 4, 3,   1, 3, 2,                       // base
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static GameObject BuildFingerPrefab(Material[] mats) {
            var root = new GameObject("GiantFinger");
            try {
                GameObject model = null;
                var pointerAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Legacy/pointer.fbx");
                if (pointerAsset != null) {
                    model = (GameObject)PrefabUtility.InstantiatePrefab(pointerAsset);
                    model.name = "Model";
                    model.transform.SetParent(root.transform, false);

                    // Normalize: longest mesh axis becomes vertical, pointing down,
                    // scaled to ~1.8 m, resting just above the touch point (the root).
                    Bounds b = CalculateLocalBounds(model);
                    Vector3 size = b.size;
                    float longest = Mathf.Max(size.x, size.y, size.z);
                    if (longest > 0.0001f) {
                        if (longest == size.x)      model.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
                        else if (longest == size.z) model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                        else                        model.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                        float scale = 1.8f / longest;
                        model.transform.localScale = Vector3.one * scale;
                    }
                    Bounds world = CalculateWorldBounds(model);
                    model.transform.localPosition = new Vector3(
                        -world.center.x, -world.min.y + 0.02f, -world.center.z);

                    foreach (var r in model.GetComponentsInChildren<Renderer>())
                        if (r.sharedMaterial == null || r.sharedMaterial.shader == null ||
                            r.sharedMaterial.shader.name.Contains("Error") ||
                            r.sharedMaterial.shader.name.Contains("InternalError"))
                            r.sharedMaterial = mats[Mats.Skin];
                } else {
                    Debug.LogWarning("DollHouseSetup: pointer.fbx not found; using capsule finger");
                    model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    model.name = "Model";
                    model.transform.SetParent(root.transform, false);
                    model.transform.localScale = new Vector3(0.3f, 0.9f, 0.3f);
                    model.transform.localPosition = new Vector3(0f, 0.92f, 0f);
                    model.GetComponent<MeshRenderer>().sharedMaterial = mats[Mats.Skin];
                    UnityEngine.Object.DestroyImmediate(model.GetComponent<Collider>());
                }

                root.AddComponent<RealtimeView>();
                root.AddComponent<RealtimeTransform>();
                var finger = root.AddComponent<GiantFinger>();
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                var fso = new SerializedObject(finger);
                SerializedProperty list = fso.FindProperty("_renderers");
                list.arraySize = renderers.Length;
                for (int i = 0; i < renderers.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
                fso.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, kResourcesDir + "/GiantFinger.prefab");
            } finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Bounds CalculateLocalBounds(GameObject go) {
            var filters = go.GetComponentsInChildren<MeshFilter>();
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var f in filters) {
                if (f.sharedMesh == null) continue;
                if (first) { b = f.sharedMesh.bounds; first = false; }
                else b.Encapsulate(f.sharedMesh.bounds);
            }
            return b;
        }

        private static Bounds CalculateWorldBounds(GameObject go) {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var b = new Bounds(go.transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in renderers) {
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // --------------------------------------------------------------- scene

        private static void BuildScene(GameObject avatarPrefab, GameObject fingerPrefab, Material[] mats) {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            int furnitureLayer = LayerMask.NameToLayer(kFurnitureLayerName);
            int ceilingLayer   = LayerMask.NameToLayer(kCeilingLayerName);
            if (furnitureLayer < 0 || ceilingLayer < 0)
                throw new Exception("Layers missing; run ConfigureProject first.");

            // --- lighting
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.shadows = LightShadows.Soft;
            lightGO.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            RenderSettings.ambientMode  = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

            // --- room (8 m x 6 m, 2.7 m high)
            var room = new GameObject("Room");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(room.transform, false);
            floor.transform.localScale = new Vector3(0.8f, 1f, 0.6f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mats[Mats.Floor];

            CreateWall(room.transform, mats[Mats.Wall], new Vector3(0f, 1.35f,  3.05f), new Vector3(8.2f, 2.7f, 0.1f), "Wall N");
            CreateWall(room.transform, mats[Mats.Wall], new Vector3(0f, 1.35f, -3.05f), new Vector3(8.2f, 2.7f, 0.1f), "Wall S");
            CreateWall(room.transform, mats[Mats.Wall], new Vector3( 4.05f, 1.35f, 0f), new Vector3(0.1f, 2.7f, 6.2f), "Wall E");
            CreateWall(room.transform, mats[Mats.Wall], new Vector3(-4.05f, 1.35f, 0f), new Vector3(0.1f, 2.7f, 6.2f), "Wall W");

            var ceiling = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ceiling.name = "Ceiling";
            ceiling.transform.SetParent(room.transform, false);
            ceiling.transform.localPosition = new Vector3(0f, 2.7f, 0f);
            ceiling.transform.localRotation = Quaternion.Euler(180f, 0f, 0f); // faces down
            ceiling.transform.localScale = new Vector3(0.8f, 1f, 0.6f);
            ceiling.layer = ceilingLayer;
            var ceilingRenderer = ceiling.GetComponent<MeshRenderer>();
            ceilingRenderer.sharedMaterial = mats[Mats.Ceiling];
            ceilingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // --- furniture (reused 2016 assets)
            var furnitureParent = new GameObject("Furniture");
            PlaceFurniture(furnitureParent.transform, furnitureLayer, "Assets/Legacy/Furniture/Assets_Beds/prefabs/bed1.prefab",              new Vector3(-2.6f, 0f,  1.6f),   0f);
            PlaceFurniture(furnitureParent.transform, furnitureLayer, "Assets/Legacy/Furniture/Assets_Beds/prefabs/bed2.prefab",              new Vector3(-2.6f, 0f, -1.6f),   0f);
            PlaceFurniture(furnitureParent.transform, furnitureLayer, "Assets/Legacy/Furniture/Shelf/Shelf.prefab",                           new Vector3( 3.5f, 0f,  1.8f), -90f);
            PlaceFurniture(furnitureParent.transform, furnitureLayer, "Assets/Legacy/Furniture/FreeFurnitureSet/Models/table.FBX",            new Vector3( 0.8f, 0f,  0.6f),   0f);
            PlaceFurniture(furnitureParent.transform, furnitureLayer, "Assets/Legacy/Furniture/FreeFurnitureSet/Models/chair.FBX",            new Vector3( 0.8f, 0f,  1.5f), 180f);
            PlaceFurniture(furnitureParent.transform, furnitureLayer, "Assets/Legacy/Furniture/FreeFurnitureSet/Models/chair.FBX",            new Vector3( 1.7f, 0f,  0.6f), -90f);
            PlaceFurniture(furnitureParent.transform, furnitureLayer, "Assets/Legacy/Furniture/FreeFurnitureSet/Models/cabinet.FBX",          new Vector3( 3.5f, 0f, -1.8f), -90f);
            PlaceFurniture(furnitureParent.transform, furnitureLayer, "Assets/Legacy/Furniture/FreeFurnitureSet/Models/desk.FBX",             new Vector3(-0.5f, 0f, -2.3f), 180f);
            PlaceFurniture(furnitureParent.transform, furnitureLayer, "Assets/Legacy/Furniture/TableMetal/Table_Metal.FBX",                   new Vector3( 2.6f, 0f, -2.3f),   0f);
            PlaceFurniture(furnitureParent.transform, furnitureLayer, "Assets/Legacy/Furniture/Wooden_table_and_chair/FBX/table_and_chair.FBX", new Vector3(-0.9f, 0f, 2.3f), 90f);

            // --- XR rig (User A)
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/Prefabs/XR Origin (XR Rig).prefab");
            if (rigPrefab == null) throw new Exception("XR Origin (XR Rig).prefab not found");
            var xrRig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            xrRig.transform.position = new Vector3(0f, 0f, -1.8f);

            Transform headT  = FindDeep(xrRig.transform, "Main Camera");
            Transform leftT  = FindDeep(xrRig.transform, "Left Controller");
            Transform rightT = FindDeep(xrRig.transform, "Right Controller");
            if (headT == null || leftT == null || rightT == null)
                throw new Exception("XR rig transforms not found (head/left/right)");

            // --- Bird's-eye rig (User B)
            var bevRig = new GameObject("BirdsEyeRig");
            var bevCamGO = new GameObject("BEV Camera");
            bevCamGO.transform.SetParent(bevRig.transform, false);
            bevCamGO.transform.position = new Vector3(0f, 9f, 0f);
            bevCamGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var bevCam = bevCamGO.AddComponent<Camera>();
            bevCam.orthographic = true;
            bevCam.orthographicSize = 3.4f;
            bevCam.clearFlags = CameraClearFlags.SolidColor;
            bevCam.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
            bevCam.cullingMask = ~(1 << ceilingLayer);
            bevCamGO.AddComponent<AudioListener>();

            // --- Normcore
            var normcoreGO = new GameObject("Normcore");
            var realtime = normcoreGO.AddComponent<Realtime>();
            AssignAppSettings(realtime);

            // Normcore 3 enables "join room on start" ('Test Room') by default,
            // which races RoleManager's own Connect() and kills it. Disable it.
            var rtso = new SerializedObject(realtime);
            SerializedProperty joinOnStart = rtso.FindProperty("_joinRoomOnStartOptions._enabled");
            if (joinOnStart != null) {
                joinOnStart.boolValue = false;
                rtso.ApplyModifiedPropertiesWithoutUndo();
            }
            var avatarManager = normcoreGO.AddComponent<RealtimeAvatarManager>();

            var amso = new SerializedObject(avatarManager);
            amso.FindProperty("_localAvatarPrefab").objectReferenceValue = avatarPrefab;
            SerializedProperty lp = amso.FindProperty("_localPlayer");
            lp.FindPropertyRelative("root").objectReferenceValue      = xrRig.transform;
            lp.FindPropertyRelative("head").objectReferenceValue      = headT;
            lp.FindPropertyRelative("leftHand").objectReferenceValue  = leftT;
            lp.FindPropertyRelative("rightHand").objectReferenceValue = rightT;
            amso.ApplyModifiedPropertiesWithoutUndo();

            // --- ceiling webcam stream (scene RealtimeView)
            var streamGO = new GameObject("CeilingStream");
            streamGO.AddComponent<RealtimeView>();
            var stream = streamGO.AddComponent<WebcamCeilingStream>();
            var sso = new SerializedObject(stream);
            sso.FindProperty("_ceilingRenderer").objectReferenceValue = ceilingRenderer;
            sso.ApplyModifiedPropertiesWithoutUndo();

            // --- bird's-eye controller
            var bevController = bevRig.AddComponent<BirdsEyeController>();
            var bso = new SerializedObject(bevController);
            bso.FindProperty("_realtime").objectReferenceValue = realtime;
            bso.FindProperty("_camera").objectReferenceValue = bevCam;
            bso.FindProperty("_furnitureMask").intValue = 1 << furnitureLayer;
            bso.FindProperty("_fingerSurfaceMask").intValue = ~(1 << ceilingLayer) & ~(1 << 2); // everything except ceiling + ignore-raycast
            bso.ApplyModifiedPropertiesWithoutUndo();

            // --- role manager
            var roleManager = normcoreGO.AddComponent<RoleManager>();
            var rso = new SerializedObject(roleManager);
            rso.FindProperty("_realtime").objectReferenceValue = realtime;
            rso.FindProperty("_avatarManager").objectReferenceValue = avatarManager;
            rso.FindProperty("_vrAvatarPrefab").objectReferenceValue = avatarPrefab;
            rso.FindProperty("_xrRig").objectReferenceValue = xrRig;
            rso.FindProperty("_birdsEyeRig").objectReferenceValue = bevRig;
            rso.FindProperty("_ceilingStream").objectReferenceValue = stream;
            rso.ApplyModifiedPropertiesWithoutUndo();

            // --- scene-view UUIDs for every scene RealtimeView
            foreach (var view in UnityEngine.Object.FindObjectsByType<RealtimeView>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                EnsureSceneViewUUID(view);

            EditorSceneManager.SaveScene(scene, kScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(kScenePath, true) };
            Debug.Log("DollHouseSetup: scene saved to " + kScenePath);
        }

        private static void CreateWall(Transform parent, Material mat, Vector3 pos, Vector3 scale, string name) {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = pos;
            wall.transform.localScale = scale;
            wall.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void PlaceFurniture(Transform parent, int layer, string assetPath, Vector3 position, float yRotation) {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null) {
                Debug.LogWarning("DollHouseSetup: furniture asset missing, skipped: " + assetPath);
                return;
            }

            var root = new GameObject("Furniture_" + asset.name);
            root.transform.SetParent(parent, false);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            model.transform.SetParent(root.transform, false);

            // Normalize wildly different asset scales to believable furniture sizes.
            Bounds b = CalculateWorldBounds(model);
            float maxXZ = Mathf.Max(b.size.x, b.size.z);
            float height = b.size.y;
            if (maxXZ > 0.001f && height > 0.001f) {
                float scale = Mathf.Min(1f * 2.2f / maxXZ, 1.8f / height);
                scale = Mathf.Min(scale, 1f); // never scale up past authored size...
                if (Mathf.Max(maxXZ, height) < 0.4f)
                    scale = 1.0f / Mathf.Max(maxXZ, height); // ...unless it's tiny
                model.transform.localScale = Vector3.one * scale;
            }

            // Sit on the floor, centered on the root.
            b = CalculateWorldBounds(model);
            model.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);

            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yRotation, 0f);

            // Collider for touch raycasts.
            b = CalculateWorldBounds(model);
            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(b.center);
            box.size = Quaternion.Inverse(root.transform.rotation) * b.size;
            box.size = new Vector3(Mathf.Abs(box.size.x), Mathf.Abs(box.size.y), Mathf.Abs(box.size.z));

            SetLayerRecursive(root, layer);
            root.AddComponent<RealtimeView>();
            root.AddComponent<RealtimeTransform>();
        }

        private static void SetLayerRecursive(GameObject go, int layer) {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private static Transform FindDeep(Transform root, string name) {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform result = FindDeep(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private static void AssignAppSettings(Realtime realtime) {
            string path = kSettingsDir + "/NormcoreAppSettings.asset";
            var appSettings = AssetDatabase.LoadAssetAtPath<NormcoreAppSettings>(path);
            if (appSettings == null) {
                appSettings = ScriptableObject.CreateInstance<NormcoreAppSettings>();
                var aso = new SerializedObject(appSettings);
                SerializedProperty p = aso.GetIterator();
                bool keySet = false;
                while (p.NextVisible(true)) {
                    if (p.propertyType == SerializedPropertyType.String &&
                        p.name.ToLowerInvariant().Contains("appkey")) {
                        p.stringValue = "YOUR_NORMCORE_APP_KEY_HERE";
                        keySet = true;
                    }
                }
                aso.ApplyModifiedPropertiesWithoutUndo();
                if (!keySet) Debug.LogWarning("DollHouseSetup: could not find app key property on NormcoreAppSettings");
                AssetDatabase.CreateAsset(appSettings, path);
            }

            var rso = new SerializedObject(realtime);
            SerializedProperty prop = rso.GetIterator();
            bool assigned = false;
            while (prop.NextVisible(true)) {
                if (prop.propertyType == SerializedPropertyType.ObjectReference &&
                    prop.name.ToLowerInvariant().Contains("appsettings")) {
                    prop.objectReferenceValue = appSettings;
                    assigned = true;
                    break;
                }
            }
            rso.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("DollHouseSetup: app settings " + (assigned ? "assigned to Realtime" : "NOT FOUND on Realtime component!"));
        }

        private static void EnsureSceneViewUUID(RealtimeView view) {
            var so = new SerializedObject(view);
            SerializedProperty prop = so.GetIterator();
            while (prop.NextVisible(true)) {
                if (!prop.name.ToLowerInvariant().Contains("sceneviewuuid"))
                    continue;

                if (prop.propertyType == SerializedPropertyType.String) {
                    if (string.IsNullOrEmpty(prop.stringValue) ||
                        prop.stringValue == Guid.Empty.ToString())
                        prop.stringValue = Guid.NewGuid().ToString();
                    so.ApplyModifiedPropertiesWithoutUndo();
                    return;
                }
                if (prop.isArray && prop.arrayElementType == "byte") {
                    byte[] bytes = Guid.NewGuid().ToByteArray();
                    bool allZero = true;
                    for (int i = 0; i < prop.arraySize; i++)
                        if (prop.GetArrayElementAtIndex(i).intValue != 0) { allZero = false; break; }
                    if (prop.arraySize != 16 || allZero) {
                        prop.arraySize = 16;
                        for (int i = 0; i < 16; i++)
                            prop.GetArrayElementAtIndex(i).intValue = bytes[i];
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                    return;
                }
                Debug.LogWarning($"DollHouseSetup: unhandled sceneViewUUID property type {prop.propertyType} on {view.name}");
            }
            Debug.LogWarning("DollHouseSetup: no sceneViewUUID property found on RealtimeView of " + view.name);
        }
    }
}
