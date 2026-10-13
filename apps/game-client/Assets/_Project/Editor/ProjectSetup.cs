using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HappyWorld.EditorTools
{
    /// <summary>
    /// One-click setup and build so nobody has to configure Player Settings by hand.
    /// Menu: Happy World → Apply Project Settings / Build Android APK (Development).
    /// Batch: Unity -batchmode -quit -projectPath apps/game-client -executeMethod HappyWorld.EditorTools.ProjectSetup.BuildAndroidDev
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        public const string ApkPath = "Builds/Android/HappyWorldOnline-dev.apk";

        static ProjectSetup()
        {
            // First open of a fresh checkout: create the (empty) main scene and apply settings once.
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(ScenePath)) Apply();
            };
        }

        [MenuItem("Happy World/Apply Project Settings")]
        public static void Apply()
        {
            PlayerSettings.companyName = "Happy World";
            PlayerSettings.productName = "Happy World Online";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.happyworld.online");
            PlayerSettings.bundleVersion = "0.4.0";
            PlayerSettings.Android.bundleVersionCode = 4;

            // landscape only
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // Plain http:// to a dev backend works only in Development builds; release builds must use https.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.forceInternetPermission = true;

            EnsureScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[HappyWorld] Project settings applied");
        }

        /// <summary>The game builds itself at runtime (GameSession), so the scene only needs a camera and a light.</summary>
        private static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        [MenuItem("Happy World/Build Android APK (Development)")]
        public static void BuildAndroidDev()
        {
            Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            });
            var ok = report.summary.result == BuildResult.Succeeded;
            Debug.Log("[HappyWorld] Android build " + report.summary.result + " → " + ApkPath + " (" + report.summary.totalSize / (1024 * 1024) + " MB)");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
