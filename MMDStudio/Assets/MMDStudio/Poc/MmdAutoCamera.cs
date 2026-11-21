using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MmdStudio.Poc
{
    // D-01: beat-based automatic camera. Shots start on bar lines (every 1, 2 or 4 bars; loud bars
    // may cut at the half bar), each from a template (front full body, upper body, close-up, slow
    // orbit) chosen by a seeded random generator weighted by the bar energy, framed on the target
    // character as it moves (sampled from the scene). Shake adds small per-beat offsets. The
    // result is a list of MMD camera keys (cuts = keys one frame apart), saved as a camera VMD the
    // user can edit elsewhere and played through the camera VMD path (H2a-04). Same input and seed
    // give the same keys.
    [Serializable]
    public sealed class AutoCameraSettings
    {
        public const int Version = 1;
        public int version = Version;
        public string target = "";   // character ID
        public int cutBars = 2;      // 1, 2 or 4
        public float shake = .2f;    // 0..1
        public int seed = 1;
        public double bpm;           // detected, for the record
        public string audio = "";    // stored path of the analysed audio
        public void Validate()
        {
            if (cutBars != 1 && cutBars != 2 && cutBars != 4) throw new ArgumentOutOfRangeException(nameof(cutBars), "컷 간격은 1·2·4마디여야 합니다.");
            if (float.IsNaN(shake) || shake < 0 || shake > 1) throw new ArgumentOutOfRangeException(nameof(shake), "흔들림은 0~1이어야 합니다.");
            if (target == null || target.Length > 64) throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    public static class MmdAutoCamera
    {
        public enum Shot { Wide, Upper, Close, Orbit }
        public sealed class Result
        {
            public List<MmdCameraKey> Keys = new List<MmdCameraKey>();
            public List<(double start, Shot shot)> Shots = new List<(double, Shot)>();
            public List<uint> CutFrames = new List<uint>();
        }
        // Body of the character at a time: feet (lowest point), height and facing (unit, horizontal).
        public struct Body { public Vector3 Centre; public float Feet, Height; public Vector3 Front; }

        public static Result Generate(MmdBeatInfo beats, AutoCameraSettings settings, Func<double, Body> body, double duration)
        {
            settings.Validate();
            var random = new System.Random(settings.seed);
            var result = new Result();
            // Shot boundaries: bar lines every cutBars bars, loud bars cut at the half bar.
            var bars = beats.Bars.Where(b => b < duration).ToList();
            if (bars.Count == 0 || bars[0] > 1e-6) bars.Insert(0, 0);
            var starts = new List<double>();
            for (var i = 0; i < bars.Count; i += settings.cutBars)
            {
                starts.Add(bars[i]);
                var energy = EnergyAt(beats, bars[i]);
                if (settings.cutBars >= 2 && energy > .75f && i + settings.cutBars / 2 < bars.Count) starts.Add(bars[i + settings.cutBars / 2]);
            }
            starts = starts.Distinct().OrderBy(t => t).ToList();
            var previous = (Shot)(-1);
            for (var s = 0; s < starts.Count; s++)
            {
                var start = starts[s]; var end = s + 1 < starts.Count ? starts[s + 1] : duration;
                if (end - start < .2) continue;
                var shot = Pick(random, EnergyAt(beats, start), previous);
                previous = shot;
                result.Shots.Add((start, shot));
                var startFrame = (uint)Math.Round(start * MmdCameraMotion.FramesPerSecond);
                var endFrame = (uint)Math.Round(end * MmdCameraMotion.FramesPerSecond);
                if (s > 0 && result.Keys.Count > 0 && startFrame > 0)
                {
                    // Hard cut: hold the previous shot up to the frame before.
                    var hold = result.Keys[result.Keys.Count - 1];
                    if (hold.Frame < startFrame - 1) result.Keys.Add(Copy(hold, startFrame - 1, Linear));
                    result.CutFrames.Add(startFrame);
                }
                var orbitDirection = random.NextDouble() < .5 ? -1 : 1;
                var orbitSweep = (float)(40 + 30 * random.NextDouble()) * orbitDirection;
                var yawBase = (float)((random.NextDouble() * 2 - 1) * (shot == Shot.Close ? 15 : 25));
                var pitchBase = (float)(random.NextDouble() * 14 - 4);
                // Keys at the shot start, on each beat inside (shake/orbit) and at the end.
                var times = new List<double> { start };
                if (shot == Shot.Orbit || settings.shake > 0) times.AddRange(beats.Beats.Where(b => b > start + .05 && b < end - .05));
                times.Add(Math.Max(start, end - 1.0 / MmdCameraMotion.FramesPerSecond));
                foreach (var t in times.Distinct().OrderBy(t => t))
                {
                    var fraction = end - start <= 0 ? 0 : (float)((t - start) / (end - start));
                    var yaw = yawBase + (shot == Shot.Orbit ? orbitSweep * (fraction - .5f) : 0);
                    var shake = settings.shake * (t == start ? 0 : 1);
                    var jitter = new Vector3((float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1));
                    var frame = (uint)Math.Round(t * MmdCameraMotion.FramesPerSecond);
                    if (result.Keys.Count > 0 && frame <= result.Keys[result.Keys.Count - 1].Frame) continue;
                    result.Keys.Add(Frame(shot, body(t), yaw, pitchBase, jitter * shake, frame));
                }
                _ = endFrame;
            }
            return result;
        }

        private static float EnergyAt(MmdBeatInfo beats, double time)
        {
            for (var i = beats.Bars.Length - 1; i >= 0; i--) if (beats.Bars[i] <= time + 1e-6) return i < beats.BarEnergy.Length ? beats.BarEnergy[i] : .5f;
            return beats.BarEnergy.Length > 0 ? beats.BarEnergy[0] : .5f;
        }

        private static Shot Pick(System.Random random, float energy, Shot previous)
        {
            // Quiet bars favour wide shots; loud bars favour close-ups and moving shots.
            var weights = new Dictionary<Shot, float>
            {
                { Shot.Wide, Mathf.Lerp(.45f, .15f, energy) }, { Shot.Upper, .3f },
                { Shot.Close, Mathf.Lerp(.05f, .3f, energy) }, { Shot.Orbit, Mathf.Lerp(.2f, .25f, energy) },
            };
            weights.Remove(previous);
            var total = weights.Values.Sum(); var roll = random.NextDouble() * total;
            foreach (var entry in weights) { roll -= entry.Value; if (roll <= 0) return entry.Key; }
            return weights.Keys.Last();
        }

        // Look point, framed height and field of view per template.
        private static MmdCameraKey Frame(Shot shot, Body body, float yawDegrees, float pitchDegrees, Vector3 jitter, uint frame)
        {
            float lookHeight, framed, fov;
            switch (shot)
            {
                case Shot.Close: lookHeight = .9f; framed = .3f; fov = 26; break;
                case Shot.Upper: lookHeight = .72f; framed = .6f; fov = 30; break;
                case Shot.Orbit: lookHeight = .55f; framed = 1.05f; fov = 32; break;
                default: lookHeight = .5f; framed = 1.25f; fov = 30; break;
            }
            var height = Mathf.Max(.3f, body.Height);
            var look = new Vector3(body.Centre.x, body.Feet + height * lookHeight, body.Centre.z) + jitter * height * .02f;
            var distance = height * framed * .5f / Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
            var front = Quaternion.Euler(0, yawDegrees, 0) * (body.Front.sqrMagnitude > 1e-6f ? body.Front : Vector3.forward);
            var direction = Quaternion.AngleAxis(pitchDegrees, Vector3.Cross(front, Vector3.up)) * front; // from look point to camera
            var camera = look + direction.normalized * distance + jitter * height * .03f;
            return Key(frame, camera, look, fov);
        }

        // A camera at `position` looking at `look`, as an MMD camera key (inverse of MmdCameraMotion).
        public static MmdCameraKey Key(uint frame, Vector3 position, Vector3 look, float fieldOfView)
        {
            var offset = look - position;
            var forward = offset.normalized;
            var pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1, 1));
            var yaw = -Mathf.Atan2(forward.x, forward.z) - Mathf.PI;
            yaw = Mathf.Repeat(yaw + Mathf.PI, 2 * Mathf.PI) - Mathf.PI;
            var key = new MmdCameraKey
            {
                Frame = frame, Distance = -offset.magnitude / MmdCameraMotion.MmdUnit,
                Target = new Vector3(-look.x, look.y, -look.z) / MmdCameraMotion.MmdUnit,
                Rotation = new Vector3(pitch, yaw, 0), ViewAngle = Mathf.Round(fieldOfView),
            };
            for (var c = 0; c < 6; c++) key.Curves[c] = Smooth;
            return key;
        }
        private static readonly Vector4 Smooth = new Vector4(.25f, .1f, .75f, .9f); // gentle ease in/out
        private static Vector4 Linear => MmdCameraMotion.LinearCurve;
        private static MmdCameraKey Copy(MmdCameraKey source, uint frame, Vector4 curve)
        {
            var key = new MmdCameraKey { Frame = frame, Distance = source.Distance, Target = source.Target, Rotation = source.Rotation, ViewAngle = source.ViewAngle };
            for (var c = 0; c < 6; c++) key.Curves[c] = curve;
            return key;
        }
    }
}
