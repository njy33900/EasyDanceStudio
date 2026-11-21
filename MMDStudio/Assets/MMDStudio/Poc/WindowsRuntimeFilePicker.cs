using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace MmdStudio.Poc
{
    public static class WindowsRuntimeFilePicker
    {
        public static string Open(bool model) => model
            ? Show(false, "PMX 모델 (*.pmx)\0*.pmx\0\0", "PMX 모델 선택", "pmx", null)
            : Show(false, "VMD 모션 (*.vmd)\0*.vmd\0\0", "VMD 모션 선택", "vmd", null);
        public static string OpenCameraMotion() => Show(false, "VMD 카메라 모션 (*.vmd)\0*.vmd\0\0", "카메라 VMD 선택", "vmd", null);
        public static string OpenAudio() => Show(false, "WAV 오디오 (*.wav)\0*.wav\0\0", "오디오 파일 선택", "wav", null);
        public static string OpenScene() => Show(false, "Easy Dance Studio 장면 (*.mmdscene)\0*.mmdscene\0\0", "장면 열기", "mmdscene", null);
        // Save dialog with overwrite confirmation; suggestedName may be null.
        public static string SaveScene(string suggestedName) => Show(true, "Easy Dance Studio 장면 (*.mmdscene)\0*.mmdscene\0\0", "장면 저장", "mmdscene", suggestedName);

        public static string SaveText(string suggestedName) => Show(true, "텍스트 파일 (*.txt)\0*.txt\0\0", "진단 정보 내보내기", "txt", suggestedName);

        private static string Show(bool save, string filter, string title, string extension, string suggestedName)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var buffer = Marshal.AllocHGlobal(32768 * 2);
            Marshal.WriteInt16(buffer, 0);
            if (!string.IsNullOrEmpty(suggestedName) && suggestedName.Length < 1024)
            {
                var chars = (suggestedName + "\0").ToCharArray();
                Marshal.Copy(chars, 0, buffer, chars.Length);
            }
            // OFN_EXPLORER | NOCHANGEDIR | PATHMUSTEXIST, plus FILEMUSTEXIST (open) or OVERWRITEPROMPT (save).
            var flags = 0x00080000 | 0x00000800 | 0x00000008 | (save ? 0x00000002 : 0x00001000);
            var data = new OpenFileName
            {
                owner = GetActiveWindow(),
                filter = filter,
                file = buffer,
                maxFile = 32768,
                title = title,
                defaultExtension = extension,
                flags = flags
            };
            var directory = Directory.GetCurrentDirectory();
            try
            {
                data.size = Marshal.SizeOf(typeof(OpenFileName));
                if (IntPtr.Size == 8 && data.size != 152)
                    throw new InvalidOperationException("Invalid x64 OPENFILENAME layout.");
                if (save ? GetSaveFileName(data) : GetOpenFileName(data)) return Path.GetFullPath(Marshal.PtrToStringUni(buffer));
                var error = CommDlgExtendedError();
                if (error != 0) throw new Win32Exception((int)error, "Windows 파일 선택기 오류: 0x" + error.ToString("X"));
                return null;
            }
            finally { Marshal.FreeHGlobal(buffer); Directory.SetCurrentDirectory(directory); }
#else
            throw new PlatformNotSupportedException("Windows 파일 선택기만 지원합니다.");
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class OpenFileName
        {
            public int size;
            public IntPtr owner;
            public IntPtr instance;
            public string filter;
            public IntPtr customFilter;
            public int maxCustomFilter;
            public int filterIndex = 1;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public string initialDirectory;
            public string title;
            public int flags;
            public short fileOffset;
            public short extensionOffset;
            public string defaultExtension;
            public IntPtr customData;
            public IntPtr hook;
            public IntPtr template;
            public IntPtr reserved;
            public int reservedValue;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileName([In, Out] OpenFileName data);
        [DllImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSaveFileName([In, Out] OpenFileName data);
        [DllImport("comdlg32.dll")]
        private static extern uint CommDlgExtendedError();
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
#endif
    }
}
