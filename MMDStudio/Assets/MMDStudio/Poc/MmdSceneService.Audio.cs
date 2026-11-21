using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MmdStudio.Poc
{
    // H2a-03: one local audio track on the common scene clock.
    // - The scene clock stays the master (motions, physics bakes and later the offline output
    //   all follow it); the AudioSource follows the clock.
    // - Seeking sets the source position in samples: round(seconds x sample rate) (timeSamples).
    // - While playing, the source runs freely and is re-positioned only when it drifts more
    //   than ResyncSeconds from the clock (a frame hitch, a device change), so steady playback
    //   has no clicks.
    // - Speed 0.5-2x (decision 11): the pitch changes with the speed (AudioSource.pitch); Unity
    //   has no built-in pitch-preserving time stretch. The UI says so.
    // - Loop wraps the clock and restarts the audio at the wrapped time. Past the end of the
    //   audio (a motion longer than the song) the scene plays on silently.
    // - The scene duration is the longest of the motions and the audio.
    public sealed class MmdAudioTrack : IDisposable
    {
        public string Source { get; private set; }
        public AudioClip Clip { get; private set; }
        public int Frequency { get; private set; }
        public int Channels { get; private set; }
        public long Frames { get; private set; }
        public string Format { get; private set; }
        public double DurationSeconds => Frequency > 0 ? (double)Frames / Frequency : 0;

        // Decodes on a worker thread; the clip is created on the calling (main) thread.
        public static async Task<MmdAudioTrack> LoadAsync(string path, Action<string> stage, CancellationToken cancellationToken)
        {
            var full = Path.GetFullPath(path);
            stage?.Invoke("오디오 읽는 중: " + Path.GetFileName(full));
            var data = await Task.Run(() => MmdWavReader.Read(full), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            stage?.Invoke("오디오 준비 중: " + Path.GetFileName(full));
            return FromData(full, data);
        }

        public static MmdAudioTrack Load(string path) => FromData(Path.GetFullPath(path), MmdWavReader.Read(path));

        private static MmdAudioTrack FromData(string full, MmdWavData data)
        {
            if (data.Frames > int.MaxValue) throw new InvalidDataException("오디오가 너무 깁니다: " + Path.GetFileName(full));
            var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(full), (int)data.Frames, data.Channels, data.Frequency, false);
            try
            {
                if (!clip.SetData(data.Samples, 0)) throw new InvalidOperationException("오디오 데이터를 준비하지 못했습니다: " + Path.GetFileName(full));
            }
            catch { UnityEngine.Object.Destroy(clip); throw; }
            return new MmdAudioTrack { Source = full, Clip = clip, Frequency = data.Frequency, Channels = data.Channels, Frames = data.Frames, Format = data.Describe() };
        }

        public void Dispose()
        {
            if (Clip != null) UnityEngine.Object.Destroy(Clip);
            Clip = null;
        }
        public bool Disposed => Clip == null;
    }

    public sealed partial class MmdSceneService
    {
        // Larger than a few audio buffers (1024 samples at 44.1-48 kHz is about 21-23 ms), so
        // the buffer granularity of timeSamples never triggers a jump by itself.
        public const double ResyncSeconds = .08;
        public MmdAudioTrack Audio { get; private set; }
        private AudioSource audioSource;
        internal AudioSource AudioSourceForTest => audioSource;
        internal int AudioResyncs { get; private set; }
        private float audioVolume = 1;
        public float AudioVolume
        {
            get => audioVolume;
            set
            {
                if (float.IsNaN(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value), "음량은 0~100% 범위여야 합니다.");
                audioVolume = value; SyncAudio(false);
            }
        }
        private bool audioMuted;
        public bool AudioMuted { get => audioMuted; set { audioMuted = value; SyncAudio(false); } }
        // Sample the source is (or will start) at for the current clock time.
        public long AudioTargetSample => Audio == null ? 0 : (long)Math.Round(Seconds * Audio.Frequency);

        // Puts a track on the clock (null removes it) and returns the previous one, still alive,
        // for the caller to keep (undo) or dispose.
        public MmdAudioTrack SwapAudio(MmdAudioTrack track)
        {
            if (track != null && track.Disposed) throw new ArgumentException("해제된 오디오입니다.", nameof(track));
            var previous = Audio;
            if (audioSource != null) { audioSource.Stop(); audioSource.clip = null; }
            Audio = track;
            if (Duration <= 0) { playing = false; Seconds = 0; }
            Seek(Math.Min(Seconds, Duration));
            SyncAudio(true);
            return previous;
        }

        private void EnsureAudioSource()
        {
            if (audioSource != null) return;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false; audioSource.loop = false; audioSource.spatialBlend = 0;
            // The studio scene has no listener of its own; the clip is 2D, so any position works.
            if (FindAnyObjectByType<AudioListener>() == null)
                (Camera.main != null ? Camera.main.gameObject : gameObject).AddComponent<AudioListener>();
        }

        // Makes the source match the clock. `force` re-positions it even while it plays (a seek
        // or a loop wrap); otherwise only a drift beyond ResyncSeconds does.
        private void SyncAudio(bool force)
        {
            if (Audio == null)
            {
                if (audioSource != null && audioSource.isPlaying) audioSource.Stop();
                return;
            }
            EnsureAudioSource();
            if (audioSource.clip != Audio.Clip) { audioSource.Stop(); audioSource.clip = Audio.Clip; force = true; }
            audioSource.volume = audioVolume; audioSource.mute = audioMuted;
            audioSource.pitch = (float)speed;
            var target = AudioTargetSample;
            if (!playing || target >= Audio.Frames)
            {
                if (audioSource.isPlaying) audioSource.Pause();
                if (force || !playing) audioSource.timeSamples = (int)Math.Max(0, Math.Min(target, Audio.Frames - 1));
                return;
            }
            if (!audioSource.isPlaying)
            {
                audioSource.timeSamples = (int)target;
                audioSource.Play();
                return;
            }
            if (force || Math.Abs(audioSource.timeSamples - target) > ResyncSeconds * Audio.Frequency)
            {
                if (!force) AudioResyncs++;
                audioSource.timeSamples = (int)target;
            }
        }

        private void ReleaseAudio()
        {
            if (audioSource != null) { audioSource.Stop(); audioSource.clip = null; }
            Audio?.Dispose(); Audio = null;
        }
    }
}
