using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MmdStudio.Poc
{
    // H1-06: autosave and crash recovery. While there are unsaved changes the scene is written
    // every AutosaveSeconds to a separate recovery file (never the original). A normal save, new
    // scene, open or quit removes it; after a crash it stays, and the next launch offers to
    // restore it (original path kept, scene marked unsaved) or discard it.
    public sealed partial class MmdSceneWorkspace
    {
        [Serializable] private sealed class RecoveryInfo
        {
            public int version = 1;
            public string originalPath, savedAtUtc, projectId;
            public int objects;
        }

        // null disables recovery (automated probes, unless they pass -recoveryDir).
        internal string RecoveryDirectory { get; set; }
        internal float AutosaveSeconds { get; set; } = 120;
        internal int AutosaveCount { get; private set; }
        internal string RecoveryScenePath => RecoveryDirectory == null ? null : Path.Combine(RecoveryDirectory, "recovery" + MmdSceneDocument.Extension);
        private string RecoveryInfoPath => RecoveryDirectory == null ? null : Path.Combine(RecoveryDirectory, "recovery.json");
        internal bool RecoveryPending => RecoveryScenePath != null && File.Exists(RecoveryScenePath) && File.Exists(RecoveryInfoPath);
        private int dirtyVersion, recoveredVersion;
        private float lastAutosaveAt = -1;
        // True once this session wrote the recovery file or adopted it; only then may it delete it.
        private bool recoveryOwned;

        private void ConfigureRecovery()
        {
            var explicitDirectory = PocArguments.Value("-recoveryDir");
            var automated = Environment.GetCommandLineArgs().Any(a => a.StartsWith("-poc", StringComparison.OrdinalIgnoreCase));
            RecoveryDirectory = explicitDirectory != null ? Path.GetFullPath(explicitDirectory)
                : automated ? null : Path.Combine(Application.persistentDataPath, "Recovery");
            if (float.TryParse(PocArguments.Value("-autosaveSeconds"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0) AutosaveSeconds = seconds;
            if (RecoveryPending) StartCoroutine(OfferRecoveryNextFrame());
        }

        // Called whenever the document changes (MarkDirty).
        private void NoteDirty()
        {
            dirtyVersion++;
            if (lastAutosaveAt < 0) lastAutosaveAt = Time.realtimeSinceStartup;
        }

        private void UpdateAutosave()
        {
            if (RecoveryDirectory == null || Busy || DialogVisible) return;
            if (!Dirty)
            {
                // Saved, new or opened: the recovery copy is no longer needed.
                if (recoveryOwned) ClearRecovery();
                lastAutosaveAt = -1;
                return;
            }
            if (dirtyVersion == recoveredVersion) return;
            // An earlier session's recovery file that the user deferred ("나중에") is never
            // overwritten; autosave resumes once it is restored or discarded.
            if (!recoveryOwned && RecoveryPending) return;
            if (lastAutosaveAt < 0) lastAutosaveAt = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - lastAutosaveAt < AutosaveSeconds) return;
            WriteRecovery();
        }

        internal bool WriteRecovery()
        {
            try
            {
                Directory.CreateDirectory(RecoveryDirectory);
                var scene = RecoveryScenePath;
                MmdSceneDocument.Write(scene, Capture(scene));
                var info = new RecoveryInfo
                {
                    originalPath = ProjectPath, savedAtUtc = DateTime.UtcNow.ToString("o"), projectId = projectId,
                    objects = Scene.Objects.Count + unresolved.Count,
                };
                var temporary = RecoveryInfoPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(info, true));
                if (File.Exists(RecoveryInfoPath)) File.Replace(temporary, RecoveryInfoPath, null); else File.Move(temporary, RecoveryInfoPath);
                recoveredVersion = dirtyVersion; lastAutosaveAt = Time.realtimeSinceStartup; recoveryOwned = true; AutosaveCount++;
                return true;
            }
            catch (Exception ex)
            {
                // Autosave must never interrupt work; retry at the next interval.
                lastAutosaveAt = Time.realtimeSinceStartup;
                Debug.LogWarning("Autosave failed: " + ex.Message);
                return false;
            }
        }

        private void ClearRecovery()
        {
            try
            {
                if (RecoveryScenePath != null && File.Exists(RecoveryScenePath)) File.Delete(RecoveryScenePath);
                if (RecoveryInfoPath != null && File.Exists(RecoveryInfoPath)) File.Delete(RecoveryInfoPath);
            }
            catch (Exception ex) { Debug.LogWarning("Recovery cleanup failed: " + ex.Message); }
            recoveryOwned = false; recoveredVersion = dirtyVersion;
        }

        private IEnumerator OfferRecoveryNextFrame()
        {
            yield return null;
            RecoveryInfo info;
            try { info = JsonUtility.FromJson<RecoveryInfo>(File.ReadAllText(RecoveryInfoPath)); }
            catch (Exception ex) { Debug.LogWarning("Recovery info unreadable: " + ex.Message); yield break; }
            var when = DateTime.TryParse(info.savedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var saved)
                ? saved.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "알 수 없음";
            var original = string.IsNullOrEmpty(info.originalPath) ? "새 장면(저장한 적 없음)" : Path.GetFileName(info.originalPath);
            ShowDialog("이전에 저장하지 않은 작업이 남아 있습니다.\n장면: " + original + " · 항목 " + info.objects + "개\n자동 저장: " + when +
                       "\n복구하면 원래 파일은 바꾸지 않고 '저장하지 않음' 상태로 엽니다.",
                () => { message = "복구를 미뤘습니다. 다음 실행 때 다시 묻습니다."; },
                ("RecoveryRestore", "복구", () => StartCoroutine(RestoreRecovery(info))),
                ("RecoveryDiscard", "버리기", () => { ClearRecovery(); message = "자동 저장된 작업을 버렸습니다."; }),
                ("RecoveryLater", "나중에", null));
        }

        private IEnumerator RestoreRecovery(RecoveryInfo info)
        {
            yield return LoadScene(RecoveryScenePath);
            if (Error.Length > 0 || ProjectPath == null) { message = "복구하지 못했습니다. 자동 저장 파일은 남겨 두었습니다."; Refresh(); yield break; }
            // The recovered scene belongs to its original file (or none); it is not saved yet.
            ProjectPath = string.IsNullOrEmpty(info.originalPath) ? null : info.originalPath;
            untrackedChange = true; UpdateDirtyFromHistory(); recoveryOwned = true; recoveredVersion = dirtyVersion;
            message = "자동 저장된 작업을 복구했습니다. 저장하면 원래 파일에 기록됩니다.";
            Refresh();
        }

        private void OnApplicationQuit()
        {
            // Normal quit (saved, or the user chose not to save): drop the recovery copy.
            if (recoveryOwned) ClearRecovery();
        }
    }
}
