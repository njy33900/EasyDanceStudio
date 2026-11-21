using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H1-07: recently opened or saved scenes (up to RecentLimit), kept in the app settings folder
    // as a list of paths only (never scene contents). The toolbar's "최근 장면" button lists them;
    // a file that no longer exists is marked and can be removed from the list.
    public sealed partial class MmdSceneWorkspace
    {
        [Serializable] private sealed class RecentData
        {
            public int version = 1;
            public List<string> paths = new List<string>();
        }

        internal const int RecentLimit = 10;
        // null keeps the list in memory only (automated probes, unless they pass -recentFile).
        internal string RecentFilePath { get; set; }
        private readonly List<string> recent = new List<string>();
        internal IReadOnlyList<string> RecentScenes => recent;
        private VisualElement dialogContent;

        private void ConfigureRecent()
        {
            var explicitFile = PocArguments.Value("-recentFile");
            var automated = Environment.GetCommandLineArgs().Any(a => a.StartsWith("-poc", StringComparison.OrdinalIgnoreCase));
            RecentFilePath = explicitFile != null ? Path.GetFullPath(explicitFile)
                : automated ? null : Path.Combine(Application.persistentDataPath, "recent.json");
            LoadRecent();
        }

        internal void LoadRecent()
        {
            recent.Clear();
            if (RecentFilePath == null || !File.Exists(RecentFilePath)) return;
            try
            {
                var data = JsonUtility.FromJson<RecentData>(File.ReadAllText(RecentFilePath));
                foreach (var path in data?.paths ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(path) || recent.Count >= RecentLimit) continue;
                    if (!recent.Any(item => SamePath(item, path))) recent.Add(path);
                }
            }
            // A damaged settings file only costs the list; it is rewritten on the next change.
            catch (Exception ex) { Debug.LogWarning("Recent scenes unreadable: " + ex.Message); }
        }

        private void SaveRecent()
        {
            if (RecentFilePath == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecentFilePath));
                var temporary = RecentFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(new RecentData { paths = recent.ToList() }, true));
                if (File.Exists(RecentFilePath)) File.Replace(temporary, RecentFilePath, null); else File.Move(temporary, RecentFilePath);
            }
            catch (Exception ex) { Debug.LogWarning("Recent scenes not saved: " + ex.Message); }
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        // Most recent first; an existing entry moves to the top.
        internal void RememberRecent(string path)
        {
            var full = Path.GetFullPath(path);
            recent.RemoveAll(item => SamePath(item, full));
            recent.Insert(0, full);
            if (recent.Count > RecentLimit) recent.RemoveRange(RecentLimit, recent.Count - RecentLimit);
            SaveRecent();
        }

        internal bool ForgetRecent(string path)
        {
            var removed = recent.RemoveAll(item => SamePath(item, path)) > 0;
            if (removed) SaveRecent();
            return removed;
        }

        internal int ForgetMissingRecent()
        {
            var removed = recent.RemoveAll(item => !File.Exists(item));
            if (removed > 0) SaveRecent();
            return removed;
        }

        internal void ShowRecent()
        {
            if (Busy || ConfirmVisible || DialogVisible) return;
            ShowDialog(recent.Count == 0 ? "최근 장면이 없습니다. 장면을 열거나 저장하면 여기에 표시됩니다." : "최근 장면",
                null, ("RecentClose", "닫기", null));
            var tidy = TextButton(dialogButtons, "RecentForgetMissing", "없는 파일 정리", () =>
            {
                var count = ForgetMissingRecent();
                message = count > 0 ? "목록에서 없는 파일 " + count + "개를 지웠습니다." : "없는 파일이 없습니다.";
                FillRecent(); UpdateStatus();
            });
            tidy.PlaceBehind(dialogButtons.Q<Button>("RecentClose"));
            tidy.style.marginRight = 6;
            FillRecent();
        }

        private void FillRecent()
        {
            dialogContent.Clear();
            dialogContent.style.display = recent.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            dialogButtons.Q<Button>("RecentForgetMissing")?.SetEnabled(recent.Any(item => !File.Exists(item)));
            for (var i = 0; i < recent.Count; i++)
            {
                var path = recent[i];
                var exists = File.Exists(path);
                var row = Element(dialogContent, "eds-recent-row"); row.name = "RecentRow" + i;
                var open = TextButton(row, "RecentOpen" + i, Path.GetFileName(path) + (exists ? "" : "  (파일 없음)"), () => OpenRecent(path), "eds-recent-open");
                open.tooltip = path;
                if (!exists) { open.AddToClassList("eds-recent-open--missing"); open.SetEnabled(false); }
                Text(open, Path.GetDirectoryName(path), "eds-recent-folder");
                TextButton(row, "RecentRemove" + i, "×", () =>
                {
                    ForgetRecent(path); message = "최근 목록에서 지웠습니다: " + Path.GetFileName(path);
                    FillRecent(); UpdateStatus();
                }, "eds-recent-remove").tooltip = "목록에서 지우기 (파일은 그대로)";
            }
        }

        internal void OpenRecent(string path)
        {
            if (!DialogVisible) return;
            CloseDialog();
            if (!File.Exists(path)) { Error = "파일이 없습니다: " + path; Refresh(); return; }
            RequestDiscardOrSave(() => StartCoroutine(LoadScene(path)));
            Refresh();
        }
    }
}
