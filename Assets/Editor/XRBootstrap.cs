using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;

namespace RowVR.Editor
{
    /// <summary>
    /// Enables OpenXR for Android and turns on the features this app needs.
    ///
    /// This used to be a manual checklist, on the assumption that XR Plug-in Management
    /// could not be driven from code. It can - XRPackageMetadataStore.AssignLoader and
    /// FeatureHelpers are supported editor APIs - and leaving it manual cost a real build:
    /// with no loader assigned, Unity produces an APK that installs, launches and runs
    /// perfectly well as a *flat 2D Android panel*. There is no error. The only symptom is
    /// "xrDevice=0x0" buried in logcat and the absence of com.oculus.intent.category.VR
    /// from the manifest. A 13-minute build to discover a forgotten checkbox is exactly the
    /// kind of thing this project puts in code instead.
    /// </summary>
    public static class XRBootstrap
    {
        const BuildTargetGroup Target = BuildTargetGroup.Android;
        const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

        /// <summary>
        /// Feature ids, read from the OpenXR and XR Hands packages rather than guessed.
        /// Interaction profile: OpenXR requires at least one even for a hands-only app,
        /// because the runtime uses it to negotiate the session.
        /// </summary>
        static readonly (string Id, string Name)[] RequiredFeatures =
        {
            // NOT "...input.handtracking". That id is declared by BOTH XR Hands and the
            // OpenXR package's Microsoft HoloLens hand interaction profile; XR Hands
            // deprecated its own use of it over the collision and moved the subsystem to
            // "handtrackingsubsystem". Looking up the old id silently resolves to
            // MicrosoftHandInteraction, so hand tracking stays off while the report claims
            // it was enabled. See HandTracking.featureId2 in com.unity.xr.hands.
            ("com.unity.openxr.feature.input.handtrackingsubsystem", "Hand Tracking Subsystem"),
            ("com.unity.openxr.feature.input.metahandtrackingaim", "Meta Hand Tracking Aim"),
            ("com.unity.openxr.feature.metaquest", "Meta Quest Support"),
            ("com.unity.openxr.feature.input.oculustouch", "Oculus Touch Interaction Profile"),

            // XR_EXT_hand_interaction. When Horizon OS switches to hands-only mode it drops
            // the Touch profile for both hands ("IP changed: .../oculus/touch_controller, -1")
            // and the shell rebinds to ext/hand_interaction_ext. Without this profile the
            // app has no interaction profile for hands at all in hand mode; with it, the app
            // advertises hand input the same way the shell does.
            ("com.unity.openxr.feature.input.handinteraction", "Hand Interaction Profile"),

            // Passthrough (REQ-0005, ADR-0014), from com.unity.xr.meta-openxr: the camera
            // feature is Quest passthrough, driven by AR Foundation's ARCameraManager; the
            // session feature runs the AR session it needs.
            ("com.unity.openxr.feature.arfoundation-meta-session", "Meta Quest: Session"),
            ("com.unity.openxr.feature.arfoundation-meta-camera", "Meta Quest: Camera (Passthrough)"),
        };

        /// <summary>
        /// Features that must stay off. MicrosoftHandInteraction is a HoloLens interaction
        /// profile with no place in a Quest build; it is listed because the id collision
        /// above switched it on by mistake, and an unintended interaction profile changes
        /// what the runtime negotiates at session start.
        /// </summary>
        static readonly (string Id, string Name)[] UnwantedFeatures =
        {
            ("com.unity.openxr.feature.input.handtracking", "Microsoft Hand Interaction"),
        };

        [MenuItem("RowVR/Bootstrap XR Settings")]
        public static void Apply()
        {
            var report = new List<string>();

            if (!AssignOpenXRLoader(report)) return;
            EnableFeatures(report);
            DisableFeatures(report);

            AssetDatabase.SaveAssets();
            Debug.Log("[RowVR] XR bootstrap:\n  " + string.Join("\n  ", report));
        }

        static bool AssignOpenXRLoader(List<string> report)
        {
            if (!TryGetPerBuildTargetSettings(out var perBuildTarget)) return false;

            // XRGeneralSettingsForBuildTarget only *reads* - it returns null when the target
            // has no entry rather than creating one, which is the state a project is in
            // until someone opens the XR Plug-in Management UI. These two calls are what the
            // UI does on your behalf.
            if (!perBuildTarget.HasManagerSettingsForBuildTarget(Target))
            {
                perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(Target);
                report.Add("created default XR settings and provider list for Android");
            }

            var settings = perBuildTarget.SettingsForBuildTarget(Target);
            if (settings?.Manager == null)
            {
                Debug.LogError("[RowVR] Android XR settings still have no manager after creating " +
                               "defaults. Aborting rather than producing a non-VR build silently.");
                return false;
            }

            // Without this XR never starts, and the app comes up flat even with a loader
            // assigned.
            settings.InitManagerOnStart = true;

            var already = settings.Manager.activeLoaders
                .Any(l => l != null && l.GetType().FullName == OpenXRLoaderType);

            if (already)
            {
                report.Add("OpenXR loader already assigned for Android");
            }
            else if (XRPackageMetadataStore.AssignLoader(settings.Manager, OpenXRLoaderType, Target))
            {
                report.Add("assigned the OpenXR loader for Android");
            }
            else
            {
                Debug.LogError(
                    $"[RowVR] Failed to assign {OpenXRLoaderType} for Android. Without it the APK " +
                    "builds and runs, but as a flat 2D app with no XR at all.");
                return false;
            }

            EditorUtility.SetDirty(perBuildTarget);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(settings.Manager);
            return true;
        }

        /// <summary>
        /// The container asset is registered with EditorBuildSettings under a well-known
        /// key. The package's own accessors for it are internal, so go through the public
        /// config-object API, and create the asset when a fresh clone has none.
        /// </summary>
        static bool TryGetPerBuildTargetSettings(out XRGeneralSettingsPerBuildTarget perBuildTarget)
        {
            if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out perBuildTarget)
                && perBuildTarget != null)
            {
                return true;
            }

            const string directory = "Assets/XR";
            const string path = directory + "/XRGeneralSettingsPerBuildTarget.asset";

            if (!AssetDatabase.IsValidFolder(directory))
                AssetDatabase.CreateFolder("Assets", "XR");

            perBuildTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (perBuildTarget == null)
            {
                perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perBuildTarget, path);
            }

            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perBuildTarget, true);
            return perBuildTarget != null;
        }

        static void EnableFeatures(List<string> report)
        {
            // Populates the feature list for the target; without it the lookups below
            // return null on a project that has never opened the OpenXR settings UI.
            FeatureHelpers.RefreshFeatures(Target);

            foreach (var (id, name) in RequiredFeatures)
            {
                var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(Target, id);
                if (feature == null)
                {
                    report.Add($"MISSING feature '{name}' ({id}) - not available for Android");
                    Debug.LogWarning(
                        $"[RowVR] OpenXR feature '{name}' ({id}) was not found for Android. Hand " +
                        "tracking may not work on device.");
                    continue;
                }

                if (feature.enabled)
                {
                    report.Add($"'{name}' already enabled");
                    continue;
                }

                feature.enabled = true;
                EditorUtility.SetDirty(feature);
                report.Add($"enabled '{name}'");
            }
        }

        static void DisableFeatures(List<string> report)
        {
            foreach (var (id, name) in UnwantedFeatures)
            {
                var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(Target, id);
                if (feature == null || !feature.enabled) continue;

                feature.enabled = false;
                EditorUtility.SetDirty(feature);
                report.Add($"disabled '{name}' (not wanted on Quest)");
            }
        }

        /// <summary>
        /// Reports what is actually configured, for the validation pass. Says nothing about
        /// whether it works on device - only that the settings are what we intended.
        /// </summary>
        public static string Describe()
        {
            var settings = EditorBuildSettings.TryGetConfigObject(
                XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perBuildTarget)
                ? perBuildTarget.SettingsForBuildTarget(Target)
                : null;
            if (settings?.Manager == null) return "  XR (Android)      : NOT CONFIGURED";

            var loaders = settings.Manager.activeLoaders
                .Where(l => l != null)
                .Select(l => l.GetType().Name)
                .ToArray();

            var lines = new List<string>
            {
                "  XR loaders        : " + (loaders.Length == 0 ? "NONE (app will be flat 2D)" : string.Join(", ", loaders)),
                "  init on start     : " + settings.InitManagerOnStart,
            };

            FeatureHelpers.RefreshFeatures(Target);
            foreach (var (id, name) in RequiredFeatures)
            {
                var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(Target, id);
                var state = feature == null ? "NOT AVAILABLE" : feature.enabled ? "on" : "OFF";
                lines.Add($"  {name,-30}: {state}");
            }

            return string.Join("\n", lines);
        }
    }
}
