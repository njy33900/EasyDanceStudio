using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MmdStudio.Poc
{
    // H2a-04: camera VMD on the common scene clock. Our own reader for the camera section keeps
    // the six interpolation curves separate (X, Y, Z, rotation, distance, view angle); UMT's reader
    // merges X/Y/Z into one "movement" curve and its converter bakes 60 Hz samples. Evaluation is
    // continuous in time, so offline output (H3) gets the exact pose at any frame time.
    // Conventions follow UMT (VMDAnimationClipConverter.BakeCameraFrames, MIT, Candidum Games):
    // MMD units x 0.08, X/Z negated, the camera sits on the rig's local Z at the key's distance
    // and looks along the rig's forward; keys one VMD frame apart are hard cuts. Unlike UMT the
    // distance keeps its sign (a positive MMD distance puts the camera in front of the target).
    // Not supported (reported as warnings, the rest still plays): orthographic projection keys
    // (shown in perspective); light and self-shadow keys; bone/morph keys in the same file.
    public sealed class MmdCameraKey
    {
        public uint Frame;
        public float Distance;
        public Vector3 Target;     // MMD space
        public Vector3 Rotation;   // radians, MMD space
        public float ViewAngle;    // degrees
        public bool Orthographic;
        // Bezier control points (x1, y1, x2, y2), 0..1, for X, Y, Z, rotation, distance, view angle.
        public readonly Vector4[] Curves = new Vector4[6];
    }

    public sealed class MmdCameraMotion
    {
        public const float MmdUnit = .08f;           // UMT MMDConstants.k_MMDUnitToUnityUnit
        public const float FramesPerSecond = 30;
        public const long MaxFileBytes = 64L * 1024 * 1024;
        public const uint MaxFrame = 30 * 60 * 30;   // 30 minutes
        public string Source { get; private set; }
        public IReadOnlyList<MmdCameraKey> Keys => keys;
        private MmdCameraKey[] keys;
        public string[] Warnings { get; private set; } = new string[0];
        public double DurationSeconds => keys[keys.Length - 1].Frame / (double)FramesPerSecond;

        public static MmdCameraMotion Load(string path)
        {
            var file = new FileInfo(Path.GetFullPath(path));
            if (!file.Exists) throw new FileNotFoundException("카메라 VMD 파일이 없습니다: " + file.FullName, file.FullName);
            if (!string.Equals(file.Extension, ".vmd", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("카메라 모션은 VMD(.vmd) 파일이어야 합니다: " + file.Name);
            if (file.Length < 50 || file.Length > MaxFileBytes) throw new InvalidDataException("카메라 VMD 크기가 올바르지 않습니다(최대 64 MB): " + file.Name);
            try
            {
                using (var stream = file.OpenRead())
                using (var reader = new BinaryReader(stream))
                {
                    var motion = Read(reader, stream.Length, file.Name);
                    motion.Source = file.FullName;
                    return motion;
                }
            }
            catch (EndOfStreamException ex) { throw new InvalidDataException("카메라 VMD 파일이 손상되었습니다(예상보다 짧음): " + file.Name, ex); }
        }

        private static MmdCameraMotion Read(BinaryReader reader, long length, string name)
        {
            var header = Encoding.ASCII.GetString(reader.ReadBytes(30)).TrimEnd('\0');
            if (!header.StartsWith("Vocaloid Motion Data")) throw new InvalidDataException("VMD 파일이 아닙니다: " + name);
            var nameLength = header.StartsWith("Vocaloid Motion Data 0002") ? 20 : 10;
            reader.ReadBytes(nameLength);
            var stream = reader.BaseStream;
            var bones = Skip(reader, length, 111, name);
            var morphs = Skip(reader, length, 23, name);
            if (stream.Position + 4 > length) throw new InvalidDataException("카메라 키가 없습니다(캐릭터 모션 VMD입니다): " + name);
            var count = reader.ReadUInt32();
            if (count == 0) throw new InvalidDataException("카메라 키가 없습니다(캐릭터 모션 VMD입니다): " + name);
            if (count > 1000000 || stream.Position + count * 61L > length) throw new InvalidDataException("카메라 키 수가 올바르지 않습니다: " + name);
            var list = new MmdCameraKey[count];
            for (var i = 0; i < count; i++)
            {
                var key = new MmdCameraKey
                {
                    Frame = reader.ReadUInt32(), Distance = reader.ReadSingle(),
                    Target = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    Rotation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                };
                var raw = reader.ReadBytes(24);
                for (var c = 0; c < 6; c++)  // per channel: x1, x2, y1, y2
                    key.Curves[c] = new Vector4(raw[c * 4] / 127f, raw[c * 4 + 2] / 127f, raw[c * 4 + 1] / 127f, raw[c * 4 + 3] / 127f);
                key.ViewAngle = reader.ReadUInt32();
                key.Orthographic = reader.ReadByte() != 0;
                if (key.Frame > MaxFrame) throw new InvalidDataException("카메라 VMD가 너무 깁니다(최대 30분): " + name);
                foreach (var value in new[] { key.Distance, key.Target.x, key.Target.y, key.Target.z, key.Rotation.x, key.Rotation.y, key.Rotation.z })
                    if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 1e6f) throw new InvalidDataException("카메라 VMD에 잘못된 숫자가 있습니다: " + name);
                list[i] = key;
            }
            // Optional trailing sections: light (28 bytes) and self shadow (9 bytes).
            var lights = stream.Position + 4 <= length ? Skip(reader, length, 28, name, optional: true) : 0;
            var shadows = stream.Position + 4 <= length ? Skip(reader, length, 9, name, optional: true) : 0;
            // Later keys win on duplicate frames (stable sort keeps file order).
            var sorted = list.Select((key, index) => (key, index)).OrderBy(p => p.key.Frame).ThenBy(p => p.index).Select(p => p.key).ToList();
            for (var i = sorted.Count - 2; i >= 0; i--) if (sorted[i].Frame == sorted[i + 1].Frame) sorted.RemoveAt(i);
            var warnings = new List<string>();
            var orthographic = sorted.Count(k => k.Orthographic);
            if (orthographic > 0) warnings.Add("직교 투영 키 " + orthographic + "개는 원근으로 표시합니다");
            var oddAngles = sorted.Count(k => k.ViewAngle < 1 || k.ViewAngle > 179);
            if (oddAngles > 0) warnings.Add("시야각이 1~179도를 벗어난 키 " + oddAngles + "개는 범위 안으로 맞춥니다");
            if (lights + shadows > 0) warnings.Add("조명·셀프 섀도 키 " + (lights + shadows) + "개는 사용하지 않습니다");
            if (bones + morphs > 0) warnings.Add("같은 파일의 본·모프 키 " + (bones + morphs) + "개는 카메라 모션에 쓰지 않습니다");
            return new MmdCameraMotion { keys = sorted.ToArray(), Warnings = warnings.ToArray() };
        }

        private static uint Skip(BinaryReader reader, long length, int size, string name, bool optional = false)
        {
            var stream = reader.BaseStream;
            if (stream.Position + 4 > length) { if (optional) return 0; throw new InvalidDataException("VMD 파일이 손상되었습니다: " + name); }
            var count = reader.ReadUInt32();
            if (stream.Position + (long)count * size > length) { if (optional) return 0; throw new InvalidDataException("VMD 파일이 손상되었습니다: " + name); }
            stream.Position += (long)count * size;
            return count;
        }

        // ----- Evaluation -----
        public struct Pose
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float FieldOfView;
            public Vector3 Target;     // Unity space, the point the camera turns around
            public float Distance;     // Unity units, signed along the rig's local Z
        }

        public Pose Evaluate(double seconds)
        {
            var frame = Math.Max(0, seconds) * FramesPerSecond;
            // A key time given in seconds (101 / 30) comes back as 100.99999...: snap to the integer
            // frame so the key (and a hard cut starting there) is not missed.
            var whole = Math.Round(frame);
            if (Math.Abs(frame - whole) < 1e-6) frame = whole;
            // Last key at or before the frame.
            int low = 0, high = keys.Length;
            while (low < high) { var mid = (low + high) / 2; if (keys[mid].Frame <= frame) low = mid + 1; else high = mid; }
            var index = Math.Max(0, low - 1);
            var previous = keys[index];
            Vector3 target, rotation; float distance, angle;
            if (index + 1 >= keys.Length || frame < previous.Frame)
            {
                target = previous.Target; rotation = previous.Rotation; distance = previous.Distance; angle = previous.ViewAngle;
            }
            else
            {
                var next = keys[index + 1];
                float range = next.Frame - previous.Frame;
                // Hard cut: keys one frame apart hold the earlier key for the whole frame (MMD).
                var t = range == 1 ? 0 : (float)((frame - previous.Frame) / range);
                float At(int channel) => Bezier(next.Curves[channel], t);
                target = new Vector3(Mathf.LerpUnclamped(previous.Target.x, next.Target.x, At(0)),
                                     Mathf.LerpUnclamped(previous.Target.y, next.Target.y, At(1)),
                                     Mathf.LerpUnclamped(previous.Target.z, next.Target.z, At(2)));
                var r = At(3);
                rotation = new Vector3(previous.Rotation.x + DeltaAngle(previous.Rotation.x, next.Rotation.x) * r,
                                       previous.Rotation.y + DeltaAngle(previous.Rotation.y, next.Rotation.y) * r,
                                       previous.Rotation.z + DeltaAngle(previous.Rotation.z, next.Rotation.z) * r);
                distance = Mathf.LerpUnclamped(previous.Distance, next.Distance, At(4));
                angle = Mathf.LerpUnclamped(previous.ViewAngle, next.ViewAngle, At(5));
            }
            var centre = new Vector3(-target.x, target.y, -target.z) * MmdUnit;
            var orientation = UnityRotation(rotation);
            var offset = distance * MmdUnit;
            return new Pose
            {
                Target = centre, Rotation = orientation, Distance = offset,
                Position = centre + orientation * new Vector3(0, 0, offset),
                FieldOfView = Mathf.Clamp(angle, 1, 179),
            };
        }

        // VMD Bezier: solve x(t) = time by bisection (12 steps, as UMT), return y(t).
        internal static float Bezier(Vector4 curve, float time)
        {
            float lower = 0, upper = 1, t = time;
            for (var i = 0; i < 12; i++)
            {
                if (Cubic(curve.x, curve.z, t) < time) lower = t; else upper = t;
                t = (lower + upper) * .5f;
            }
            return Cubic(curve.y, curve.w, t);
        }
        private static float Cubic(float p1, float p2, float t)
        {
            var u = 1 - t;
            return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
        }
        private static float DeltaAngle(float current, float target)
        {
            var delta = (target - current) - 2 * Mathf.PI * Mathf.Floor((target - current) / (2 * Mathf.PI));
            return delta > Mathf.PI ? delta - 2 * Mathf.PI : delta;
        }
        // MMD camera Euler (radians) -> Unity rotation of the rig (UMT EulerToUnityCameraRotation).
        private static Quaternion UnityRotation(Vector3 euler)
        {
            var pitch = -euler.x; var yaw = euler.y + Mathf.PI; var roll = euler.z;
            float sinX = Mathf.Sin(-pitch), cosX = Mathf.Cos(-pitch), sinY = Mathf.Sin(-yaw), cosY = Mathf.Cos(-yaw), sinZ = Mathf.Sin(roll), cosZ = Mathf.Cos(roll);
            Vector3 Basis(Vector3 v)
            {
                v = new Vector3(v.x * cosZ - v.y * sinZ, v.x * sinZ + v.y * cosZ, v.z);
                v = new Vector3(v.x, v.y * cosX - v.z * sinX, v.y * sinX + v.z * cosX);
                return new Vector3(v.x * cosY + v.z * sinY, v.y, -v.x * sinY + v.z * cosY);
            }
            var up = Basis(Vector3.up); var forward = Basis(Vector3.forward);
            return Quaternion.LookRotation(new Vector3(forward.x, -forward.y, forward.z), new Vector3(-up.x, up.y, -up.z));
        }

        // ----- Writer (tests): a camera-only VMD -----
        public static void Write(string path, IList<MmdCameraKey> cameraKeys, int lightKeys = 0)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(Encoding.ASCII.GetBytes("Vocaloid Motion Data 0002".PadRight(30, '\0')));
                writer.Write(new byte[20]);
                writer.Write(0u); writer.Write(0u); // bones, morphs
                writer.Write((uint)cameraKeys.Count);
                foreach (var key in cameraKeys)
                {
                    writer.Write(key.Frame); writer.Write(key.Distance);
                    writer.Write(key.Target.x); writer.Write(key.Target.y); writer.Write(key.Target.z);
                    writer.Write(key.Rotation.x); writer.Write(key.Rotation.y); writer.Write(key.Rotation.z);
                    for (var c = 0; c < 6; c++)
                    {
                        var curve = key.Curves[c];
                        writer.Write((byte)Mathf.RoundToInt(curve.x * 127)); writer.Write((byte)Mathf.RoundToInt(curve.z * 127));
                        writer.Write((byte)Mathf.RoundToInt(curve.y * 127)); writer.Write((byte)Mathf.RoundToInt(curve.w * 127));
                    }
                    writer.Write((uint)key.ViewAngle); writer.Write((byte)(key.Orthographic ? 1 : 0));
                }
                writer.Write((uint)lightKeys);
                for (var i = 0; i < lightKeys; i++) { writer.Write((uint)i); writer.Write(new byte[24]); }
                writer.Write(0u); // self shadow
            }
        }
        public static Vector4 LinearCurve => new Vector4(20 / 127f, 20 / 127f, 107 / 127f, 107 / 127f);
    }
}
