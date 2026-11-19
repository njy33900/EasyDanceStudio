using System;
using System.Collections.Generic;
using UMT;
using UnityEngine;

namespace MmdStudio.Poc
{
    // Evaluates UMT-parsed VMD keys using the Bezier-weighted quaternion path
    // inspected in babylon-mmd. No Euler or engine animation curves in this path.
    public sealed class MmdDirectBoneTracks
    {
        private readonly VMDBoneFrame[][] tracks;
        private readonly PMXImportResult model;

        public MmdDirectBoneTracks(VMDAnimation animation, PMXImportResult imported)
        {
            model = imported;
            tracks = new VMDBoneFrame[model.bones.Length][];
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            var groups = new Dictionary<uint, VMDBoneFrame>[tracks.Length];
            for (var i = 0; i < tracks.Length; i++)
            {
                var name = model.model.bones[i].originalName.ToString();
                if (!indices.ContainsKey(name)) indices.Add(name, i);
            }
            foreach (var frame in animation.boneFrames)
            {
                if (!indices.TryGetValue(frame.boneName.ToString(), out var index)) continue;
                if (groups[index] == null) groups[index] = new Dictionary<uint, VMDBoneFrame>();
                groups[index][frame.frame] = frame;
            }
            for (var i = 0; i < groups.Length; i++)
            {
                if (groups[i] == null) continue;
                var sorted = new List<VMDBoneFrame>(groups[i].Values);
                sorted.Sort((a, b) => a.frame.CompareTo(b.frame));
                // Explicit duplicate-key policy: last source record wins.
                tracks[i] = sorted.ToArray();
            }
        }

        public bool HasTrack(int bone) => tracks[bone] != null;

        public Quaternion Rotation(int bone, float seconds)
        {
            Bounds(bone, seconds * 30.0, out var a, out var b, out var t);
            return Interpolate(a, b, Weight(b.interpolation.rotation, t)) *
                   model.mmdTransformResult.transformManager.bones[bone].initialLocalRotation;
        }

        public void Apply(float seconds)
        {
            for (var i = 0; i < tracks.Length; i++)
            {
                if (tracks[i] == null) continue;
                var flags = model.model.bones[i].flags;
                Bounds(i, seconds * 30.0, out var a, out var b, out var t);
                if ((flags & PMXBone.Flags.Translatable) != 0)
                {
                    var p = new Vector3(
                        -Mathf.Lerp(a.position.x, b.position.x, Weight(b.interpolation.positionX, t)),
                        Mathf.Lerp(a.position.y, b.position.y, Weight(b.interpolation.positionY, t)),
                        -Mathf.Lerp(a.position.z, b.position.z, Weight(b.interpolation.positionZ, t))) * 0.08f;
                    model.bones[i].localPosition = model.mmdTransformResult.transformManager.bones[i].initialLocalPosition + p;
                }
                if ((flags & PMXBone.Flags.Rotatable) != 0)
                    model.bones[i].localRotation = Interpolate(a, b, Weight(b.interpolation.rotation, t)) *
                        model.mmdTransformResult.transformManager.bones[i].initialLocalRotation;
            }
        }

        private void Bounds(int bone, double frame, out VMDBoneFrame a, out VMDBoneFrame b, out float t)
        {
            var keys = tracks[bone];
            var low = 0;
            var high = keys.Length;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (keys[mid].frame <= frame) low = mid + 1;
                else high = mid;
            }
            if (low == keys.Length) { a = b = keys[keys.Length - 1]; t = 0; return; }
            b = keys[low];
            a = low == 0 ? new VMDBoneFrame { rotation = Unity.Mathematics.quaternion.identity } : keys[low - 1];
            t = b.frame == a.frame ? 0 : (float)Math.Max(0, Math.Min(1, (frame - a.frame) / (b.frame - a.frame)));
        }

        private static Quaternion Interpolate(VMDBoneFrame a, VMDBoneFrame b, float weight)
        {
            var x = Convert(a);
            var y = Convert(b);
            // Use the same Mathematics spherical evaluator as UMT's solver.
            // A measured Engine.Quaternion.Slerp sample instead matched nlerp
            // at dot=0.959, leaving a visible numeric approximation residual.
            var q = Unity.Mathematics.math.slerp(
                new Unity.Mathematics.quaternion(x.x, x.y, x.z, x.w),
                new Unity.Mathematics.quaternion(y.x, y.y, y.z, y.w), weight).value;
            return Quaternion.Normalize(new Quaternion(q.x, q.y, q.z, q.w));
        }

        private static Quaternion Convert(VMDBoneFrame frame)
        {
            var q = frame.rotation.value;
            return Quaternion.Normalize(new Quaternion(-q.x, q.y, -q.z, q.w));
        }

        private static float Weight(VMDBezierInterpolation curve, float x)
        {
            if (x <= 0 || x >= 1 || (curve.x1 == curve.y1 && curve.x2 == curve.y2)) return x;
            var low = 0f;
            var high = 1f;
            for (var i = 0; i < 24; i++)
            {
                var t = (low + high) * 0.5f;
                if (Bezier(t, curve.x1, curve.x2) < x) low = t;
                else high = t;
            }
            return Bezier((low + high) * 0.5f, curve.y1, curve.y2);
        }

        private static float Bezier(float t, float a, float b)
        {
            var s = 1 - t;
            return 3 * s * s * t * a + 3 * s * t * t * b + t * t * t;
        }
    }
}
