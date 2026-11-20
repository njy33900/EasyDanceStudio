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
            // -pocSpringShots: PH-05 spring check instead of physics. Sandrone's chest roots are
            // forced to take the "strong" spring (test only; its physics stays off).
            var springMode = PocArguments.Has("-pocSpringShots");
            try
            {
                if (!springMode) foreach (var id in ids) ui.Scene.SetPhysics(id, true);
                else
                {
                    var target = ui.Scene.Objects.First(o => o.Id == ids[1]);
                    target.SpringForceForTest = true;
                    ui.Scene.SetSpring(target.Id, "strong");
                }
            }
            catch (Exception ex) { Finish(reportPath, report, shots, differences, ex.ToString()); yield break; }
            var latest = plan.SelectMany(p => p.times).Max() + .1f;
            for (var i = 0; i < 20000 && ui.Scene.Objects.Any(o => (o.Bake != null && o.Bake.BakedSeconds < latest) || (o.SpringBake != null && !o.SpringBake.Complete)); i++)
            {
                ui.Scene.BakePhysicsSlice();
                if (i % 2 == 0) yield return null;
            }
            ui.SetFocusView(true);
            if (springMode)
            {
                yield return SpringCloseUps(ui, ids[1], directory, shots, differences);
                Finish(reportPath, report, shots, differences, null);
                yield break;
            }
            for (var c = springMode ? 1 : 0; c < plan.Length; c++)
            {
                var item = ui.Scene.Objects.First(o => o.Id == ids[c]);
                ui.Select(item.Id);
                foreach (var time in plan[c].times)
                {
                    ui.Scene.Seek(time);
                    ui.FrameIds(item.Id);
                    yield return null;
                    var on = item.Model.bones.Select(b => b.position).ToArray();
                    var prefix = (springMode ? "spring_" : "") + plan[c].label + "_" + time.ToString("0.00") + "s";
                    yield return Capture(Path.Combine(directory, prefix + "_on.png"), shots);
                    // Physics-OFF pose at the same time and camera.
                    item.Motion.SampleFromSceneClock(ui.Scene, time);
                    var off = item.Model.bones.Select(b => b.position).ToArray();
                    differences.Add(on.Zip(off, (x, y) => Vector3.Distance(x, y)).Max());
                    yield return Capture(Path.Combine(directory, prefix + "_off.png"), shots);
                    ui.Scene.Seek(time);
                }
            }
            Finish(reportPath, report, shots, differences, null);
        }

        // PH-05 close-ups: camera in front of the chest (from the spine-to-chest direction), at the
        // moment of the largest spring swing in the first 60 s and at 16.4 s; spring OFF, medium,
        // strong at the same camera.
        private static IEnumerator SpringCloseUps(MmdSceneWorkspace ui, string id, string directory, List<string> shots, List<float> differences)
        {
            var item = ui.Scene.Objects.First(o => o.Id == id);
            var roots = item.SpringCandidates.Where(c => c.Weighted).Select(c => item.Model.bones[c.Bone]).ToArray();
            var times = new List<float>();
            foreach (var preset in new[] { "medium", "strong" })
            {
                ui.Scene.SetSpring(id, preset);
                for (var i = 0; i < 20000 && !item.SpringReady; i++) { ui.Scene.BakePhysicsSlice(); if (i % 2 == 0) yield return null; }
                if (times.Count == 0)
                {
                    // Largest swing of the chest roots (spring vs OFF local rotation) in the first 60 s.
                    var best = 0f; var bestTime = 16.4f;
                    for (var t = 1f; t < Mathf.Min(60, item.Motion.Summary.durationSeconds); t += 1f / 15)
                    {
                        item.Motion.SampleFromSceneClock(ui.Scene, t);
                        var off = roots.Select(r => r.localRotation).ToArray();
                        item.SpringBake.Apply(t);
                        var swing = roots.Select((r, k) => Quaternion.Angle(off[k], r.localRotation)).Sum();
                        if (swing > best) { best = swing; bestTime = t; }
                    }
                    times.Add(bestTime); times.Add(16.4f);
                    Debug.Log("[spring-closeup] largest swing " + best.ToString("0.0") + " deg at " + bestTime.ToString("0.00") + " s");
                }
                foreach (var time in times)
                {
                    ui.Scene.Seek(time);
                    var mid = roots.Aggregate(Vector3.zero, (a, r) => a + r.position) / roots.Length;
                    var spine = roots[0].parent != null ? roots[0].parent.position : item.Placement.position;
                    var front = mid - spine; front.y = 0;
                    if (front.sqrMagnitude < 1e-6f) front = Vector3.forward;
                    front.Normalize();
                    var yaw = Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg;
                    ui.View.Set(mid, yaw, 5, .55f); ui.View.Apply(Camera.main);
                    yield return null;
                    var stamp = time.ToString("0.00") + "s";
                    var on = item.Model.bones.Select(b => b.position).ToArray();
                    yield return Capture(Path.Combine(directory, "spring_closeup_" + stamp + "_" + preset + ".png"), shots);
                    item.Motion.SampleFromSceneClock(ui.Scene, time);
                    var offPose = item.Model.bones.Select(b => b.position).ToArray();
                    differences.Add(on.Zip(offPose, (x, y) => Vector3.Distance(x, y)).Max());
                    if (preset == "medium") yield return Capture(Path.Combine(directory, "spring_closeup_" + stamp + "_off.png"), shots);
                    ui.Scene.Seek(time);
                }
            }
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
