using System;

namespace MmdStudio.Poc
{
    // H2a-04: the scene's camera VMD on the common clock. The service only owns the track and
    // its share of the scene length; the workspace decides whether the viewport camera follows it
    // (manual navigation has priority and releases it).
    public sealed partial class MmdSceneService
    {
        public MmdCameraMotion CameraMotion { get; private set; }

        // Puts a camera track on the clock (null removes it); returns the previous one.
        public MmdCameraMotion SwapCameraMotion(MmdCameraMotion motion)
        {
            var previous = CameraMotion;
            CameraMotion = motion;
            if (Duration <= 0) { playing = false; Seconds = 0; }
            Seek(Math.Min(Seconds, Duration));
            return previous;
        }
    }
}
