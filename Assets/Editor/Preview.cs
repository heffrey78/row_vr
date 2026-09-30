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
    ///         [-previewFrames 0] [-previewFps 15]
    ///
    /// With -previewFrames N it also writes a sequence, frame_000.png onward, from the rower's
    /// eye: N frames at -previewFps of game time each, whatever the machine renders at
    /// (Time.captureFramerate), so a clip is the same every run. tools/preview.sh can turn it
    /// into a GIF.
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
        static int _frames;
        static int _frameIndex;
        static int _fps;

        public static void CaptureBatch()
        {
            _captureAt = FloatArg("-previewSeconds", 16f);
            _outDir = StringArg("-previewOut", ".devloop/preview");
            _frames = (int)FloatArg("-previewFrames", 0f);
            _fps = Math.Max(1, (int)FloatArg("-previewFps", 15f));
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
            if (_playStarted < 0)
            {
                _playStarted = EditorApplication.timeSinceStartup;
                // Fixed game time per rendered frame, so a sequence plays at real speed.
                if (_frames > 0) Time.captureFramerate = _fps;
            }
            if (Time.timeSinceLevelLoad < _captureAt) return;

            if (_frameIndex < _frames)
            {
                try { CaptureFrame(); }
                catch (Exception e) { Debug.LogException(e); _frameIndex = _frames; }
                return;
            }

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

        /// <summary>One frame of the sequence, from the rower's eye, looking a little to the right of the stern.</summary>
        static void CaptureFrame()
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<RowingRig>();
            var layers = UnityEngine.Object.FindFirstObjectByType<SceneryLayers>();
            var xrCamera = Camera.main;
            if (rig == null || xrCamera == null) { _frameIndex = _frames; return; }
            var head = rig.LastSample.HeadPosition.ToVector3();
            var forward = layers != null ? layers.RowerForward : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var look = Quaternion.LookRotation(forward + right * 0.22f + Vector3.down * 0.28f);
            Shoot(xrCamera, $"frame_{_frameIndex:000}", head, look, 640, 360);
            _frameIndex++;
        }

        static void Shoot(Camera template, string name, Vector3 position, Quaternion rotation) =>
            Shoot(template, name, position, rotation, Width, Height);

        static void Shoot(Camera template, string name, Vector3 position, Quaternion rotation, int width, int height)
        {
            var go = new GameObject("Preview Camera");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.CopyFrom(template);
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.fieldOfView = 80f;
                camera.transform.SetPositionAndRotation(position, rotation);

                var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                camera.targetTexture = target;
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
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
