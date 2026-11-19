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
        private Label info, status, time, selectionTitle;
        private Button add, addStage, connect, remove, play, stop;
        private Vector3Field position, rotation, scale;
        private Toggle visible, loop;
        private Slider timeline;
        private PanelSettings settings;
        private Font font;
        private GameObject input;
        private VisualElement viewport;
        private enum Drag { None, Orbit, Pan }
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
        private string message = "캐릭터나 배경을 추가하고 캐릭터에 VMD를 연결하세요. · 물리 OFF";
        private MmdSceneService.SceneObject Selected => Scene.Objects.FirstOrDefault(item => item.Id == SelectedId);

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
            Root.style.position = Position.Absolute;
            Root.style.left = Root.style.right = Root.style.top = Root.style.bottom = 0;
            Root.pickingMode = PickingMode.Ignore;
            Root.style.color = Color.white;
            font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 15);
            Root.style.unityFontDefinition = FontDefinition.FromFont(font);
            Root.style.fontSize = 14;
            var toolbar = Panel(Root, 48); toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            var title = new Label("Easy Dance Studio"); title.style.fontSize = 20; title.style.marginRight = 24; toolbar.Add(title);
            add = Button(toolbar, "AddModel", "캐릭터 추가", () => Pick(PickKind.Character));
            addStage = Button(toolbar, "AddStage", "배경 추가", () => Pick(PickKind.Stage));
            Button(toolbar, "FrameAll", "전체 보기", () => Guard(FrameAll));
            var body = new VisualElement(); body.pickingMode = PickingMode.Ignore; body.style.flexGrow = 1; body.style.flexBasis = 0; body.style.minHeight = 0; body.style.flexDirection = FlexDirection.Row; Root.Add(body);
            var explorer = Panel(body); explorer.style.width = 240;
            explorer.Add(new Label("장면"));
            list = new ScrollView(); list.name = "ObjectList"; list.style.flexGrow = 1; explorer.Add(list);
            viewport = new VisualElement { name = "Viewport", pickingMode = PickingMode.Ignore }; viewport.style.flexGrow = 1; body.Add(viewport);
            viewport.RegisterCallback<GeometryChangedEvent>(_ => UpdateViewport(viewport));
            var hint = new Label("좌클릭: 선택 · 우클릭 드래그: 회전 · 휠클릭/Shift+우클릭: 이동 · 휠: 확대 · F: 선택 보기 · Space: 재생 · Delete: 삭제") { name = "ViewportHint", pickingMode = PickingMode.Ignore };
            hint.style.position = Position.Absolute; hint.style.left = 8; hint.style.bottom = 4; hint.style.fontSize = 12;
            hint.style.color = new Color(.75f, .82f, .9f, .8f); viewport.Add(hint);
            var inspector = Panel(body); inspector.style.width = 280;
            var content = new ScrollView(); content.style.flexGrow = 1; inspector.Add(content);
            BuildInspectorTabs(inspector, content);
            selectionTitle = new Label("선택한 항목") { name = "SelectionTitle" }; content.Add(selectionTitle);
            info = new Label(); info.name = "SelectionInfo"; info.style.whiteSpace = WhiteSpace.Normal; info.style.marginTop = info.style.marginBottom = 8; content.Add(info);
            connect = Button(content, "ConnectMotion", "선택 캐릭터 VMD 연결", () => Pick(PickKind.Motion));
            position = Vector(content, "Position", "위치");
            rotation = Vector(content, "Rotation", "회전");
            scale = Vector(content, "Scale", "크기");
            position.RegisterValueChangedCallback(_ => ApplyPlacement());
            rotation.RegisterValueChangedCallback(_ => ApplyPlacement());
            scale.RegisterValueChangedCallback(_ => ApplyPlacement());
            visible = new Toggle("표시") { name = "Visible" }; content.Add(visible);
            visible.RegisterValueChangedCallback(evt => Guard(() => { if (Selected != null) Scene.SetVisible(SelectedId, evt.newValue); }));
            Button(content, "FrameSelected", "선택 보기", () => Guard(FrameSelected));
            remove = Button(content, "RemoveModel", "선택 항목 삭제", RequestRemove);
            var playback = Panel(Root, 86);
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.height = 36; playback.Add(row);
            play = Button(row, "PlayPause", "재생", () => Guard(() => Scene.Playing = !Scene.Playing));
            stop = Button(row, "Stop", "정지", () => Guard(Scene.Stop));
            loop = new Toggle("반복") { name = "Loop" }; loop.style.marginLeft = 16; row.Add(loop);
            loop.style.width = 90; loop.labelElement.style.minWidth = loop.labelElement.style.width = 38;
            loop.RegisterValueChangedCallback(evt => Guard(() => Scene.Loop = evt.newValue));
            var speed = new Slider("속도", .5f, 2) { name = "Speed" }; speed.style.width = 220; speed.SetValueWithoutNotify(1); row.Add(speed);
            speed.labelElement.style.minWidth = speed.labelElement.style.width = 42;
            speed.RegisterValueChangedCallback(evt => Guard(() => Scene.Speed = evt.newValue));
            time = new Label(); time.style.marginLeft = 20; row.Add(time);
            timeline = new Slider(0, 1) { name = "Timeline" }; playback.Add(timeline);
            timeline.RegisterValueChangedCallback(evt => Guard(() => Scene.Seek(evt.newValue * Scene.Duration)));
            var statusPanel = Panel(Root, 34); status = new Label { name = "Status" }; statusPanel.Add(status);
            status.style.whiteSpace = WhiteSpace.NoWrap; status.style.overflow = Overflow.Hidden; status.style.textOverflow = TextOverflow.Ellipsis;
            status.style.marginTop = status.style.marginBottom = 0; status.style.height = 22; status.style.unityTextAlign = TextAnchor.MiddleLeft;
            var cube = GameObject.Find("Cube"); if (cube != null) cube.SetActive(false);
            CreateHighlight();
            CreateConfirm();
            View.Apply(Camera.main);
            Refresh();
        }
        private static VisualElement Panel(VisualElement parent, float height = 0)
        {
            var panel = new VisualElement(); panel.style.backgroundColor = new Color(.065f, .08f, .11f, 1);
            panel.style.paddingLeft = panel.style.paddingRight = 10; panel.style.paddingTop = panel.style.paddingBottom = 6;
            if (height > 0) { panel.style.height = height; panel.style.flexShrink = 0; }
            parent.Add(panel); return panel;
        }
        private static Button Button(VisualElement parent, string name, string text, Action action)
        {
            var button = new Button(action) { name = name, text = text }; button.style.minHeight = 30;
            button.style.backgroundColor = new Color(.17f, .21f, .28f); button.style.color = Color.white;
            button.style.marginBottom = 6; button.style.marginRight = 6; parent.Add(button); return button;
        }
        private static Vector3Field Vector(VisualElement parent, string name, string label)
        {
            var field = new Vector3Field(label) { name = name }; field.style.marginBottom = 8; parent.Add(field);
            field.style.flexDirection = FlexDirection.Column;
            field.labelElement.style.minWidth = 0; field.labelElement.style.width = StyleKeyword.Auto;
            var compositeInput = field.Q(className: "unity-base-field__input");
            if (compositeInput != null) { compositeInput.style.flexDirection = FlexDirection.Row; compositeInput.style.minHeight = 30; }
            field.Query<FloatField>().ForEach(item =>
            {
                item.isDelayed = true; item.style.flexDirection = FlexDirection.Row;
                item.style.width = 70; item.style.minWidth = 0; item.style.height = 28; item.style.flexShrink = 0;
                item.style.marginLeft = 0; item.style.marginRight = 4;
                item.labelElement.style.minWidth = item.labelElement.style.width = 14;
                var textInput = item.Q(className: "unity-base-field__input");
                if (textInput != null)
                {
                    textInput.style.minWidth = 40; textInput.style.flexGrow = 1; textInput.style.paddingLeft = 4;
                    textInput.style.paddingTop = textInput.style.paddingBottom = 0;
                    textInput.style.backgroundColor = new Color(.14f, .18f, .24f); textInput.style.color = Color.white;
                }
                item.Query<TextElement>().ForEach(text =>
                {
                    if (ReferenceEquals(text, item.labelElement)) return;
                    text.style.unityTextAlign = TextAnchor.MiddleLeft; text.style.fontSize = 14;
                    text.style.paddingTop = text.style.paddingBottom = 0; text.style.minHeight = 0; text.style.height = 24;
                });
            }); return field;
        }
        private void UpdateViewport(VisualElement viewport)
        {
            var root = Root.worldBound; var rect = viewport.worldBound;
            if (root.width > 0 && root.height > 0 && rect.width > 0 && rect.height > 0)
                Camera.main.rect = new Rect((rect.x-root.x)/root.width, 1-(rect.yMax-root.y)/root.height, rect.width/root.width, rect.height/root.height);
        }
        public void Select(string id)
        {
            if (!Scene.Objects.Any(item => item.Id == id)) throw new ArgumentException("장면 항목을 찾을 수 없습니다.");
            SelectedId = id; Error = "";
            var item = Selected;
            message = (item.Kind == MmdSceneService.ObjectKind.Stage ? "배경 선택: " : "캐릭터 선택: ") + Path.GetFileNameWithoutExtension(item.ModelSource);
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
            try
            {
                if (kind == PickKind.Character)
                {
                    var characters = Scene.Objects.Count(item => item.Kind == MmdSceneService.ObjectKind.Character);
                    var item = Scene.Import(path, null, new Vector3(characters * 1.5f, 0, 0));
                    SelectedId = item.Id; message = "캐릭터를 추가했습니다. VMD를 연결하세요."; FrameAll();
                }
                else if (kind == PickKind.Stage)
                {
                    var item = Scene.ImportStage(path, Vector3.zero);
                    SelectedId = item.Id; message = "배경을 추가했습니다. 배경은 모션 없이 고정됩니다."; FrameAll();
                }
                else
                {
                    if (Selected == null) throw new InvalidOperationException("캐릭터를 먼저 선택하세요.");
                    if (Selected.Kind != MmdSceneService.ObjectKind.Character) throw new InvalidOperationException("배경에는 VMD를 연결할 수 없습니다. 캐릭터를 선택하세요.");
                    Scene.ReplaceMotion(SelectedId, path); message = "선택한 캐릭터에 VMD를 연결했습니다.";
                }
            }
            // The failure text lives in Error so it disappears with the error instead of
            // lingering in the normal status message after later successful actions.
            catch (Exception ex) { Error = "불러오지 못했습니다: " + ex.Message + " · 기존 장면은 유지됩니다."; message = previousMessage; }
            finally { Busy = false; Scene.ResumeAfterInterruption(playing); Refresh(); }
        }
        private void ApplyPlacement()
        {
            Guard(() => { if (Selected != null) Scene.SetPlacement(SelectedId, position.value, rotation.value, scale.value); });
        }
        private void Guard(Action action)
        {
            if (Busy) return;
            try { action(); Error = ""; }
            catch (Exception ex) { Error = ex.Message; }
            Refresh();
        }
        private void FrameAll() { Frame(Scene.Objects.Where(item => item.Visible)); }
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
            View.Frame(bounds, camera.fieldOfView, camera.aspect); View.Apply(camera);
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
        // Esc cancels the delete dialog. A press must start inside the viewport; a started
        // drag continues across panels. While a text field has focus, wheel and keys are
        // ignored and a viewport press only commits the field. While the delete dialog is
        // open only Esc is handled (its full-screen overlay also blocks pointer input).
        internal void ProcessViewportInput(Vector2 screen, Vector2 delta, float scrollNotches, bool orbitHeld, bool panHeld, bool frameKey,
            bool leftHeld = false, bool playKey = false, bool deleteKey = false, bool cancelKey = false)
        {
            var pressed = (orbitHeld && !orbitWasHeld) || (panHeld && !panWasHeld);
            var leftPressed = leftHeld && !leftWasHeld; var leftReleased = !leftHeld && leftWasHeld;
            orbitWasHeld = orbitHeld; panWasHeld = panHeld; leftWasHeld = leftHeld;
            if (!orbitHeld && !panHeld) drag = Drag.None;
            if (Busy || Camera.main == null) { drag = Drag.None; clickCandidate = false; return; }
            if (ConfirmVisible)
            {
                drag = Drag.None; clickCandidate = false;
                if (cancelKey) CancelRemove();
                return;
            }
            var camera = Camera.main;
            var textFocused = TextInputFocused();
            if ((pressed || leftPressed) && drag == Drag.None && PointerInViewport(screen))
            {
                var focused = Root.panel.focusController.focusedElement as VisualElement;
                // Release focus so keys typed afterwards are not routed to a panel control.
                focused?.Blur();
                if (textFocused) return; // This press only commits the field.
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
            if (drag == Drag.Orbit) View.Orbit(delta);
            else if (drag == Drag.Pan) View.Pan(delta, camera.pixelHeight, camera.fieldOfView);
            else if (!textFocused)
            {
                if (scrollNotches != 0 && PointerInViewport(screen)) View.Zoom(scrollNotches);
                if (frameKey) FrameSelected();
                if (playKey && Scene.Duration > 0) Guard(() => Scene.Playing = !Scene.Playing);
                if (deleteKey && Selected != null) RequestRemove();
            }
            View.Apply(camera);
        }
        private void SelectFromViewport(MmdSceneService.SceneObject item)
        {
            if (item == null) { SelectedId = null; Error = ""; message = "선택을 해제했습니다."; Refresh(); }
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
            confirm = new VisualElement { name = "RemoveConfirm" };
            confirm.style.position = Position.Absolute; confirm.style.left = confirm.style.right = confirm.style.top = confirm.style.bottom = 0;
            confirm.style.backgroundColor = new Color(0, 0, 0, .55f); confirm.style.alignItems = Align.Center; confirm.style.justifyContent = Justify.Center;
            var box = new VisualElement(); box.style.backgroundColor = new Color(.1f, .12f, .16f); box.style.width = 380;
            box.style.paddingLeft = box.style.paddingRight = 16; box.style.paddingTop = box.style.paddingBottom = 14; confirm.Add(box);
            confirmText = new Label { name = "RemoveConfirmText" }; confirmText.style.whiteSpace = WhiteSpace.Normal; confirmText.style.marginBottom = 12; box.Add(confirmText);
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.justifyContent = Justify.FlexEnd; box.Add(row);
            Button(row, "ConfirmRemove", "삭제", ConfirmRemove).style.backgroundColor = new Color(.55f, .2f, .2f);
            Button(row, "CancelRemove", "취소 (Esc)", CancelRemove);
            confirm.style.display = DisplayStyle.None; Root.Add(confirm);
        }
        private void RequestRemove()
        {
            if (Busy || Selected == null) return;
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
                var item = Scene.Objects.FirstOrDefault(value => value.Id == id);
                if (item == null) return;
                Scene.Remove(id);
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
                mouse.leftButton.isPressed, Pressed(keyboard?.spaceKey), Pressed(keyboard?.deleteKey), Pressed(keyboard?.escapeKey));
        }

        private void CreateHighlight()
        {
            highlight = new GameObject("Selection Highlight");
            highlight.transform.SetParent(transform, false);
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            highlightMaterial = new Material(shader) { color = new Color(1, .72f, .18f, .9f) };
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
            if (selected == null || !selected.Visible || !TryBounds(new[] { selected }, out var bounds)) { highlight.SetActive(false); return; }
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
                var header = new Label(group.Item2 + " (" + items.Length + ")") { name = group.Item3 };
                header.style.marginTop = 6; header.style.marginBottom = 4; header.style.color = new Color(.65f, .72f, .82f); list.Add(header);
                foreach (var item in items)
                {
                    var id = item.Id;
                    var label = Path.GetFileNameWithoutExtension(item.ModelSource) + (item.Visible ? "" : " (숨김)");
                    var button = Button(list, "object-" + id, label, () => Select(id));
                    if (id == SelectedId) button.style.backgroundColor = new Color(.15f, .3f, .5f);
                    button.SetEnabled(!Busy);
                }
            }
            var selected = Selected;
            var isCharacter = selected != null && selected.Kind == MmdSceneService.ObjectKind.Character;
            add.SetEnabled(!Busy); addStage.SetEnabled(!Busy); connect.SetEnabled(!Busy && isCharacter); remove.SetEnabled(!Busy && selected != null);
            selectionTitle.text = selected == null ? "선택한 항목" : isCharacter ? "선택한 캐릭터" : "선택한 배경";
            position.SetEnabled(!Busy && selected != null); rotation.SetEnabled(!Busy && selected != null); scale.SetEnabled(!Busy && selected != null);
            visible.SetEnabled(!Busy && selected != null);
            if (selected != null)
            {
                position.SetValueWithoutNotify(selected.Placement.localPosition); rotation.SetValueWithoutNotify(selected.Placement.localEulerAngles); scale.SetValueWithoutNotify(selected.Placement.localScale);
                visible.SetValueWithoutNotify(selected.Visible);
                var motion = !isCharacter ? "배경 · 모션 없음(고정)" : selected.MotionSource == null ? "모션 없음" : Path.GetFileName(selected.MotionSource);
                info.text = Path.GetFileName(selected.ModelSource) + "\n" + motion + "\n" + string.Join("\n", selected.Summary.warnings);
            }
            else { info.text = "선택한 항목 없음"; position.SetValueWithoutNotify(Vector3.zero); rotation.SetValueWithoutNotify(Vector3.zero); scale.SetValueWithoutNotify(Vector3.one); }
            play.SetEnabled(!Busy && Scene.Duration > 0); stop.SetEnabled(!Busy && Scene.Duration > 0); timeline.SetEnabled(!Busy && Scene.Duration > 0);
            loop.SetValueWithoutNotify(Scene.Loop);
            status.text = Error.Length == 0 ? message : Error;
            status.style.color = Error.Length == 0 ? new Color(.75f, .85f, .95f) : new Color(1, .5f, .45f);
            UpdateClock();
            UpdateHighlight();
            RefreshGraphics();
        }
        private void UpdateClock()
        {
            play.text = Scene.Playing ? "일시정지" : "재생";
            timeline.SetValueWithoutNotify(Scene.Duration > 0 ? (float)(Scene.Seconds / Scene.Duration) : 0);
            time.text = Scene.Seconds.ToString("F1") + " / " + Scene.Duration.ToString("F1") + " 초 · 물리 OFF";
        }
        private void Update()
        {
            if (Scene == null) return;
            UpdateClock();
            if (DeviceInput) ReadDevices();
        }
        // The automated probe drives ProcessViewportInput directly and disables device reads.
        internal bool DeviceInput { get; set; } = true;
        // Renderer bounds follow the skinned root; also refreshed immediately by Refresh().
        private void LateUpdate() { UpdateHighlight(); }
        private void OnDestroy()
        {
            if (settings != null) Destroy(settings); if (font != null) Destroy(font); if (input != null) Destroy(input);
            if (highlightMaterial != null) Destroy(highlightMaterial);
            if (pickMesh != null) Destroy(pickMesh);
        }
    }
}
