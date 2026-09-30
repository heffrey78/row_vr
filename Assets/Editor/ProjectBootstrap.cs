using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace RowVR.Editor
{
    /// <summary>
    /// Quest 3 project configuration, expressed as code rather than as checked-in
    /// ProjectSettings YAML.
    ///
    /// Rationale: Unity's ProjectSettings/*.asset files are large, version-specific and
    /// merge badly, and a malformed one stops the Editor opening. Applying settings from
    /// code instead makes every decision reviewable in a diff, keeps CLI and GUI builds
    /// identical, and lets a fresh clone be configured with one command.
    ///
    /// Idempotent: safe to run repeatedly. Run it after first opening the project, and
    /// again after any package upgrade.
    ///
    ///   Menu:      RowVR / Bootstrap Project Settings
    ///   Headless:  make bootstrap   (-executeMethod RowVR.Editor.ProjectBootstrap.Apply)
    /// </summary>
    public static class ProjectBootstrap
    {
        public const string ApplicationIdentifier = "com.heffrey78.rowvr";
        public const string ProductName = "row_vr";
        public const string CompanyName = "heffrey78";

        // Quest 3 runs Horizon OS on Android 14 (API 34). Apps created after 2026-03-01
        // must target 34. API 32 is kept as the floor so older Horizon OS builds still load.
        const AndroidSdkVersions MinSdk = AndroidSdkVersions.AndroidApiLevel32;
        const AndroidSdkVersions TargetSdk = (AndroidSdkVersions)34;

        [MenuItem("RowVR/Bootstrap Project Settings")]
        public static void Apply()
        {
            ApplyEditorSettings();
            ApplyPlayerSettings();
            ApplyQualitySettings();
            ApplyAudioSettings();
            RenderPipelineBootstrap.Apply();

            // Must precede ConfigureXr: that method only tunes OpenXR settings that already
            // exist, and bails out when no plug-in provider has been enabled for Android.
            // XRBootstrap is what creates that state.
            XRBootstrap.Apply();
            ConfigureXr();

            AssetDatabase.SaveAssets();
            Debug.Log("[RowVR] Bootstrap applied. " + Environment.NewLine + Validate());
        }

        /// <summary>
        /// Repo hygiene settings. Text serialization and visible meta files are what make
        /// scenes and prefabs reviewable and mergeable; see .gitattributes and CLAUDE.md.
        /// </summary>
        static void ApplyEditorSettings()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            EditorSettings.externalVersionControl = "Visible Meta Files";
            EditorSettings.enterPlayModeOptionsEnabled = true;
            // Skipping domain+scene reload is the single biggest Play Mode speed win, and
            // Play Mode is tier 2 of the dev loop. Costs: statics are not reset between
            // runs, so anything static needs [RuntimeInitializeOnLoadMethod] to reset.
            EditorSettings.enterPlayModeOptions =
                EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        }

        static void ApplyPlayerSettings()
        {
            var android = NamedBuildTarget.Android;

            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(android, ApplicationIdentifier);

            // IL2CPP + ARM64 is the only combination Horizon OS accepts.
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetApiCompatibilityLevel(android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetIl2CppCompilerConfiguration(android, Il2CppCompilerConfiguration.Release);

            PlayerSettings.Android.minSdkVersion = MinSdk;
            PlayerSettings.Android.targetSdkVersion = TargetSdk;
            PlayerSettings.Android.androidIsGame = true;

            // Vulkan only. GLES is deprecated on Quest and silently costs performance.
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.MTRendering = true;
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.Android.optimizedFramePacing = false; // XR runtime owns frame pacing.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // Quest has no on-screen splash to show and the default one costs startup time.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
        }

        /// <summary>
        /// DSP buffer size for "Best latency". The catch sound is the stroke's beat with no
        /// haptics to back it up, and Android's default buffer adds tens of milliseconds
        /// before it is heard. Set on the AudioManager asset, since there is no public editor
        /// API for it.
        /// </summary>
        const int BestLatencyDspBuffer = 256;

        static void ApplyAudioSettings()
        {
            var audioManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")
                .FirstOrDefault();
            if (audioManager == null)
            {
                Debug.LogWarning("[RowVR] AudioManager settings not found; DSP buffer size not set.");
                return;
            }

            var serialized = new SerializedObject(audioManager);
            // Unity 6 keeps the Inspector's value in m_RequestedDSPBufferSize and the legacy
            // one in m_DSPBufferSize; set both so neither overrides the other.
            foreach (var name in new[] { "m_RequestedDSPBufferSize", "m_DSPBufferSize" })
            {
                var property = serialized.FindProperty(name);
                if (property != null) property.intValue = BestLatencyDspBuffer;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static int DspBufferSize()
        {
            var audioManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")
                .FirstOrDefault();
            return audioManager == null
                ? -1
                : new SerializedObject(audioManager).FindProperty("m_RequestedDSPBufferSize")?.intValue ?? -1;
        }

        static void ApplyQualitySettings()
        {
            // 4x MSAA is effectively mandatory in VR: geometric aliasing reads as shimmer at
            // the periphery and is a comfort issue, not just a visual one.
            QualitySettings.antiAliasing = 4;
            QualitySettings.vSyncCount = 0;      // The XR runtime paces frames, not vsync.
            QualitySettings.shadowDistance = 25f;
            QualitySettings.skinWeights = SkinWeights.TwoBones;
        }

        /// <summary>
        /// XR configuration via reflection.
        ///
        /// The XR Management and OpenXR editor APIs (XRGeneralSettingsPerBuildTarget,
        /// XRPackageMetadataStore, OpenXRSettings) move between package versions and are
        /// not part of Unity's stable surface. Hard-referencing them would make this
        /// assembly fail to compile on a version mismatch, which would break the whole
        /// project rather than one setting. Reflection degrades to a warning instead.
        /// Anything that cannot be set here is reported by <see cref="Validate"/> with the
        /// exact UI path to set it by hand.
        /// </summary>
        static void ConfigureXr()
        {
            var openXrSettingsType = FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
            if (openXrSettingsType == null)
            {
                Debug.LogWarning("[RowVR] OpenXR package not found; skipping XR configuration. " +
                                 "Install com.unity.xr.openxr, then re-run RowVR/Bootstrap Project Settings.");
                return;
            }

            try
            {
                var getSettings = openXrSettingsType.GetMethod(
                    "GetSettingsForBuildTargetGroup",
                    BindingFlags.Public | BindingFlags.Static);
                var settings = getSettings?.Invoke(null, new object[] { BuildTargetGroup.Android });
                if (settings == null)
                {
                    Debug.LogWarning("[RowVR] No OpenXR settings for Android yet. Enable the OpenXR " +
                                     "plug-in provider once under Project Settings > XR Plug-in Management > Android, " +
                                     "then re-run bootstrap.");
                    return;
                }

                // Single Pass Instanced roughly halves draw-call submission cost versus
                // multi-pass, and is the correct default for every Quest title.
                var renderModeProp = settings.GetType().GetProperty("renderMode");
                if (renderModeProp != null)
                {
                    var enumType = renderModeProp.PropertyType;
                    var singlePass = Enum.GetNames(enumType)
                        .FirstOrDefault(n => n.IndexOf("SinglePassInstanced", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (singlePass != null)
                    {
                        renderModeProp.SetValue(settings, Enum.Parse(enumType, singlePass));
                        Debug.Log("[RowVR] OpenXR render mode set to " + singlePass + ".");
                    }
                }

                EditorUtility.SetDirty((UnityEngine.Object)settings);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RowVR] XR configuration via reflection failed (" + e.GetType().Name +
                                 ": " + e.Message + "). Set it by hand; see Validate() output below.");
            }
        }

        /// <summary>
        /// Reports configuration that this script cannot reliably set, with the UI path for
        /// each. Printed after Apply() and available on its own from the RowVR menu.
        /// </summary>
        [MenuItem("RowVR/Validate Project Settings")]
        public static string ValidateMenu()
        {
            var report = Validate();
            Debug.Log(report);
            return report;
        }

        public static string Validate()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[RowVR] Project settings report");
            sb.AppendLine("  scripting backend : " +
                PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) + " (want IL2CPP)");
            sb.AppendLine("  architectures     : " +
                PlayerSettings.Android.targetArchitectures + " (want ARM64)");
            sb.AppendLine("  graphics API      : " +
                string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android)) + " (want Vulkan)");
            sb.AppendLine("  colour space      : " + PlayerSettings.colorSpace + " (want Linear)");
            sb.AppendLine("  min / target SDK  : " +
                PlayerSettings.Android.minSdkVersion + " / " + PlayerSettings.Android.targetSdkVersion +
                " (want 32 / 34)");
            sb.AppendLine("  serialization     : " + EditorSettings.serializationMode + " (want ForceText)");
            sb.AppendLine("  MSAA              : " + QualitySettings.antiAliasing + "x (want 4x)");
            sb.AppendLine("  DSP buffer        : " + DspBufferSize() + " (want " + BestLatencyDspBuffer + ", Best latency)");

            sb.AppendLine();
            sb.AppendLine(RenderPipelineBootstrap.Describe());
            sb.AppendLine(XRBootstrap.Describe());
            return sb.ToString();
        }

        static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType(fullName); } catch { return null; } })
                .FirstOrDefault(t => t != null);
        }
    }
}
