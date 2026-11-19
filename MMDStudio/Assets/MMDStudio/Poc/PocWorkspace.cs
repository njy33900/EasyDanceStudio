using System;
using System.Collections;
using System.IO;
using UMT;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // Stage-0 single-model workspace. Model/motion replacement is transactional;
    // production scene storage and the full studio layout are separate work.
    public sealed class PocWorkspace : MonoBehaviour
    {
        public PMXImportResult Current { get; private set; }
        public MmdPocMotion Motion { get; private set; }
        public MmdPocCamera CameraMotion { get; private set; }
        private Vector3 freeCameraPosition;
        private Quaternion freeCameraRotation;
        private float freeCameraFov;
        private bool freeCameraOrthographic;
        private Button cameraButton, detachCameraButton;
        private Label cameraLabel;
        public PocAssetLifetime Owner { get; private set; }
        public MmdPocSummary ModelSummary { get; private set; }
        public bool Busy { get; private set; }
        public string Error { get; private set; } = "";
        public string Status { get; private set; } = "PMX 모델을 가져오세요.";
        public string Mode { get; private set; } = "direct";
        private Label statusLabel, modelLabel, motionLabel, timeLabel;
        private Button importButton, motionButton, playButton, stopButton, clearButton;
        private Slider timeline;
        private PanelSettings settings;
        private Font font;
        private GameObject inputRoot;

        public void Initialize(bool createUi, string mode = "direct")
        {
            Mode = mode;
            if (createUi) BuildUi();
        }

        public void ApplyPickerResult(string path, bool model)
        {
            if (Busy) return;
            if (string.IsNullOrEmpty(path)) { Status = "파일 선택을 취소했습니다."; Error = ""; Refresh(); return; }
            StartCoroutine(model ? LoadModel(path) : LoadMotion(path));
        }

        private void Pick(bool model)
        {
            var playing = Motion != null && Motion.Playing;
            if (Motion != null) Motion.Playing = false;
            string path = null;
            try { path = WindowsRuntimeFilePicker.Open(model); }
            catch (Exception ex) { Fail(ex); return; }
            finally { if (Motion != null) Motion.ResumeAfterInterruption(playing); }
            ApplyPickerResult(path, model);
        }

        public IEnumerator LoadModel(string path, string motionPath = null)
        {
            if (Busy) yield break;
            Busy = true;
            Error = "";
            Status = "모델을 불러오는 중…";
            var playing = Motion != null && Motion.Playing;
            if (Motion != null) Motion.Playing = false;
            Refresh();
            yield return null; // Render loading feedback before synchronous import.
            var camera = Camera.main;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            var near = camera.nearClipPlane;
            var far = camera.farClipPlane;
            PMXImportResult candidate = null;
            PocAssetLifetime candidateOwner = null;
            var failed = false;
            try
            {
                var summary = MmdPocProbe.Load(path, camera, out candidate);
                candidateOwner = candidate.root.GetComponentInParent<PocAssetLifetime>();
                MmdPocMotion candidateMotion = null;
                if (!string.IsNullOrEmpty(motionPath))
                {
                    candidateMotion = candidate.root.AddComponent<MmdPocMotion>();
                    candidateMotion.ShowDiagnosticControls = false;
                    candidateMotion.Load(candidate, motionPath, Mode);
                }
                if (Owner != null) Owner.Release();
                DetachCamera(false);
                Current = candidate;
                Owner = candidateOwner;
                ModelSummary = summary;
                Motion = candidateMotion;
                candidate = null;
                candidateOwner = null;
                if (Motion != null) Motion.ResumeAfterInterruption(true);
                Status = summary.warnings.Length == 0 ? "모델을 불러왔습니다." : "모델을 불러왔습니다. 누락 자원 안내를 확인하세요.";
            }
            catch (Exception ex)
            {
                failed = true;
                if (candidateOwner != null) candidateOwner.Release();
                candidateOwner = null;
                candidate = null;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.nearClipPlane = near;
                camera.farClipPlane = far;
                if (Motion != null) Motion.ResumeAfterInterruption(playing);
                Fail(ex);
            }
            if (failed) yield return Resources.UnloadUnusedAssets();
            Busy = false;
            Refresh();
        }

        public IEnumerator LoadMotion(string path)
        {
            if (Busy || Current == null) yield break;
            Busy = true;
            Error = "";
            Status = "모션을 불러오는 중…";
            var previous = Motion;
            var time = previous == null ? 0 : previous.TimeSeconds;
            var playing = previous != null && previous.Playing;
            if (previous != null) previous.Playing = false;
            Refresh();
            yield return null;
            MmdPocMotion candidate = null;
            var failed = false;
            try
            {
                candidate = Current.root.AddComponent<MmdPocMotion>();
                candidate.ShowDiagnosticControls = false;
                candidate.Load(Current, path, Mode);
                if (previous != null) Destroy(previous);
                Motion = candidate;
                candidate = null;
                Motion.ResumeAfterInterruption(true);
                Status = "모션을 연결했습니다. 물리·음악은 꺼져 있습니다.";
            }
            catch (Exception ex)
            {
                failed = true;
                if (candidate != null) Destroy(candidate);
                candidate = null;
                if (previous != null)
                {
                    previous.Sample(time);
                    previous.ResumeAfterInterruption(playing);
                }
                Fail(ex);
            }
            if (failed) yield return Resources.UnloadUnusedAssets();
            Busy = false;
            Refresh();
        }

        public void Clear()
        {
            if (Busy) return;
            DetachCamera();
            if (Owner != null) Owner.Release();
            Current = null;
            Owner = null;
            Motion = null;
            ModelSummary = null;
            Error = "";
            Status = "장면을 비웠습니다.";
            Refresh();
        }

        private void Fail(Exception error)
        {
            Error = error.Message;
            Status = "불러오지 못했습니다. 파일을 확인한 뒤 다시 시도하세요. 기존 장면은 유지됩니다.";
            Debug.LogException(error);
            Refresh();
        }

        private void PickCamera()
        {
            var playing = Motion != null && Motion.Playing;
            if (Motion != null) Motion.Playing = false;
            string path;
            try { path = WindowsRuntimeFilePicker.Open(false); }
            catch (Exception ex) { Fail(ex); return; }
            finally { if (Motion != null) Motion.ResumeAfterInterruption(playing); }
            if (string.IsNullOrEmpty(path)) { Status = "파일 선택을 취소했습니다."; Refresh(); return; }
            StartCoroutine(LoadCamera(path));
        }

        public IEnumerator LoadCamera(string path)
        {
            if (Busy || Motion == null) yield break;
            Busy = true;
            Error = "";
            Status = "카메라 모션을 불러오는 중…";
            var playing = Motion.Playing;
            Motion.Playing = false;
            Refresh();
            yield return null;
            MmdPocCamera candidate = null;
            try
            {
                candidate = MmdPocCamera.Load(path);
                if (CameraMotion == null)
                {
                    freeCameraPosition = Camera.main.transform.position;
                    freeCameraRotation = Camera.main.transform.rotation;
                    freeCameraFov = Camera.main.fieldOfView;
                    freeCameraOrthographic = Camera.main.orthographic;
                }
                CameraMotion?.Dispose();
                CameraMotion = candidate;
                candidate = null;
                Status = "카메라 연결 완료. 캐릭터 시간에 동기화합니다.";
            }
            catch (Exception ex) { candidate?.Dispose(); Fail(ex); }
            finally { Motion.ResumeAfterInterruption(playing); Busy = false; Refresh(); }
        }

        public void DetachCamera(bool restore = true)
        {
            if (CameraMotion == null) return;
            CameraMotion.Dispose();
            CameraMotion = null;
            if (restore && Camera.main != null)
            {
                Camera.main.transform.SetPositionAndRotation(freeCameraPosition, freeCameraRotation);
                Camera.main.fieldOfView = freeCameraFov;
                Camera.main.orthographic = freeCameraOrthographic;
            }
            Refresh();
        }

        private void LateUpdate()
        {
            if (!Busy && CameraMotion != null && Motion != null) CameraMotion.Sample(Camera.main, Motion.TimeSeconds);
        }

        private void BuildUi()
        {
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                inputRoot = new GameObject("Runtime UI Input");
                inputRoot.AddComponent<UnityEngine.EventSystems.EventSystem>();
                inputRoot.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
            }
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1280, 720);
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("PocRuntimeTheme");
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = settings;
            var root = document.rootVisualElement;
            root.name = "PocWorkspace";
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.bottom = 0;
            root.style.width = 360;
            root.style.paddingLeft = root.style.paddingRight = 16;
            root.style.paddingTop = 16;
            root.style.backgroundColor = new Color(0.07f, 0.085f, 0.12f, 0.98f);
            root.style.color = Color.white;
            font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 16);
            root.style.unityFontDefinition = FontDefinition.FromFont(font);
            root.style.fontSize = 15;
            var title = new Label("MMD Studio · 런타임 검증");
            title.style.fontSize = 22;
            title.style.marginBottom = 18;
            root.Add(title);
            var content = new ScrollView(ScrollViewMode.Vertical);
            content.style.flexGrow = 1;
            root.Add(content);
            importButton = AddButton(content, "PMX 가져오기", () => Pick(true));
            motionButton = AddButton(content, "VMD 연결", () => Pick(false));
            cameraButton = AddButton(content, "카메라 VMD 연결", PickCamera);
            detachCameraButton = AddButton(content, "카메라 해제", () => DetachCamera());
            cameraLabel = AddLabel(content);
            modelLabel = AddLabel(content);
            motionLabel = AddLabel(content);
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            content.Add(row);
            playButton = AddButton(row, "재생", () => { if (Motion != null) Motion.Playing = !Motion.Playing; Refresh(); });
            stopButton = AddButton(row, "정지", () => { if (Motion != null) { Motion.Playing = false; Motion.Sample(0); } Refresh(); });
            playButton.style.flexGrow = stopButton.style.flexGrow = 1;
            timeline = new Slider(0, 1);
            timeline.style.marginTop = timeline.style.marginBottom = 10;
            timeline.RegisterValueChangedCallback(evt => { if (!Busy && Motion != null) Motion.Sample(evt.newValue * Motion.Summary.durationSeconds); });
            content.Add(timeline);
            timeLabel = AddLabel(content);
            clearButton = AddButton(content, "장면 비우기", Clear);
            statusLabel = AddLabel(content);
            var log = AddLabel(content);
            log.style.fontSize = 12;
            log.style.color = new Color(0.65f, 0.71f, 0.8f);
            log.text = "물리 OFF · 음악 OFF\n로그: " + Application.consoleLogPath;
            Refresh();
        }

        private static Button AddButton(VisualElement parent, string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.style.height = 36;
            button.style.marginBottom = 8;
            parent.Add(button);
            return button;
        }

        private static Label AddLabel(VisualElement parent)
        {
            var label = new Label();
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 14;
            parent.Add(label);
            return label;
        }

        private void Update()
        {
            if (timeline == null || Motion == null) return;
            timeline.SetValueWithoutNotify(Motion.TimeSeconds / Motion.Summary.durationSeconds);
            timeLabel.text = Motion.TimeSeconds.ToString("F1") + " / " + Motion.Summary.durationSeconds.ToString("F1") + " 초";
        }

        private void Refresh()
        {
            if (statusLabel == null) return;
            importButton.SetEnabled(!Busy);
            motionButton.SetEnabled(!Busy && Current != null);
            cameraButton.SetEnabled(!Busy && Motion != null);
            detachCameraButton.SetEnabled(!Busy && CameraMotion != null);
            cameraLabel.text = CameraMotion == null ? "카메라 모션 없음" : Path.GetFileName(CameraMotion.Source) + "\n" + MmdPocCamera.Limit;
            playButton.SetEnabled(!Busy && Motion != null);
            stopButton.SetEnabled(!Busy && Motion != null);
            clearButton.SetEnabled(!Busy && Current != null);
            timeline.SetEnabled(!Busy && Motion != null);
            playButton.text = Motion != null && Motion.Playing ? "일시정지" : "재생";
            modelLabel.text = ModelSummary == null ? "모델 없음" : Path.GetFileName(ModelSummary.source) + "\n" + string.Join("\n", ModelSummary.warnings);
            motionLabel.text = Motion == null ? "모션 없음" : Path.GetFileName(Motion.Summary.source) +
                "\n매핑 누락: 본 " + Motion.Summary.missingBones.Length + " / 모프 " + Motion.Summary.missingMorphs.Length;
            if (Motion == null) { timeline.SetValueWithoutNotify(0); timeLabel.text = "0.0 / 0.0 초"; }
            statusLabel.text = Status + (Error.Length == 0 ? "" : "\n" + Error);
            statusLabel.style.color = Error.Length == 0 ? new Color(0.75f, 0.83f, 0.95f) : new Color(1, 0.55f, 0.5f);
        }

        private void OnDestroy()
        {
            CameraMotion?.Dispose();
            if (Owner != null) Owner.Release();
            if (font != null) Destroy(font);
            if (settings != null) Destroy(settings);
            if (inputRoot != null) Destroy(inputRoot);
        }
    }
}
