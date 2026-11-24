using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;
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
            // v0.61: passes that feed viewport input every frame (0 calls otherwise). The time is
            // the input handling itself (routing, camera update, surface query for the wheel).
            public int inputCalls, cameraMovedFrames;
            public float inputMedianMs, inputP95Ms, inputWorstMs;
        }
        // H2a-02: cost of one 1920x1080 render of the main camera, independent of the display's
        // frame cap (the windowed Player may be held at the refresh rate even with vsync off).
        [Serializable] public sealed class RenderCost
        {
            public string name;
            public int renders;
            public float msPerRender;   // the lowest of the kept rounds
            public float spreadMs;      // highest minus lowest of the kept rounds (measurement noise)
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
            // H3-02: 1080p60 MP4 of the reference scene (with the song when it sits beside the VMD).
            public float exportSeconds, exportFramesPerSecond, exportSceneSeconds;
            public int exportFrames;
            public long exportBytes;
            public string exportCodec, exportAudio, exportError;
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
            // v0.61: camera input during playback (same reference look). No selection, so no gizmo
            // under the pointer; the pointer stays in the middle of the viewport.
            ui.SelectTarget(MmdSceneWorkspace.InspectorTarget.Light);
            Activate(ui, "FrameAll");
            var centre = new Vector2(Screen.width * .5f, Screen.height * .5f);
            var pointer = centre; var phase = 0f;
            void Drag(bool left, bool right, Vector2 next)
            {
                ui.ProcessViewportInput(next, next - pointer, 0, right, false, false, left);
                pointer = next;
            }
            // Left drag: a slow circle (orbit), pressed for the whole pass.
            Drag(true, false, centre);
            yield return Measure("playback_camera_orbit_left_drag", 1, 6, passes, () =>
            {
                phase += Time.unscaledDeltaTime * 2;
                Drag(true, false, centre + new Vector2(Mathf.Sin(phase) * 160, Mathf.Sin(phase * 2) * 40));
            }, ui);
            Drag(false, false, pointer);
            // Right drag: up and down (dolly between closer and farther).
            phase = 0; Drag(false, true, centre);
            yield return Measure("playback_camera_dolly_right_drag", 1, 6, passes, () =>
            {
                phase += Time.unscaledDeltaTime * 2;
                Drag(false, true, centre + new Vector2(0, Mathf.Sin(phase) * 120));
            }, ui);
            Drag(false, false, pointer);
            Activate(ui, "FrameAll");
            // Wheel over the dancing first character: one notch in, one out, every frame (each
            // notch queries the surface under the cursor on the current pose).
            var notch = 1f; int wheelHits = 0, wheelCalls = 0;
            yield return Measure("playback_camera_wheel_to_surface", 1, 6, passes, () =>
            {
                var body = BodyScreen(ui.Scene.Objects.First(o => o.Kind == MmdSceneService.ObjectKind.Character));
                ui.ProcessViewportInput(body, Vector2.zero, notch, false, false, false);
                wheelCalls++; if (ui.LastZoomHitSurface) wheelHits++;
                notch = -notch;
            }, ui);
            notes.Add("v0.61 wheel passes hit a surface on " + wheelHits + " of " + wheelCalls + " notches.");
            Activate(ui, "FrameAll");
            // v0.61: screen correction on every object (each material slot carries a property block).
            foreach (var item in ui.Scene.Objects) ui.Scene.SetCorrection(item.Id, Corrected(item));
            yield return Measure("effects_on_screen_corrections_all_objects", 2, 10, passes);
            foreach (var item in ui.Scene.Objects) ui.Scene.SetCorrection(item.Id, new ColorCorrectionData());
            // H2a-02 / decision 9: DOF (bokeh, on the first character) + SSAO + vignette on top of the reference look.
            var rich = ui.Graphics.Settings; rich.dofEnabled = true; rich.dofTarget = ui.Scene.Objects.First().Id; rich.ssaoEnabled = true; rich.vignetteEnabled = true;
            ui.Graphics.Apply(rich);
            yield return Measure("effects_on_dof_ssao_vignette", 2, 15, passes);
            rich.effectQuality = MmdEffectQuality.High; ui.Graphics.Apply(rich);
            yield return Measure("effects_on_dof_ssao_vignette_high", 2, 15, passes);
            // Render cost per frame (paused, same frame): reference look, + DOF/SSAO/vignette, high quality, all off.
            ui.Scene.Playing = false;
            var reference = ui.Graphics.Settings; reference.dofEnabled = false; reference.ssaoEnabled = false; reference.vignetteEnabled = false; reference.effectQuality = MmdEffectQuality.Medium;
            var bare = reference.Clone(); bare.bloomEnabled = false; bare.tonemapping = false; bare.shadowQuality = MmdShadowQuality.Off;
            var focus = ui.Scene.Objects.First().Id;
            SceneGraphicsSettings Variant(SceneGraphicsSettings from, Action<SceneGraphicsSettings> change) { var next = from.Clone(); change(next); if (next.dofEnabled) next.dofTarget = focus; return next; }
            SceneGraphicsSettings Look(int i) => Variant(reference, s => MmdLookPresets.CopyLook(MmdLookPresets.BuiltIn[i].values, s));
            // Three rounds; the first only allocates targets and warms shaders. Sub-millisecond
            // renders vary with GPU clocks, so the lower of the two kept rounds is reported, with
            // the spread between them.
            var costRounds = new Dictionary<string, List<float>>(); var costOrder = new List<string>();
            for (var round = 0; round < 3; round++)
            {
                void Cost(string name, SceneGraphicsSettings settings)
                {
                    ui.Graphics.Apply(settings);
                    var cost = MeasureRender(name);
                    if (round == 0) return;
                    if (!costRounds.TryGetValue(name, out var list)) { costRounds[name] = list = new List<float>(); costOrder.Add(name); }
                    list.Add(cost.msPerRender);
                }
                Cost("reference_bloom_tonemap_shadows_medium", reference);
                // v0.61: the same frame with a screen correction on every object.
                foreach (var item in ui.Scene.Objects) ui.Scene.SetCorrection(item.Id, Corrected(item));
                Cost("reference_with_screen_corrections_all_objects", reference);
                foreach (var item in ui.Scene.Objects) ui.Scene.SetCorrection(item.Id, new ColorCorrectionData());
                Cost("plus_dof_ssao_vignette", Variant(rich, s => s.effectQuality = MmdEffectQuality.Medium));
                Cost("plus_dof_ssao_vignette_high", Variant(rich, s => s.effectQuality = MmdEffectQuality.High));
                Cost("plus_dof_ssao_vignette_low", Variant(rich, s => s.effectQuality = MmdEffectQuality.Low));
                Cost("all_effects_off_shadows_off", bare);
                // H2a-05: each built-in look preset on the reference scene.
                for (var i = 0; i < MmdLookPresets.BuiltIn.Length; i++)
                    Cost("look_" + i + "_" + new[] { "bright_stage", "evening_outdoor", "dark_club", "bright_studio" }[Math.Min(i, 3)], Look(i));
                // What a look costs, feature by feature: each one added to the reference alone, and
                // each one taken out of the heaviest look (evening outdoor: high shadows, SSAO, DOF, vignette).
                var evening = MmdLookPresets.BuiltIn[Math.Min(1, MmdLookPresets.BuiltIn.Length - 1)].values;
                Cost("reference_plus_shadows_high", Variant(reference, s => s.shadowQuality = MmdShadowQuality.High));
                Cost("reference_plus_ssao", Variant(reference, s => { s.ssaoEnabled = true; s.ssaoIntensity = evening.ssaoIntensity; s.ssaoRadius = evening.ssaoRadius; }));
                Cost("reference_plus_dof", Variant(reference, s => { s.dofEnabled = true; s.dofStrength = evening.dofStrength; }));
                Cost("reference_plus_vignette", Variant(reference, s => { s.vignetteEnabled = true; s.vignetteIntensity = evening.vignetteIntensity; }));
                Cost("look_1_minus_shadows_high", Variant(Look(1), s => s.shadowQuality = MmdShadowQuality.Medium));
                Cost("look_1_minus_ssao", Variant(Look(1), s => s.ssaoEnabled = false));
                Cost("look_1_minus_dof", Variant(Look(1), s => s.dofEnabled = false));
                Cost("look_1_minus_vignette", Variant(Look(1), s => s.vignetteEnabled = false));
                // The look as the app applies it: the preset also turns the characters toon (two passes, outline).
                var characters = ui.Scene.Objects.Where(o => o.Kind == MmdSceneService.ObjectKind.Character).ToArray();
                foreach (var item in characters) ui.Scene.SetMaterialStyle(item.Id, new MaterialStyleData { style = MaterialStyleData.Toon });
                Cost("look_1_with_toon_characters", Look(1));
                Cost("reference_with_toon_characters", reference);
                foreach (var item in characters) ui.Scene.SetMaterialStyle(item.Id, new MaterialStyleData());
            }
            report.renderCosts = costOrder.Select(name => new RenderCost
            {
                name = name, renders = 120, msPerRender = costRounds[name].Min(), spreadMs = costRounds[name].Max() - costRounds[name].Min(),
            }).ToArray();
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
            var song = Directory.GetFiles(Path.GetDirectoryName(vmd), "*.wav").FirstOrDefault();
            if (song != null) yield return ui.ApplyPickerResult(song, MmdSceneWorkspace.PickKind.Audio);
            var exportPath = Path.Combine(Path.GetDirectoryName(reportPath), "export-1080p60.mp4");
            var exportStart = Math.Min(30, ui.Scene.Duration * .2);
            ui.StartOutput(MmdSceneWorkspace.OutputFormat.Mp4, new OutputData { fps = 60 }, exportStart, exportStart + 10, exportPath);
            yield return null;
            while (ui.Busy) yield return null;
            report.exportError = ui.Error;
            report.exportSceneSeconds = 10;
            if (File.Exists(exportPath))
            {
                var info = MmdMediaWriter.Probe(exportPath);
                report.exportSeconds = (float)ui.LastOutputSeconds; report.exportFrames = (int)info.videoFrames;
                report.exportFramesPerSecond = (float)(info.videoFrames / Math.Max(1e-3, ui.LastOutputSeconds));
                report.exportBytes = new FileInfo(exportPath).Length;
                report.exportCodec = info.VideoCodecName + " " + info.width + "x" + info.height + " " + info.fpsNum + "/" + info.fpsDen;
                report.exportAudio = info.AudioCodecName + " " + info.sampleRate + " Hz " + info.channels + " ch " + (info.audioDuration / 1e7).ToString("0.000") + " s";
            }

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

        // v0.61 helpers: a toolbar button press, the first character's upper body on screen, and
        // a typical correction (object colour/brightness, plus gloss on the first PMX material).
        private static void Activate(MmdSceneWorkspace ui, string name)
        {
            var button = ui.Root.Q<UnityEngine.UIElements.Button>(name);
            if (button == null || !button.enabledInHierarchy) throw new InvalidOperationException("UI button unavailable: " + name);
            button.Focus();
            using (var evt = UnityEngine.UIElements.NavigationSubmitEvent.GetPooled()) button.SendEvent(evt);
        }
        private static Vector2 BodyScreen(MmdSceneService.SceneObject item)
        {
            var bone = item.Model.root.GetComponentsInChildren<Transform>().FirstOrDefault(part => part.name == "上半身");
            Vector3 world;
            if (bone != null) world = bone.position; else { MmdSceneWorkspace.TryBounds(new[] { item }, out var bounds); world = bounds.center; }
            var point = Camera.main.WorldToScreenPoint(world);
            return new Vector2(point.x, point.y);
        }
        private static ColorCorrectionData Corrected(MmdSceneService.SceneObject item)
        {
            var data = new ColorCorrectionData { tint = new Color(1, .95f, .9f), brightness = 1.05f };
            data.materials.Add(new MaterialCorrectionData { index = 0, name = item.StylerOrCreate.PmxName(0), tint = new Color(1, .9f, .9f), gloss = .5f });
            return data;
        }

        private static IEnumerator Measure(string name, float warmupSeconds, float seconds, List<Pass> passes, Action perFrame = null, MmdSceneWorkspace ui = null)
        {
            var input = new List<float>(4096); var moved = 0;
            var inputClock = new Stopwatch();
            void Feed()
            {
                if (perFrame == null) return;
                var camera = Camera.main; var before = (camera.transform.position, camera.transform.rotation);
                inputClock.Restart(); perFrame(); inputClock.Stop();
                input.Add((float)inputClock.Elapsed.TotalMilliseconds);
                if ((camera.transform.position, camera.transform.rotation) != before) moved++;
            }
            var warm = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - warm < warmupSeconds) { yield return null; Feed(); }
            input.Clear(); moved = 0;
            var frames = new List<float>(4096);
            var gen0 = GC.CollectionCount(0);
            var managedStart = GC.GetTotalMemory(false);
            var start = Time.realtimeSinceStartup; var last = start;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return null;
                var now = Time.realtimeSinceStartup; frames.Add((now - last) * 1000f); last = now;
                Feed();
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
            if (input.Count > 0)
            {
                var inputSorted = input.OrderBy(value => value).ToArray();
                pass.inputCalls = input.Count; pass.cameraMovedFrames = moved;
                pass.inputMedianMs = inputSorted[inputSorted.Length / 2];
                pass.inputP95Ms = inputSorted[Mathf.Clamp(Mathf.CeilToInt(.95f * inputSorted.Length) - 1, 0, inputSorted.Length - 1)];
                pass.inputWorstMs = inputSorted.Last();
            }
            passes.Add(pass);
            Debug.Log("[perf-probe] " + name + " avg " + pass.averageFps.ToString("F1") + " fps, p95 " + pass.p95FrameMs.ToString("F2") + " ms, worst " + pass.worstFrameMs.ToString("F1") + " ms" +
                (input.Count > 0 ? ", input median " + pass.inputMedianMs.ToString("F3") + " ms worst " + pass.inputWorstMs.ToString("F2") + " ms, camera moved " + moved + "/" + input.Count : ""));
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
