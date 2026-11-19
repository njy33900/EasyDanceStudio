using System;
using UnityEngine;

namespace MmdStudio.Poc
{
    // MVP-03: pivot/yaw/pitch/distance orbit camera. Pure state; the workspace
    // decides when pointer input may reach it and applies it to Camera.main.
    public sealed class MmdViewportCamera
    {
        public const float MinDistance = .05f, MaxDistance = 500, PitchLimit = 89;
        public const float DegreesPerPixel = .3f, ZoomPerNotch = .88f;
        public Vector3 Pivot { get; private set; } = new Vector3(0, 1, 0);
        public float Yaw { get; private set; } = 180;
        public float Pitch { get; private set; } = 5;
        public float Distance { get; private set; } = 4;
        public Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0);
        public Vector3 Position => Pivot + Rotation * new Vector3(0, 0, -Distance);

        public const float DefaultYaw = 180, DefaultPitch = 5;
        public void Reset()
        {
            Pivot = new Vector3(0, 1, 0); Yaw = DefaultYaw; Pitch = DefaultPitch; Distance = 4;
        }
        public void Set(Vector3 pivot, float yaw, float pitch, float distance)
        {
            if (!Finite(pivot.x) || !Finite(pivot.y) || !Finite(pivot.z) || !Finite(yaw) || !Finite(pitch) || !Finite(distance))
                throw new ArgumentOutOfRangeException(nameof(pivot), "시점 값이 올바르지 않습니다.");
            Pivot = pivot; Yaw = Mathf.Repeat(yaw, 360);
            Pitch = Mathf.Clamp(pitch, -PitchLimit, PitchLimit); Distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public void Orbit(Vector2 pixels)
        {
            if (!Finite(pixels)) return;
            Yaw = Mathf.Repeat(Yaw + pixels.x * DegreesPerPixel, 360);
            Pitch = Mathf.Clamp(Pitch - pixels.y * DegreesPerPixel, -PitchLimit, PitchLimit);
        }
        // Moves pivot and camera together so the point under the cursor follows it.
        public void Pan(Vector2 pixels, float viewportPixelHeight, float verticalFov)
        {
            if (!Finite(pixels) || viewportPixelHeight <= 0) return;
            var worldPerPixel = 2 * Distance * Mathf.Tan(verticalFov * .5f * Mathf.Deg2Rad) / viewportPixelHeight;
            var rotation = Rotation;
            Pivot -= (rotation * Vector3.right * pixels.x + rotation * Vector3.up * pixels.y) * worldPerPixel;
        }
        public void Zoom(float notches)
        {
            if (float.IsNaN(notches) || float.IsInfinity(notches)) return;
            Distance = Mathf.Clamp(Distance * Mathf.Pow(ZoomPerNotch, Mathf.Clamp(notches, -10, 10)), MinDistance, MaxDistance);
        }
        // Keeps the current viewing angle; fits the bounding sphere into the vertical FOV.
        public void Frame(Bounds bounds, float verticalFov, float aspect)
        {
            var radius = Mathf.Max(.05f, bounds.extents.magnitude);
            var halfFov = Mathf.Max(1, verticalFov) * .5f * Mathf.Deg2Rad;
            var halfHorizontal = Mathf.Atan(Mathf.Tan(halfFov) * Mathf.Max(.1f, aspect));
            Pivot = bounds.center;
            Distance = Mathf.Clamp(radius / Mathf.Sin(Mathf.Min(halfFov, halfHorizontal)) * 1.05f, MinDistance, MaxDistance);
        }
        public void Apply(Camera camera)
        {
            if (camera == null) return;
            camera.transform.SetPositionAndRotation(Position, Rotation);
            camera.nearClipPlane = Mathf.Max(.005f, Distance * .002f);
            camera.farClipPlane = Mathf.Max(1000, Distance * 20);
        }
        private static bool Finite(Vector2 value) =>
            !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.x) && !float.IsInfinity(value.y);
    }
}
