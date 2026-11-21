using System;

namespace MmdStudio.Poc
{
    // H3: output settings saved with the scene and the exact frame timing.
    [Serializable]
    public sealed class OutputData
    {
        public const string Landscape = "landscape", Portrait = "portrait", Square = "square", Custom = "custom";
        public string preset = Landscape;
        public int width = 1920, height = 1080; // used by "custom"; presets fix their own size
        public int fps = 60;                    // 60 (default, decision 12) or 30
        public bool guides = true;              // show the output frame in the viewport when it is narrower

        public OutputData Clone() => (OutputData)MemberwiseClone();
        public (int width, int height) Size => preset == Portrait ? (1080, 1920) : preset == Square ? (1080, 1080) : preset == Custom ? (width, height) : (1920, 1080);
        public float Aspect { get { var s = Size; return s.width / (float)s.height; } }
        public void Validate()
        {
            if (preset != Landscape && preset != Portrait && preset != Square && preset != Custom) throw new ArgumentOutOfRangeException(nameof(preset), "출력 화면비 값이 올바르지 않습니다: " + preset);
            if (fps != 30 && fps != 60) throw new ArgumentOutOfRangeException(nameof(fps), "출력 FPS는 30 또는 60이어야 합니다.");
            if (preset == Custom && (width < 16 || height < 16 || width > 7680 || height > 4320 || (width & 1) != 0 || (height & 1) != 0))
                throw new ArgumentOutOfRangeException(nameof(width), "출력 크기는 16~7680×16~4320의 짝수여야 합니다.");
        }
    }

    // [start, end) in integer ticks of 1/600000 s (exact for 30/60 fps and common audio rates).
    // frameCount = ceilDiv((end - start) x fps, ticksPerSecond); frame i is shown at the rational
    // time start + i / fps, evaluated from integers so no error accumulates over long outputs.
    public static class MmdOutputTiming
    {
        public const long TicksPerSecond = 600000;
        public static long ToTicks(double seconds) => (long)Math.Round(Math.Max(0, seconds) * TicksPerSecond);
        public static long FrameCount(long startTicks, long endTicks, int fps) =>
            endTicks <= startTicks ? 0 : CeilDiv((endTicks - startTicks) * fps, TicksPerSecond);
        public static double FrameSeconds(long startTicks, long index, int fps) =>
            (startTicks * (double)fps + index * (double)TicksPerSecond) / (TicksPerSecond * (double)fps);
        // Audio sample at which frame i starts: start * rate + i * rate / fps, floored.
        public static long AudioSample(long startTicks, long index, int fps, int rate) =>
            (startTicks * rate * fps + index * rate * TicksPerSecond) / (TicksPerSecond * fps);
        private static long CeilDiv(long a, long b) => (a + b - 1) / b;
    }
}
