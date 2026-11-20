using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H1-01 repeated cancel/retry check: in one Player process, opens the given .mmdscene,
    // then repeats N cycles of "start opening, cancel during entry k" followed by a full open.
    // After every cycle it counts the Unity objects a load creates and samples managed/native
    // memory. Object counts must be identical every cycle (nothing accumulates); memory growth
    // after the first cycle is reported and must stay under a small bound.
    public sealed class PocSceneCycleProbe : MonoBehaviour
    {
        [Serializable] private sealed class Sample
        {
            public int cycle, cancelAtEntry;
            public bool cancelKeptScene, fullOpenOk;
            public string cancelStage;
            public int meshes, materials, textures, gameObjects, scriptableObjects, lifetimes, candidates;
            public long monoUsedBytes, unityAllocatedBytes, unityReservedBytes;
            public float openSeconds;
        }
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, backend, configuration, sceneFile;
            public int cycles, entries;
            public bool objectCountsStable;
            public double allocatedGrowthMiB, reservedGrowthMiB, monoGrowthMiB;
            public Sample[] samples;
        }

        private const double GrowthLimitMiB = 64;
        // Gene's 13 materials are built inside one frame, so "재질" is targeted on Sandrone only.
        private static readonly (int entry, string stage)[] Targets =
        {
            (1, "PMX 파일"), (2, "텍스처"), (1, "이름 정리"), (2, "VMD"), (3, "텍스처"),
            (1, "VMD"), (2, "이름 정리"), (2, "재질"), (1, "텍스처"), (3, "PMX"),
        };

        public IEnumerator Run(MmdSceneWorkspace ui, string reportPath, string sceneFile)
        {
            var report = new Report
            {
                sceneFile = sceneFile,
#if ENABLE_IL2CPP
                backend = "IL2CPP",
#else
                backend = "Mono",
#endif
                configuration = Debug.isDebugBuild ? "Debug" : "Release",
            };
            var samples = new List<Sample>();
            int.TryParse(PocArguments.Value("-pocCycles") ?? "10", out var cycles);
            report.cycles = cycles = Math.Max(2, cycles);
            ui.SuppressQuitPrompt = true; ui.DeviceInput = false; ui.Scene.AutomaticClock = false;
            yield return null;
            int entries;
            try { entries = report.entries = MmdSceneDocument.Read(sceneFile).sceneObjects.Count; }
            catch (Exception ex) { Finish(reportPath, report, samples, ex.ToString()); yield break; }
            yield return ui.OpenFrom(sceneFile);
            if (ui.Error.Length > 0 || ui.Scene.Objects.Count != entries) { Finish(reportPath, report, samples, "initial open failed: " + ui.Error); yield break; }
            var label = ui.Root.Q<Label>("LoadingLabel");
            for (var cycle = 1; cycle <= cycles; cycle++)
            {
                // Cancel targets cycle through entries and loading stages (PMX read/rename,
                // textures, materials, VMD) so every cancellable step is hit repeatedly.
                var target = Targets[(cycle - 1) % Targets.Length];
                var sample = new Sample { cycle = cycle, cancelAtEntry = Math.Min(target.entry, entries) };
                // Cancel part way: at a different entry each cycle.
                var before = ui.Scene.Objects.ToArray();
                ui.StartCoroutine(ui.OpenFrom(sceneFile));
                var marker = "장면 불러오는 중 (" + sample.cancelAtEntry + "/";
                var waited = 0f;
                while (!(ui.Busy && label.text.Contains(marker) && label.text.Contains(target.stage)) && waited < 60) { waited += Time.unscaledDeltaTime; yield return null; }
                sample.cancelStage = ui.Busy ? label.text : "(finished before cancel)";
                var cancel = ui.Root.Q<Button>("CancelLoading");
                if (!ui.Busy || cancel == null || !cancel.enabledInHierarchy) { Finish(reportPath, report, samples, "cycle " + cycle + ": could not cancel at entry " + sample.cancelAtEntry); yield break; }
                cancel.Focus();
                using (var evt = NavigationSubmitEvent.GetPooled()) cancel.SendEvent(evt);
                while (ui.Busy) yield return null;
                yield return null;
                sample.cancelKeptScene = ui.Scene.Objects.SequenceEqual(before) && ui.Error.Length == 0 && ui.Scene.CandidateCount == 0;
                // Full open replaces the scene.
                yield return ui.OpenFrom(sceneFile);
                sample.fullOpenOk = ui.Error.Length == 0 && ui.Scene.Objects.Count == entries && ui.Scene.Objects.All(item => !before.Contains(item));
                sample.openSeconds = ui.SceneOpenSeconds;
                yield return null; yield return null; // deferred Destroy of the replaced scene
                var unload = Resources.UnloadUnusedAssets();
                while (!unload.isDone) yield return null;
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                yield return null;
                sample.meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
                sample.materials = Resources.FindObjectsOfTypeAll<Material>().Length;
                sample.textures = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
                sample.gameObjects = Resources.FindObjectsOfTypeAll<GameObject>().Length;
                sample.scriptableObjects = Resources.FindObjectsOfTypeAll<ScriptableObject>().Length;
                sample.lifetimes = Resources.FindObjectsOfTypeAll<PocAssetLifetime>().Length;
                sample.candidates = ui.Scene.CandidateCount;
                sample.monoUsedBytes = Profiler.GetMonoUsedSizeLong();
                sample.unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
                sample.unityReservedBytes = Profiler.GetTotalReservedMemoryLong();
                samples.Add(sample);
                Debug.Log("[scene-cycle] " + JsonUtility.ToJson(sample));
            }
            var first = samples[0]; var last = samples[samples.Count - 1];
            report.objectCountsStable = samples.All(s => s.meshes == first.meshes && s.materials == first.materials && s.textures == first.textures &&
                s.gameObjects == first.gameObjects && s.scriptableObjects == first.scriptableObjects && s.lifetimes == first.lifetimes && s.candidates == 0);
            const double MiB = 1024.0 * 1024.0;
            report.allocatedGrowthMiB = (last.unityAllocatedBytes - first.unityAllocatedBytes) / MiB;
            report.reservedGrowthMiB = (last.unityReservedBytes - first.unityReservedBytes) / MiB;
            report.monoGrowthMiB = (last.monoUsedBytes - first.monoUsedBytes) / MiB;
            string error = null;
            if (!samples.All(s => s.cancelKeptScene && s.fullOpenOk)) error = "a cancel did not keep the scene or a full open failed";
            else if (!report.objectCountsStable) error = "object counts changed between cycles";
            else if (report.allocatedGrowthMiB > GrowthLimitMiB) error = "Unity allocated memory grew by " + report.allocatedGrowthMiB.ToString("0.0") + " MiB";
            Finish(reportPath, report, samples, error);
        }

        private static void Finish(string path, Report report, List<Sample> samples, string error)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? ""; report.samples = samples.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
