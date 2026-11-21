using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H1-05 checks (scripts/bash/run-scene-format.sh), using the scene saved by the UI probe:
    // schema 1 -> 2 migration (in memory, original untouched until saved), a folder name with
    // Korean/space/special characters, a scene on another drive (absolute asset paths) and a
    // read-only scene file (save fails safely). Network paths are not covered here.
    public sealed class PocSceneFormatProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool success;
            public string error, backend, sceneFile, otherDriveFile, specialFile, notes;
            public string[] checks;
        }
        private readonly List<string> checks = new List<string>();
        private void Check(string name, bool passed) { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }

        public IEnumerator Run(MmdSceneWorkspace ui, string reportPath, string sceneFile)
        {
            var report = new Report
            {
#if ENABLE_IL2CPP
                backend = "IL2CPP",
#else
                backend = "Mono",
#endif
                sceneFile = Path.GetFullPath(sceneFile),
            };
            ui.DeviceInput = false; ui.Scene.AutomaticClock = false; ui.SuppressQuitPrompt = true;
            var project = Path.GetDirectoryName(report.sceneFile);
            yield return null;
            yield return ui.OpenFrom(sceneFile);
            string[] ids = null; string characterId = null;
            try
            {
                if (ui.Error.Length > 0) throw new InvalidOperationException("open failed: " + ui.Error);
                ids = ui.Scene.Objects.Select(o => o.Id).ToArray();
                characterId = ui.Scene.Objects.First(o => o.Kind == MmdSceneService.ObjectKind.Character && o.Motion != null).Id;
                Check("current_files_are_schema_2", MmdSceneDocument.Read(sceneFile).schemaVersion == 2 && ui.LastOpenMigratedFrom == 0);
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }

            // 1. Schema 1 file with the flat physics fields used during 0.5 development.
            var legacy = Path.Combine(project, "legacy-v1.mmdscene");
            byte[] legacyBytes = null;
            try
            {
                var current = MmdSceneDocument.Read(sceneFile);
                var v1 = new SceneFileV1
                {
                    schemaVersion = 1, application = current.application, projectId = current.projectId, selectedId = current.selectedId,
                    view = current.view, graphics = current.graphics, timeline = current.timeline,
                    sceneObjects = current.sceneObjects.Select(o => new SceneObjectDataV1
                    {
                        id = o.id, type = o.type, assetPath = o.assetPath, motionPath = o.motionPath, position = o.position,
                        rotationEuler = o.rotationEuler, scale = o.scale, visible = o.visible,
                        physics = o.id == characterId, physicsParts = o.id == characterId ? new[] { "hair" } : null,
                        physicsStrength = o.id == characterId ? new[] { .5f } : null,
                    }).ToList(),
                };
                File.WriteAllText(legacy, JsonUtility.ToJson(v1, true));
                legacyBytes = File.ReadAllBytes(legacy);
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(legacy);
            try
            {
                var item = ui.Scene.Objects.FirstOrDefault(o => o.Id == characterId);
                Check("schema_1_opens_migrated_in_memory", ui.Error.Length == 0 && ui.LastOpenMigratedFrom == 1 && !ui.Dirty &&
                    ui.Scene.Objects.Select(o => o.Id).SequenceEqual(ids) && item != null && item.PhysicsEnabled && Mathf.Approximately(item.StrengthOf("hair"), .5f) &&
                    File.ReadAllBytes(legacy).SequenceEqual(legacyBytes));
                if (!ui.SaveTo(legacy)) throw new InvalidOperationException("save failed: " + ui.Error);
                var text = File.ReadAllText(legacy);
                var saved = MmdSceneDocument.Read(legacy);
                var data = saved.sceneObjects.First(o => o.id == characterId);
                Check("schema_1_saves_as_schema_2_physics_block", text.Contains("\"schemaVersion\": 2") && saved.schemaVersion == 2 &&
                    data.physics.enabled && data.physics.strength.Any(s => s.part == "hair" && Mathf.Approximately(s.value, .5f)) &&
                    MmdSceneDocument.MigratedFromVersion == 0);
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }

            // 2. Korean, space and special characters in folder and file names. Back to the
            // current scene first: the schema 1 copy above has no audio block.
            yield return ui.OpenFrom(sceneFile);
            var specialDirectory = Path.Combine(project, "한글 경로 (#1) & '특수' 폴더");
            report.specialFile = Path.Combine(specialDirectory, "장면 테스트 #2.mmdscene");
            try
            {
                Directory.CreateDirectory(specialDirectory);
                if (!ui.SaveTo(report.specialFile)) throw new InvalidOperationException("special save failed: " + ui.Error);
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(report.specialFile);
            try
            {
                // H2a-03: the scene carries a WAV; it must survive the special-character path too.
                Check("korean_space_special_character_path_roundtrip", ui.Error.Length == 0 && ui.UnresolvedCount == 0 &&
                    ui.Scene.Audio != null && ui.UnresolvedAudioPath == null &&
                    ui.Scene.Objects.Select(o => o.Id).SequenceEqual(ids) &&
                    string.Equals(ui.ProjectPath, Path.GetFullPath(report.specialFile), StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }

            // 3. Scene on another drive: asset paths are stored absolute.
            var tempRoot = Path.GetPathRoot(Path.GetTempPath());
            var otherDrive = !string.Equals(tempRoot, Path.GetPathRoot(project), StringComparison.OrdinalIgnoreCase);
            if (otherDrive)
            {
                var directory = Path.Combine(Path.GetTempPath(), "EasyDanceStudio-format-" + Guid.NewGuid().ToString("N"));
                report.otherDriveFile = Path.Combine(directory, "other-drive.mmdscene");
                try
                {
                    Directory.CreateDirectory(directory);
                    if (!ui.SaveTo(report.otherDriveFile)) throw new InvalidOperationException("other drive save failed: " + ui.Error);
                }
                catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }
                yield return ui.OpenFrom(report.otherDriveFile);
                try
                {
                    var stored = MmdSceneDocument.Read(report.otherDriveFile).sceneObjects;
                    var storedAudio = MmdSceneDocument.Read(report.otherDriveFile).audio.path;
                    Check("other_drive_stores_absolute_paths_and_reopens", ui.Error.Length == 0 && ui.UnresolvedCount == 0 &&
                        ui.Scene.Audio != null && ui.UnresolvedAudioPath == null && Path.IsPathRooted(storedAudio.Replace('/', Path.DirectorySeparatorChar)) &&
                        ui.Scene.Objects.Select(o => o.Id).SequenceEqual(ids) &&
                        stored.All(o => Path.IsPathRooted(o.assetPath.Replace('/', Path.DirectorySeparatorChar))));
                    Directory.Delete(directory, true);
                }
                catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }
            }
            else report.notes = "Temp folder is on the same drive as the project; other-drive case not exercised. ";

            // 4. Read-only scene file: saving fails with a clear message, the file is untouched.
            var readOnly = Path.Combine(project, "readonly.mmdscene");
            byte[] readOnlyBytes = null;
            try
            {
                if (!ui.SaveTo(readOnly)) throw new InvalidOperationException("read-only setup save failed: " + ui.Error);
                File.SetAttributes(readOnly, File.GetAttributes(readOnly) | FileAttributes.ReadOnly);
                readOnlyBytes = File.ReadAllBytes(readOnly);
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }
            yield return ui.OpenFrom(readOnly);
            try
            {
                ui.Root.Q<Slider>("Exposure").value = .9f;
                ui.ProcessViewportInput(new Vector2(-10, -10), Vector2.zero, 0, false, false, false, saveKey: true);
                Check("read_only_save_fails_safely", ui.Error.Contains("저장하지 못했습니다") && ui.Dirty && File.ReadAllBytes(readOnly).SequenceEqual(readOnlyBytes) &&
                    !Directory.GetFiles(project, ".readonly.mmdscene.*.tmp").Any());
                File.SetAttributes(readOnly, File.GetAttributes(readOnly) & ~FileAttributes.ReadOnly);
                ui.ProcessViewportInput(new Vector2(-10, -10), Vector2.zero, 0, false, false, false, saveKey: true);
                Check("save_succeeds_after_read_only_cleared", ui.Error.Length == 0 && !ui.Dirty &&
                    Mathf.Approximately(MmdSceneDocument.Read(readOnly).graphics.exposure, .9f));
            }
            catch (Exception ex) { Finish(reportPath, report, ex.ToString()); yield break; }
            finally { try { if (File.Exists(readOnly)) File.SetAttributes(readOnly, FileAttributes.Normal); } catch { } }
            report.notes += "Network (UNC) paths not tested: no share available.";
            Finish(reportPath, report, null);
        }

        private void Finish(string path, Report report, string error)
        {
            report.success = string.IsNullOrEmpty(error); report.error = error ?? ""; report.checks = checks.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success) Debug.LogError(error);
            Application.Quit(report.success ? 0 : 1);
        }
    }
}
