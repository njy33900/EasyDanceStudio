using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace MmdStudio.Poc
{
    // MVP-09 integration/performance measurement (spec NFR-03/04, 8.2): reference scene of two
    // characters with full-length VMD, one stage and basic effects, played on the shared clock
    // in the actual Player. Records import/save/open times, frame times and memory. The 30 FPS
    // MVP target is reported, not enforced: a slower machine produces a valid report.
    public sealed class PocScenePerformanceProbe : MonoBehaviour
    {
        [Serializable] public sealed class Pass
        {
            public string name;
            public int frames;
            public float seconds, averageFps, medianFrameMs, p95FrameMs, p99FrameMs, worstFrameMs, onePercentLowFps;
            public int gen0Collections;
            public long managedBytesStart, managedBytesEnd;
            public bool meetsThirtyFpsTarget;
        }
        // H2a-02: cost of one 1920x1080 render of the main camera, independent of the display's
        // frame cap (the windowed Player may be held at the refresh rate even with vsync off).
        [Serializable] public sealed class RenderCost
        {
            public string name;
            public int renders;
            public float msPerRender;
        }
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, backend, configuration, device, gpu, cpu, os, unity;
            public int systemMemoryMb, graphicsMemoryMb, screenWidth, screenHeight, vSyncCount, targetFrameRate;
            public float importFirstCharacterSeconds, importSecondCharacterSeconds, connectMotionsSeconds, importStageSeconds, saveSeconds, openSceneSeconds;
            public double motionDurationSeconds;
            public int objects;
            public long allocatedBytes, reservedBytes, monoUsedBytes, workingSetBytes;
            public Pass[] passes;
            public RenderCost[] renderCosts;
            public string[] notes;
        }

        public IEnumerator Run(MmdSceneWorkspace ui, string reportPath)
        {
            var report = new Report
            {
#if ENABLE_IL2CPP
                backend = "IL2CPP",
#else
                backend = "Mono",
#endif
                configuration = Debug.isDebugBuild ? "Debug" : "Release",
                device = SystemInfo.deviceModel, gpu = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
                cpu = SystemInfo.processorType + " (" + SystemInfo.processorCount + " threads)", os = SystemInfo.operatingSystem,
                unity = Application.unityVersion, systemMemoryMb = SystemInfo.systemMemorySize, graphicsMemoryMb = SystemInfo.graphicsMemorySize,
            };
            var notes = new List<string>();
            ui.SuppressQuitPrompt = true; ui.DeviceInput = false;
            // Measure uncapped: vsync would cap at the display refresh rate and hide headroom.
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            report.vSyncCount = QualitySettings.vSyncCount; report.targetFrameRate = Application.targetFrameRate;
            yield return null; yield return null;
            report.screenWidth = Screen.width; report.screenHeight = Screen.height;
            if (Screen.width < 1920 || Screen.height < 1080) notes.Add("Window is smaller than 1920x1080 (" + Screen.width + "x" + Screen.height + "); the display may not fit a 1080p window.");

            var vmd = PocArguments.Value("-pocVmd");
            var stopwatch = Stopwatch.StartNew();
            yield return ui.ApplyPickerResult(PocArguments.Value("-pocPmx"), MmdSceneWorkspace.PickKind.Character);
            report.importFirstCharacterSeconds = (float)stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();
            yield return ui.ApplyPickerResult(PocArguments.Value("-pocSecondPmx"), MmdSceneWorkspace.PickKind.Character);
            report.importSecondCharacterSeconds = (float)stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();
            foreach (var item in ui.Scene.Objects.ToArray())
            {
                ui.Select(item.Id);
                yield return ui.ApplyPickerResult(vmd, MmdSceneWorkspace.PickKind.Motion);
            }
            report.connectMotionsSeconds = (float)stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();
            yield return ui.ApplyPickerResult(PocArguments.Value("-pocStagePmx"), MmdSceneWorkspace.PickKind.Stage);
            report.importStageSeconds = (float)stopwatch.Elapsed.TotalSeconds;
            if (ui.Error.Length > 0 || ui.Scene.Objects.Count != 3 || ui.Scene.Objects.Count(item => item.Motion != null) != 2)
            {
                Finish(reportPath, report, notes, "Reference scene could not be built: " + ui.Error); yield break;
            }

            // Reference look: basic effects (Bloom, tonemapping, medium soft shadows).
            ui.ApplyGraphics(new SceneGraphicsSettings { bloomEnabled = true, tonemapping = true, shadowQuality = MmdShadowQuality.Medium });
            var sceneFile = Path.Combine(Path.GetDirectoryName(reportPath), "perf-scene.mmdscene");
            stopwatch.Restart();
            ui.SaveTo(sceneFile);
            report.saveSeconds = (float)stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();
            yield return ui.OpenFrom(sceneFile);
            report.openSceneSeconds = (float)stopwatch.Elapsed.TotalSeconds;
            if (ui.Error.Length > 0 || ui.Scene.Objects.Count != 3 || ui.LastLoadProblems.Length > 0)
            {
                Finish(reportPath, report, notes, "Saved reference scene did not reopen: " + ui.Error); yield break;
            }
            report.objects = ui.Scene.Objects.Count;
            report.motionDurationSeconds = ui.Scene.Duration;
            ui.Select(ui.Scene.Objects.First().Id);
            ui.Graphics.Apply(ui.Graphics.Settings);

            var passes = new List<Pass>();
            ui.Scene.Loop = true; ui.Scene.Speed = 1; ui.Scene.AutomaticClock = true;
            ui.Scene.Seek(Math.Min(30, ui.Scene.Duration * .2)); ui.Scene.Playing = true;
            yield return Measure("effects_on_shadows_medium", 3, 20, passes);
            // H2a-02 / decision 9: DOF (bokeh, on the first character) + SSAO + vignette on top of the reference look.
            var rich = ui.Graphics.Settings; rich.dofEnabled = true; rich.dofTarget = ui.Scene.Objects.First().Id; rich.ssaoEnabled = true; rich.vignetteEnabled = true;
            ui.Graphics.Apply(rich);
            yield return Measure("effects_on_dof_ssao_vignette", 2, 15, passes);
            rich.effectQuality = MmdEffectQuality.High; ui.Graphics.Apply(rich);
            yield return Measure("effects_on_dof_ssao_vignette_high", 2, 15, passes);
            // Render cost per frame (paused, same frame): reference look, + DOF/SSAO/vignette, high quality, all off.
            ui.Scene.Playing = false;
            var costs = new List<RenderCost>();
            var reference = ui.Graphics.Settings; reference.dofEnabled = false; reference.ssaoEnabled = false; reference.vignetteEnabled = false; reference.effectQuality = MmdEffectQuality.Medium;
            var bare = reference.Clone(); bare.bloomEnabled = false; bare.tonemapping = false; bare.shadowQuality = MmdShadowQuality.Off;
            // Two rounds; only the second is kept (the first allocates targets and warms shaders).
            for (var round = 0; round < 2; round++)
            {
                costs.Clear();
                ui.Graphics.Apply(reference); costs.Add(MeasureRender("reference_bloom_tonemap_shadows_medium"));
                rich.effectQuality = MmdEffectQuality.Medium; ui.Graphics.Apply(rich); costs.Add(MeasureRender("plus_dof_ssao_vignette"));
                rich.effectQuality = MmdEffectQuality.High; ui.Graphics.Apply(rich); costs.Add(MeasureRender("plus_dof_ssao_vignette_high"));
                rich.effectQuality = MmdEffectQuality.Low; ui.Graphics.Apply(rich); costs.Add(MeasureRender("plus_dof_ssao_vignette_low"));
                ui.Graphics.Apply(bare); costs.Add(MeasureRender("all_effects_off_shadows_off"));
            }
            report.renderCosts = costs.ToArray();
            ui.Graphics.Apply(reference);
            ui.Scene.Playing = true;
            var plain = ui.Graphics.Settings; plain.bloomEnabled = false; plain.tonemapping = false; plain.shadowQuality = MmdShadowQuality.Off;
            ui.Graphics.Apply(plain);
            yield return Measure("effects_off_shadows_off", 2, 15, passes);
            var high = ui.Graphics.Settings; high.bloomEnabled = true; high.tonemapping = true; high.shadowQuality = MmdShadowQuality.High;
            ui.Graphics.Apply(high);
            yield return Measure("effects_on_shadows_high", 2, 15, passes);
            ui.Scene.Playing = false;
            report.passes = passes.ToArray();

            report.allocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
            report.reservedBytes = Profiler.GetTotalReservedMemoryLong();
            report.monoUsedBytes = Profiler.GetMonoUsedSizeLong();
            report.workingSetBytes = Environment.WorkingSet;
            if (report.workingSetBytes <= 0) notes.Add("Process working set is not reported by this runtime.");
            if (!Debug.isDebugBuild) notes.Add("Release build.");
            else notes.Add("Development build: timings include development overhead; use the Release report for the target.");
            Finish(reportPath, report, notes, null);
        }

        private static RenderCost MeasureRender(string name)
        {
            const int width = 1920, height = 1080, renders = 120;
            var camera = Camera.main;
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = texture;
                void Sync() { RenderTexture.active = texture; probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); probe.Apply(); }
                for (var i = 0; i < 5; i++) camera.Render();
                Sync();
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < renders; i++) camera.Render();
                Sync(); // waits for the GPU to finish the queued renders
                var cost = new RenderCost { name = name, renders = renders, msPerRender = (float)(watch.Elapsed.TotalMilliseconds / renders) };
                Debug.Log("[perf-probe] render " + name + " " + cost.msPerRender.ToString("F2") + " ms");
                return cost;
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                texture.Release(); Destroy(texture); Destroy(probe);
            }
        }

        private static IEnumerator Measure(string name, float warmupSeconds, float seconds, List<Pass> passes)
        {
            var warm = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - warm < warmupSeconds) yield return null;
            var frames = new List<float>(4096);
            var gen0 = GC.CollectionCount(0);
            var managedStart = GC.GetTotalMemory(false);
            var start = Time.realtimeSinceStartup; var last = start;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return null;
                var now = Time.realtimeSinceStartup; frames.Add((now - last) * 1000f); last = now;
            }
            var sorted = frames.OrderBy(value => value).ToArray();
            float Percentile(float p) => sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Length) - 1, 0, sorted.Length - 1)];
            var total = Time.realtimeSinceStartup - start;
            var worstOnePercent = sorted.Skip((int)(sorted.Length * .99f)).DefaultIfEmpty(sorted.Last()).Average();
            var pass = new Pass
            {
                name = name, frames = frames.Count, seconds = total, averageFps = frames.Count / total,
                medianFrameMs = Percentile(.5f), p95FrameMs = Percentile(.95f), p99FrameMs = Percentile(.99f), worstFrameMs = sorted.Last(),
                onePercentLowFps = 1000f / worstOnePercent, gen0Collections = GC.CollectionCount(0) - gen0,
                managedBytesStart = managedStart, managedBytesEnd = GC.GetTotalMemory(false),
            };
            pass.meetsThirtyFpsTarget = pass.averageFps >= 30;
            passes.Add(pass);
            Debug.Log("[perf-probe] " + name + " avg " + pass.averageFps.ToString("F1") + " fps, p95 " + pass.p95FrameMs.ToString("F2") + " ms, worst " + pass.worstFrameMs.ToString("F1") + " ms");
        }

        private static void Finish(string path, Report report, List<string> notes, string error)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? ""; report.notes = notes.ToArray();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
