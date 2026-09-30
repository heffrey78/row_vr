using System.IO;
using System.Linq;
using RowVR.Recording;
using RowVR.Rowing;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RowVR.Editor
{
    /// <summary>
    /// Builds the rowing scene from code, once.
    ///
    /// Scenes are normally authored by hand, and after this runs that is exactly what
    /// happens - the .unity file is committed and edited in the Editor from then on. This
    /// exists for the initial construction because the wiring it does is the part that
    /// fails silently: <see cref="XRHandsPoseSource"/> receives joint poses in tracking
    /// space, so a RowingRig whose trackingOrigin is unset reports a handle frozen at the
    /// world origin, with no error anywhere. Doing it in code makes that wiring reviewable
    /// in a diff instead of a checkbox someone has to remember.
    ///
    /// The XR rig itself is created by invoking Unity's own menu item rather than assembled
    /// by hand, so the result is exactly what the GUI would produce - including the
    /// TrackedPoseDriver bindings, which are easy to get subtly wrong.
    ///
    /// Runs from the menu, or headless:
    ///   Unity -batchmode -nographics -projectPath . \
    ///         -executeMethod RowVR.Editor.SceneBootstrap.CreateBatch
    /// </summary>
    public static class SceneBootstrap
    {
        const string SceneDirectory = "Assets/Scenes";
        const string ScenePath = SceneDirectory + "/Row.unity";
        const string XROriginMenuItem = "GameObject/XR/XR Origin (VR)";
        const string SimulatorPackage = "com.unity.xr.interaction.toolkit";
        const string SimulatorSample = "XR Interaction Simulator";
        const string HandsPackage = "com.unity.xr.hands";
        const string HandVisualizerSample = "HandVisualizer";

        [MenuItem("RowVR/Create Rowing Scene")]
        public static void Create() => TryCreate();

        const string UnlitShaderPath = "Assets/Shaders/RowVRUnlit.shader";
        const string IllustratedShaderPath = "Assets/Shaders/RowVRIllustrated.shader";
        const string TextShaderPath = "Assets/Shaders/RowVRText.shader";
        const string PanelFontPath = "Assets/Fonts/PatrickHand-Regular.ttf";
        const string FadeShaderPath = "Assets/Shaders/RowVRPassthroughFade.shader";

        /// <summary>
        /// Adds the scenery (ADR-0008) to the open scene, or brings existing scenery up to
        /// date. Safe to run repeatedly. Save the scene afterwards.
        /// </summary>
        [MenuItem("RowVR/Add Scenery")]
        public static void AddScenery()
        {
            var camera = Object.FindFirstObjectByType<XROrigin>()?.Camera ?? Camera.main;
            if (camera == null)
            {
                Debug.LogError("[RowVR] No XR Origin camera in the open scene; open Row.unity first.");
                return;
            }
            if (ConfigureScenery(camera))
            {
                EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
                Debug.Log("[RowVR] Scenery added: layers, sky, water and course; camera clear set to " +
                          "transparent; fog on. Save the scene.");
            }
        }

        const string RapidsScenePath = SceneDirectory + "/Rapids.unity";

        /// <summary>
        /// Builds Assets/Scenes/Rapids.unity (REQ-0007) from the lake scene: same rig, hands,
        /// boat model and sky, turned to face downstream, with the river in place of the lake,
        /// a raft in place of the scull, a handle in place of the oars, and the rapids' run,
        /// card and sound. Rebuilds it if it exists: it is generated, not hand-edited.
        /// Headless: -executeMethod RowVR.Editor.SceneBootstrap.CreateRapidsBatch
        /// </summary>
        [MenuItem("RowVR/Create Rapids Scene")]
        public static void CreateRapids() => TryCreateRapids();

        public static void CreateRapidsBatch() => EditorApplication.Exit(TryCreateRapids() ? 0 : 1);

        static bool TryCreateRapids()
        {
            try
            {
                var lake = EditorSceneManager.OpenScene(ScenePath);
                if (!EditorSceneManager.SaveScene(lake, RapidsScenePath, true)) return false;
                var scene = EditorSceneManager.OpenScene(RapidsScenePath);

                var layers = Object.FindFirstObjectByType<SceneryLayers>();
                var rig = Object.FindFirstObjectByType<RowingRig>();
                if (layers == null || rig == null)
                {
                    Debug.LogError("[RowVR] Row.unity has no scenery or rig; run Create Rowing Scene first.");
                    return false;
                }

                // Scenery: facing downstream, the river instead of the lake.
                var course = layers.GetComponent<CourseScenery>();
                if (course != null) Object.DestroyImmediate(course);
                if (layers.GetComponent<RiverScenery>() == null) layers.gameObject.AddComponent<RiverScenery>();
                var serializedLayers = new SerializedObject(layers);
                serializedLayers.FindProperty("facingTravel").boolValue = true;
                serializedLayers.ApplyModifiedPropertiesWithoutUndo();

                // A river palette: greener water, cooler light, the orange raft.
                var palette = layers.GetComponent<ScenePalette>();
                if (palette != null)
                {
                    palette.waterDeep = new Color(0.2f, 0.4f, 0.4f);
                    palette.waterLight = new Color(0.36f, 0.56f, 0.52f);
                    palette.skyHorizon = new Color(0.86f, 0.9f, 0.88f);
                    palette.horizonGlow = new Color(0.96f, 0.86f, 0.7f);
                    EditorUtility.SetDirty(palette);
                }
                // A gorge: nearer mist, which also hides where the banks wrap.
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = 45f;
                RenderSettings.fogEndDistance = 330f;
                RenderSettings.fogColor = layers.HorizonColour;

                // The rower: a raft and a handle instead of a scull and oars.
                var go = rig.gameObject;
                foreach (var drop in new Component[] { go.GetComponent<BoatHullView>(), go.GetComponent<MetricsPanel>() })
                    if (drop != null) Object.DestroyImmediate(drop);
                var grip = go.GetComponent<GripDisplay>();
                if (grip != null)
                {
                    var serializedGrip = new SerializedObject(grip);
                    serializedGrip.FindProperty("mode").enumValueIndex = (int)GripDisplay.GripMode.Handle;
                    serializedGrip.ApplyModifiedPropertiesWithoutUndo();
                }
                var motion = go.GetComponent<BoatMotion>();
                if (motion != null)
                {
                    var serializedMotion = new SerializedObject(motion);
                    serializedMotion.FindProperty("driveScenery").boolValue = false;
                    serializedMotion.ApplyModifiedPropertiesWithoutUndo();
                }
                if (go.GetComponent<RapidsMotion>() == null) go.AddComponent<RapidsMotion>();
                if (go.GetComponent<RaftView>() == null) go.AddComponent<RaftView>();
                if (go.GetComponent<RapidsSound>() == null) go.AddComponent<RapidsSound>();
                var panel = go.GetComponent<RapidsPanel>();
                if (panel == null) panel = go.AddComponent<RapidsPanel>();
                var serializedPanel = new SerializedObject(panel);
                serializedPanel.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(PanelFontPath);
                serializedPanel.FindProperty("textShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(TextShaderPath);
                serializedPanel.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(go);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) return false;
                RegisterScene(RapidsScenePath);
                Debug.Log($"[RowVR] Created {RapidsScenePath}.");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        /// <summary>Adds a scene to Build Settings after the ones already there, enabled.</summary>
        static void RegisterScene(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>
        /// Batch entry point: opens Row.unity, applies the scene setup that has grown since it
        /// was first built (scenery, and the bring-up geometry it replaces), and saves it.
        /// Lets scene changes be made and committed headless, like everything else here.
        /// </summary>
        public static void UpdateSceneBatch()
        {
            var ok = false;
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath);
                var camera = Object.FindFirstObjectByType<XROrigin>()?.Camera;
                ok = camera != null && ConfigureScenery(camera);
                if (ok) ok = EditorSceneManager.SaveScene(scene);
                Debug.Log(ok ? $"[RowVR] Updated and saved {ScenePath}." : $"[RowVR] Could not update {ScenePath}.");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// Everything here has to be saved in the scene rather than set at runtime:
        ///  - the shader reference, or a device build strips the shader;
        ///  - the fog, or URP strips the fog shader variants from the build, since it keeps
        ///    only the fog modes some saved scene uses;
        ///  - the camera clear, so the scene itself is passthrough-ready.
        /// </summary>
        static bool ConfigureScenery(Camera camera)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(UnlitShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[RowVR] {UnlitShaderPath} not found; scenery not added.");
                return false;
            }

            var layers = Object.FindFirstObjectByType<SceneryLayers>();
            if (layers == null)
            {
                var scenery = new GameObject("Scenery");
                layers = scenery.AddComponent<SceneryLayers>();
                scenery.AddComponent<SkyDome>();
                scenery.AddComponent<WaterSurface>();
                scenery.AddComponent<CourseScenery>();
            }

            if (layers.GetComponent<ScenePalette>() == null) layers.gameObject.AddComponent<ScenePalette>();

            var serialized = new SerializedObject(layers);
            serialized.FindProperty("unlitShader").objectReferenceValue = shader;
            serialized.FindProperty("illustratedShader").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Shader>(IllustratedShaderPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Transparent black: where no layer draws, passthrough will show (ADR-0008).
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.farClipPlane = 1000f;
            EditorUtility.SetDirty(camera);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 800f;
            RenderSettings.fogColor = layers.HorizonColour;

            var water = layers.GetComponent<WaterSurface>();
            if (water != null)
            {
                // Illustrated dashes are sparser and larger than the first ripple texture.
                var serializedWater = new SerializedObject(water);
                serializedWater.FindProperty("tileSize").floatValue = 7f;
                serializedWater.ApplyModifiedPropertiesWithoutUndo();
            }

            var rig = Object.FindFirstObjectByType<RowingRig>();
            if (rig != null) ConfigureRig(rig);

            // The bring-up floor and forward marker were for finding your bearings in an
            // empty scene. The scenery replaces them; left on, the floor shows through the
            // water and the marker floats in the sky.
            foreach (var name in new[] { "Floor", "Forward Marker" })
            {
                var bringUp = GameObject.Find(name);
                if (bringUp != null) bringUp.SetActive(false);
            }
            return true;
        }

        /// <summary>
        /// Batch entry point. Carries its own exit code, for the same reason
        /// <see cref="BuildQuest"/> does: with -quit Unity reports success even when the
        /// work failed, and a scene that was never created would go unnoticed.
        /// </summary>
        public static void CreateBatch()
        {
            var ok = TryCreate();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool TryCreate()
        {
            if (File.Exists(ScenePath))
            {
                Debug.LogError(
                    $"[RowVR] {ScenePath} already exists. This builds the scene from scratch and " +
                    "will not overwrite your edits - delete the file first if you really want to " +
                    "regenerate it.");
                return false;
            }

            EnsureSampleImported(SimulatorPackage, SimulatorSample);
            EnsureSampleImported(HandsPackage, HandVisualizerSample);

            // Empty rather than DefaultGameObjects: the XR rig brings its own camera and
            // audio listener, and a second one of either is a warning at best.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            CreateReferenceGeometry();

            if (!EditorApplication.ExecuteMenuItem(XROriginMenuItem))
            {
                Debug.LogError(
                    $"[RowVR] Could not run '{XROriginMenuItem}'. Is the XR Interaction Toolkit " +
                    "installed? Nothing was saved.");
                return false;
            }

            var origin = Object.FindFirstObjectByType<XROrigin>();
            if (origin == null || origin.Camera == null)
            {
                Debug.LogError("[RowVR] XR Origin was created but has no camera. Nothing was saved.");
                return false;
            }

            // Floor-relative tracking: a rower's handle height only means anything relative
            // to the floor. Device-relative would make it depend on where the headset was
            // when the app started.
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            var rigObject = new GameObject("Rowing Rig");
            var rig = rigObject.AddComponent<RowingRig>();
            rigObject.AddComponent<StrokeReadout>();
            rigObject.AddComponent<HandRecorder>();
            rigObject.AddComponent<HandleView>();
            rigObject.AddComponent<OarsView>();
            rigObject.AddComponent<GripDisplay>();
            rigObject.AddComponent<StrokeAudio>();
            rigObject.AddComponent<BoatFrame>();
            rigObject.AddComponent<BoatHullView>();
            rigObject.AddComponent<BoatMotion>();
            rigObject.AddComponent<Soundscape>();
            // Tier 2 replays a recorded take by default; untick it to use the simulator.
            rigObject.AddComponent<ClipPlayback>();

            // Both fields are private [SerializeField], so they are set through
            // SerializedObject rather than by making them public purely for this script.
            var serialized = new SerializedObject(rig);
            if (!SetReference(serialized, "trackingOrigin", origin.transform)) return false;
            if (!SetReference(serialized, "head", origin.Camera.transform)) return false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AddSimulatorIfImported();
            AddHandVisualizerIfImported(origin.transform);
            ConfigureScenery(origin.Camera);

            Directory.CreateDirectory(SceneDirectory);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"[RowVR] Failed to save the scene to {ScenePath}.");
                return false;
            }

            AssetDatabase.Refresh();
            RegisterInBuildSettings();

            Debug.Log(
                $"[RowVR] Created {ScenePath} and added it to Build Settings.\n" +
                "  XR Origin (floor-relative) with its camera\n" +
                "  Rowing Rig: RowingRig + StrokeReadout + HandRecorder + HandleView + OarsView + GripDisplay + StrokeAudio + ClipPlayback, trackingOrigin and head wired\n" +
                "Commit the scene, then edit it in the Editor from here on.");
            return true;
        }

        /// <summary>
        /// The boat and the rower-facing panel on the Rowing Rig, and the debugging aids off.
        /// StrokeReadout still logs every catch for make logs; its floating panel is hidden.
        /// </summary>
        static void ConfigureRig(RowingRig rig)
        {
            var go = rig.gameObject;
            if (go.GetComponent<BoatFrame>() == null) go.AddComponent<BoatFrame>();
            if (go.GetComponent<BoatHullView>() == null) go.AddComponent<BoatHullView>();
            if (go.GetComponent<BoatMotion>() == null) go.AddComponent<BoatMotion>();
            if (go.GetComponent<Soundscape>() == null) go.AddComponent<Soundscape>();
            var panel = go.GetComponent<MetricsPanel>();
            if (panel == null) panel = go.AddComponent<MetricsPanel>();

            var serializedPanel = new SerializedObject(panel);
            serializedPanel.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(PanelFontPath);
            serializedPanel.FindProperty("textShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(TextShaderPath);
            serializedPanel.ApplyModifiedPropertiesWithoutUndo();

            var passthrough = go.GetComponent<PassthroughFade>();
            if (passthrough == null) passthrough = go.AddComponent<PassthroughFade>();
            var serializedPassthrough = new SerializedObject(passthrough);
            serializedPassthrough.FindProperty("fadeShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(FadeShaderPath);
            serializedPassthrough.ApplyModifiedPropertiesWithoutUndo();

            var menu = go.GetComponent<MenuCard>();
            if (menu == null) menu = go.AddComponent<MenuCard>();
            var serializedMenu = new SerializedObject(menu);
            serializedMenu.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(PanelFontPath);
            serializedMenu.FindProperty("textShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(TextShaderPath);
            serializedMenu.ApplyModifiedPropertiesWithoutUndo();

            var readout = go.GetComponent<StrokeReadout>();
            if (readout != null)
            {
                var serializedReadout = new SerializedObject(readout);
                serializedReadout.FindProperty("drawInWorld").boolValue = false;
                serializedReadout.FindProperty("drawOnScreen").boolValue = false;
                serializedReadout.ApplyModifiedPropertiesWithoutUndo();
            }

            var playback = go.GetComponent<ClipPlayback>();
            if (playback != null)
            {
                var serializedPlayback = new SerializedObject(playback);
                serializedPlayback.FindProperty("showHands").boolValue = false;
                serializedPlayback.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(go);
        }

        static bool SetReference(SerializedObject serialized, string propertyName, Object value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError(
                    $"[RowVR] RowingRig has no serialized field '{propertyName}'. It was renamed " +
                    "and this script was not updated. Refusing to save a rig that would silently " +
                    "never track.");
                return false;
            }

            property.objectReferenceValue = value;
            return true;
        }

        /// <summary>
        /// The XR Interaction Simulator ships as a package *sample*, not as a menu item, so
        /// it does not exist in the project until imported. Tier 2 of the dev loop depends
        /// on it, so import it here rather than leaving a scene that mysteriously cannot be
        /// driven without a headset.
        /// </summary>
        static void EnsureSampleImported(string package, string sampleName)
        {
            var sample = Sample.FindByPackage(package, string.Empty)
                .FirstOrDefault(s => s.displayName == sampleName);

            if (sample.Equals(default(Sample)))
            {
                Debug.LogWarning(
                    $"[RowVR] Could not find the '{sampleName}' sample in {package}. " +
                    "Import it by hand from Window > Package Manager.");
                return;
            }

            if (sample.isImported)
            {
                Debug.Log($"[RowVR] '{sampleName}' sample already imported.");
                return;
            }

            if (sample.Import(Sample.ImportOptions.HideImportWindow |
                              Sample.ImportOptions.OverridePreviousImports))
            {
                Debug.Log($"[RowVR] Imported the '{sampleName}' sample.");
                AssetDatabase.Refresh();
            }
            else
            {
                Debug.LogWarning($"[RowVR] Importing the '{sampleName}' sample failed.");
            }
        }

        /// <summary>
        /// A floor and a couple of markers. Not decoration: an empty scene renders as bare
        /// skybox, which is indistinguishable from the app having failed to start. Geometry
        /// at known positions also gives the rower something to judge handle travel against.
        /// </summary>
        static void CreateReferenceGeometry()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(2f, 1f, 2f);

            // Dark, not the default near-white: the readout is white text that follows the
            // rower's gaze, and looking down at the handle put it against a white floor
            // where it could not be read.
            var floorMaterial = CreateMaterial("Floor", new Color(0.18f, 0.19f, 0.21f));
            if (floorMaterial != null) floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

            // Straight ahead at seated eye height, so "am I facing the right way" is obvious.
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Forward Marker";
            marker.transform.position = new Vector3(0f, 1.2f, 3f);
            marker.transform.localScale = Vector3.one * 0.3f;
        }

        /// <summary>
        /// Match the file name exactly. The sample ships two prefabs - the simulator and a
        /// separate "XR Interaction Simulator UI" HUD - and a substring search returns both
        /// in unspecified order. Picking the UI one produces a scene that looks right, shows
        /// the simulator's on-screen panel, and has no simulated hands whatsoever.
        /// </summary>
        static void AddSimulatorIfImported()
        {
            var guid = AssetDatabase.FindAssets($"\"{SimulatorSample}\" t:Prefab")
                .FirstOrDefault(g => Path.GetFileNameWithoutExtension(
                    AssetDatabase.GUIDToAssetPath(g)) == SimulatorSample);

            if (guid == null)
            {
                Debug.LogWarning(
                    "[RowVR] XR Interaction Simulator prefab not found, so the scene has no " +
                    "simulated hands and tier 2 will not work yet.\n" +
                    "  Window > Package Manager > XR Interaction Toolkit > Samples > " +
                    "XR Interaction Simulator > Import, then drag the prefab into the scene.");
                return;
            }

            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            // EditorOnly: stripped from player builds. The simulator's lifecycle manager is
            // guarded only by ENABLE_VR, which is true on Quest, so left in a device build it
            // starts a *simulated* XRHandSubsystem and, by default, removes the real HMD input
            // device. Anything that takes "the first running hand subsystem" - our pose source
            // and Unity's own HandVisualizer - then reads the simulator's hands, which are
            // never tracked on device. The symptom was the runtime tracking both hands while
            // the app saw neither, with the subsystem updating ~146 times a second and
            // returning UpdateSuccessFlags.None every time.
            instance.tag = "EditorOnly";
            Debug.Log($"[RowVR] Added the XR Interaction Simulator from {path} (EditorOnly).");
        }

        /// <summary>
        /// The XR Hands visualizer is a component plus a set of mesh prefabs, not a single
        /// drop-in prefab, so it has to be assembled. Its scripts live in a sample assembly
        /// this one does not reference, so the type is found through its MonoScript and the
        /// fields are set by name - a compile-time dependency on a sample would break the
        /// build for anyone who has not imported it.
        ///
        /// Both meshes AND debug joints are switched on: if the mesh material fails to
        /// render under URP, the joint spheres still prove tracking is alive, which is the
        /// question being asked on device.
        /// </summary>
        /// <summary>
        /// A lit material for whichever render pipeline is actually active, saved as an asset
        /// so the build includes it. The first version assumed URP because the URP package is
        /// installed - but no URP pipeline asset is assigned, so the project renders with the
        /// built-in pipeline, and a URP shader there draws solid magenta.
        /// </summary>
        static Material CreateMaterial(string name, Color color)
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null;
            var shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
            if (shader == null)
            {
                Debug.LogWarning($"[RowVR] No lit shader found for the active pipeline; '{name}' keeps the default material.");
                return null;
            }

            const string directory = "Assets/Materials";
            if (!AssetDatabase.IsValidFolder(directory)) AssetDatabase.CreateFolder("Assets", "Materials");

            var path = $"{directory}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            // Built-in Standard calls it _Color, URP Lit _BaseColor. Set both, so the material
            // stays correct if the project moves to URP later.
            material.SetColor("_Color", color);
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void AddHandVisualizerIfImported(Transform trackingOrigin)
        {
            var scriptGuid = AssetDatabase.FindAssets("HandVisualizer t:MonoScript")
                .FirstOrDefault(g => AssetDatabase.GUIDToAssetPath(g)
                    .EndsWith("/HandVisualizer.cs", System.StringComparison.Ordinal));

            var type = scriptGuid == null
                ? null
                : AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(scriptGuid))
                    ?.GetClass();

            if (type == null)
            {
                Debug.LogWarning(
                    "[RowVR] Hand Visualizer script not found. Hands will be tracked but " +
                    "invisible, which makes on-device testing nearly impossible to interpret.");
                return;
            }

            // Parent to the XR Origin. Hand joints arrive in tracking space, exactly as they
            // do for XRHandsPoseSource, so a visualizer sitting at the scene root draws the
            // hands relative to the world origin instead of relative to the rower - they
            // render, but somewhere on the floor rather than on the end of your arms.
            var host = new GameObject("Hand Visualizer");
            host.transform.SetParent(trackingOrigin, false);
            var component = host.AddComponent(type);
            var serialized = new SerializedObject(component);

            SetAsset(serialized, "m_MetaQuestLeftHandMesh", "Left Hand Tracking", "t:Prefab");
            SetAsset(serialized, "m_MetaQuestRightHandMesh", "Right Hand Tracking", "t:Prefab");
            SetAsset(serialized, "m_DebugDrawPrefab", "Joint", "t:Prefab");
            SetAsset(serialized, "m_VelocityPrefab", "VelocityPrefab", "t:Prefab");
            SetAsset(serialized, "m_HandMeshMaterial", "HandsDefaultMaterial", "t:Material");

            // Meshes only. The debug joints and velocity arrows were useful for proving
            // tracking was alive, but on device they draw red and blue lines streaming off
            // every joint each frame, which is maddening while rowing. The prefabs stay
            // wired so either can be switched back on in the Inspector for a debug session.
            SetBool(serialized, "m_DrawMeshes", true);
            SetBool(serialized, "m_DebugDrawJoints", false);
            var velocity = serialized.FindProperty("m_VelocityType");
            if (velocity != null)
            {
                // VelocityType is { Linear, Angular, None }; the default, Linear, draws arrows.
                var none = System.Array.IndexOf(velocity.enumNames, "None");
                if (none >= 0) velocity.enumValueIndex = none;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[RowVR] Added the Hand Visualizer (meshes only).");
        }

        static void SetAsset(SerializedObject serialized, string field, string assetName, string filter)
        {
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"[RowVR] Hand Visualizer has no field '{field}'; skipping.");
                return;
            }

            var guid = AssetDatabase.FindAssets($"\"{assetName}\" {filter}")
                .FirstOrDefault(g => Path.GetFileNameWithoutExtension(
                                         AssetDatabase.GUIDToAssetPath(g)) == assetName &&
                                     AssetDatabase.GUIDToAssetPath(g).Contains("HandVisualizer"));

            if (guid == null)
            {
                Debug.LogWarning($"[RowVR] Could not find asset '{assetName}' for '{field}'.");
                return;
            }

            property.objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));
        }

        static void SetBool(SerializedObject serialized, string field, bool value)
        {
            var property = serialized.FindProperty(field);
            if (property != null) property.boolValue = value;
        }

        static void RegisterInBuildSettings()
        {
            // BuildQuest.Apk aborts when EditorBuildSettings.scenes is empty, so without
            // this a tier 3 build fails several minutes in for a reason that has nothing to
            // do with the code being tested.
            if (EditorBuildSettings.scenes.Any(s => s.path == ScenePath)) return;

            EditorBuildSettings.scenes = EditorBuildSettings.scenes
                .Append(new EditorBuildSettingsScene(ScenePath, true))
                .ToArray();
        }
    }
}
