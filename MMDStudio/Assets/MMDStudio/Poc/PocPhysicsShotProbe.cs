using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MmdStudio.Poc
{
    // PH-03 visual check: in the real scene workspace, loads both characters with the full motion,
    // turns experimental physics on, bakes up to the latest shot time, then captures each
    // character with physics OFF and ON at the same time and camera (UI hidden).
    public sealed class PocPhysicsShotProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, backend;
            public string[] shots;
            public float[] maxDifferenceM;
        }

        public IEnumerator Run(MmdSceneWorkspace ui, string directory)
        {
            var report = new Report
            {
#if ENABLE_IL2CPP
                backend = "IL2CPP",
#else
                backend = "Mono",
#endif
            };
            var shots = new List<string>(); var differences = new List<float>();
            Directory.CreateDirectory(directory);
            var reportPath = Path.Combine(directory, "physics-shots.json");
            ui.Scene.AutomaticClock = false; ui.DeviceInput = false; ui.SuppressQuitPrompt = true;
            yield return null;
            var vmd = PocArguments.Value("-pocVmd");
            var plan = new[]
            {
                (pmx: PocArguments.Value("-pocPmx"), label: "gene", times: new[] { 8f, 14.22f }),
                (pmx: PocArguments.Value("-pocSecondPmx"), label: "sandrone", times: new[] { 8f, 16.4f }),
            };
            var ids = new List<string>();
            foreach (var entry in plan)
            {
                yield return ui.ApplyPickerResult(entry.pmx, true);
                yield return ui.ApplyPickerResult(vmd, false);
                var item = ui.Scene.Objects.FirstOrDefault(o => o.Id == ui.SelectedId);
                if (item == null || item.Motion == null || ui.Error.Length > 0) { Finish(reportPath, report, shots, differences, "load failed: " + ui.Error); yield break; }
                ids.Add(item.Id);
            }
            try { foreach (var id in ids) ui.Scene.SetPhysics(id, true); }
            catch (Exception ex) { Finish(reportPath, report, shots, differences, ex.ToString()); yield break; }
            var latest = plan.SelectMany(p => p.times).Max() + .1f;
            for (var i = 0; i < 20000 && ui.Scene.Objects.Any(o => o.Bake != null && o.Bake.BakedSeconds < latest); i++)
            {
                ui.Scene.BakePhysicsSlice();
                if (i % 2 == 0) yield return null;
            }
            ui.SetFocusView(true);
            for (var c = 0; c < plan.Length; c++)
            {
                var item = ui.Scene.Objects.First(o => o.Id == ids[c]);
                ui.Select(item.Id);
                foreach (var time in plan[c].times)
                {
                    ui.Scene.Seek(time);
                    ui.FrameIds(item.Id);
                    yield return null;
                    var on = item.Model.bones.Select(b => b.position).ToArray();
                    yield return Capture(Path.Combine(directory, plan[c].label + "_" + time.ToString("0.00") + "s_on.png"), shots);
                    // Physics-OFF pose at the same time and camera.
                    item.Motion.SampleFromSceneClock(ui.Scene, time);
                    var off = item.Model.bones.Select(b => b.position).ToArray();
                    differences.Add(on.Zip(off, (x, y) => Vector3.Distance(x, y)).Max());
                    yield return Capture(Path.Combine(directory, plan[c].label + "_" + time.ToString("0.00") + "s_off.png"), shots);
                    ui.Scene.Seek(time);
                }
            }
            Finish(reportPath, report, shots, differences, null);
        }

        private static IEnumerator Capture(string path, List<string> shots)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Destroy(texture);
            shots.Add(path);
        }

        private static void Finish(string path, Report report, List<string> shots, List<float> differences, string error)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? "";
            report.shots = shots.ToArray(); report.maxDifferenceM = differences.ToArray();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
