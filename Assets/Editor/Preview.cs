using System;
using System.IO;
using RowVR.Rowing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RowVR.Editor
{
    /// <summary>
    /// Renders stills of the rowing scene in Play Mode, headless: `make preview`.
    ///
    /// Tier 2 so far needed a person at the Editor to look at the scene. This lets the scene
    /// be checked with no window and no headset: it opens Row.unity, enters Play Mode with
    /// whatever ClipPlayback replays, lets the rower row for a while, and saves PNGs from the
    /// rower's eye and from outside the boat.
    ///
    /// The shots use their own camera placed at the recorded head pose, so they do not depend
    /// on the XR camera, which has no headset to follow here. Pixels the scene leaves at
    /// alpha 0 (where passthrough would show) come out black.
    ///
    ///   Unity -batchmode -projectPath . -executeMethod RowVR.Editor.Preview.CaptureBatch
    ///         [-previewSeconds 16] [-previewOut .devloop/preview]
    ///
    /// Not -nographics: rendering needs a GPU and a display.
    /// </summary>
    public static class Preview
    {
        const string ScenePath = "Assets/Scenes/Row.unity";
        const int Width = 1280;
        const int Height = 720;
        const double TimeoutSeconds = 180;

        static double _playStarted = -1;
        static double _batchStarted;
        static float _captureAt;
        static string _outDir;

        public static void CaptureBatch()
        {
            _captureAt = FloatArg("-previewSeconds", 16f);
            _outDir = StringArg("-previewOut", ".devloop/preview");
            Directory.CreateDirectory(_outDir);

            EditorSceneManager.OpenScene(ScenePath);
            _batchStarted = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - _batchStarted > TimeoutSeconds)
            {
                Debug.LogError("[RowVR] preview: timed out before capturing.");
                EditorApplication.Exit(1);
                return;
            }

            if (!EditorApplication.isPlaying) return;
            if (_playStarted < 0) _playStarted = EditorApplication.timeSinceStartup;
            if (Time.timeSinceLevelLoad < _captureAt) return;

            EditorApplication.update -= Tick;
            var ok = false;
            try
            {
                ok = CaptureAll();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            // Leave Play Mode before exiting: exiting from inside it can hang the batch
            // Editor. tools/preview.sh also stops Unity itself once the stills are written.
            _exitCode = ok ? 0 : 1;
            EditorApplication.playModeStateChanged += ExitWhenStopped;
            EditorApplication.ExitPlaymode();
        }

        static int _exitCode;

        static void ExitWhenStopped(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= ExitWhenStopped;
            EditorApplication.Exit(_exitCode);
        }

        static bool CaptureAll()
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<RowingRig>();
            var layers = UnityEngine.Object.FindFirstObjectByType<SceneryLayers>();
            var xrCamera = Camera.main;
            if (rig == null || xrCamera == null)
            {
                Debug.LogError("[RowVR] preview: no RowingRig or main camera in the scene.");
                return false;
            }

            var head = rig.IsTracking ? rig.LastSample.HeadPosition.ToVector3() : xrCamera.transform.position;
            var forward = layers != null ? layers.RowerForward : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);

            Shoot(xrCamera, "eye-forward", head, Quaternion.LookRotation(forward + Vector3.down * 0.25f));
            Shoot(xrCamera, "eye-left", head, Quaternion.LookRotation(-right + forward * 0.35f + Vector3.down * 0.3f));
            Shoot(xrCamera, "eye-back", head, Quaternion.LookRotation(-forward + Vector3.down * 0.1f));
            var outside = head - forward * 4.5f + right * 3.5f + Vector3.up * 2.2f;
            Shoot(xrCamera, "boat", outside, Quaternion.LookRotation(head + Vector3.down * 0.8f - outside));

            Debug.Log($"[RowVR] preview: captured at {Time.timeSinceLevelLoad:F1}s, phase {rig.State.Phase}, " +
                      $"tracking {rig.IsTracking}, distance {(layers != null ? layers.Distance : 0):F1} m, into {_outDir}");
            return true;
        }

        static void Shoot(Camera template, string name, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Preview Camera");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.CopyFrom(template);
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.fieldOfView = 80f;
                camera.transform.SetPositionAndRotation(position, rotation);

                var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                camera.targetTexture = target;
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;

                File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), image.EncodeToPNG());
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static string StringArg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        static float FloatArg(string name, float fallback) =>
            float.TryParse(StringArg(name, null), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
