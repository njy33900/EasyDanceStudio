using System;
using System.Runtime.InteropServices;

namespace MmdStudio.Poc
{
    // H3-02/H3-03: P/Invoke surface of Plugins/x86_64/EdsMedia.dll (Media Foundation H.264/AAC
    // MP4 writer and reader; source in MMDStudio/Native/EdsMedia, built by
    // scripts/build-native-media.bat). Errors come back as HRESULTs.
    public sealed class MmdMediaWriter : IDisposable
    {
        private IntPtr handle;
        public int Width { get; }
        public int Height { get; }
        public bool HasAudio { get; }

        public MmdMediaWriter(string path, int width, int height, int fpsNum, int fpsDen, int videoBitrate, int sampleRate, int channels)
        {
            Width = width; Height = height; HasAudio = sampleRate > 0;
            int hr;
            // Windows N editions ship without Media Foundation (MFPlat.dll) until the Media Feature Pack is installed.
            try { hr = EdsMf_Open(path, width, height, fpsNum, fpsDen, videoBitrate, sampleRate, channels, 24000, out handle); }
            catch (DllNotFoundException ex) { throw new InvalidOperationException("MP4 출력에는 Windows 미디어 기능(Media Foundation)이 필요합니다. Windows N 에디션이면 '미디어 기능 팩'을 설치하세요.", ex); }
            Check(hr, "인코더를 열지 못했습니다");
        }
        public void WriteVideo(byte[] bgraBottomUp, long frameIndex)
        {
            if (bgraBottomUp.Length != Width * Height * 4) throw new ArgumentException("frame size");
            Check(EdsMf_WriteVideo(handle, bgraBottomUp, frameIndex), "영상 프레임을 쓰지 못했습니다");
        }
        public void WriteAudio(short[] pcm, int frames) => Check(EdsMf_WriteAudio(handle, pcm, frames), "오디오를 쓰지 못했습니다");
        public void Finish()
        {
            var h = handle; handle = IntPtr.Zero;
            Check(EdsMf_Finish(h), "MP4 파일을 마무리하지 못했습니다");
        }
        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            EdsMf_Abort(handle); handle = IntPtr.Zero;
        }

        internal static void Check(int hr, string what)
        {
            if (hr >= 0) return;
            var reason = (uint)hr == 0x80070070 ? "디스크 공간이 부족합니다" : (uint)hr == 0x80070005 ? "쓰기 권한이 없습니다" :
                (uint)hr == 0xC00D36B4 ? "지원하지 않는 형식입니다" : (uint)hr == 0x80070057 ? "설정 값이 올바르지 않습니다" : "Media Foundation 오류";
            throw new InvalidOperationException(what + ": " + reason + " (0x" + ((uint)hr).ToString("X8") + ")");
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProbeInfo
        {
            public uint videoCodec, width, height, fpsNum, fpsDen;
            public long videoFrames, videoDuration, firstVideoTime;
            public uint audioCodec, sampleRate, channels;
            public long audioDuration, firstAudioTime, containerDuration;
            public string VideoCodecName => FourCc(videoCodec);
            public string AudioCodecName => audioCodec == 0x1610 ? "AAC" : audioCodec == 0 ? "" : "0x" + audioCodec.ToString("X");
            private static string FourCc(uint code) => code == 0 ? "" : new string(new[] { (char)(code & 255), (char)((code >> 8) & 255), (char)((code >> 16) & 255), (char)(code >> 24) });
        }
        public static ProbeInfo Probe(string path)
        {
            Check(EdsMf_Probe(path, out var info), "MP4 파일을 읽지 못했습니다");
            return info;
        }
        public static short[] ReadAudio(string path, int maxFrames, out int sampleRate, out int channels)
        {
            var buffer = new short[maxFrames * 2];
            Check(EdsMf_ReadAudio(path, buffer, maxFrames, out var frames, out sampleRate, out channels), "MP4 오디오를 읽지 못했습니다");
            if (channels > 2) throw new InvalidOperationException("unexpected channel count");
            Array.Resize(ref buffer, frames * channels);
            return buffer;
        }

        // Decoded frame as BGRA, bottom-up rows (Unity texture order).
        public static byte[] ReadVideoFrame(string path, int frameIndex, int width, int height)
        {
            var bgra = new byte[width * height * 4];
            Check(EdsMf_ReadVideoFrame(path, frameIndex, bgra, width, height), "MP4 영상 프레임을 읽지 못했습니다");
            return bgra;
        }

        private const string Dll = "EdsMedia";
        [DllImport(Dll, CharSet = CharSet.Unicode)] private static extern int EdsMf_ReadVideoFrame(string path, int frameIndex, byte[] bgra, int width, int height);
        [DllImport(Dll, CharSet = CharSet.Unicode)] private static extern int EdsMf_Open(string path, int width, int height, int fpsNum, int fpsDen, int videoBitrate, int sampleRate, int channels, int audioBytesPerSecond, out IntPtr handle);
        [DllImport(Dll)] private static extern int EdsMf_WriteVideo(IntPtr handle, byte[] bgra, long frameIndex);
        [DllImport(Dll)] private static extern int EdsMf_WriteAudio(IntPtr handle, short[] pcm, int frames);
        [DllImport(Dll)] private static extern int EdsMf_Finish(IntPtr handle);
        [DllImport(Dll)] private static extern void EdsMf_Abort(IntPtr handle);
        [DllImport(Dll, CharSet = CharSet.Unicode)] private static extern int EdsMf_Probe(string path, out ProbeInfo info);
        [DllImport(Dll, CharSet = CharSet.Unicode)] private static extern int EdsMf_ReadAudio(string path, short[] pcm, int maxFrames, out int frames, out int sampleRate, out int channels);
    }
}
