using System;
using System.IO;
using System.Text;

namespace MmdStudio.Poc
{
    // H2a-03: our own RIFF/WAVE decoder (decision 11: WAV is the required format). Reading the
    // file ourselves gives the exact sample count and rate for sample-accurate seeking and
    // later output sync (H3-03), works the same on Mono and IL2CPP, and needs no streaming
    // request. Supported: PCM 8/16/24/32-bit integer, IEEE float 32/64-bit, plain or
    // WAVE_FORMAT_EXTENSIBLE, 1-8 channels, 8-192 kHz. Compressed WAV (ADPCM, MP3-in-WAV, ...)
    // is rejected with a message. Pure code: safe on a worker thread.
    public sealed class MmdWavData
    {
        public int Channels, Frequency, BitsPerSample;
        public bool Float;
        public long Frames;          // samples per channel
        public float[] Samples;      // interleaved, -1..1
        public double DurationSeconds => Frequency > 0 ? (double)Frames / Frequency : 0;
        public string Describe() => Frequency + " Hz · " + (Channels == 1 ? "모노" : Channels == 2 ? "스테레오" : Channels + "채널") +
            " · " + BitsPerSample + "비트" + (Float ? " float" : "");
    }

    public static class MmdWavReader
    {
        public const long MaxFileBytes = 2L * 1024 * 1024 * 1024 - 1;
        // Decoded memory cap: 30 minutes of 48 kHz stereo (about 660 MB of floats).
        public const long MaxSamples = 48000L * 2 * 60 * 30;
        private const ushort Pcm = 1, IeeeFloat = 3, Extensible = 0xFFFE;

        public static MmdWavData Read(string path)
        {
            var file = new FileInfo(Path.GetFullPath(path));
            if (!file.Exists) throw new FileNotFoundException("오디오 파일이 없습니다: " + file.FullName, file.FullName);
            if (!string.Equals(file.Extension, ".wav", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("지원하는 오디오 형식은 WAV(.wav)입니다: " + file.Name);
            if (file.Length > MaxFileBytes) throw new InvalidDataException("WAV 파일이 너무 큽니다(최대 2 GB): " + file.Name);
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (var reader = new BinaryReader(stream))
            {
                try { return Read(reader, stream.Length, file.Name); }
                catch (EndOfStreamException ex) { throw new InvalidDataException("WAV 파일이 손상되었습니다(헤더가 잘림): " + file.Name, ex); }
            }
        }

        public static MmdWavData Read(BinaryReader reader, long length, string name)
        {
            if (length < 12 || Tag(reader) != "RIFF") throw new InvalidDataException("WAV 파일이 아닙니다(RIFF 헤더 없음): " + name);
            reader.ReadUInt32(); // RIFF size: often wrong in streamed files, not trusted
            if (Tag(reader) != "WAVE") throw new InvalidDataException("WAV 파일이 아닙니다(WAVE 형식 아님): " + name);
            ushort format = 0, channels = 0, bits = 0, blockAlign = 0; uint rate = 0;
            var haveFormat = false;
            var stream = reader.BaseStream;
            while (stream.Position + 8 <= length)
            {
                var id = Tag(reader);
                long size = reader.ReadUInt32();
                var start = stream.Position;
                if (id == "fmt ")
                {
                    if (size < 16) throw new InvalidDataException("WAV 형식 정보가 손상되었습니다: " + name);
                    format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadUInt32();
                    reader.ReadUInt32(); blockAlign = reader.ReadUInt16(); bits = reader.ReadUInt16();
                    if (format == Extensible)
                    {
                        if (size < 40) throw new InvalidDataException("WAV 확장 형식 정보가 손상되었습니다: " + name);
                        reader.ReadUInt16(); reader.ReadUInt16(); reader.ReadUInt32(); // cbSize, valid bits, channel mask
                        format = reader.ReadUInt16(); // first two bytes of the sub-format GUID
                    }
                    haveFormat = true;
                }
                else if (id == "data")
                {
                    if (!haveFormat) throw new InvalidDataException("WAV 형식 정보(fmt)가 데이터보다 뒤에 있거나 없습니다: " + name);
                    // Streamed writers leave 0 or 0xFFFFFFFF; a truncated file is shorter than
                    // its header says. Use what is actually there.
                    var available = length - start;
                    if (size == 0 || size == uint.MaxValue || size > available) size = available;
                    return Decode(reader, size, format, channels, rate, bits, blockAlign, name);
                }
                var next = start + size + (size & 1); // chunks are word aligned
                if (next > length) break;
                stream.Position = next;
            }
            throw new InvalidDataException(haveFormat ? "WAV 파일에 소리 데이터(data)가 없습니다: " + name : "WAV 형식 정보(fmt)가 없습니다: " + name);
        }

        private static MmdWavData Decode(BinaryReader reader, long size, ushort format, ushort channels, uint rate, ushort bits, ushort blockAlign, string name)
        {
            if (format != Pcm && format != IeeeFloat)
                throw new InvalidDataException("압축된 WAV(형식 코드 " + format + ")는 지원하지 않습니다. PCM 또는 float WAV로 변환하세요: " + name);
            var isFloat = format == IeeeFloat;
            if (isFloat ? bits != 32 && bits != 64 : bits != 8 && bits != 16 && bits != 24 && bits != 32)
                throw new InvalidDataException("지원하지 않는 WAV 비트 깊이입니다(" + bits + "비트): " + name);
            if (channels < 1 || channels > 8) throw new InvalidDataException("지원하지 않는 채널 수입니다(" + channels + "): " + name);
            if (rate < 8000 || rate > 192000) throw new InvalidDataException("지원하지 않는 샘플레이트입니다(" + rate + " Hz): " + name);
            var bytes = bits / 8;
            if (blockAlign != bytes * channels) throw new InvalidDataException("WAV 블록 정렬 값이 올바르지 않습니다: " + name);
            var frames = size / blockAlign;
            if (frames == 0) throw new InvalidDataException("WAV 파일에 소리 데이터가 없습니다: " + name);
            if (frames * channels > MaxSamples) throw new InvalidDataException("오디오가 너무 깁니다(최대 48 kHz 스테레오 기준 30분): " + name);
            var samples = new float[frames * channels];
            var buffer = new byte[(1 << 16) - (1 << 16) % blockAlign];
            long written = 0, remaining = frames * blockAlign;
            while (remaining > 0)
            {
                var want = (int)Math.Min(buffer.Length, remaining);
                var got = Fill(reader, buffer, want);
                got -= got % bytes;
                if (got == 0) break;
                for (var i = 0; i < got; i += bytes) samples[written++] = Sample(buffer, i, bits, isFloat);
                remaining -= got;
            }
            if (written != samples.Length) Array.Resize(ref samples, (int)(written - written % channels));
            return new MmdWavData { Channels = channels, Frequency = (int)rate, BitsPerSample = bits, Float = isFloat, Frames = samples.Length / channels, Samples = samples };
        }

        private static int Fill(BinaryReader reader, byte[] buffer, int count)
        {
            var total = 0;
            while (total < count)
            {
                var read = reader.Read(buffer, total, count - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        private static float Sample(byte[] b, int i, int bits, bool isFloat)
        {
            if (isFloat)
            {
                var value = bits == 32 ? BitConverter.ToSingle(b, i) : (float)BitConverter.ToDouble(b, i);
                return float.IsNaN(value) ? 0 : Math.Max(-1f, Math.Min(1f, value));
            }
            switch (bits)
            {
                case 8: return (b[i] - 128) / 128f;                                   // unsigned
                case 16: return (short)(b[i] | b[i + 1] << 8) / 32768f;
                case 24: return ((b[i] | b[i + 1] << 8 | b[i + 2] << 16) << 8 >> 8) / 8388608f;
                default: return (b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24) / 2147483648f;
            }
        }

        private static string Tag(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));

        // ----- Writer (tests and fixtures): PCM 8/16/24-bit or 32-bit float -----
        public static void Write(string path, float[] interleaved, int channels, int frequency, int bits = 16, bool extensible = false)
        {
            if (bits != 8 && bits != 16 && bits != 24 && bits != 32) throw new ArgumentOutOfRangeException(nameof(bits));
            var isFloat = bits == 32;
            var bytes = bits / 8;
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                var data = interleaved.Length * bytes;
                var fmtSize = extensible ? 40 : 16;
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(4 + 8 + fmtSize + 8 + data);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt ")); writer.Write(fmtSize);
                writer.Write(extensible ? Extensible : isFloat ? IeeeFloat : Pcm); writer.Write((ushort)channels); writer.Write(frequency);
                writer.Write(frequency * channels * bytes); writer.Write((ushort)(channels * bytes)); writer.Write((ushort)bits);
                if (extensible)
                {
                    writer.Write((ushort)22); writer.Write((ushort)bits); writer.Write(channels == 2 ? 3u : 4u);
                    writer.Write(isFloat ? IeeeFloat : Pcm); // GUID 0000xxxx-0000-0010-8000-00aa00389b71
                    writer.Write(new byte[] { 0, 0, 0, 0, 0x10, 0, 0x80, 0, 0, 0xaa, 0, 0x38, 0x9b, 0x71 });
                }
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(data);
                foreach (var value in interleaved)
                {
                    var v = Math.Max(-1f, Math.Min(1f, value));
                    if (isFloat) writer.Write(v);
                    else if (bits == 8) writer.Write((byte)Math.Round(v * 127 + 128));
                    else if (bits == 16) writer.Write((short)Math.Round(v * 32767));
                    else { var s = (int)Math.Round(v * 8388607); writer.Write((byte)s); writer.Write((byte)(s >> 8)); writer.Write((byte)(s >> 16)); }
                }
            }
        }
    }
}
