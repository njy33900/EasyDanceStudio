using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H2a-03: the scene's audio track in the explorer ("환경 > 오디오") and inspector: connect a
    // WAV, remove it, volume and mute; a missing WAV is kept as an unresolved reference like
    // a missing model (R0-02a). Connect/remove are undoable and keep the decoded clip alive
    // while the history needs it.
    public sealed partial class MmdSceneWorkspace
    {
        private VisualElement audioSection;
        private Label audioInfo, audioPitchChip;
        private Button audioConnect, audioRemove;
        private Slider audioVolume;
        private Toggle audioMute;
        private sealed class UnresolvedAudio
        {
            public string Stored, BaseScene, Reason;
        }
        private UnresolvedAudio unresolvedAudio;
        internal string UnresolvedAudioPath => unresolvedAudio?.Stored;

        private sealed class AudioCommand : UndoCommand
        {
            public MmdAudioTrack Previous, Next; public UnresolvedAudio PreviousMissing;
            public override void Undo(MmdSceneWorkspace w) { w.Scene.SwapAudio(Previous); w.unresolvedAudio = PreviousMissing; }
            public override void Redo(MmdSceneWorkspace w) { w.Scene.SwapAudio(Next); w.unresolvedAudio = null; }
            public override void Drop(MmdSceneWorkspace w, bool applied) => w.DiscardAudio(applied ? Previous : Next);
        }
        // Disposes a track unless the scene still plays it.
        private void DiscardAudio(MmdAudioTrack track)
        {
            if (track != null && track != Scene.Audio) track.Dispose();
        }

        private void BuildAudioSection(VisualElement content)
        {
            audioSection = Section(content, "AudioSection", "오디오");
            audioInfo = Text(audioSection, "", "eds-section-text"); audioInfo.name = "AudioInfo";
            var row = Element(audioSection, "eds-row-buttons");
            audioConnect = TextButton(row, "AudioConnect", "WAV 연결", () => Pick(PickKind.Audio), "eds-button--block");
            audioConnect.style.marginRight = 6;
            audioRemove = TextButton(row, "AudioRemove", "오디오 제거", () => Guard(RemoveAudio), "eds-button--block", "eds-button--danger");
            audioVolume = new Slider("음량", 0, 100) { name = "AudioVolume", showInputField = true }; audioVolume.AddToClassList("eds-graphics-slider");
            var input = audioVolume.Q<TextField>(); if (input != null) input.isDelayed = true;
            audioSection.Add(audioVolume);
            audioVolume.RegisterValueChangedCallback(evt => Guard(() => Record("오디오 음량", null, () => Scene.AudioVolume = Mathf.Clamp01(Mathf.Round(evt.newValue) / 100f), coalesce: true)));
            audioMute = new Toggle("음소거") { name = "AudioMute" }; audioSection.Add(audioMute);
            audioMute.RegisterValueChangedCallback(evt => Guard(() => Record("음소거", null, () => Scene.AudioMuted = evt.newValue)));
            Text(audioSection, "장면 시간에 맞춰 재생되고, 시간 이동은 샘플 단위로 맞춥니다. 재생 속도가 1배가 아니면 음높이도 함께 바뀝니다. 지원 형식: WAV(PCM·float).", "eds-note").name = "AudioNote";
        }

        // Playback bar notice: speed != 1 with audio changes the pitch (decision 11).
        private void BuildAudioPitchChip(VisualElement options)
        {
            audioPitchChip = Text(options, "음높이 변경", "eds-chip"); audioPitchChip.name = "AudioPitchChip";
            audioPitchChip.tooltip = "재생 속도가 1배가 아니면 오디오의 음높이도 함께 바뀝니다.";
            audioPitchChip.style.marginLeft = 6;
            Show(audioPitchChip, false);
        }
        internal bool AudioPitchNoticeVisible => audioPitchChip != null && audioPitchChip.style.display.value == DisplayStyle.Flex;
        private void UpdateAudioPitchChip()
        {
            if (audioPitchChip == null) return;
            var show = Scene.Audio != null && Math.Abs(Scene.Speed - 1) > 1e-6;
            if (AudioPitchNoticeVisible != show) Show(audioPitchChip, show);
        }

        private string AudioExplorerLabel =>
            Scene.Audio != null ? "오디오 · " + Path.GetFileNameWithoutExtension(Scene.Audio.Source) :
            unresolvedAudio != null ? "오디오 (누락)" : "오디오 (없음)";

        private void RefreshAudio()
        {
            if (audioSection == null) return;
            Show(audioSection, Target == InspectorTarget.Audio);
            var track = Scene.Audio;
            audioInfo.text = track != null
                ? Path.GetFileName(track.Source) + "\n길이 " + Clock(track.DurationSeconds) + " · " + track.Format
                : unresolvedAudio != null ? "WAV 누락: " + unresolvedAudio.Stored + " (" + unresolvedAudio.Reason + ")\n다른 WAV를 연결하면 대체됩니다. 연결 전에는 저장해도 경로가 유지됩니다."
                : "연결된 오디오가 없습니다. WAV 파일을 연결하면 장면 시간에 맞춰 재생됩니다.";
            audioConnect.SetEnabled(!Busy);
            audioRemove.SetEnabled(!Busy && (track != null || unresolvedAudio != null));
            audioVolume.SetEnabled(!Busy && track != null); audioMute.SetEnabled(!Busy && track != null);
            audioVolume.SetValueWithoutNotify(Mathf.Round(Scene.AudioVolume * 100));
            audioMute.SetValueWithoutNotify(Scene.AudioMuted);
            UpdateAudioPitchChip();
        }

        private void RemoveAudio()
        {
            if (Scene.Audio == null && unresolvedAudio == null) return;
            var command = new AudioCommand { Label = "오디오 제거", Previous = Scene.Audio, Next = null, PreviousMissing = unresolvedAudio };
            command.Redo(this); Push(command);
            message = "오디오를 장면에서 제거했습니다. 실행 취소(Ctrl+Z)로 되돌릴 수 있습니다.";
        }

        // Connects a decoded track (picker result); replaces the current or missing audio.
        private void ConnectAudio(MmdAudioTrack track)
        {
            var command = new AudioCommand { Label = "오디오 연결", Previous = Scene.Audio, Next = track, PreviousMissing = unresolvedAudio };
            command.Redo(this); Push(command);
            message = "오디오를 연결했습니다: " + Path.GetFileName(track.Source) + " · " + Clock(track.DurationSeconds);
        }

        // New scene / opened scene: the audio state is replaced outside the history.
        private void ResetAudio(MmdAudioTrack track, UnresolvedAudio missing, float volume, bool muted)
        {
            var previous = Scene.SwapAudio(track);
            unresolvedAudio = missing;
            Scene.AudioVolume = volume; Scene.AudioMuted = muted;
            DiscardAudio(previous);
        }

        private AudioData CaptureAudio(string sceneFile) => new AudioData
        {
            path = Scene.Audio != null ? MmdSceneDocument.ToStored(sceneFile, Scene.Audio.Source)
                : unresolvedAudio != null ? Rebase(unresolvedAudio.Stored, unresolvedAudio.BaseScene, sceneFile) : "",
            volume = Scene.AudioVolume, muted = Scene.AudioMuted,
        };
        private void RebaseUnresolvedAudio(string targetScene)
        {
            if (unresolvedAudio == null) return;
            unresolvedAudio.Stored = Rebase(unresolvedAudio.Stored, unresolvedAudio.BaseScene, targetScene);
            unresolvedAudio.BaseScene = targetScene;
        }

        // Scene open: decodes the referenced WAV (missing or failing -> unresolved, the rest of
        // the scene still opens). The result is handed back for the commit step.
        private sealed class PreparedAudio
        {
            public MmdAudioTrack Track; public UnresolvedAudio Missing; public string Problem;
        }
        private IEnumerator PrepareAudio(SceneFile file, string scenePath, string full, System.Threading.CancellationToken token, PreparedAudio result, Action<string> stage)
        {
            var stored = file.audio?.path;
            if (string.IsNullOrEmpty(stored)) yield break;
            var wav = MmdSceneDocument.Resolve(scenePath, stored);
            if (!File.Exists(wav))
            {
                result.Missing = new UnresolvedAudio { Stored = stored, BaseScene = full, Reason = "오디오 파일 없음" };
                result.Problem = "오디오 파일 없음: " + stored;
                yield break;
            }
            var task = MmdAudioTrack.LoadAsync(wav, stage, token);
            while (!task.IsCompleted) yield return null;
            if (task.IsCanceled || (task.IsFaulted && task.Exception.GetBaseException() is OperationCanceledException)) yield break;
            if (task.IsFaulted)
            {
                var reason = "오디오 불러오기 실패 (" + task.Exception.GetBaseException().Message + ")";
                result.Missing = new UnresolvedAudio { Stored = stored, BaseScene = full, Reason = reason };
                result.Problem = reason + ": " + stored;
                yield break;
            }
            result.Track = task.Result;
        }
    }
}
