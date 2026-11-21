using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H1-08: diagnostics. The top bar's "진단" shows version, backend, OS, CPU, GPU, display,
    // a scene summary (counts only) and recent errors/warnings; the user reads it and can then
    // export exactly that text to a file or open the log folder. No model file contents; the
    // user profile folder is written as %USERPROFILE% and the machine name is never included.
    public sealed partial class MmdSceneWorkspace
    {
        internal string DiagnosticsText { get; private set; }

        internal static string MaskUserPaths(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(profile)) return text;
            var masked = text;
            foreach (var form in new[] { profile, profile.Replace('\\', '/') })
            {
                var at = masked.IndexOf(form, StringComparison.OrdinalIgnoreCase);
                while (at >= 0)
                {
                    masked = masked.Substring(0, at) + "%USERPROFILE%" + masked.Substring(at + form.Length);
                    at = masked.IndexOf(form, at + "%USERPROFILE%".Length, StringComparison.OrdinalIgnoreCase);
                }
            }
            return masked;
        }

        internal string BuildDiagnostics()
        {
            var b = new StringBuilder();
            var now = DateTimeOffset.Now;
            b.AppendLine("Easy Dance Studio 진단 정보");
            b.AppendLine("생성: " + now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            b.AppendLine();
            b.AppendLine("[앱]");
            b.AppendLine("버전: " + Application.version + " · Unity " + Application.unityVersion);
#if ENABLE_IL2CPP
            const string backend = "IL2CPP";
#else
            const string backend = "Mono";
#endif
            b.AppendLine("빌드: " + (Debug.isDebugBuild ? "Debug" : "Release") + " · " + backend + " · " + (Application.isEditor ? "Editor" : "Player"));
            b.AppendLine("실행 시간: " + TimeSpan.FromSeconds(Time.realtimeSinceStartup).ToString(@"hh\:mm\:ss"));
            b.AppendLine();
            b.AppendLine("[시스템]");
            b.AppendLine("OS: " + SystemInfo.operatingSystem);
            b.AppendLine("CPU: " + SystemInfo.processorType + " · " + SystemInfo.processorCount + "스레드 · 메모리 " + SystemInfo.systemMemorySize + " MB");
            b.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceVendor + " · VRAM " + SystemInfo.graphicsMemorySize + " MB");
            b.AppendLine("그래픽 API: " + SystemInfo.graphicsDeviceVersion + " · 셰이더 " + SystemInfo.graphicsShaderLevel);
            b.AppendLine("화면: " + Screen.width + "×" + Screen.height + " · " + Screen.currentResolution.refreshRateRatio.value.ToString("0.#", CultureInfo.InvariantCulture) +
                         " Hz · DPI " + Screen.dpi.ToString("0", CultureInfo.InvariantCulture) + " · " + Screen.fullScreenMode);
            b.AppendLine("관리 메모리: " + (GC.GetTotalMemory(false) / (1024 * 1024)) + " MB");
            b.AppendLine();
            b.AppendLine("[장면]");
            var characters = Scene.Objects.Count(o => o.Kind == MmdSceneService.ObjectKind.Character);
            b.AppendLine("캐릭터 " + characters + " · 배경 " + (Scene.Objects.Count - characters) + " · 모션 " + Scene.Objects.Count(o => o.Motion != null) +
                         " · 누락 항목 " + UnresolvedCount + " · 누락 모션 " + UnresolvedMotionCount);
            b.AppendLine("물리 ON " + Scene.Objects.Count(o => o.PhysicsEnabled) + " · 신체 흔들림 " + Scene.Objects.Count(o => o.SpringPreset != null) +
                         " · 길이 " + Scene.Duration.ToString("0.0", CultureInfo.InvariantCulture) + "초 · " + (Dirty ? "저장 안 됨" : "저장됨") +
                         " · 실행 취소 " + UndoCount);
            b.AppendLine("오디오: " + (Scene.Audio != null ? Path.GetFileName(Scene.Audio.Source) + " · " + Scene.Audio.Format :
                UnresolvedAudioPath != null ? "누락 (" + Path.GetFileName(UnresolvedAudioPath) + ")" : "없음"));
            b.AppendLine("장면 파일: " + (ProjectPath == null ? "(새 장면)" : Path.GetFileName(ProjectPath)));
            b.AppendLine();
            b.AppendLine("[로그]");
            var log = Application.consoleLogPath;
            b.AppendLine("로그 파일: " + (string.IsNullOrEmpty(log) ? "(없음)" : log));
            if (!string.IsNullOrEmpty(Error)) b.AppendLine("현재 오류 표시: " + Error);
            var recentLog = MmdDiagnosticsLog.Snapshot();
            b.AppendLine("최근 오류·경고 " + recentLog.Count + "개 (최대 " + MmdDiagnosticsLog.Capacity + "):");
            foreach (var entry in recentLog) b.AppendLine("  " + entry);
            return MaskUserPaths(b.ToString());
        }

        private void ShowDiagnostics()
        {
            if (Busy || ConfirmVisible || DialogVisible) return;
            DiagnosticsText = BuildDiagnostics();
            ShowDialog("진단 정보 — 아래 내용만 파일로 내보냅니다. 모델 파일 내용은 들어가지 않고, 사용자 폴더는 %USERPROFILE%로 표시됩니다.",
                null, ("DiagnosticsExport", "파일로 내보내기", PickDiagnosticsExport), ("DiagnosticsOpenLog", "로그 폴더 열기", OpenLogFolder),
                ("DiagnosticsClose", "닫기", null));
            dialogContent.parent.AddToClassList("eds-dialog--wide");
            dialogContent.style.display = DisplayStyle.Flex;
            // A selectable label grows with its text (a read-only TextField clips to its own height).
            var field = new Label(DiagnosticsText) { name = "DiagnosticsBody" };
            field.selection.isSelectable = true;
            field.AddToClassList("eds-diagnostics");
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "DiagnosticsScroll" };
            scroll.AddToClassList("eds-diagnostics-scroll");
            scroll.Add(field); dialogContent.Add(scroll);
        }

        private void PickDiagnosticsExport()
        {
            string path;
            try { path = FileDialogs.SaveText("EasyDanceStudio-진단-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".txt"); }
            catch (Exception ex) { Error = ex.Message; Refresh(); return; }
            if (path == null) { message = "내보내기를 취소했습니다."; Refresh(); return; }
            ExportDiagnostics(path);
        }

        // Writes the text the user was shown (not a fresh snapshot).
        internal bool ExportDiagnostics(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                File.WriteAllText(full, (DiagnosticsText ?? BuildDiagnostics()).Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"), new UTF8Encoding(true));
                Error = ""; message = "진단 정보를 저장했습니다: " + Path.GetFileName(full);
                Refresh(); return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Error = "진단 정보를 저장하지 못했습니다: " + ex.Message; Refresh(); return false;
            }
        }

        internal static string LogFolder
        {
            get
            {
                var log = Application.consoleLogPath;
                return string.IsNullOrEmpty(log) ? null : Path.GetDirectoryName(log);
            }
        }

        private void OpenLogFolder()
        {
            var folder = LogFolder;
            if (folder == null || !Directory.Exists(folder)) { Error = "로그 폴더를 찾을 수 없습니다."; Refresh(); return; }
            Application.OpenURL(new Uri(folder + Path.DirectorySeparatorChar).AbsoluteUri);
            message = "로그 폴더를 열었습니다: " + MaskUserPaths(folder);
        }
    }

    // Keeps the most recent errors and warnings (first line + first stack frame) for diagnostics.
    internal static class MmdDiagnosticsLog
    {
        internal const int Capacity = 30;
        private static readonly Queue<string> entries = new Queue<string>();
        private static readonly object gate = new object();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            lock (gate) entries.Clear();
            Application.logMessageReceivedThreaded -= Received;
            Application.logMessageReceivedThreaded += Received;
        }

        private static void Received(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log) return;
            var kind = type == LogType.Warning ? "경고" : type == LogType.Exception ? "예외" : "오류";
            var line = FirstLine(condition);
            // First frame outside UnityEngine.Debug / Logger (the caller that logged it).
            var frame = (stackTrace ?? "").Split('\n').Select(item => item.Trim())
                .FirstOrDefault(item => item.Length > 0 && !item.StartsWith("UnityEngine.Debug", StringComparison.Ordinal) &&
                                        !item.StartsWith("UnityEngine.Logger", StringComparison.Ordinal)) ?? "";
            var text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + kind + ": " + line + (frame.Length > 0 ? "  @ " + frame : "");
            if (text.Length > 400) text = text.Substring(0, 400) + "…";
            lock (gate)
            {
                entries.Enqueue(text);
                while (entries.Count > Capacity) entries.Dequeue();
            }
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var end = text.IndexOfAny(new[] { '\r', '\n' });
            return (end < 0 ? text : text.Substring(0, end)).Trim();
        }

        internal static List<string> Snapshot() { lock (gate) return entries.ToList(); }
    }
}
