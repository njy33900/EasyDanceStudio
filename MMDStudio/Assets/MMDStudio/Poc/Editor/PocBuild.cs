using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MmdStudio.Poc
{
    public static class PocBuild
    {
        [Serializable] private sealed class BuildInfo
        {
            public string configuration, backend;
            public bool developmentBuild;
        }
        public static void BuildWindows()
        {
            var output = PocArguments.Value("-pocOutput");
            if (string.IsNullOrWhiteSpace(output) || !Path.IsPathRooted(output))
                throw new ArgumentException("-pocOutput must specify an absolute EXE path.");

            if (GraphicsSettings.defaultRenderPipeline == null)
                throw new InvalidOperationException("A URP asset must be assigned before building.");

            const string scenePath = "Assets/MMDStudio/Poc/PocSmoke.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 1, -4);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.10f, 0.14f);
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(35, -30, 0);
            light.intensity = 2;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = new Vector3(0, 1, 0);
            cube.transform.rotation = Quaternion.Euler(15, 30, 0);
            new GameObject("Player Smoke Probe").AddComponent<PocSmoke>();

            if (!EditorSceneManager.SaveScene(scene, scenePath))
                throw new IOException("Could not save the PoC scene.");

            PlayerSettings.companyName = "MmdStudio";
            PlayerSettings.productName = "MmdStudioPoC";
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            // The external model does not exist in Assets at build time. Keep the
            // fallback shader and its variants available for runtime materials.
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader is unavailable.");
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
            var included = false;
            for (var i = 0; i < shaders.arraySize; i++)
                included |= shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader;
            if (!included)
            {
                shaders.InsertArrayElementAtIndex(shaders.arraySize);
                shaders.GetArrayElementAtIndex(shaders.arraySize - 1).objectReferenceValue = shader;
                graphics.ApplyModifiedPropertiesWithoutUndo();
            }
            var backendName = PocArguments.Value("-pocBackend") ?? "mono";
            var configuration = (PocArguments.Value("-pocConfiguration") ?? "debug").ToLowerInvariant();
            if (configuration != "debug" && configuration != "release")
                throw new ArgumentException("-pocConfiguration must be debug or release.");
            if (backendName != "mono" && backendName != "il2cpp")
                throw new ArgumentException("-pocBackend must be mono or il2cpp.");
            var previousBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            try
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,
                    backendName == "il2cpp" ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = configuration == "debug" ? BuildOptions.Development : BuildOptions.None
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Player build failed: " + report.summary.result);
            }
            finally { PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, previousBackend); }
            CopyThirdPartyNotices(Path.GetDirectoryName(output));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "build-info.json"),
                JsonUtility.ToJson(new BuildInfo { configuration = configuration, backend = backendName, developmentBuild = configuration == "debug" }, true));
            Debug.Log("POC_BUILD_SUCCESS " + configuration + " " + backendName + " " + output);
        }

        private static void CopyThirdPartyNotices(string outputDirectory)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UMT.PMXImporter).Assembly);
            if (package == null) throw new InvalidOperationException("UMT package provenance is missing.");
            foreach (var source in Directory.GetFiles(package.resolvedPath, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(source);
                if (!(name == "LICENSE.md" || name == "Third Party Notices.md" ||
                      name == "COPYING" || name == "AUTHORS")) continue;
                var relative = source.Substring(package.resolvedPath.Length).TrimStart('/', '\\');
                var destination = Path.Combine(outputDirectory, "ThirdPartyNotices", "UnityMMDTools", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(source, destination, true);
            }
            var demoNotices = Path.Combine(outputDirectory, "ThirdPartyNotices", "UMTDemo");
            Directory.CreateDirectory(demoNotices);
            foreach (var name in new[] { "LICENSE.md", "PROVENANCE.md" })
                File.Copy(Path.Combine("Assets/MMDStudio/ThirdParty/UMTDemo", name), Path.Combine(demoNotices, name), true);
        }
    }
}
