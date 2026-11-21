using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MmdStudio.Poc
{
    // MVP-07 restart check: a fresh Player process opens the .mmdscene written by the UI
    // probe and compares the live restored state with the file (spec 8.2: save, relaunch,
    // reload restores placement, motion and settings).
    public sealed class PocSceneRestoreProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, backend, sceneFile, screenshot;
            public int objects;
            public string[] checks;
        }
        private readonly List<string> checks = new List<string>();
        private void Check(string name, bool passed) { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }

        public IEnumerator Run(MmdSceneWorkspace ui, string reportPath, string sceneFile)
        {
            var report = new Report { sceneFile = sceneFile };
#if ENABLE_IL2CPP
            report.backend = "IL2CPP";
#else
            report.backend = "Mono";
#endif
            ui.SuppressQuitPrompt = true; ui.DeviceInput = false; ui.Scene.AutomaticClock = false;
            yield return null;
            try { Check("fresh_process_starts_empty_and_clean", ui.Scene.Objects.Count == 0 && !ui.Dirty && ui.ProjectPath == null); }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(sceneFile);
            try
            {
                var file = MmdSceneDocument.Read(sceneFile);
                report.objects = ui.Scene.Objects.Count;
                Check("scene_file_opened_without_errors", ui.Error.Length == 0 && ui.LastLoadProblems.Length == 0 && !ui.Dirty &&
                    string.Equals(ui.ProjectPath, Path.GetFullPath(sceneFile), StringComparison.OrdinalIgnoreCase));
                Check("all_objects_restored_in_order", ui.Scene.Objects.Count == file.sceneObjects.Count && file.sceneObjects.Count >= 3);
                for (var i = 0; i < file.sceneObjects.Count; i++)
                {
                    var data = file.sceneObjects[i]; var live = ui.Scene.Objects[i];
                    var kind = data.type == SceneObjectData.StageType ? MmdSceneService.ObjectKind.Stage : MmdSceneService.ObjectKind.Character;
                    var motion = string.IsNullOrEmpty(data.motionPath) ? null : MmdSceneDocument.Resolve(sceneFile, data.motionPath);
                    if (live.Kind != kind || !SamePath(live.ModelSource, MmdSceneDocument.Resolve(sceneFile, data.assetPath)) ||
                        !(motion == null ? live.MotionSource == null : SamePath(live.MotionSource, motion)) ||
                        (live.Placement.localPosition - data.position).magnitude > 1e-4f ||
                        Quaternion.Angle(live.Placement.localRotation, Quaternion.Euler(data.rotationEuler)) > .01f ||
                        (live.Placement.localScale - data.scale).magnitude > 1e-4f || live.Visible != data.visible)
                        throw new InvalidOperationException("object_" + i + "_matches_file");
                }
                checks.Add("objects_match_kind_paths_motion_placement_visibility");
                Check("graphics_settings_restored_exactly", JsonUtility.ToJson(ui.Graphics.Settings) == JsonUtility.ToJson(file.graphics) &&
                    Math.Abs(Camera.main.fieldOfView - file.graphics.fieldOfView) < 1e-3f);
                Check("view_restored", (ui.View.Pivot - file.view.pivot).magnitude < 1e-4f && Math.Abs(Mathf.DeltaAngle(ui.View.Yaw, file.view.yaw)) < 1e-3f &&
                    Math.Abs(ui.View.Pitch - file.view.pitch) < 1e-3f && Math.Abs(ui.View.Distance - file.view.distance) < 1e-3f);
                Check("timeline_restored_paused", ui.Scene.Loop == file.timeline.loop && Math.Abs(ui.Scene.Speed - file.timeline.speed) < 1e-9 &&
                    Math.Abs(ui.Scene.Seconds - Math.Min(file.timeline.timeSeconds, ui.Scene.Duration)) < 1e-6 && !ui.Scene.Playing);
                // H2a-03: the UI probe saves the scene with a WAV (volume 80%).
                Check("audio_restored_with_volume", !string.IsNullOrEmpty(file.audio.path) && ui.Scene.Audio != null && ui.UnresolvedAudioPath == null &&
                    SamePath(ui.Scene.Audio.Source, MmdSceneDocument.Resolve(sceneFile, file.audio.path)) &&
                    ui.Scene.AudioVolume == file.audio.volume && ui.Scene.AudioMuted == file.audio.muted);
                Check("characters_sampled_at_restored_time", ui.Scene.Objects.Where(item => item.Motion != null).All(item => Math.Abs(item.Motion.TimeSeconds - (float)ui.Scene.Seconds) < 1e-4f));
                report.screenshot = Path.Combine(Path.GetDirectoryName(reportPath), "scene-restore.png");
                ScreenCapture.CaptureScreenshot(report.screenshot);
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }
            yield return new WaitForSecondsRealtime(.3f);
            try
            {
                Check("restored_scene_screenshot_created", File.Exists(report.screenshot));
                Finish(reportPath, report, null);
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); }
        }
        private static bool SamePath(string a, string b) => a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        private void Finish(string path, Report report, string error)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? ""; report.checks = checks.ToArray();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
