using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // MVP-10 runtime transform gizmo (spec 4.8 #4-5): W move, E rotate, R scale on the selected
    // object, in world axes at its placement origin. Drawn by UI Toolkit over the viewport (always
    // on top, no shader dependency); hit-testing and dragging use the projected handles in screen
    // space. Every drag step goes through MmdSceneService.SetPlacement (validated) and updates the
    // inspector fields, so numeric and gizmo edits stay in sync both ways.
    public sealed partial class MmdSceneWorkspace
    {
        public enum GizmoMode { Move, Rotate, Scale }
        public GizmoMode Gizmo { get; private set; } = GizmoMode.Move;
        public const int UniformHandle = 3;
        public const float GizmoPixels = 90, GizmoHitPixels = 9, RingSegments = 64;
        private static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward };
        private static readonly Color[] AxisColours = { new Color(.9f, .28f, .3f), new Color(.19f, .64f, .42f), new Color(.24f, .39f, .87f) };
        private static readonly Color ActiveColour = new Color(.96f, .65f, .14f);
        private VisualElement gizmoOverlay;
        private Button gizmoMoveButton, gizmoRotateButton, gizmoScaleButton;
        internal int HoverHandle { get; private set; } = -1;
        internal int DragHandle { get; private set; } = -1;
        private Vector2 dragStartMouse, dragLastMouse, dragAxisScreen;
        private Vector3 dragStartPosition, dragStartScale;
        private Quaternion dragStartRotation;
        private float dragWorldLength, dragAngle;

        private void BuildGizmoUi(VisualElement card)
        {
            gizmoOverlay = new VisualElement { name = "GizmoOverlay", pickingMode = PickingMode.Ignore };
            gizmoOverlay.style.position = Position.Absolute; gizmoOverlay.style.left = gizmoOverlay.style.right = gizmoOverlay.style.top = gizmoOverlay.style.bottom = 0;
            gizmoOverlay.generateVisualContent += DrawGizmo;
            card.Add(gizmoOverlay);
            var bar = new VisualElement { name = "GizmoModes" }; bar.AddToClassList("eds-gizmo-bar"); card.Add(bar);
            gizmoMoveButton = GizmoModeButton(bar, "GizmoMove", "이동", "W", GizmoMode.Move);
            gizmoRotateButton = GizmoModeButton(bar, "GizmoRotate", "회전", "E", GizmoMode.Rotate);
            gizmoScaleButton = GizmoModeButton(bar, "GizmoScale", "크기", "R", GizmoMode.Scale);
        }
        private Button GizmoModeButton(VisualElement bar, string name, string text, string key, GizmoMode mode)
        {
            var button = new Button(() => SetGizmoMode(mode)) { name = name, text = text + "  " + key };
            button.AddToClassList("eds-gizmo-button");
            var hint = text + " 기즈모 (" + key + ")";
            button.RegisterCallback<PointerEnterEvent>(_ => { hoverHint = hint; UpdateStatus(); });
            button.RegisterCallback<PointerLeaveEvent>(_ => { if (hoverHint == hint) hoverHint = null; UpdateStatus(); });
            bar.Add(button); return button;
        }
        internal void SetGizmoMode(GizmoMode mode)
        {
            Gizmo = mode; DragHandle = -1; HoverHandle = -1; RefreshGizmo();
        }
        private void RefreshGizmo()
        {
            if (gizmoOverlay == null) return;
            gizmoMoveButton.EnableInClassList("eds-gizmo-button--active", Gizmo == GizmoMode.Move);
            gizmoRotateButton.EnableInClassList("eds-gizmo-button--active", Gizmo == GizmoMode.Rotate);
            gizmoScaleButton.EnableInClassList("eds-gizmo-button--active", Gizmo == GizmoMode.Scale);
            var enabled = !Busy && GizmoTarget != null;
            gizmoMoveButton.SetEnabled(enabled); gizmoRotateButton.SetEnabled(enabled); gizmoScaleButton.SetEnabled(enabled);
            gizmoOverlay.MarkDirtyRepaint();
        }
        // The gizmo exists only for a visible selected object while the inspector shows it.
        internal MmdSceneService.SceneObject GizmoTarget =>
            Target == InspectorTarget.Object && Selected != null && Selected.Visible ? Selected : null;
        internal bool GizmoVisible => GizmoTarget != null && Camera.main != null && GizmoFrame(out _, out _);

        // Gizmo origin (world) and the world length that spans GizmoPixels on screen.
        private bool GizmoFrame(out Vector3 origin, out float worldLength)
        {
            origin = default; worldLength = 0;
            var item = GizmoTarget; var camera = Camera.main;
            if (item == null || camera == null) return false;
            origin = item.Placement.position;
            var depth = Vector3.Dot(origin - camera.transform.position, camera.transform.forward);
            if (depth <= camera.nearClipPlane) return false;
            worldLength = GizmoPixels * 2 * depth * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) / Mathf.Max(1, camera.pixelHeight);
            return true;
        }
        private static Vector2 Screen2(Camera camera, Vector3 world) { var p = camera.WorldToScreenPoint(world); return new Vector2(p.x, p.y); }
        private static Vector3 RingPoint(int axis, Vector3 origin, float radius, float t)
        {
            var u = Axes[(axis + 1) % 3]; var v = Axes[(axis + 2) % 3];
            return origin + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * radius;
        }
        // Screen-space handle points, for drawing, hit-testing and the automated probe.
        internal Vector2 GizmoOriginScreen() => GizmoFrame(out var o, out _) ? Screen2(Camera.main, o) : new Vector2(float.NaN, float.NaN);
        internal Vector2 GizmoAxisEndScreen(int axis) => GizmoFrame(out var o, out var l) ? Screen2(Camera.main, o + Axes[axis] * l) : new Vector2(float.NaN, float.NaN);
        internal Vector2 GizmoRingScreen(int axis, float t) => GizmoFrame(out var o, out var l) ? Screen2(Camera.main, RingPoint(axis, o, l * .8f, t)) : new Vector2(float.NaN, float.NaN);

        internal int GizmoHandleAt(Vector2 screen)
        {
            if (!GizmoFrame(out var origin, out var length)) return -1;
            var camera = Camera.main; var tolerance = GizmoHitPixels * Mathf.Max(1, Screen.height / 720f);
            var o = Screen2(camera, origin);
            if (Gizmo == GizmoMode.Scale && (screen - o).magnitude <= tolerance * 1.3f) return UniformHandle;
            var best = -1; var bestDistance = tolerance;
            for (var axis = 0; axis < 3; axis++)
            {
                float distance;
                if (Gizmo == GizmoMode.Rotate)
                {
                    distance = float.PositiveInfinity;
                    var previous = Screen2(camera, RingPoint(axis, origin, length * .8f, 0));
                    for (var i = 1; i <= RingSegments; i++)
                    {
                        var next = Screen2(camera, RingPoint(axis, origin, length * .8f, i * Mathf.PI * 2 / RingSegments));
                        distance = Mathf.Min(distance, SegmentDistance(screen, previous, next)); previous = next;
                    }
                }
                else
                {
                    var end = Screen2(camera, origin + Axes[axis] * length);
                    if ((end - o).magnitude < 4) continue; // axis points at the camera
                    distance = SegmentDistance(screen, o + (end - o) * .15f, end);
                }
                if (distance < bestDistance) { bestDistance = distance; best = axis; }
            }
            return best;
        }
        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; var t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        private bool BeginGizmoDrag(Vector2 screen)
        {
            var handle = GizmoHandleAt(screen);
            if (handle < 0 || !GizmoFrame(out var origin, out var length)) return false;
            var item = GizmoTarget; var camera = Camera.main;
            DragHandle = handle; dragStartMouse = dragLastMouse = screen; dragWorldLength = length; dragAngle = 0;
            dragStartPosition = item.Placement.localPosition; dragStartRotation = item.Placement.localRotation; dragStartScale = item.Placement.localScale;
            dragAxisScreen = handle == UniformHandle ? Vector2.one : Screen2(camera, origin + Axes[handle] * length) - Screen2(camera, origin);
            RefreshGizmo();
            return true;
        }
        private void UpdateGizmoDrag(Vector2 screen)
        {
            var item = GizmoTarget;
            if (item == null || DragHandle < 0) { DragHandle = -1; return; }
            var camera = Camera.main;
            var position = dragStartPosition; var rotation = dragStartRotation; var scale = dragStartScale;
            var moved = screen - dragStartMouse;
            if (Gizmo == GizmoMode.Move && DragHandle < 3)
            {
                if (dragAxisScreen.sqrMagnitude > 1) position += Axes[DragHandle] * (Vector2.Dot(moved, dragAxisScreen) / dragAxisScreen.sqrMagnitude * dragWorldLength);
            }
            else if (Gizmo == GizmoMode.Rotate && DragHandle < 3)
            {
                var centre = Screen2(camera, dragStartPosition);
                var from = dragLastMouse - centre; var to = screen - centre;
                if (from.magnitude > 3 && to.magnitude > 3) dragAngle += Vector2.SignedAngle(from, to);
                // Counter-clockwise on screen is a positive rotation when the axis faces the camera.
                var sign = Vector3.Dot(Axes[DragHandle], camera.transform.forward) < 0 ? 1 : -1;
                rotation = Quaternion.AngleAxis(dragAngle * sign, Axes[DragHandle]) * dragStartRotation;
            }
            else if (Gizmo == GizmoMode.Scale)
            {
                if (DragHandle == UniformHandle)
                    scale = dragStartScale * Mathf.Exp((moved.x + moved.y) / (GizmoPixels * 1.5f));
                else if (dragAxisScreen.sqrMagnitude > 1)
                {
                    var factor = Mathf.Max(.01f, 1 + Vector2.Dot(moved, dragAxisScreen) / dragAxisScreen.sqrMagnitude);
                    scale[DragHandle] = dragStartScale[DragHandle] * factor;
                }
                scale = new Vector3(Mathf.Clamp(scale.x, .001f, 1000), Mathf.Clamp(scale.y, .001f, 1000), Mathf.Clamp(scale.z, .001f, 1000));
            }
            dragLastMouse = screen;
            try
            {
                Scene.SetPlacement(item.Id, position, rotation.eulerAngles, scale);
                MarkDirty(); Error = "";
            }
            catch (Exception ex) { Error = ex.Message; DragHandle = -1; }
            RefreshTransformFields();
            UpdateTitle(); UpdateStatus(); UpdateHighlight(); gizmoOverlay.MarkDirtyRepaint();
        }
        private void EndGizmoDrag()
        {
            if (DragHandle < 0) return;
            DragHandle = -1;
            var item = GizmoTarget;
            if (item != null) message = (Gizmo == GizmoMode.Move ? "이동" : Gizmo == GizmoMode.Rotate ? "회전" : "크기") + ": " + System.IO.Path.GetFileNameWithoutExtension(item.ModelSource);
            Refresh();
        }
        private void UpdateGizmoHover(Vector2 screen, bool overViewport)
        {
            var hover = overViewport && DragHandle < 0 ? GizmoHandleAt(screen) : -1;
            if (hover != HoverHandle) { HoverHandle = hover; gizmoOverlay?.MarkDirtyRepaint(); }
        }
        private void RefreshTransformFields()
        {
            var selected = Selected;
            if (selected == null) return;
            position.SetValueWithoutNotify(Display(selected.Placement.localPosition));
            rotation.SetValueWithoutNotify(Display(selected.Placement.localEulerAngles));
            scale.SetValueWithoutNotify(Display(selected.Placement.localScale));
        }

        private Vector2 ToOverlay(Vector2 screen) =>
            gizmoOverlay.WorldToLocal(RuntimePanelUtils.ScreenToPanel(gizmoOverlay.panel, new Vector2(screen.x, Screen.height - screen.y)));
        private void DrawGizmo(MeshGenerationContext context)
        {
            if (gizmoOverlay.panel == null || !GizmoFrame(out var origin, out var length)) return;
            var camera = Camera.main; var p = context.painter2D;
            p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            var o = ToOverlay(Screen2(camera, origin));
            Color AxisColour(int handle) => handle == DragHandle || (DragHandle < 0 && handle == HoverHandle) ? ActiveColour : AxisColours[handle];
            if (Gizmo == GizmoMode.Rotate)
            {
                for (var axis = 0; axis < 3; axis++)
                {
                    p.strokeColor = AxisColour(axis); p.lineWidth = axis == DragHandle || axis == HoverHandle ? 3.2f : 2.2f;
                    p.BeginPath();
                    for (var i = 0; i <= RingSegments; i++)
                    {
                        var point = ToOverlay(Screen2(camera, RingPoint(axis, origin, length * .8f, i * Mathf.PI * 2 / RingSegments)));
                        if (i == 0) p.MoveTo(point); else p.LineTo(point);
                    }
                    p.Stroke();
                }
            }
            else
            {
                for (var axis = 0; axis < 3; axis++)
                {
                    var end = ToOverlay(Screen2(camera, origin + Axes[axis] * length));
                    var direction = end - o; if (direction.magnitude < 4) continue;
                    var colour = AxisColour(axis); var unit = direction.normalized; var normal = new Vector2(-unit.y, unit.x);
                    p.strokeColor = colour; p.fillColor = colour; p.lineWidth = axis == DragHandle || axis == HoverHandle ? 3.2f : 2.4f;
                    p.BeginPath(); p.MoveTo(o + direction * .15f); p.LineTo(Gizmo == GizmoMode.Move ? end - unit * 10 : end - unit * 5); p.Stroke();
                    p.BeginPath();
                    if (Gizmo == GizmoMode.Move) { p.MoveTo(end); p.LineTo(end - unit * 13 + normal * 5.5f); p.LineTo(end - unit * 13 - normal * 5.5f); }
                    else { p.MoveTo(end + (unit + normal) * 5); p.LineTo(end + (-unit + normal) * 5); p.LineTo(end + (-unit - normal) * 5); p.LineTo(end + (unit - normal) * 5); }
                    p.ClosePath(); p.Fill();
                }
            }
            var centreColour = Gizmo == GizmoMode.Scale && (DragHandle == UniformHandle || (DragHandle < 0 && HoverHandle == UniformHandle)) ? ActiveColour : new Color(.2f, .2f, .23f);
            p.fillColor = centreColour; p.BeginPath();
            if (Gizmo == GizmoMode.Scale) { p.MoveTo(o + new Vector2(-6, -6)); p.LineTo(o + new Vector2(6, -6)); p.LineTo(o + new Vector2(6, 6)); p.LineTo(o + new Vector2(-6, 6)); p.ClosePath(); }
            else p.Arc(o, 3.5f, Angle.Degrees(0), Angle.Degrees(360));
            p.Fill();
        }
    }
}
