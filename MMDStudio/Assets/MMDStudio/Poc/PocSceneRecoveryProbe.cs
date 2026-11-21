using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H1-06 check across a forced kill (scripts/bash/run-recovery.sh):
    //  write  (-pocRecoveryWrite <report>): open the scene, make unsaved changes, wait for the
    //          autosave, write the expected state, then idle until the runner kills the process.
    //  check  (-pocRecoveryCheck <report> -pocRecoveryExpect <write report>): the recovery dialog
    //          must appear; "복구" must restore the changes (original path kept, unsaved), and a save
    //          must remove the recovery file. With -pocRecoveryDiscard, "버리기" must remove it.
    public sealed class PocSceneRecoveryProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, backend, phase, sceneFile, recoveryScene;
            public string[] ids, physicsIds, hiddenIds;
            public float exposure;
            public int autosaves;
            public float autosaveWaitSeconds;
            public string[] checks;
        }
        private readonly List<string> checks = new List<string>();
        private void Check(string name, bool passed) { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
        private static void Press(VisualElement root, string name)
        {
            var button = root.Q<Button>(name);
            if (button == null || !button.enabledInHierarchy) throw new InvalidOperationException("button unavailable: " + name);
            button.Focus(); // the submit event goes to the focused element
            using (var evt = NavigationSubmitEvent.GetPooled()) button.SendEvent(evt);
        }

        public IEnumerator Write(MmdSceneWorkspace ui, string reportPath, string sceneFile)
        {
            var report = NewReport("write");
            report.sceneFile = Path.GetFullPath(sceneFile); report.recoveryScene = ui.RecoveryScenePath;
            ui.DeviceInput = false; ui.Scene.AutomaticClock = false;
            yield return null;
            if (ui.RecoveryDirectory == null) { Finish(reportPath, report, "recovery is disabled (missing -recoveryDir)", true); yield break; }
            yield return ui.OpenFrom(sceneFile);
            MmdSceneService.SceneObject character = null, stage = null;
            try
            {
                if (ui.Error.Length > 0) throw new InvalidOperationException("open failed: " + ui.Error);
                character = ui.Scene.Objects.First(o => o.Kind == MmdSceneService.ObjectKind.Character && o.Motion != null);
                stage = ui.Scene.Objects.First(o => o.Kind == MmdSceneService.ObjectKind.Stage);
                ui.Root.Q<Slider>("Exposure").value = .7f;
                ui.Select(character.Id); ui.Root.Q<Toggle>("Physics").value = true;
                ui.Select(stage.Id); ui.Root.Q<Toggle>("Visible").value = false;
                Check("changes_mark_scene_unsaved", ui.Dirty && character.PhysicsEnabled && !stage.Visible && !ui.RecoveryPending);
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString(), true); yield break; }
            var waited = 0f;
            while (ui.AutosaveCount == 0 && waited < 20) { waited += Time.unscaledDeltaTime; yield return null; }
            report.autosaveWaitSeconds = waited; report.autosaves = ui.AutosaveCount;
            try
            {
                Check("autosave_writes_recovery_file_not_original", ui.AutosaveCount > 0 && ui.RecoveryPending &&
                    MmdSceneDocument.Read(sceneFile).graphics.exposure != .7f && Math.Abs(MmdSceneDocument.Read(ui.RecoveryScenePath).graphics.exposure - .7f) < 1e-4f);
                report.ids = ui.Scene.Objects.Select(o => o.Id).ToArray();
                report.physicsIds = ui.Scene.Objects.Where(o => o.PhysicsEnabled).Select(o => o.Id).ToArray();
                report.hiddenIds = ui.Scene.Objects.Where(o => !o.Visible).Select(o => o.Id).ToArray();
                report.exposure = .7f;
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString(), true); yield break; }
            // Leave the process running: the runner kills it (no OnApplicationQuit, like a crash).
            Finish(reportPath, report, null, false);
            while (true) yield return null;
        }

        public IEnumerator CheckRestore(MmdSceneWorkspace ui, string reportPath, string expectedPath, bool discard)
        {
            var report = NewReport(discard ? "discard" : "restore");
            ui.DeviceInput = false; ui.Scene.AutomaticClock = false; ui.SuppressQuitPrompt = true;
            Report expected;
            try { expected = JsonUtility.FromJson<Report>(File.ReadAllText(expectedPath)); }
            catch (Exception ex) { Finish(reportPath, report, "expected report unreadable: " + ex.Message, true); yield break; }
            report.sceneFile = expected.sceneFile; report.recoveryScene = ui.RecoveryScenePath;
            var waited = 0f;
            while (!(ui.DialogVisible && ui.Root.Q<Button>("RecoveryRestore") != null) && waited < 10) { waited += Time.unscaledDeltaTime; yield return null; }
            try
            {
                Check("launch_offers_recovery", ui.DialogVisible && ui.DialogText.Contains("저장하지 않은 작업") && ui.RecoveryPending && ui.Scene.Objects.Count == 0);
                Press(ui.Root, discard ? "RecoveryDiscard" : "RecoveryRestore");
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString(), true); yield break; }
            yield return null;
            while (ui.Busy) yield return null;
            yield return null;
            try
            {
                if (discard)
                {
                    Check("discard_removes_recovery_and_keeps_empty_scene", !ui.RecoveryPending && ui.Scene.Objects.Count == 0 && !ui.Dirty);
                    Finish(reportPath, report, null, true); yield break;
                }
                var physics = ui.Scene.Objects.Where(o => o.PhysicsEnabled).Select(o => o.Id).ToArray();
                var hidden = ui.Scene.Objects.Where(o => !o.Visible).Select(o => o.Id).ToArray();
                Check("restore_brings_back_unsaved_changes", ui.Error.Length == 0 && ui.Scene.Objects.Select(o => o.Id).SequenceEqual(expected.ids) &&
                    physics.SequenceEqual(expected.physicsIds) && hidden.SequenceEqual(expected.hiddenIds) &&
                    Math.Abs(ui.Graphics.Settings.exposure - expected.exposure) < 1e-4f);
                Check("restored_scene_keeps_original_path_and_is_unsaved", string.Equals(ui.ProjectPath, expected.sceneFile, StringComparison.OrdinalIgnoreCase) &&
                    ui.Dirty && ui.RecoveryPending && Math.Abs(MmdSceneDocument.Read(expected.sceneFile).graphics.exposure - expected.exposure) > 1e-4f);
                if (!ui.Save()) throw new InvalidOperationException("save failed: " + ui.Error);
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString(), true); yield break; }
            yield return null; yield return null;
            try
            {
                Check("save_writes_original_and_removes_recovery", !ui.Dirty && !ui.RecoveryPending &&
                    Math.Abs(MmdSceneDocument.Read(expected.sceneFile).graphics.exposure - expected.exposure) < 1e-4f);
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString(), true); yield break; }
            Finish(reportPath, report, null, true);
        }

        private Report NewReport(string phase)
        {
            return new Report
            {
                phase = phase,
#if ENABLE_IL2CPP
                backend = "IL2CPP",
#else
                backend = "Mono",
#endif
            };
        }

        private void Finish(string path, Report report, string error, bool quit)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? ""; report.checks = checks.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(error);
            if (quit) Application.Quit(report.success ? 0 : 1);
        }
    }
}
