using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // D-01: "자동 카메라 (박자)" in the camera inspector. Analyses the scene audio, generates a
    // camera VMD next to the scene file (or in the app folder for an unsaved scene), connects it as
    // the scene's camera motion (undoable, following) and records the generator version, inputs and
    // seed with the scene so the same result can be generated again.
    public sealed partial class MmdSceneWorkspace
    {
        private DropdownField autoTarget, autoCut;
        private Slider autoShake;
        private IntegerField autoSeed;
        private Label autoInfo;
        private readonly List<string> autoTargetIds = new List<string>();
        internal AutoCameraSettings LastAutoCamera { get; private set; }
        internal MmdBeatInfo LastBeats { get; private set; }
        internal MmdAutoCamera.Result LastAutoResult { get; private set; }
        private static readonly string[] CutLabels = { "1마디 (빠름)", "2마디 (보통)", "4마디 (느림)" };
        private static readonly int[] CutBars = { 1, 2, 4 };

        private void BuildAutoCameraControls(VisualElement section)
        {
            Text(section, "자동 카메라 (박자)", "eds-section-title").style.marginTop = 10;
            autoTarget = new DropdownField("대상 캐릭터", new List<string> { "(없음)" }, 0) { name = "AutoCameraTarget" }; section.Add(autoTarget);
            autoCut = new DropdownField("컷 간격", CutLabels.ToList(), 1) { name = "AutoCameraCut" }; section.Add(autoCut);
            autoShake = new Slider("흔들림", 0, 1) { name = "AutoCameraShake", showInputField = true, value = .2f }; autoShake.AddToClassList("eds-graphics-slider"); section.Add(autoShake);
            autoSeed = new IntegerField("시드") { name = "AutoCameraSeed", value = 1 }; section.Add(autoSeed);
            TextButton(section, "AutoCameraGenerate", "박자에 맞춰 카메라 만들기", () => Guard(() => StartCoroutine(GenerateAutoCamera(CurrentAutoSettings()))), "eds-button--block");
            autoInfo = Text(section, "장면 오디오(WAV)의 박자를 찾아 컷을 마디·박자에 맞춘 카메라 VMD를 만들고 연결합니다. 같은 설정과 시드는 같은 결과를 냅니다. 만든 VMD는 MMD 등에서 고칠 수 있습니다.", "eds-note");
            autoInfo.name = "AutoCameraInfo";
        }

        internal AutoCameraSettings CurrentAutoSettings()
        {
            var index = autoTarget.choices.IndexOf(autoTarget.value) - 1;
            return new AutoCameraSettings
            {
                target = index >= 0 && index < autoTargetIds.Count ? autoTargetIds[index] : "",
                cutBars = CutBars[Math.Max(0, Array.IndexOf(CutLabels, autoCut.value))],
                shake = Mathf.Clamp01(autoShake.value), seed = autoSeed.value,
            };
        }

        private void RefreshAutoCamera()
        {
            if (autoTarget == null) return;
            var previous = autoTarget.index > 0 && autoTarget.index - 1 < autoTargetIds.Count ? autoTargetIds[autoTarget.index - 1] : null;
            autoTargetIds.Clear();
            var choices = new List<string> { "(없음)" };
            foreach (var item in Scene.Objects.Where(o => o.Kind == MmdSceneService.ObjectKind.Character))
            {
                choices.Add(Path.GetFileNameWithoutExtension(item.ModelSource) + (choices.Contains(Path.GetFileNameWithoutExtension(item.ModelSource)) ? " (" + choices.Count + ")" : ""));
                autoTargetIds.Add(item.Id);
            }
            autoTarget.choices = choices;
            var keep = previous != null ? autoTargetIds.IndexOf(previous) : -1;
            autoTarget.SetValueWithoutNotify(choices[keep >= 0 ? keep + 1 : choices.Count > 1 ? 1 : 0]);
        }

        internal IEnumerator GenerateAutoCamera(AutoCameraSettings settings)
        {
            if (Busy) yield break;
            settings.Validate();
            var track = Scene.Audio ?? throw new InvalidOperationException("자동 카메라에는 장면 오디오(WAV)가 필요합니다. 오디오를 먼저 연결하세요.");
            var target = Scene.Objects.FirstOrDefault(o => o.Id == settings.target && o.Kind == MmdSceneService.ObjectKind.Character)
                ?? throw new InvalidOperationException("자동 카메라의 대상 캐릭터를 고르세요.");
            Busy = true; Error = ""; message = "박자를 찾는 중…"; Refresh();
            yield return null;
            var samples = new float[track.Clip.samples * track.Clip.channels];
            track.Clip.GetData(samples, 0);
            var analysis = System.Threading.Tasks.Task.Run(() => MmdBeatAnalyzer.Analyze(samples, track.Channels, track.Frequency));
            while (!analysis.IsCompleted) yield return null;
            var playing = Scene.Playing; var seconds = Scene.Seconds;
            try
            {
                var beats = analysis.GetAwaiter().GetResult();
                settings.bpm = Math.Round(beats.Bpm, 2);
                settings.audio = track.Source;
                Scene.Playing = false;
                // The character's body at a time: bones' extent after sampling the scene there.
                var front = target.Placement.rotation * Vector3.forward;
                MmdAutoCamera.Body BodyAt(double time)
                {
                    Scene.Seek(Math.Min(time, Scene.Duration));
                    var points = target.Model.bones.Where(b => b != null).Select(b => b.position).ToArray();
                    var min = points.Aggregate(Vector3.Min); var max = points.Aggregate(Vector3.Max);
                    return new MmdAutoCamera.Body { Centre = (min + max) * .5f, Feet = min.y, Height = (max.y - min.y) * 1.1f, Front = new Vector3(front.x, 0, front.z).normalized };
                }
                var result = MmdAutoCamera.Generate(beats, settings, BodyAt, Math.Min(Scene.Duration, beats.DurationSeconds));
                if (result.Keys.Count == 0) throw new InvalidOperationException("카메라 컷을 만들 수 없습니다(오디오가 너무 짧습니다).");
                var file = AutoCameraFile(settings.seed);
                MmdCameraMotion.Write(file, result.Keys);
                var motion = MmdCameraMotion.Load(file);
                Scene.Seek(seconds);
                ConnectCameraMotion(motion);
                LastAutoCamera = settings; LastBeats = beats; LastAutoResult = result; lastAutoFile = file;
                message = "자동 카메라: BPM " + beats.Bpm.ToString("0.0") + " · 컷 " + result.CutFrames.Count + "개 · " + Path.GetFileName(file);
                autoInfo.text = message + "\n같은 설정·시드(" + settings.seed + ")로 다시 만들면 같은 결과입니다.";
            }
            catch (Exception ex) { Error = "자동 카메라를 만들지 못했습니다: " + ex.GetBaseException().Message; Scene.Seek(seconds); }
            finally { Busy = false; Scene.ResumeAfterInterruption(playing); Refresh(); }
        }

        private string AutoCameraFile(int seed)
        {
            var directory = ProjectPath != null ? Path.GetDirectoryName(ProjectPath) : Path.Combine(Application.persistentDataPath, "AutoCamera");
            Directory.CreateDirectory(directory);
            var name = (ProjectPath != null ? Path.GetFileNameWithoutExtension(ProjectPath) : "scene") + ".autocam-" + seed + ".vmd";
            return Path.Combine(directory, name);
        }

        private string lastAutoFile;
        // Saved with the scene while the generated VMD is the scene's camera motion (version 0 = none).
        private AutoCameraSettings CaptureAutoCamera(string sceneFile)
        {
            if (LastAutoCamera == null || Scene.CameraMotion == null || lastAutoFile == null ||
                !string.Equals(Path.GetFullPath(Scene.CameraMotion.Source), Path.GetFullPath(lastAutoFile), StringComparison.OrdinalIgnoreCase))
                return new AutoCameraSettings { version = 0 };
            var copy = JsonUtility.FromJson<AutoCameraSettings>(JsonUtility.ToJson(LastAutoCamera));
            copy.audio = MmdSceneDocument.ToStored(sceneFile, LastAutoCamera.audio);
            return copy;
        }
        // Opening a scene: the generator's inputs come back into the controls.
        private void RestoreAutoCamera(AutoCameraSettings stored, string scenePath)
        {
            LastAutoCamera = null; lastAutoFile = null; LastAutoResult = null;
            if (stored == null || stored.version == 0 || Scene.CameraMotion == null) return;
            LastAutoCamera = stored; lastAutoFile = Scene.CameraMotion.Source;
            if (!string.IsNullOrEmpty(stored.audio)) LastAutoCamera.audio = MmdSceneDocument.Resolve(scenePath, stored.audio);
            autoCut.SetValueWithoutNotify(CutLabels[Math.Max(0, Array.IndexOf(CutBars, stored.cutBars))]);
            autoShake.SetValueWithoutNotify(stored.shake); autoSeed.SetValueWithoutNotify(stored.seed);
            RefreshAutoCamera();
            var index = autoTargetIds.IndexOf(stored.target);
            if (index >= 0) autoTarget.SetValueWithoutNotify(autoTarget.choices[index + 1]);
        }
    }
}
