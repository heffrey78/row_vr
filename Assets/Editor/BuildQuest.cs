using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RowVR.Editor
{
    /// <summary>
    /// Headless APK build entry point for tier 3 of the dev loop.
    ///
    /// Invoked by tools/build.sh as:
    ///   Unity -batchmode -nographics -projectPath . \
    ///         -executeMethod RowVR.Editor.BuildQuest.Apk -- -output Build/row_vr.apk -devBuild
    ///
    /// Deliberately does NOT use -quit: it calls EditorApplication.Exit itself so the shell
    /// gets a meaningful exit code. With -quit, Unity exits 0 even when the build failed,
    /// and the loop would happily install a stale APK.
    /// </summary>
    public static class BuildQuest
    {
        const string DefaultOutput = "Build/row_vr.apk";

        public static void Apk()
        {
            var exitCode = 0;
            try
            {
                var args = CommandLine();
                var output = args.Value("-output", DefaultOutput);
                var devBuild = args.Flag("-devBuild");

                // Apply the same settings the GUI uses, so a CLI build and an Editor build
                // produce the same APK. Without this the two silently drift apart.
                ProjectBootstrap.Apply();

                var scenes = ScenesInBuild();
                if (scenes.Length == 0)
                {
                    Debug.LogError("[RowVR] No enabled scenes in Build Settings. " +
                                   "Add your scene under File > Build Profiles, or run RowVR/Bootstrap Project Settings.");
                    EditorApplication.Exit(2);
                    return;
                }

                var absoluteOutput = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput));

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = absoluteOutput,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = devBuild
                        // Development + script debugging costs a few fps but makes logcat
                        // stack traces readable and lets a debugger attach over adb.
                        ? BuildOptions.Development | BuildOptions.AllowDebugging
                        : BuildOptions.None,
                };

                Debug.Log($"[RowVR] Building {(devBuild ? "development" : "release")} APK -> {absoluteOutput}");
                Debug.Log($"[RowVR] Scenes: {string.Join(", ", scenes)}");

                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;

                Debug.Log($"[RowVR] Build {summary.result} in {summary.totalTime}, " +
                          $"{summary.totalSize / (1024 * 1024)} MB, " +
                          $"{summary.totalErrors} error(s), {summary.totalWarnings} warning(s).");

                if (summary.result != BuildResult.Succeeded)
                {
                    foreach (var step in report.steps)
                    {
                        foreach (var msg in step.messages.Where(m =>
                                     m.type == LogType.Error || m.type == LogType.Exception))
                        {
                            Debug.LogError($"[RowVR]   {step.name}: {msg.content}");
                        }
                    }
                    exitCode = 1;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[RowVR] Build threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                exitCode = 1;
            }

            EditorApplication.Exit(exitCode);
        }

        static string[] ScenesInBuild()
        {
            return EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
        }

        static CommandLineArgs CommandLine()
        {
            // Unity passes its own arguments through too; everything after a bare "--" is
            // ours. Fall back to scanning the whole list if no separator was supplied.
            var all = Environment.GetCommandLineArgs();
            var separator = Array.IndexOf(all, "--");
            var ours = separator >= 0 ? all.Skip(separator + 1) : all;
            return new CommandLineArgs(ours.ToArray());
        }

        sealed class CommandLineArgs
        {
            readonly string[] _args;
            public CommandLineArgs(string[] args) => _args = args;

            public string Value(string name, string fallback)
            {
                var i = Array.IndexOf(_args, name);
                return i >= 0 && i + 1 < _args.Length ? _args[i + 1] : fallback;
            }

            public bool Flag(string name) => Array.IndexOf(_args, name) >= 0;
        }
    }
}
