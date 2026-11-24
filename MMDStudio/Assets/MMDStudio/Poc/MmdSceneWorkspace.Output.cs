using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H3: output. One offline renderer for three formats:
    // - PNG (H3-01): the current scene time, chosen size, opaque.
    // - MP4 (H3-02/03): [start, end) at 30/60 fps, H.264 + AAC (scene audio) through Media Foundation.
    // - Transparent PNG sequence (H3-04): characters only (backgrounds, floor and sky excluded),
    //   straight (non-premultiplied) alpha.
    // Every frame is placed by its integer frame time, rendered from the scene camera into an
    // off-screen target of the output size (no UI, independent of the window and the frame rate),
    // after the physics bake is complete. Files are written to a temporary name in the target
    // folder and moved into place only when complete; cancel or failure removes them and the
    // original file (if any) stays. The user's scene time, playback, camera and selection come back.
    public sealed partial class MmdSceneWorkspace
    {
        public enum OutputFormat { Mp4, Png, PngSequence }
        internal OutputData Output { get; private set; } = new OutputData();
        internal bool Exporting { get; private set; }
        internal string LastOutputPath { get; private set; }
        internal int LastOutputFrames { get; private set; }
        internal double LastOutputSeconds { get; private set; }
        private VisualElement outputGuide, guideLeft, guideRight;
        private float lastFrameMilliseconds = 25;

        // ----- Scene settings -----
        internal void SetOutput(OutputData data)
        {
            var next = data.Clone(); next.Validate();
            Output = next; RefreshOutputGuide();
        }

        // ----- Viewport guide: the part of the camera image the output keeps -----
        private void BuildOutputGuide(VisualElement viewport)
        {
            outputGuide = new VisualElement { name = "OutputGuide", pickingMode = PickingMode.Ignore };
            outputGuide.style.position = Position.Absolute; outputGuide.style.top = outputGuide.style.bottom = 0;
            foreach (var side in new[] { "Left", "Right" })
            {
                var shade = new VisualElement { name = "OutputGuide" + side, pickingMode = PickingMode.Ignore };
                shade.style.position = Position.Absolute; shade.style.top = shade.style.bottom = 0;
                shade.style.backgroundColor = new Color(0, 0, 0, .18f);
                Root.Insert(0, shade);
                if (side == "Left") guideLeft = shade; else guideRight = shade;
            }
            outputGuide.style.borderLeftWidth = outputGuide.style.borderRightWidth = outputGuide.style.borderTopWidth = outputGuide.style.borderBottomWidth = 2;
            outputGuide.style.borderLeftColor = outputGuide.style.borderRightColor = outputGuide.style.borderTopColor = outputGuide.style.borderBottomColor = new Color(1, .55f, .1f, .9f);
            Root.Insert(0, outputGuide);
            Root.RegisterCallback<GeometryChangedEvent>(_ => RefreshOutputGuide());
        }
        // Output frame in panel coordinates (full camera, same vertical field of view).
        internal bool OutputGuideVisible => outputGuide != null && outputGuide.style.display.value == DisplayStyle.Flex;
        internal Rect OutputGuideScreenRect()
        {
            var aspect = Output.Aspect; var screen = Screen.width / (float)Mathf.Max(1, Screen.height);
            if (aspect >= screen - 1e-3f) return new Rect(0, 0, Screen.width, Screen.height);
            var width = Screen.height * aspect;
            return new Rect((Screen.width - width) * .5f, 0, width, Screen.height);
        }
        private void RefreshOutputGuide()
        {
            if (outputGuide == null || Root == null) return;
            var narrower = Output.Aspect < Screen.width / (float)Mathf.Max(1, Screen.height) - 1e-3f;
            var show = Output.guides && narrower && !FocusView && !Exporting;
            foreach (var element in new[] { outputGuide, guideLeft, guideRight }) Show(element, show);
            if (!show) return;
            var scale = Root.worldBound.width / Mathf.Max(1, Screen.width);
            var rect = OutputGuideScreenRect();
            outputGuide.style.left = rect.xMin * scale; outputGuide.style.width = rect.width * scale;
            guideLeft.style.left = 0; guideLeft.style.width = rect.xMin * scale;
            guideRight.style.left = rect.xMax * scale; guideRight.style.width = (Screen.width - rect.xMax) * scale;
        }

        // ----- Dialog -----
        private static readonly string[] FormatLabels = { "MP4 동영상", "PNG 이미지 (현재 프레임)", "투명 PNG 시퀀스" };
        private static readonly string[] PresetLabels = { "가로 1920×1080", "세로 1080×1920 (9:16)", "정사각 1080×1080", "사용자 지정" };
        private static readonly string[] PresetIds = { OutputData.Landscape, OutputData.Portrait, OutputData.Square, OutputData.Custom };
        internal void ShowOutputDialog()
        {
            if (Busy) return;
            var format = OutputFormat.Mp4;
            var draft = Output.Clone();
            double start = 0, end = Scene.Duration;
            ShowDialog("출력", () => { message = "출력을 취소했습니다."; },
                ("OutputStart", "출력", () => Guard(() =>
                {
                    SetOutput(draft);
                    StartOutput(format, draft, start, end, null);
                })),
                ("OutputCancel", "취소", null));
            dialogContent.style.display = DisplayStyle.Flex; dialogContent.parent.AddToClassList("eds-dialog--wide");
            var formatField = new DropdownField("형식", FormatLabels.ToList(), 0) { name = "OutputFormat" }; dialogContent.Add(formatField);
            var presetField = new DropdownField("화면비", PresetLabels.ToList(), Array.IndexOf(PresetIds, draft.preset)) { name = "OutputPreset" }; dialogContent.Add(presetField);
            var widthField = new IntegerField("너비") { name = "OutputWidth", value = draft.width }; dialogContent.Add(widthField);
            var heightField = new IntegerField("높이") { name = "OutputHeight", value = draft.height }; dialogContent.Add(heightField);
            var fpsField = new DropdownField("FPS", new List<string> { "60", "30" }, draft.fps == 30 ? 1 : 0) { name = "OutputFps" }; dialogContent.Add(fpsField);
            var startField = new DoubleField("시작 (초)") { name = "OutputStartSeconds", value = start }; dialogContent.Add(startField);
            var endField = new DoubleField("끝 (초)") { name = "OutputEndSeconds", value = end }; dialogContent.Add(endField);
            var guides = new Toggle("뷰포트에 출력 영역 표시 (세로·정사각)") { name = "OutputGuides", value = draft.guides }; dialogContent.Add(guides);
            var summary = Text(dialogContent, "", "eds-note"); summary.name = "OutputSummary";
            void Update()
            {
                format = (OutputFormat)Math.Max(0, Array.IndexOf(FormatLabels, formatField.value));
                draft.preset = PresetIds[Math.Max(0, Array.IndexOf(PresetLabels, presetField.value))];
                draft.width = widthField.value; draft.height = heightField.value; draft.fps = fpsField.value == "30" ? 30 : 60; draft.guides = guides.value;
                start = startField.value; end = endField.value;
                Show(widthField, draft.preset == OutputData.Custom); Show(heightField, draft.preset == OutputData.Custom);
                var range = format != OutputFormat.Png;
                Show(fpsField, range); Show(startField, range); Show(endField, range);
                summary.text = OutputSummary(format, draft, start, end);
            }
            formatField.RegisterValueChangedCallback(_ => Update()); presetField.RegisterValueChangedCallback(_ => Update());
            widthField.RegisterValueChangedCallback(_ => Update()); heightField.RegisterValueChangedCallback(_ => Update());
            fpsField.RegisterValueChangedCallback(_ => Update()); startField.RegisterValueChangedCallback(_ => Update());
            endField.RegisterValueChangedCallback(_ => Update()); guides.RegisterValueChangedCallback(_ => Update());
            Update();
        }

        internal string OutputSummary(OutputFormat format, OutputData data, double start, double end)
        {
            try { data.Validate(); } catch (ArgumentException ex) { return ex.Message; }
            var size = data.Size;
            if (format == OutputFormat.Png) return size.width + "×" + size.height + " · 현재 시간 " + Clock(Scene.Seconds) + " · UI 제외";
            var frames = MmdOutputTiming.FrameCount(MmdOutputTiming.ToTicks(start), MmdOutputTiming.ToTicks(end), data.fps);
            if (frames <= 0) return "끝 시간이 시작 시간보다 커야 합니다.";
            var estimate = frames * lastFrameMilliseconds / 1000;
            return size.width + "×" + size.height + " · " + data.fps + " FPS · 프레임 " + frames + "개 (" + Clock(end - start) + ")" +
                " · 예상 약 " + Math.Ceiling(estimate) + "초" + (data.fps == 60 ? " (30 FPS의 약 2배)" : "") +
                (format == OutputFormat.Mp4 ? (Scene.Audio != null ? " · 오디오 포함(AAC)" : " · 오디오 없음") + " · H.264" : " · 배경·바닥·하늘 제외, 알파 포함") +
                (Scene.PhysicsBaking ? " · 물리 계산을 먼저 마칩니다" : "");
        }

        // Asks for the destination, then runs the output (probes pass the path directly).
        internal void StartOutput(OutputFormat format, OutputData data, double start, double end, string path)
        {
            data.Validate();
            if (format != OutputFormat.Png && MmdOutputTiming.FrameCount(MmdOutputTiming.ToTicks(start), MmdOutputTiming.ToTicks(end), data.fps) <= 0)
                throw new ArgumentException("출력 구간이 비어 있습니다(끝 > 시작).");
            if (format != OutputFormat.Png && (start < 0 || end > Scene.Duration + 1e-9)) throw new ArgumentException("출력 구간은 0~" + Scene.Duration.ToString("0.###") + "초 안이어야 합니다.");
            if (path == null)
            {
                var suggested = (ProjectPath == null ? "출력" : Path.GetFileNameWithoutExtension(ProjectPath)) + (format == OutputFormat.Mp4 ? ".mp4" : ".png");
                path = FileDialogs.SaveOutput(suggested, format == OutputFormat.Mp4 ? "mp4" : "png");
                if (path == null) { message = "출력을 취소했습니다."; return; }
            }
            StartCoroutine(RunOutput(format, data.Clone(), start, end, Path.GetFullPath(path)));
        }

        // ----- Offline renderer -----
        internal IEnumerator RunOutput(OutputFormat format, OutputData data, double start, double end, string path)
        {
            if (Busy) yield break;
            var camera = Camera.main;
            var size = data.Size;
            var previousMessage = message;
            var state = (seconds: Scene.Seconds, playing: Scene.Playing, automatic: Scene.AutomaticClock);
            var clear = (camera.clearFlags, camera.backgroundColor, camera.allowHDR);
            var hidden = new List<Renderer>();
            var blends = new List<(Material material, float src, float dst)>();
            Busy = true; Exporting = true; Error = "";
            // v0.61: output always draws the scene's screen corrections ("보정 전 보기" is a preview aid).
            Scene.ShowCorrections(true);
            loadCancellation = new System.Threading.CancellationTokenSource();
            var token = loadCancellation.Token;
            Scene.Playing = false; Scene.AutomaticClock = false;
            highlight?.Hide(); RefreshOutputGuide();
            message = "출력 준비 중…"; Refresh(); RefreshLoadingCard();
            yield return null;
            var directory = Path.GetDirectoryName(path);
            var temporary = format == OutputFormat.PngSequence
                ? Path.Combine(directory, "." + Path.GetFileNameWithoutExtension(path) + "." + Guid.NewGuid().ToString("N") + ".tmp")
                : Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            MmdMediaWriter writer = null; AudioSlicer audio = null;
            RenderTexture target = null; Texture2D readback = null;
            Exception failure = null; var cancelled = false;
            var frames = format == OutputFormat.Png ? 1 : MmdOutputTiming.FrameCount(MmdOutputTiming.ToTicks(start), MmdOutputTiming.ToTicks(end), data.fps);
            var startTicks = MmdOutputTiming.ToTicks(format == OutputFormat.Png ? Scene.Seconds : start);
            var current = Scene.Seconds;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var written = new List<string>();
            Exception Step(Action action) { try { action(); return null; } catch (Exception ex) { return ex; } }
            try
            {
                failure = Step(() => { if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("출력 폴더가 없습니다: " + directory); });
                // Physics output goes through the bake cache: finish it first (spec 5, PH-03).
                while (failure == null && Scene.PhysicsBaking && !token.IsCancellationRequested)
                {
                    Scene.BakePhysicsSlice();
                    message = "출력 전 물리 계산 중…"; UpdateStatus(); RefreshLoadingCard();
                    yield return null;
                }
                if (token.IsCancellationRequested) cancelled = true;
                if (failure == null && !cancelled) failure = Step(() =>
                {
                    if (format == OutputFormat.PngSequence)
                    {
                        // Characters only: backgrounds hidden, transparent clear, LDR target keeps the alpha.
                        foreach (var item in Scene.Objects.Where(o => o.Kind == MmdSceneService.ObjectKind.Stage))
                            foreach (var renderer in item.Model.root.GetComponentsInChildren<Renderer>(true))
                                if (renderer.enabled) { renderer.enabled = false; hidden.Add(renderer); }
                        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0, 0, 0, 0); camera.allowHDR = false;
                        // Transparent parts must accumulate alpha (over: one, one minus source alpha).
                        foreach (var item in Scene.Objects)
                            foreach (var material in item.Model.root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials))
                                if (material != null && material.HasProperty("_SrcBlendAlpha") && material.HasProperty("_DstBlendAlpha") && material.renderQueue >= 2450)
                                {
                                    blends.Add((material, material.GetFloat("_SrcBlendAlpha"), material.GetFloat("_DstBlendAlpha")));
                                    material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                                    material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                                }
                        Directory.CreateDirectory(temporary);
                    }
                    target = new RenderTexture(size.width, size.height, 24, RenderTextureFormat.ARGB32) { name = "Output Target" };
                    readback = new Texture2D(size.width, size.height, format == OutputFormat.Mp4 ? TextureFormat.BGRA32 : TextureFormat.RGBA32, false);
                    if (format == OutputFormat.Mp4)
                    {
                        audio = Scene.Audio != null ? new AudioSlicer(Scene.Audio, Scene.AudioMuted ? 0 : Scene.AudioVolume) : null;
                        writer = new MmdMediaWriter(temporary, size.width, size.height, data.fps, 1, VideoBitrate(size.width, size.height, data.fps),
                            audio?.Rate ?? 0, audio?.Channels ?? 0);
                    }
                });
                for (long i = 0; i < frames && failure == null && !cancelled; i++)
                {
                    var index = i;
                    var seconds = format == OutputFormat.Png ? current : MmdOutputTiming.FrameSeconds(startTicks, index, data.fps);
                    failure = Step(() => { Scene.Seek(Math.Min(seconds, Scene.Duration)); ApplyCameraMotion(); UpdateDofFocus(); });
                    if (failure != null) break;
                    // SDEF skinning runs in LateUpdate: render after this frame's update.
                    yield return new WaitForEndOfFrame();
                    var frameClock = System.Diagnostics.Stopwatch.StartNew();
                    failure = Step(() =>
                    {
                        var previousTarget = camera.targetTexture;
                        camera.targetTexture = target;
                        try { camera.Render(); } finally { camera.targetTexture = previousTarget; }
                        var active = RenderTexture.active; RenderTexture.active = target;
                        readback.ReadPixels(new Rect(0, 0, size.width, size.height), 0, 0, false);
                        RenderTexture.active = active;
                        if (format == OutputFormat.Mp4)
                        {
                            writer.WriteVideo(readback.GetRawTextureData(), index);
                            if (audio != null)
                            {
                                var from = MmdOutputTiming.AudioSample(startTicks, index, data.fps, audio.Rate);
                                var to = MmdOutputTiming.AudioSample(startTicks, index + 1, data.fps, audio.Rate);
                                if (to > from) writer.WriteAudio(audio.Slice(from, (int)(to - from)), (int)(to - from));
                            }
                        }
                        else
                        {
                            if (format == OutputFormat.Png) ForceOpaque(readback); else Unpremultiply(readback);
                            var file = format == OutputFormat.Png ? temporary : Path.Combine(temporary, Path.GetFileNameWithoutExtension(path) + "_" + index.ToString("D6") + ".png");
                            File.WriteAllBytes(file, readback.EncodeToPNG());
                            written.Add(file);
                        }
                    });
                    lastFrameMilliseconds = Mathf.Lerp(lastFrameMilliseconds, (float)frameClock.Elapsed.TotalMilliseconds + Time.unscaledDeltaTime * 1000, .2f);
                    if (format != OutputFormat.Png)
                    {
                        var remaining = (frames - i - 1) * clock.Elapsed.TotalSeconds / (i + 1);
                        message = "출력 중 " + (i + 1) + "/" + frames + " (" + ((i + 1) * 100 / frames) + "%) · 남은 시간 약 " + Math.Ceiling(remaining) + "초";
                        UpdateStatus(); RefreshLoadingCard();
                    }
                    if (token.IsCancellationRequested) cancelled = true;
                }
                if (failure == null && !cancelled) failure = Step(() =>
                {
                    if (writer != null) { var finishing = writer; writer = null; finishing.Finish(); }
                    Commit(format, temporary, path, written);
                    LastOutputPath = path; LastOutputFrames = (int)frames; LastOutputSeconds = clock.Elapsed.TotalSeconds;
                });
            }
            finally
            {
                writer?.Dispose();
                if (failure != null || cancelled) RemoveTemporary(temporary);
                foreach (var renderer in hidden) if (renderer != null) renderer.enabled = true;
                foreach (var (material, src, dst) in blends) if (material != null) { material.SetFloat("_SrcBlendAlpha", src); material.SetFloat("_DstBlendAlpha", dst); }
                camera.clearFlags = clear.clearFlags; camera.backgroundColor = clear.backgroundColor; camera.allowHDR = clear.allowHDR;
                if (target != null) { target.Release(); Destroy(target); }
                if (readback != null) Destroy(readback);
                Scene.Seek(Math.Min(state.seconds, Scene.Duration));
                Scene.AutomaticClock = state.automatic; Scene.ResumeAfterInterruption(state.playing);
                loadCancellation?.Dispose(); loadCancellation = null;
                Busy = false; Exporting = false;
                Error = failure == null ? "" : "출력하지 못했습니다: " + failure.Message + " · 기존 파일은 그대로입니다.";
                message = cancelled ? "출력을 취소했습니다. 만들던 파일은 지웠습니다." : failure != null ? previousMessage :
                    "출력했습니다: " + Path.GetFileName(path) + (format == OutputFormat.PngSequence ? " 외 " + (frames - 1) + "개" : "") + " · " + clock.Elapsed.TotalSeconds.ToString("0.0") + "초";
                LastOutputCancelled = cancelled; LastOutputError = failure?.Message;
                RefreshOutputGuide(); Refresh(); RefreshLoadingCard();
            }
        }
        internal bool LastOutputCancelled { get; private set; }
        internal string LastOutputError { get; private set; }

        // About 0.125 bit per pixel per frame (1080p60: 15.6 Mbit/s), at most 80 Mbit/s.
        private static int VideoBitrate(int width, int height, int fps) => (int)Math.Min(80_000_000L, (long)width * height * fps / 8);

        private static void Commit(OutputFormat format, string temporary, string path, List<string> written)
        {
            if (format == OutputFormat.PngSequence)
            {
                // All or nothing (v0.6 review 1): the whole sequence exists before any file reaches
                // the target folder; existing frames of the same names are moved aside first, the new
                // ones moved in, and any failure (a locked or read-only frame, a full disk) puts every
                // file back as it was. The set-aside frames are deleted only after success.
                var directory = Path.GetDirectoryName(path);
                var aside = temporary + ".previous";
                var setAside = new List<(string final, string backup)>(); var placed = new List<string>();
                try
                {
                    foreach (var file in written)
                    {
                        var final = Path.Combine(directory, Path.GetFileName(file));
                        if (!File.Exists(final)) continue;
                        Directory.CreateDirectory(aside);
                        var backup = Path.Combine(aside, Path.GetFileName(file));
                        File.Move(final, backup); setAside.Add((final, backup));
                    }
                    foreach (var file in written)
                    {
                        var final = Path.Combine(directory, Path.GetFileName(file));
                        File.Move(file, final); placed.Add(final);
                    }
                }
                catch (Exception ex)
                {
                    foreach (var final in placed) { try { File.Delete(final); } catch (Exception cleanup) { Debug.LogWarning("Output rollback: " + cleanup.Message); } }
                    var kept = 0;
                    foreach (var (final, backup) in setAside)
                    {
                        try { File.Move(backup, final); } catch (Exception restore) { kept++; Debug.LogWarning("Output rollback: " + restore.Message); }
                    }
                    if (kept == 0) { try { if (Directory.Exists(aside)) Directory.Delete(aside, true); } catch (Exception cleanup) { Debug.LogWarning("Output rollback: " + cleanup.Message); } }
                    throw new IOException(ex.Message + (kept > 0 ? " · 되돌리지 못한 기존 프레임 " + kept + "개는 " + aside + "에 있습니다" : ""), ex);
                }
                if (Directory.Exists(aside)) Directory.Delete(aside, true);
                Directory.Delete(temporary, true);
                return;
            }
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        private static void RemoveTemporary(string temporary)
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            }
            catch (Exception ex) { Debug.LogWarning("Output temporary not removed: " + ex.Message); }
        }

        // Opaque PNG: alpha is 1 everywhere (the policy for single images).
        private static void ForceOpaque(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            for (var i = 0; i < pixels.Length; i++) pixels[i].a = 255;
            texture.SetPixels32(pixels); texture.Apply(false);
        }
        // Transparent sequence: the render over a clear (0,0,0,0) background is premultiplied;
        // PNG stores straight alpha, so colour is divided by alpha.
        private static void Unpremultiply(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (p.a == 0) { pixels[i] = new Color32(0, 0, 0, 0); continue; }
                if (p.a == 255) continue;
                pixels[i] = new Color32((byte)Math.Min(255, p.r * 255 / p.a), (byte)Math.Min(255, p.g * 255 / p.a), (byte)Math.Min(255, p.b * 255 / p.a), p.a);
            }
            texture.SetPixels32(pixels); texture.Apply(false);
        }

        // Scene audio as 16-bit PCM by sample index (silence outside the clip). Sources at 44.1 or
        // 48 kHz are passed through; other rates are resampled (linear) to 48 kHz. More than two
        // channels are mixed down to stereo (the AAC encoder takes 1 or 2).
        private sealed class AudioSlicer
        {
            private readonly AudioClip clip;
            private readonly float volume;
            private readonly int sourceRate, sourceChannels;
            public int Rate { get; }
            public int Channels { get; }
            public AudioSlicer(MmdAudioTrack track, float volume)
            {
                clip = track.Clip; this.volume = volume;
                sourceRate = track.Frequency; sourceChannels = track.Channels;
                Rate = sourceRate == 44100 || sourceRate == 48000 ? sourceRate : 48000;
                Channels = Math.Min(2, sourceChannels);
            }
            public short[] Slice(long from, int frames)
            {
                var result = new short[Math.Max(0, frames) * Channels];
                if (frames <= 0) return result;
                var ratio = sourceRate / (double)Rate;
                var first = (long)Math.Floor(from * ratio);
                var last = (long)Math.Ceiling((from + frames) * ratio) + 1;
                var begin = Math.Max(0, first); var stop = Math.Min(clip.samples, last);
                var source = new float[Math.Max(0, stop - begin) * sourceChannels];
                if (source.Length > 0) clip.GetData(source, (int)begin);
                float At(long frame, int channel)
                {
                    if (frame < begin || frame >= stop) return 0;
                    var index = (frame - begin) * sourceChannels;
                    if (Channels == 2 && sourceChannels > 2) return channel == 0 ? source[index] : source[index + 1]; // front left/right
                    return source[index + Math.Min(channel, sourceChannels - 1)];
                }
                for (var i = 0; i < frames; i++)
                {
                    var position = (from + i) * ratio; var whole = (long)Math.Floor(position); var fraction = (float)(position - whole);
                    for (var c = 0; c < Channels; c++)
                    {
                        var value = (At(whole, c) * (1 - fraction) + At(whole + 1, c) * fraction) * volume;
                        result[i * Channels + c] = (short)Mathf.Clamp(Mathf.RoundToInt(value * 32767), -32768, 32767);
                    }
                }
                return result;
            }
        }
    }
}
