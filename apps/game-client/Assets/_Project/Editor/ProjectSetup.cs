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
            // The world is built from code (CreatePrimitive adds Box/Sphere/MeshCollider), so no scene references
            // those engine classes and IL2CPP engine stripping removed them: "Can't add component because class
            // 'BoxCollider' doesn't exist" on device (Phase 4.5). Without colliders, tapping other players fails.
            PlayerSettings.stripEngineCode = false;

            EnsureScene();
            EnsureAlwaysIncludedShaders();
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

        /// <summary>
        /// Everything is created from code (primitives, uGUI, TextMesh), so no asset references these shaders and
        /// the build strips them: on device every surface rendered magenta (Phase 4.5). A project created without
        /// a template also starts with an empty "Always Included Shaders" list, so we fill it here.
        /// </summary>
        private static void EnsureAlwaysIncludedShaders()
        {
            string[] names =
            {
                "Legacy Shaders/Diffuse", "Unlit/Color", "Skybox/Procedural",
                "UI/Default", "Sprites/Default", "Sprites/Mask",
                "Hidden/CubeBlur", "Hidden/CubeCopy", "Hidden/CubeBlend",
                // anime avatars: their own toon shader (also in Resources) and its fallback
                "HappyWorld/AnimeToon", "Legacy Shaders/Transparent/Cutout/Diffuse",
            };
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(settings);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in names)
            {
                var shader = Shader.Find(name);
                if (shader == null) { Debug.LogWarning("[HappyWorld] Shader not found: " + name); continue; }
                var present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) present = true;
                if (present) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }
            so.ApplyModifiedProperties();
        }

        [MenuItem("Happy World/Build Android APK (Development)")]
        public static void BuildAndroidDev()
        {
            Apply();
            // Dev builds also ship x86_64 so they run natively on the Android emulator: libunity's ARM64 code
            // crashes with SIGILL under the emulator's ARM translation (seen in profiler init, Phase 4.5).
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64;
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

    /// <summary>
    /// On Windows set to the Thai region, Java defaults to the Buddhist calendar (year 25xx) and AGP fails in
    /// :launcher:mergeDebugJavaResource with VerifyException in MsDosDateTimeUtils.packDate (zip dates stop at 2107).
    /// gradle.properties jvmargs are overridden by the -Dorg.gradle.jvmargs Unity passes on the command line, and
    /// Unity does not forward JAVA_TOOL_OPTIONS, so the exported project sets a Gregorian default locale itself
    /// before any task runs (found in Phase 4.5).
    /// </summary>
    public sealed class GradleLocaleFix : UnityEditor.Android.IPostGenerateGradleAndroidProject
    {
        private const string Marker = "// HappyWorld: Gregorian locale";
        private const string Statement = "java.util.Locale.setDefault(java.util.Locale.US)";
        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            var settings = Path.Combine(Path.GetDirectoryName(unityLibraryPath), "settings.gradle");
            if (!File.Exists(settings)) { Debug.LogWarning("[HappyWorld] settings.gradle not found: " + settings); return; }
            // Unity keeps an unchanged settings.gradle between builds, so drop any earlier copy before re-adding.
            var lines = new System.Collections.Generic.List<string>(File.ReadAllLines(settings));
            lines.RemoveAll(l => l.Trim() == Marker || l.Trim() == Statement);
            var text = string.Join("\n", lines).Trim();
            // appended: Gradle requires pluginManagement {} to be the first statement in settings.gradle
            File.WriteAllText(settings, text + "\n\n" + Marker + "\n" + Statement + "\n");
        }
    }
}
