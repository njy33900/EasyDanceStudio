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
            Text(missingSection, "이 항목은 장면 파일에 그대로 보존되며 저장해도 사라지지 않습니다. 파일 재연결은 다음 단계에서 지원합니다.", "eds-note");
            var missingButtons = Element(missingSection, "eds-row-buttons");
            TextButton(missingButtons, "MissingRemove", "장면에서 항목 삭제", RequestRemove, "eds-button--block", "eds-button--danger");

            transformSection = Section(content, "TransformSection", "변형");
            position = Vector(transformSection, "Position", "위치");
            rotation = Vector(transformSection, "Rotation", "회전");
            scale = Vector(transformSection, "Scale", "크기");
            position.RegisterValueChangedCallback(_ => ApplyPlacement());
            rotation.RegisterValueChangedCallback(_ => ApplyPlacement());
            scale.RegisterValueChangedCallback(_ => ApplyPlacement());
            visible = new Toggle("표시") { name = "Visible" }; transformSection.Add(visible);
            visible.RegisterValueChangedCallback(evt => Guard(() => { if (Selected != null) { Scene.SetVisible(SelectedId, evt.newValue); MarkDirty(); } }));
            var objectButtons = Element(transformSection, "eds-row-buttons");
            TextButton(objectButtons, "FrameSelected", "선택 보기", () => Guard(FrameSelected), "eds-button--block").style.marginRight = 6;
            remove = TextButton(objectButtons, "RemoveModel", "삭제", RequestRemove, "eds-button--block", "eds-button--danger");

            motionSection = Section(content, "MotionSection", "모션");
            motionInfo = Text(motionSection, "", "eds-section-text"); motionInfo.name = "MotionInfo";
            connect = TextButton(motionSection, "ConnectMotion", "VMD 연결", () => Pick(PickKind.Motion), "eds-button--block");
            // PH-03: experimental per-character physics, baked ahead of playback.
            physics = new Toggle("물리 (실험)") { name = "Physics" }; motionSection.Add(physics);
            physics.RegisterValueChangedCallback(evt => Guard(() => { if (Selected != null) { Scene.SetPhysics(SelectedId, evt.newValue); MarkDirty(); RefreshPhysicsInfo(); } }));
            physicsInfo = Text(motionSection, "", "eds-note"); physicsInfo.name = "PhysicsInfo";

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
            loop.RegisterValueChangedCallback(evt => Guard(() => { Scene.Loop = evt.newValue; MarkDirty(); }));
            speed = new Slider("속도", .5f, 2) { name = "Speed" }; speed.AddToClassList("eds-speed"); speed.SetValueWithoutNotify(1); options.Add(speed);
            speed.RegisterValueChangedCallback(evt => Guard(() => { Scene.Speed = evt.newValue; MarkDirty(); }));
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
        public void Select(string id)
        {
            if (!Scene.Objects.Any(item => item.Id == id)) throw new ArgumentException("장면 항목을 찾을 수 없습니다.");
            SelectedId = id; Target = InspectorTarget.Object; Error = "";
            var item = Selected;
            message = (item.Kind == MmdSceneService.ObjectKind.Stage ? "배경 선택: " : "캐릭터 선택: ") + Path.GetFileNameWithoutExtension(item.ModelSource);
            Refresh();
        }
        // Light/Camera entries in the explorer: show their settings; no 3D object is selected.
        internal void SelectTarget(InspectorTarget target)
        {
            if (target == InspectorTarget.Object) throw new ArgumentException(nameof(target));
            SelectedId = null; Target = target; Error = "";
            message = target == InspectorTarget.Light ? "조명 선택" : "카메라 선택";
            Refresh();
        }
        private void Pick(PickKind kind)
        {
            if (Busy) return;
            var playing = Scene.Playing; Scene.Playing = false;
            string path;
            try { path = WindowsRuntimeFilePicker.Open(kind != PickKind.Motion); }
            catch (Exception ex) { Error = ex.Message; Refresh(); return; }
            finally { Scene.ResumeAfterInterruption(playing); }
            if (string.IsNullOrEmpty(path)) { message = "파일 선택을 취소했습니다."; Error = ""; Refresh(); return; }
            StartCoroutine(ApplyPickerResult(path, kind));
        }
        public IEnumerator ApplyPickerResult(string path, bool model) => ApplyPickerResult(path, model ? PickKind.Character : PickKind.Motion);
        public IEnumerator ApplyPickerResult(string path, PickKind kind)
        {
            if (Busy) yield break;
            if (string.IsNullOrEmpty(path)) { message = "파일 선택을 취소했습니다."; Error = ""; Refresh(); yield break; }
            var previousMessage = message;
            Busy = true; Error = ""; message = "불러오는 중…";
            var playing = Scene.Playing; Scene.Playing = false; Refresh();
            yield return null;
            System.Threading.Tasks.Task<MmdSceneService.SceneObject> importTask = null;
            System.Threading.Tasks.Task motionTask = null;
            if (kind != PickKind.Motion)
            {
                loadCancellation = new System.Threading.CancellationTokenSource();
                RefreshLoadingCard();
                var objectKind = kind == PickKind.Stage ? MmdSceneService.ObjectKind.Stage : MmdSceneService.ObjectKind.Character;
                var position = objectKind == MmdSceneService.ObjectKind.Stage ? Vector3.zero :
                    new Vector3(Scene.Objects.Count(item => item.Kind == MmdSceneService.ObjectKind.Character) * 1.5f, 0, 0);
                importTask = Scene.ImportAsync(path, position, objectKind, stage =>
                {
                    if (loadCancellation == null || loadCancellation.IsCancellationRequested) return;
                    message = stage; UpdateStatus(); RefreshLoadingCard();
                }, loadCancellation.Token);
                while (!importTask.IsCompleted) yield return null;
            }
            else if (Selected != null && Selected.Kind == MmdSceneService.ObjectKind.Character)
            {
                loadCancellation = new System.Threading.CancellationTokenSource();
                RefreshLoadingCard();
                motionTask = Scene.ReplaceMotionAsync(SelectedId, path, stage =>
                {
                    if (loadCancellation == null || loadCancellation.IsCancellationRequested) return;
                    message = stage; UpdateStatus(); RefreshLoadingCard();
                }, loadCancellation.Token);
                while (!motionTask.IsCompleted) yield return null;
            }
            try
            {
                if (kind == PickKind.Character)
                {
                    var item = importTask.GetAwaiter().GetResult();
                    SelectedId = item.Id; MarkDirty(); message = "캐릭터를 추가했습니다. VMD를 연결하세요."; FrameAll();
                }
                else if (kind == PickKind.Stage)
                {
                    var item = importTask.GetAwaiter().GetResult();
                    SelectedId = item.Id; MarkDirty(); message = "배경을 추가했습니다. 배경은 모션 없이 고정됩니다."; FrameAll();
                }
                else
                {
                    if (Selected == null) throw new InvalidOperationException("캐릭터를 먼저 선택하세요.");
                    if (Selected.Kind != MmdSceneService.ObjectKind.Character) throw new InvalidOperationException("배경에는 VMD를 연결할 수 없습니다. 캐릭터를 선택하세요.");
                    motionTask.GetAwaiter().GetResult(); ForgetUnresolvedMotion(SelectedId); MarkDirty(); message = "선택한 캐릭터에 VMD를 연결했습니다.";
                }
            }
            catch (OperationCanceledException) { Error = ""; message = "불러오기를 취소했습니다."; }
            // The failure text lives in Error so it disappears with the error instead of
            // lingering in the normal status message after later successful actions.
            catch (Exception ex) { Error = "불러오지 못했습니다: " + ex.Message + " · 기존 장면은 유지됩니다."; message = previousMessage; }
            finally
            {
                loadCancellation?.Dispose(); loadCancellation = null;
                Busy = false; Scene.ResumeAfterInterruption(playing); Refresh(); RefreshLoadingCard();
            }
        }
        private void ApplyPlacement()
        {
            Guard(() => { if (Selected != null) { Scene.SetPlacement(SelectedId, position.value, rotation.value, scale.value); MarkDirty(); } });
        }
        private void Guard(Action action)
        {
            if (Busy) return;
            try { action(); Error = ""; }
            catch (Exception ex) { Error = ex.Message; }
            Refresh();
        }
        private void FrameAll() { Frame(Scene.Objects.Where(item => item.Visible)); }
        // Probe hook: frame the given objects.
        internal void FrameIds(params string[] ids) { Frame(Scene.Objects.Where(item => ids.Contains(item.Id))); }
        private void FrameSelected()
        {
            if (Selected != null) Frame(new[] { Selected }); else FrameAll();
        }
        internal static bool TryBounds(System.Collections.Generic.IEnumerable<MmdSceneService.SceneObject> items, out Bounds bounds)
        {
            var renderers = items.SelectMany(item => item.Model.root.GetComponentsInChildren<SkinnedMeshRenderer>()).ToArray();
            bounds = default;
            if (renderers.Length == 0) return false;
            bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return true;
        }
        private void Frame(System.Collections.Generic.IEnumerable<MmdSceneService.SceneObject> items)
        {
            if (!TryBounds(items, out var bounds)) return;
            var camera = Camera.main;
            View.FrameInRect(bounds, camera.fieldOfView, Screen.width, Screen.height, SafeViewportRect()); View.Apply(camera);
        }

        // Pointer is over an opaque UI panel (the viewport and its hint ignore picking).
        // screen uses Input System coordinates: origin bottom-left.
        internal bool PointerOverUi(Vector2 screen)
        {
            if (Root?.panel == null) return true;
            var panelPosition = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(screen.x, Screen.height - screen.y));
            return Root.panel.Pick(panelPosition) != null;
        }
        internal bool PointerInViewport(Vector2 screen) =>
            Camera.main != null && Camera.main.pixelRect.Contains(screen) && !PointerOverUi(screen);
        internal bool TextInputFocused()
        {
            for (var element = Root?.panel?.focusController?.focusedElement as VisualElement; element != null; element = element.parent)
                // Text fields, and composite numeric fields (Vector3Field takes focus for its FloatFields).
                if (element.ClassListContains("unity-base-text-field") || element.ClassListContains("unity-composite-field")) return true;
            return false;
        }
        public const float ClickSlopPixels = 5;
        // One frame of viewport input. Right drag orbits, middle or Shift+right drag pans,
        // wheel zooms, left click (press+release within ClickSlopPixels) picks. Keys:
        // F frames the selection, Space plays/pauses, Delete asks to delete the selection,
        // Esc cancels the delete dialog, W/E/R switch the gizmo (gizmoKey 1/2/3); a left press on a
        // gizmo handle drags it instead of picking. A press must start inside the viewport; a started
        // drag continues across panels. While a text field has focus, wheel and keys are
        // ignored and a viewport press only commits the field. While the delete dialog is
        // open only Esc is handled (its full-screen overlay also blocks pointer input).
        internal void ProcessViewportInput(Vector2 screen, Vector2 delta, float scrollNotches, bool orbitHeld, bool panHeld, bool frameKey,
            bool leftHeld = false, bool playKey = false, bool deleteKey = false, bool cancelKey = false, bool saveKey = false, int gizmoKey = 0, bool focusKey = false)
        {
            var pressed = (orbitHeld && !orbitWasHeld) || (panHeld && !panWasHeld);
            var leftPressed = leftHeld && !leftWasHeld; var leftReleased = !leftHeld && leftWasHeld;
            orbitWasHeld = orbitHeld; panWasHeld = panHeld; leftWasHeld = leftHeld;
            if (!orbitHeld && !panHeld && drag != Drag.Gizmo) drag = Drag.None;
            if (drag == Drag.Gizmo && !leftHeld) { drag = Drag.None; EndGizmoDrag(); }
            if (Camera.main == null) { CancelPointerDrag(); return; }
            if (DialogVisible)
            {
                CancelPointerDrag();
                if (cancelKey) CancelDialog();
                return;
            }
            if (ConfirmVisible)
            {
                CancelPointerDrag();
                if (cancelKey) CancelRemove();
                return;
            }
            var textFocused = TextInputFocused();
            if (!textFocused && focusKey) { SetFocusView(!FocusView); return; }
            if (FocusView && cancelKey) { SetFocusView(false); return; }
            if (Busy) { CancelPointerDrag(); return; }
            var camera = Camera.main;
            if ((pressed || leftPressed) && drag == Drag.None && PointerInViewport(screen))
            {
                var focused = Root.panel.focusController.focusedElement as VisualElement;
                // Release focus so keys typed afterwards are not routed to a panel control.
                focused?.Blur();
                if (textFocused) return; // This press only commits the field.
                if (leftPressed && BeginGizmoDrag(screen)) { drag = Drag.Gizmo; return; }
                if (leftPressed) { clickCandidate = true; clickStart = screen; }
                else { drag = panHeld ? Drag.Pan : Drag.Orbit; return; } // The press frame's delta precedes the drag.
            }
            if (clickCandidate && (screen - clickStart).sqrMagnitude > ClickSlopPixels * ClickSlopPixels) clickCandidate = false;
            if (leftReleased && clickCandidate)
            {
                clickCandidate = false;
                SelectFromViewport(PickAt(screen));
            }
            if (!leftHeld) clickCandidate = false;
            if (drag == Drag.Gizmo) { UpdateGizmoDrag(screen); return; }
            UpdateGizmoHover(screen, PointerInViewport(screen));
            if (drag == Drag.Orbit) View.Orbit(delta);
            else if (drag == Drag.Pan) View.Pan(delta, camera.pixelHeight, camera.fieldOfView);
            else if (!textFocused)
            {
                if (scrollNotches != 0 && PointerInViewport(screen)) View.Zoom(scrollNotches);
                if (frameKey) FrameSelected();
                if (playKey && Scene.Duration > 0) Guard(() => Scene.Playing = !Scene.Playing);
                if (deleteKey && (Selected != null || SelectedUnresolved != null)) RequestRemove();
                if (saveKey) Guard(() => Save());
                if (gizmoKey >= 1 && gizmoKey <= 3) SetGizmoMode((GizmoMode)(gizmoKey - 1));
            }
            View.Apply(camera);
        }
        private void CancelPointerDrag()
        {
            if (drag == Drag.Gizmo) EndGizmoDrag();
            drag = Drag.None; clickCandidate = false;
        }
        private void SelectFromViewport(MmdSceneService.SceneObject item)
        {
            if (item == null) { SelectedId = null; Target = InspectorTarget.Object; Error = ""; message = "선택을 해제했습니다."; Refresh(); }
            else Select(item.Id);
        }

        // Nearest visible object whose skinned mesh triangles the screen ray hits.
        // Each renderer's world bounds is tested first; candidate meshes are baked at
        // their current pose, so picking follows animation. Triangles are double-sided.
        internal MmdSceneService.SceneObject PickAt(Vector2 screen)
        {
            var camera = Camera.main;
            if (camera == null || !PointerInViewport(screen)) return null;
            var ray = camera.ScreenPointToRay(screen);
            MmdSceneService.SceneObject best = null; var bestDistance = float.PositiveInfinity;
            foreach (var item in Scene.Objects)
            {
                if (!item.Visible) continue;
                foreach (var renderer in item.Model.root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!renderer.enabled || renderer.sharedMesh == null) continue;
                    if (!renderer.bounds.IntersectRay(ray, out var enter) || enter >= bestDistance) continue;
                    if (RayHitsBakedMesh(renderer, ray, bestDistance, out var distance)) { best = item; bestDistance = distance; }
                }
            }
            return best;
        }
        private bool RayHitsBakedMesh(SkinnedMeshRenderer renderer, Ray ray, float limit, out float distance)
        {
            distance = limit;
            if (pickMesh == null) pickMesh = new Mesh { name = "Viewport Pick" };
            // useScale false: vertices are in the renderer's unscaled local space, so the
            // full localToWorldMatrix applies. The ray is moved into that space instead;
            // keeping the transformed direction unnormalized keeps t in world units.
            renderer.BakeMesh(pickMesh, false);
            var toLocal = renderer.transform.localToWorldMatrix.inverse;
            var origin = toLocal.MultiplyPoint3x4(ray.origin); var direction = toLocal.MultiplyVector(ray.direction);
            pickMesh.GetVertices(pickVertices);
            var hit = false;
            for (var sub = 0; sub < pickMesh.subMeshCount; sub++)
            {
                pickMesh.GetTriangles(pickTriangles, sub);
                for (var i = 0; i + 2 < pickTriangles.Count; i += 3)
                {
                    var t = IntersectTriangle(origin, direction, pickVertices[pickTriangles[i]], pickVertices[pickTriangles[i + 1]], pickVertices[pickTriangles[i + 2]]);
                    if (t > 0 && t < distance) { distance = t; hit = true; }
                }
            }
            return hit;
        }
        // Möller–Trumbore; returns the ray parameter or -1.
        private static float IntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
        {
            var e1 = b - a; var e2 = c - a; var p = Vector3.Cross(direction, e2); var det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) return -1;
            var inv = 1 / det; var s = origin - a; var u = Vector3.Dot(s, p) * inv;
            if (u < 0 || u > 1) return -1;
            var q = Vector3.Cross(s, e1); var v = Vector3.Dot(direction, q) * inv;
            if (v < 0 || u + v > 1) return -1;
            return Vector3.Dot(e2, q) * inv;
        }

        internal bool ConfirmVisible { get; private set; }
        private void CreateConfirm()
        {
            // Full-screen pickable overlay: blocks panels and the viewport while open.
            confirm = Element(Root, "eds-overlay"); confirm.name = "RemoveConfirm";
            var box = Element(confirm, "eds-dialog");
            confirmText = Text(box, "", "eds-dialog-text"); confirmText.name = "RemoveConfirmText";
            var row = Element(box, "eds-dialog-buttons");
            TextButton(row, "CancelRemove", "취소", CancelRemove);
            TextButton(row, "ConfirmRemove", "삭제", ConfirmRemove, "eds-button--danger-fill").style.marginRight = 0;
            confirm.style.display = DisplayStyle.None;
        }
        private void RequestRemove()
        {
            if (Busy || (Selected == null && SelectedUnresolved == null)) return;
            var missing = SelectedUnresolved;
            if (missing != null)
            {
                confirmId = SelectedId;
                confirmText.text = "누락된 " + (missing.Data.type == SceneObjectData.StageType ? "배경" : "캐릭터") + " '" + Path.GetFileNameWithoutExtension(missing.Data.assetPath) +
                    "'을(를) 장면에서 삭제할까요?\n저장하면 이 참조가 장면 파일에서 사라집니다. 실행 취소는 아직 지원하지 않습니다.";
                confirm.style.display = DisplayStyle.Flex; ConfirmVisible = true;
                Root.Q<Button>("CancelRemove").Focus();
                return;
            }
            var item = Selected;
            confirmId = item.Id;
            confirmText.text = (item.Kind == MmdSceneService.ObjectKind.Stage ? "배경" : "캐릭터") + " '" + Path.GetFileNameWithoutExtension(item.ModelSource) +
                "'을(를) 장면에서 삭제할까요?\n원본 파일은 삭제되지 않습니다. 실행 취소는 아직 지원하지 않습니다.";
            confirm.style.display = DisplayStyle.Flex; ConfirmVisible = true;
            Root.Q<Button>("CancelRemove").Focus();
        }
        private void CancelRemove()
        {
            if (!ConfirmVisible) return;
            confirm.style.display = DisplayStyle.None; ConfirmVisible = false; confirmId = null;
            message = "삭제를 취소했습니다."; Refresh();
        }
        private void ConfirmRemove()
        {
            if (!ConfirmVisible) return;
            var id = confirmId; confirm.style.display = DisplayStyle.None; ConfirmVisible = false; confirmId = null;
            (Root.panel?.focusController?.focusedElement as VisualElement)?.Blur();
            Guard(() =>
            {
                if (RemoveUnresolved(id))
                {
                    if (SelectedId == id) SelectedId = Scene.Objects.FirstOrDefault()?.Id;
                    message = "누락 항목을 장면에서 삭제했습니다.";
                    return;
                }
                var item = Scene.Objects.FirstOrDefault(value => value.Id == id);
                if (item == null) return;
                Scene.Remove(id); ForgetUnresolvedMotion(id); MarkDirty();
                if (SelectedId == id) SelectedId = Scene.Objects.FirstOrDefault()?.Id;
                message = item.Kind == MmdSceneService.ObjectKind.Stage ? "선택한 배경을 삭제했습니다." : "선택한 캐릭터를 삭제했습니다.";
            });
        }
        private void ReadDevices()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            var shift = keyboard != null && keyboard.shiftKey.isPressed;
            var right = mouse.rightButton.isPressed;
            var scroll = mouse.scroll.ReadValue().y;
            // Windows reports 120 per wheel notch unless the Input System normalizes it.
            var notches = Mathf.Abs(scroll) >= 10 ? scroll / 120 : scroll;
            bool Pressed(UnityEngine.InputSystem.Controls.KeyControl key) => keyboard != null && key.wasPressedThisFrame;
            ProcessViewportInput(mouse.position.ReadValue(), mouse.delta.ReadValue(), notches,
                right && !shift, mouse.middleButton.isPressed || (right && shift), Pressed(keyboard?.fKey),
                mouse.leftButton.isPressed, Pressed(keyboard?.spaceKey), Pressed(keyboard?.deleteKey), Pressed(keyboard?.escapeKey),
                keyboard != null && keyboard.ctrlKey.isPressed && keyboard.sKey.wasPressedThisFrame,
                keyboard == null || keyboard.ctrlKey.isPressed ? 0 : keyboard.wKey.wasPressedThisFrame ? 1 : keyboard.eKey.wasPressedThisFrame ? 2 : keyboard.rKey.wasPressedThisFrame ? 3 : 0,
                keyboard != null && !keyboard.ctrlKey.isPressed && keyboard.hKey.wasPressedThisFrame);
        }

        private void CreateHighlight()
        {
            highlight = new GameObject("Selection Highlight");
            highlight.transform.SetParent(transform, false);
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            highlightMaterial = new Material(shader) { color = new Color(.15f, .39f, .92f, .95f) };
            highlightBox = Line("Bounds", 16, false);
            highlightRing = Line("Ground", 48, true);
            highlight.SetActive(false);
        }
        private LineRenderer Line(string name, int count, bool loop)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(highlight.transform, false);
            line.sharedMaterial = highlightMaterial; line.positionCount = count; line.loop = loop; line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            line.startColor = line.endColor = Color.white; line.numCapVertices = 2;
            return line;
        }
        internal void UpdateHighlight()
        {
            if (highlight == null) return;
            var selected = Selected;
            if (FocusView || selected == null || !selected.Visible || !TryBounds(new[] { selected }, out var bounds)) { highlight.SetActive(false); return; }
            HighlightBounds = bounds; highlight.SetActive(true);
            var min = bounds.min; var max = bounds.max;
            Vector3 P(float x, float y, float z) => new Vector3(x, y, z);
            var b0 = P(min.x, min.y, min.z); var b1 = P(max.x, min.y, min.z); var b2 = P(max.x, min.y, max.z); var b3 = P(min.x, min.y, max.z);
            var t0 = P(min.x, max.y, min.z); var t1 = P(max.x, max.y, min.z); var t2 = P(max.x, max.y, max.z); var t3 = P(min.x, max.y, max.z);
            highlightBox.SetPositions(new[] { b0, b1, b2, b3, b0, t0, t1, b1, t1, t2, b2, t2, t3, b3, t3, t0 });
            var width = Mathf.Clamp(View.Distance * .0025f, .002f, .5f);
            highlightBox.widthMultiplier = width; highlightRing.widthMultiplier = width * 1.5f;
            var centre = selected.Placement.position; centre.y = min.y;
            var radius = Mathf.Max(.1f, Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.1f);
            for (var i = 0; i < highlightRing.positionCount; i++)
            {
                var angle = i * Mathf.PI * 2 / highlightRing.positionCount;
                highlightRing.SetPosition(i, centre + new Vector3(Mathf.Cos(angle) * radius, .002f, Mathf.Sin(angle) * radius));
            }
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
                selected.PhysicsReady ? "물리 준비 완료 · 어느 시간으로 이동해도 같은 결과" :
                "물리 준비 중 " + (selected.PhysicsProgress * 100).ToString("0") + "% · 준비된 구간까지 물리로 재생";
            if (text != lastPhysicsText) { physicsInfo.text = text; Show(physicsInfo, text.Length > 0); lastPhysicsText = text; }
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
