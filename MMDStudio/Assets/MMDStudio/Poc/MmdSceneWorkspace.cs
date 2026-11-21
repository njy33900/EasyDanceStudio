using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    public sealed partial class MmdSceneWorkspace : MonoBehaviour
    {
        public MmdSceneService Scene { get; private set; }
        public string SelectedId { get; private set; }
        public string Error { get; private set; } = "";
        public bool Busy { get; private set; }
        internal VisualElement Root { get; private set; }
        private ScrollView list;
        public enum PickKind { Character, Stage, Motion }
        // What the inspector shows: a scene object (SelectedId), the light, or the camera.
        public enum InspectorTarget { Object, Light, Camera }
        public InspectorTarget Target { get; private set; }
        private Label info, status, time, selectionTitle, speedValue, motionInfo, materialInfo;
        private Button add, addStage, connect, remove, play, stop, skipStart;
        private MmdIcon playIcon;
        private Vector3Field position, rotation, scale;
        private Toggle visible, loop, physics;
        private Label physicsInfo;
        private VisualElement physicsParts;
        private DropdownField springPreset;
        private Label springInfo;
        private Button springAll;
        private static readonly System.Collections.Generic.List<string> SpringChoices = new System.Collections.Generic.List<string> { "끔", "약", "중", "강" };
        private static string SpringIdOf(string label) => label == "약" ? "light" : label == "중" ? "medium" : label == "강" ? "strong" : null;
        private static string SpringLabelOf(string id) => id == null ? "끔" : MmdSpringBones.PresetLabels[id];
        private string physicsPartsKey;
        private Slider timeline, speed;
        private VisualElement transformSection, motionSection, materialSection, missingSection, emptyState;
        private Label missingInfo;
        private PanelSettings settings;
        private Font font;
        private GameObject input;
        private VisualElement viewport;
        private VisualElement viewportHint;
        private string hoverHint;
        private enum Drag { None, Orbit, Pan, Gizmo }
        private Drag drag;
        private bool orbitWasHeld, panWasHeld, leftWasHeld, clickCandidate;
        private Vector2 clickStart;
        private Mesh pickMesh;
        private readonly System.Collections.Generic.List<Vector3> pickVertices = new System.Collections.Generic.List<Vector3>();
        private readonly System.Collections.Generic.List<int> pickTriangles = new System.Collections.Generic.List<int>();
        private VisualElement confirm;
        private Label confirmText;
        private string confirmId;
        private GameObject highlight;
        private LineRenderer highlightBox, highlightRing;
        private Material highlightMaterial;
        public MmdViewportCamera View { get; } = new MmdViewportCamera();
        internal bool HighlightVisible => highlight != null && highlight.activeSelf;
        internal Bounds HighlightBounds { get; private set; }
        private string message = "캐릭터나 배경을 추가하고 캐릭터에 VMD를 연결하세요.";
        private MmdSceneService.SceneObject Selected => Scene.Objects.FirstOrDefault(item => item.Id == SelectedId);
        private static readonly Color IconColour = new Color(.25f, .25f, .28f);

        public void Initialize()
        {
            Scene = gameObject.AddComponent<MmdSceneService>();
            Scene.RetainReplacedMotions = true; // H1-03: a replaced VMD stays available for undo
            Graphics = gameObject.AddComponent<MmdSceneGraphics>();
            Graphics.Initialize(Camera.main);
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                input = new GameObject("Scene UI Input");
                input.AddComponent<UnityEngine.EventSystems.EventSystem>();
                input.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
            }
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1280, 720);
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("PocRuntimeTheme");
            var document = gameObject.AddComponent<UIDocument>(); document.panelSettings = settings;
            Root = document.rootVisualElement;
            Root.name = "SceneWorkspace";
            var theme = Resources.Load<StyleSheet>("StudioTheme");
            if (theme == null) throw new InvalidOperationException("StudioTheme.uss is missing from Resources.");
            Root.styleSheets.Add(theme);
            Root.AddToClassList("eds-root");
            Root.style.position = Position.Absolute;
            Root.style.left = Root.style.right = Root.style.top = Root.style.bottom = 0;
            Root.pickingMode = PickingMode.Ignore;
            font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 15);
            Root.style.unityFontDefinition = FontDefinition.FromFont(font);

            // ----- Top bar: title + project on the left, file/view icons on the right -----
            topbar = Element(Root, "eds-topbar"); topbar.name = "TopBar";
            Text(topbar, "Easy Dance Studio", "eds-app-title");
            titleLabel = Text(topbar, "", "eds-project-name"); titleLabel.name = "ProjectTitle";
            Element(topbar, "eds-spacer").pickingMode = PickingMode.Ignore;
            BuildProjectToolbar(topbar);
            Element(topbar, "eds-divider-v");
            IconButton(topbar, "FrameAll", MmdIconKind.Frame, "전체 보기 — 보이는 모든 항목에 카메라 맞춤", () => Guard(FrameAll));

            var body = Element(Root, "eds-body"); body.pickingMode = PickingMode.Ignore;

            // ----- Scene explorer -----
            explorer = Element(body, "eds-explorer"); explorer.name = "Explorer";
            var explorerHeader = Element(explorer, "eds-card-header");
            Text(explorerHeader, "장면 탐색기", "eds-panel-title");
            HideButton(explorerHeader, "HideExplorer", "Explorer");
            var treeRoot = Element(explorer, "eds-tree-root");
            treeRoot.Add(new MmdIcon(MmdIconKind.Chevron, 14, new Color(.63f, .63f, .67f)) { style = { marginRight = 6 } });
            Text(treeRoot, "장면", null);
            list = new ScrollView { name = "ObjectList" }; list.style.flexGrow = 1; explorer.Add(list);
            var actions = Element(explorer, "eds-explorer-actions");
            add = TextButton(actions, "AddModel", "캐릭터 추가", () => Pick(PickKind.Character), "eds-button--block");
            add.style.marginRight = 6;
            addStage = TextButton(actions, "AddStage", "배경 추가", () => Pick(PickKind.Stage), "eds-button--block");

            // ----- Viewport card -----
            var viewportWrap = Element(body, "eds-viewport-wrap"); viewportWrap.pickingMode = PickingMode.Ignore;
            viewport = Element(viewportWrap, "eds-viewport"); viewport.name = "Viewport"; viewport.pickingMode = PickingMode.Ignore;
            viewport.RegisterCallback<GeometryChangedEvent>(_ => UpdateViewport(viewport));
            emptyState = Element(viewport, "eds-empty"); emptyState.pickingMode = PickingMode.Ignore; emptyState.name = "EmptyState";
            emptyState.Add(new MmdIcon(MmdIconKind.Cube, 40, new Color(.55f, .55f, .6f)));
            Text(emptyState, "3D 뷰포트", "eds-empty-title").pickingMode = PickingMode.Ignore;
            Text(emptyState, "캐릭터와 배경을 추가해 배치하고 재생하세요", "eds-empty-sub").pickingMode = PickingMode.Ignore;
            viewportHint = Text(viewport, "좌클릭 선택 · 우클릭 회전 · 휠클릭 이동 · 휠 확대 · W/E/R 기즈모 · F 맞춤 · Space 재생 · Delete 삭제 · H 집중 보기", "eds-viewport-hint");
            viewportHint.name = "ViewportHint"; viewportHint.pickingMode = PickingMode.Ignore;
            BuildGizmoUi(viewport);
            body.Insert(0, viewportWrap); // 3D overlays paint behind the floating cards.

            // ----- Inspector: selection header, then stacked sections -----
            inspector = Element(body, "eds-inspector"); inspector.name = "Inspector";
            var inspectorHeader = Element(inspector, "eds-card-header");
            Text(inspectorHeader, "속성", "eds-panel-title");
            HideButton(inspectorHeader, "HideInspector", "Inspector");
            var content = new ScrollView { name = "InspectorContent" }; content.style.flexGrow = 1; inspector.Add(content);
            selectionTitle = Text(content, "선택한 항목", "eds-selection-title"); selectionTitle.name = "SelectionTitle";
            info = Text(content, "", "eds-selection-info"); info.name = "SelectionInfo";

            missingSection = Section(content, "MissingSection", "누락된 파일");
            missingInfo = Text(missingSection, "", "eds-section-text"); missingInfo.name = "MissingInfo";
            Text(missingSection, "이 항목은 장면 파일에 그대로 보존되며 저장해도 사라지지 않습니다. '파일 다시 찾기'로 옮겨진 PMX를 고르면 같은 ID·배치·모션 연결을 유지한 채 다시 불러옵니다.", "eds-note");
            var missingButtons = Element(missingSection, "eds-row-buttons");
            TextButton(missingButtons, "MissingRelink", "파일 다시 찾기", PickRelink, "eds-button--block").style.marginRight = 6;
            TextButton(missingButtons, "MissingRemove", "장면에서 항목 삭제", RequestRemove, "eds-button--block", "eds-button--danger");

            transformSection = Section(content, "TransformSection", "변형");
            position = Vector(transformSection, "Position", "위치");
            rotation = Vector(transformSection, "Rotation", "회전");
            scale = Vector(transformSection, "Scale", "크기");
            position.RegisterValueChangedCallback(_ => ApplyPlacement());
            rotation.RegisterValueChangedCallback(_ => ApplyPlacement());
            scale.RegisterValueChangedCallback(_ => ApplyPlacement());
            visible = new Toggle("표시") { name = "Visible" }; transformSection.Add(visible);
            visible.RegisterValueChangedCallback(evt => Guard(() => { if (Selected != null) { var id = SelectedId; Record("표시 전환", id, () => Scene.SetVisible(id, evt.newValue)); } }));
            var objectButtons = Element(transformSection, "eds-row-buttons");
            TextButton(objectButtons, "FrameSelected", "선택 보기", () => Guard(FrameSelected), "eds-button--block").style.marginRight = 6;
            remove = TextButton(objectButtons, "RemoveModel", "삭제", RequestRemove, "eds-button--block", "eds-button--danger");

            motionSection = Section(content, "MotionSection", "모션");
            motionInfo = Text(motionSection, "", "eds-section-text"); motionInfo.name = "MotionInfo";
            connect = TextButton(motionSection, "ConnectMotion", "VMD 연결", () => Pick(PickKind.Motion), "eds-button--block");
            // PH-03: experimental per-character physics, baked ahead of playback.
            physics = new Toggle("물리 (실험)") { name = "Physics" }; motionSection.Add(physics);
            physics.RegisterValueChangedCallback(evt => Guard(() => { if (Selected != null) { var id = SelectedId; Record("물리 켜기/끄기", id, () => Scene.SetPhysics(id, evt.newValue)); RefreshPhysicsInfo(); } }));
            physicsInfo = Text(motionSection, "", "eds-note"); physicsInfo.name = "PhysicsInfo";
            // PH-04: per-part strength sliders for the parts the selected model has.
            physicsParts = new VisualElement { name = "PhysicsParts" }; motionSection.Add(physicsParts);
            // PH-05: chest/hip spring bones for models that have the bones but no rigid bodies.
            springPreset = new DropdownField("신체 흔들림", SpringChoices, 0) { name = "SpringPreset" }; motionSection.Add(springPreset);
            springPreset.RegisterValueChangedCallback(evt => Guard(() => { if (Selected != null) { var id = SelectedId; Record("신체 흔들림", id, () => Scene.SetSpring(id, SpringIdOf(evt.newValue))); RefreshPhysicsInfo(); } }));
            springInfo = Text(motionSection, "", "eds-note"); springInfo.name = "SpringInfo";
            springAll = TextButton(motionSection, "SpringApplyAll", "모든 캐릭터에 적용", () => Guard(() =>
            {
                var id = SpringIdOf(springPreset.value) ?? "medium";
                var count = 0;
                Record("신체 흔들림 일괄 적용", null, () => count = Scene.ApplySpringToAll(id));
                message = "신체 흔들림(" + MmdSpringBones.PresetLabels[id] + ")을 캐릭터 " + count + "명에게 적용했습니다. 모델 이용 규약을 확인하세요.";
            }), "eds-button--block");

            materialSection = Section(content, "MaterialSection", "재질");
            materialInfo = Text(materialSection, "", "eds-section-text"); materialInfo.name = "MaterialInfo";
            Text(materialSection, "재질 편집(툰·PBR)은 2차 예정입니다.", "eds-note");

            BuildGraphicsSections(content);

            // ----- Playback bar -----
            playback = Element(Root, "eds-playback"); playback.name = "PlaybackBar";
            skipStart = IconButton(playback, "SkipStart", MmdIconKind.SkipStart, "처음으로", () => Guard(() => Scene.Seek(0)));
            play = IconButton(playback, "PlayPause", MmdIconKind.Play, "재생 / 일시정지 (Space)", () => Guard(() => Scene.Playing = !Scene.Playing));
            playIcon = play.Q<MmdIcon>();
            stop = IconButton(playback, "Stop", MmdIconKind.Stop, "정지 — 처음으로 되돌림", () => Guard(Scene.Stop));
            timeline = new Slider(0, 1) { name = "Timeline" }; timeline.AddToClassList("eds-timeline"); playback.Add(timeline);
            timeline.RegisterValueChangedCallback(evt => Guard(() => Scene.Seek(evt.newValue * Scene.Duration)));
            time = Text(playback, "", "eds-time"); time.name = "Time";
            var options = Element(playback, "eds-playback-options");
            loop = new Toggle("반복") { name = "Loop" }; loop.AddToClassList("eds-loop"); options.Add(loop);
            loop.RegisterValueChangedCallback(evt => Guard(() => Record("반복", null, () => Scene.Loop = evt.newValue)));
            speed = new Slider("속도", .5f, 2) { name = "Speed" }; speed.AddToClassList("eds-speed"); speed.SetValueWithoutNotify(1); options.Add(speed);
            speed.RegisterValueChangedCallback(evt => Guard(() => Record("재생 속도", null, () => Scene.Speed = evt.newValue, coalesce: true)));
            speedValue = Text(options, "1.0×", "eds-speed-value");
            HideButton(playback, "HidePlaybackBar", "PlaybackBar");

            // ----- Status bar -----
            statusBar = Element(Root, "eds-statusbar"); statusBar.name = "StatusBar";
            status = Text(statusBar, "", "eds-status"); status.name = "Status";
            Text(statusBar, "물리 OFF", "eds-chip");
            HideButton(statusBar, "HideStatusBar", "StatusBar");

            var cube = GameObject.Find("Cube"); if (cube != null) cube.SetActive(false);
            CreateHighlight();
            BuildViewportLayoutControls();
            CreateConfirm();
            CreateDialog();
            Application.wantsToQuit += OnWantsToQuit;
            Camera.main.rect = new Rect(0, 0, 1, 1);
            View.Apply(Camera.main);
            Refresh();
            ConfigureRecent();
            ConfigureRecovery();
        }

        // ----- Small UI builders (styling lives in Resources/StudioTheme.uss) -----
        private static VisualElement Element(VisualElement parent, string className)
        {
            var element = new VisualElement(); if (className != null) element.AddToClassList(className); parent.Add(element); return element;
        }
        private static Label Text(VisualElement parent, string text, string className)
        {
            var label = new Label(text); if (className != null) label.AddToClassList(className); parent.Add(label); return label;
        }
        private static VisualElement Section(VisualElement parent, string name, string title)
        {
            var section = Element(parent, "eds-section"); section.name = name;
            Text(section, title, "eds-section-title");
            return section;
        }
        private static Button Button(VisualElement parent, string name, string text, Action action) => TextButton(parent, name, text, action);
        private static Button TextButton(VisualElement parent, string name, string text, Action action, params string[] classes)
        {
            var button = new Button(action) { name = name, text = text }; button.AddToClassList("eds-button");
            foreach (var value in classes) button.AddToClassList(value);
            parent.Add(button); return button;
        }
        // Icon-only button; the hint is shown in the status bar while hovered.
        private Button IconButton(VisualElement parent, string name, MmdIconKind icon, string hint, Action action)
        {
            var button = new Button(action) { name = name, tooltip = hint }; button.AddToClassList("eds-icon-button");
            button.Add(new MmdIcon(icon, 18, IconColour));
            button.RegisterCallback<PointerEnterEvent>(_ => { hoverHint = hint; UpdateStatus(); });
            button.RegisterCallback<PointerLeaveEvent>(_ => { if (hoverHint == hint) hoverHint = null; UpdateStatus(); });
            parent.Add(button); return button;
        }
        private static Vector3Field Vector(VisualElement parent, string name, string label)
        {
            var field = new Vector3Field(label) { name = name }; field.AddToClassList("eds-vector"); parent.Add(field);
            field.Query<FloatField>().ForEach(item => item.isDelayed = true);
            return field;
        }
        private void UpdateViewport(VisualElement viewport) => Camera.main.rect = new Rect(0, 0, 1, 1);
        private void ApplyPlacement()
        {
            Guard(() => { if (Selected != null) { var id = SelectedId; Record("변형", id, () => Scene.SetPlacement(id, position.value, rotation.value, scale.value), coalesce: true); } });
        }
        private void Guard(Action action)
        {
            if (Busy) return;
            try { action(); Error = ""; }
            catch (Exception ex) { Error = ex.Message; }
            Refresh();
        }
        private void Refresh()
        {
            list.Clear();
            foreach (var group in new[] { (MmdSceneService.ObjectKind.Character, "캐릭터", "CharacterGroup"), (MmdSceneService.ObjectKind.Stage, "배경", "StageGroup") })
            {
                var items = Scene.Objects.Where(item => item.Kind == group.Item1).ToArray();
                Text(list, group.Item2 + " (" + items.Length + ")", "eds-tree-group").name = group.Item3;
                var missing = UnresolvedOfType(group.Item1 == MmdSceneService.ObjectKind.Stage ? SceneObjectData.StageType : SceneObjectData.CharacterType).ToArray();
                list.Q<Label>(group.Item3).text = group.Item2 + " (" + (items.Length + missing.Length) + ")";
                foreach (var item in items)
                {
                    var id = item.Id;
                    var label = Path.GetFileNameWithoutExtension(item.ModelSource) + (item.Visible ? "" : " (숨김)") + (UnresolvedMotionOf(id) != null ? " (모션 누락)" : "");
                    var row = ExplorerRow(list, "object-" + id, label, item.Kind == MmdSceneService.ObjectKind.Stage ? MmdIconKind.Stage : MmdIconKind.Character,
                        () => Select(id), Target == InspectorTarget.Object && id == SelectedId);
                    if (!item.Visible) row.AddToClassList("eds-row--hidden");
                    if (UnresolvedMotionOf(id) != null) row.AddToClassList("eds-row--missing");
                }
                foreach (var entry in missing)
                {
                    var id = entry.Data.id;
                    var row = ExplorerRow(list, "unresolved-" + id, Path.GetFileNameWithoutExtension(entry.Data.assetPath) + " (누락)",
                        group.Item1 == MmdSceneService.ObjectKind.Stage ? MmdIconKind.Stage : MmdIconKind.Character, () => SelectUnresolved(id),
                        Target == InspectorTarget.Object && id == SelectedId);
                    row.AddToClassList("eds-row--missing");
                }
            }
            Text(list, "환경", "eds-tree-group");
            ExplorerRow(list, "explorer-light", "조명", MmdIconKind.Light, () => SelectTarget(InspectorTarget.Light), Target == InspectorTarget.Light);
            ExplorerRow(list, "explorer-camera", "카메라", MmdIconKind.Camera, () => SelectTarget(InspectorTarget.Camera), Target == InspectorTarget.Camera);

            var selected = Selected;
            var isCharacter = selected != null && selected.Kind == MmdSceneService.ObjectKind.Character;
            add.SetEnabled(!Busy); addStage.SetEnabled(!Busy); connect.SetEnabled(!Busy && isCharacter); remove.SetEnabled(!Busy && selected != null);
            var selectedMissing = Target == InspectorTarget.Object ? SelectedUnresolved : null;
            selectionTitle.text = Target == InspectorTarget.Light ? "조명" : Target == InspectorTarget.Camera ? "카메라" :
                selectedMissing != null ? (selectedMissing.Data.type == SceneObjectData.StageType ? "누락된 배경" : "누락된 캐릭터") :
                selected == null ? "선택한 항목" : isCharacter ? "선택한 캐릭터" : "선택한 배경";
            Show(missingSection, selectedMissing != null);
            if (selectedMissing != null)
                missingInfo.text = selectedMissing.Reason + "\n모델: " + selectedMissing.Data.assetPath +
                    (string.IsNullOrEmpty(selectedMissing.Data.motionPath) ? "" : "\n모션: " + selectedMissing.Data.motionPath);
            position.SetEnabled(!Busy && selected != null); rotation.SetEnabled(!Busy && selected != null); scale.SetEnabled(!Busy && selected != null);
            visible.SetEnabled(!Busy && selected != null);
            Show(transformSection, selected != null); Show(motionSection, isCharacter); Show(materialSection, selected != null);
            RefreshPhysicsInfo();
            ShowGraphicsSections(Target);
            if (selected != null)
            {
                position.SetValueWithoutNotify(Display(selected.Placement.localPosition)); rotation.SetValueWithoutNotify(Display(selected.Placement.localEulerAngles)); scale.SetValueWithoutNotify(Display(selected.Placement.localScale));
                visible.SetValueWithoutNotify(selected.Visible);
                var motion = !isCharacter ? "배경 · 모션 없음(고정)" : selected.MotionSource == null ? "모션 없음" : Path.GetFileName(selected.MotionSource);
                info.text = Path.GetFileName(selected.ModelSource) + "\n" + motion;
                var missingMotion = UnresolvedMotionOf(selected.Id);
                motionInfo.text = missingMotion != null ? "VMD 누락: " + missingMotion + "\n다른 VMD를 연결하면 대체됩니다. 연결 전에는 저장해도 경로가 유지됩니다." :
                    selected.MotionSource == null ? "연결된 VMD가 없습니다." : Path.GetFileName(selected.MotionSource) +
                    (selected.Motion != null ? " · " + selected.Motion.Summary.durationSeconds.ToString("F1") + "초" : "");
                var summary = selected.Summary;
                materialInfo.text = "재질 " + summary.materials + "개 · 텍스처 " + summary.loadedTextures + "/" + summary.textureSlots +
                    (summary.warnings.Length > 0 ? "\n" + string.Join("\n", summary.warnings) : "");
            }
            else
            {
                info.text = Target == InspectorTarget.Light ? "방향광의 색·강도·방향과 그림자" : Target == InspectorTarget.Camera ? "시야각과 저장된 시점" :
                    selectedMissing != null ? Path.GetFileName(selectedMissing.Data.assetPath) :
                    Scene.Objects.Count == 0 ? "장면이 비어 있습니다." : "탐색기나 뷰포트에서 항목을 선택하세요.";
                position.SetValueWithoutNotify(Vector3.zero); rotation.SetValueWithoutNotify(Vector3.zero); scale.SetValueWithoutNotify(Vector3.one);
            }
            Show(emptyState, Scene.Objects.Count == 0);
            play.SetEnabled(!Busy && Scene.Duration > 0); stop.SetEnabled(!Busy && Scene.Duration > 0); skipStart.SetEnabled(!Busy && Scene.Duration > 0);
            timeline.SetEnabled(!Busy && Scene.Duration > 0);
            loop.SetValueWithoutNotify(Scene.Loop); speed.SetValueWithoutNotify((float)Scene.Speed);
            UpdateTitle();
            UpdateStatus();
            UpdateClock();
            UpdateHighlight();
            RefreshGraphics();
            RefreshGizmo();
        }
        private Button ExplorerRow(VisualElement parent, string name, string text, MmdIconKind icon, Action action, bool selected)
        {
            var row = new Button(action) { name = name, text = text }; row.AddToClassList("eds-row");
            if (selected) row.AddToClassList("eds-row--selected");
            var mark = new MmdIcon(icon, 15, selected ? new Color(.11f, .31f, .85f) : new Color(.45f, .45f, .5f));
            mark.AddToClassList("eds-row-icon"); row.Insert(0, mark);
            row.SetEnabled(!Busy); parent.Add(row); return row;
        }
        // Display rounding only (e.g. 20.00001 -> 20); the transform keeps its exact value until edited.
        private static Vector3 Display(Vector3 value) => new Vector3(Mathf.Round(value.x * 1e4f) / 1e4f, Mathf.Round(value.y * 1e4f) / 1e4f, Mathf.Round(value.z * 1e4f) / 1e4f);
        private static void Show(VisualElement element, bool visible) => element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        private void UpdateStatus()
        {
            if (status == null) return;
            var error = Error.Length > 0;
            status.text = hoverHint ?? (error ? Error : message);
            status.EnableInClassList("eds-status--error", error && hoverHint == null);
            RefreshFocusNotice();
        }
        private static string Clock(double seconds)
        {
            var tenths = (long)Math.Floor(Math.Max(0, seconds) * 10);
            return (tenths / 600).ToString("00") + ":" + (tenths / 10 % 60).ToString("00") + "." + (tenths % 10);
        }
        private void UpdateClock()
        {
            playIcon.Kind = Scene.Playing ? MmdIconKind.Pause : MmdIconKind.Play;
            timeline.SetValueWithoutNotify(Scene.Duration > 0 ? (float)(Scene.Seconds / Scene.Duration) : 0);
            time.text = Clock(Scene.Seconds) + " / " + Clock(Scene.Duration);
            speedValue.text = Scene.Speed.ToString("0.0") + "×";
        }
        private void Update()
        {
            if (Scene == null) return;
            UpdateClock();
            if (DeviceInput) ReadDevices();
            RefreshFocusNotice();
            RefreshPhysicsInfo();
            UpdateAutosave();
        }
        // Bake progress for the selected character; also on the status bar while any bake runs.
        private string lastPhysicsText;
        internal void RefreshPhysicsInfo()
        {
            if (physics == null || Scene == null) return;
            var selected = Selected;
            var available = selected != null && selected.Kind == MmdSceneService.ObjectKind.Character && selected.Motion != null;
            physics.SetEnabled(!Busy && available);
            physics.SetValueWithoutNotify(selected != null && selected.PhysicsEnabled);
            var text = selected == null || selected.Kind != MmdSceneService.ObjectKind.Character ? "" :
                selected.PhysicsError != null ? "물리를 끈 이유: " + selected.PhysicsError :
                !available ? "VMD를 연결하면 물리를 켤 수 있습니다." :
                !selected.PhysicsEnabled ? "머리카락·옷·신체 등 모델의 모든 강체를 미리 계산해 재생합니다." :
                (selected.PhysicsReady ? "물리 준비 완료 · 어느 시간으로 이동해도 같은 결과" :
                "물리 준비 중 " + (selected.PhysicsProgress * 100).ToString("0") + "% · 준비된 구간까지 물리로 재생" +
                (Scene.Playing ? "" : " (일시정지 중 빠르게 계산)")) +
                (selected.PhysicsJumpTicks > 0 ? "\n주의: 물리가 불안정한 구간이 있습니다(" + selected.PhysicsJumpTicks + "틱, 처음 " +
                    selected.PhysicsFirstJumpSeconds.ToString("0.0") + "초). 머리카락·옷이 튈 수 있습니다." : "");
            if (text != lastPhysicsText) { physicsInfo.text = text; Show(physicsInfo, text.Length > 0); lastPhysicsText = text; }
            // PH-05 spring controls.
            var springOk = selected != null && selected.Kind == MmdSceneService.ObjectKind.Character;
            springPreset.SetEnabled(!Busy && springOk && selected.Motion != null && selected.SpringAvailable);
            springPreset.SetValueWithoutNotify(SpringLabelOf(selected?.SpringPreset));
            springAll.SetEnabled(!Busy && Scene.Objects.Any(o => o.Motion != null && o.SpringAvailable));
            var springText = !springOk ? "" : selected.SpringSummary +
                (selected.SpringPreset == null ? "" : selected.SpringReady ? "\n신체 흔들림 준비 완료" :
                    "\n신체 흔들림 준비 중 " + (selected.SpringProgress * 100).ToString("0") + "%") +
                (selected.SpringAvailable ? "\n원본 모델은 바꾸지 않습니다. 모델 이용 규약을 확인하세요." : "");
            if (springInfo.text != springText) { springInfo.text = springText; Show(springInfo, springText.Length > 0); }
            var parts = selected != null && (selected.PhysicsEnabled || selected.SpringPreset != null) ? selected.MotionParts : new string[0];
            var key = (selected?.Id ?? "") + "|" + string.Join(",", parts);
            if (key != physicsPartsKey)
            {
                physicsPartsKey = key;
                physicsParts.Clear();
                foreach (var part in parts)
                {
                    var id = selected.Id; var name = part;
                    var slider = new Slider(MmdPhysicsParts.Labels[part] + " 물리", 0, 100) { name = "PhysicsStrength-" + part, showInputField = true };
                    slider.SetValueWithoutNotify(selected.StrengthOf(part) * 100);
                    slider.RegisterValueChangedCallback(evt => Guard(() => Record("물리 강도", id, () => Scene.SetPhysicsStrength(id, name, Mathf.Round(evt.newValue) / 100f), coalesce: true)));
                    physicsParts.Add(slider);
                }
                Show(physicsParts, parts.Length > 0);
            }
            physicsParts.SetEnabled(!Busy);
        }
        // The automated probe drives ProcessViewportInput directly and disables device reads.
        internal bool DeviceInput { get; set; } = true;
        // Renderer bounds follow the skinned root; also refreshed immediately by Refresh().
        private void LateUpdate()
        {
            UpdateHighlight();
            // The camera or a placement may have moved: redraw the gizmo while it exists.
            if (gizmoOverlay != null && GizmoTarget != null) gizmoOverlay.MarkDirtyRepaint();
        }
        private void OnDestroy()
        {
            loadCancellation?.Cancel();
            if (settings != null) Destroy(settings); if (font != null) Destroy(font); if (input != null) Destroy(input);
            if (highlightMaterial != null) Destroy(highlightMaterial);
            if (pickMesh != null) Destroy(pickMesh);
            Application.wantsToQuit -= OnWantsToQuit;
        }
    }
}
