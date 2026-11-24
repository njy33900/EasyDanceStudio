using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H1-04: the studio side of viewport input (ViewportInputRouter holds the rules) and mesh
    // picking (split from MmdSceneWorkspace.cs). v0.61: left drag orbits, right drag dollies, the
    // wheel zooms toward the surface under the cursor; a pick also tells which PMX material was hit.
    public sealed partial class MmdSceneWorkspace : IViewportInputTarget
    {
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
        public const float ClickSlopPixels = ViewportInputRouter.ClickSlopPixels;
        private readonly ViewportInputRouter inputRouter = new ViewportInputRouter();
        internal bool PointerDragging => inputRouter.Dragging;
        // One frame of viewport input (see ViewportInputRouter for the rules). Probes call this
        // directly; the Player feeds it from ReadDevices. dollyHeld: right button without Shift.
        internal void ProcessViewportInput(Vector2 screen, Vector2 delta, float scrollNotches, bool dollyHeld, bool panHeld, bool frameKey,
            bool leftHeld = false, bool playKey = false, bool deleteKey = false, bool cancelKey = false, bool saveKey = false, int gizmoKey = 0, bool focusKey = false,
            bool undoKey = false, bool redoKey = false, bool focusLost = false, bool fullScreenKey = false) =>
            inputRouter.Route(new ViewportInputFrame
            {
                Screen = screen, Delta = delta, ScrollNotches = scrollNotches, DollyHeld = dollyHeld, PanHeld = panHeld, LeftHeld = leftHeld,
                FrameKey = frameKey, PlayKey = playKey, DeleteKey = deleteKey, CancelKey = cancelKey, SaveKey = saveKey, GizmoKey = gizmoKey,
                FocusKey = focusKey, UndoKey = undoKey, RedoKey = redoKey, FocusLost = focusLost, FullScreenKey = fullScreenKey,
            }, this);
        private void ReadDevices() { if (ViewportInputFrame.ReadDevices(out var frame)) inputRouter.Route(frame, this); }
        private void CancelPointerDrag() => inputRouter.Cancel(this);
        // Alt+Tab or a click into another window ends any drag (the button may be released elsewhere).
        private void OnApplicationFocus(bool focused) { if (!focused && Scene != null) CancelPointerDrag(); }

        // ----- IViewportInputTarget: what the router may ask of the studio -----
        bool IViewportInputTarget.CameraReady => Camera.main != null;
        bool IViewportInputTarget.DialogOpen => DialogVisible;
        void IViewportInputTarget.CancelDialog() => CancelDialog();
        bool IViewportInputTarget.ConfirmOpen => ConfirmVisible;
        void IViewportInputTarget.CancelConfirm() => CancelRemove();
        bool IViewportInputTarget.TextFocused => TextInputFocused();
        bool IViewportInputTarget.FocusView => FocusView;
        void IViewportInputTarget.SetFocusView(bool focused) => SetFocusView(focused);
        bool IViewportInputTarget.Busy => Busy;
        bool IViewportInputTarget.FullScreen => FullScreen;
        void IViewportInputTarget.ToggleFullScreen() => ToggleFullScreen(); // also while loading (Guard would skip it)
        bool IViewportInputTarget.PointerInViewport(Vector2 screen) => PointerInViewport(screen);
        void IViewportInputTarget.ReleaseFocus() => (Root.panel?.focusController?.focusedElement as VisualElement)?.Blur();
        bool IViewportInputTarget.BeginGizmoDrag(Vector2 screen) => BeginGizmoDrag(screen);
        void IViewportInputTarget.UpdateGizmoDrag(Vector2 screen) => UpdateGizmoDrag(screen);
        void IViewportInputTarget.EndGizmoDrag() => EndGizmoDrag();
        void IViewportInputTarget.UpdateGizmoHover(Vector2 screen, bool overViewport) => UpdateGizmoHover(screen, overViewport);
        void IViewportInputTarget.ClickAt(Vector2 screen)
        {
            var hit = RaycastScene(screen);
            SelectFromViewport(hit.Item);
            if (hit.Item != null) PickCorrectionMaterial(hit.Item, hit.PmxMaterial);
        }
        // H2a-04 / v0.61: manual navigation has priority over a camera VMD. It is released only
        // when the camera actually moves (the router never sends a zero amount), continuing from
        // the pose the motion showed.
        void IViewportInputTarget.Orbit(Vector2 delta)
        {
            if (!Finite(delta.x) || !Finite(delta.y)) return;
            ReleaseCameraMotion(); View.Orbit(delta);
        }
        void IViewportInputTarget.Pan(Vector2 delta)
        {
            if (!Finite(delta.x) || !Finite(delta.y)) return;
            ReleaseCameraMotion(); View.Pan(delta, Camera.main.pixelHeight, Camera.main.fieldOfView);
        }
        void IViewportInputTarget.Dolly(float pixels)
        {
            if (!Finite(pixels) || pixels == 0) return;
            ReleaseCameraMotion(); View.Dolly(pixels, Camera.main.pixelHeight);
        }
        void IViewportInputTarget.Zoom(float notches, Vector2 screen) => ZoomAt(notches, screen);
        // Wheel: toward the model surface under the cursor, or the pivot when there is none.
        internal void ZoomAt(float notches, Vector2 screen)
        {
            if (!Finite(notches) || notches == 0) return;
            ReleaseCameraMotion();
            var hit = RaycastScene(screen);
            if (hit.Item != null) View.ZoomToward(hit.Point, notches); else View.Zoom(notches);
            LastZoomHitSurface = hit.Item != null;
        }
        internal bool LastZoomHitSurface { get; private set; }
        void IViewportInputTarget.ApplyView() { if (!CameraFollowing) View.Apply(Camera.main); }
        void IViewportInputTarget.FrameSelection() { ReleaseCameraMotion(); FrameSelected(); }
        void IViewportInputTarget.TogglePlay() { if (Scene.Duration > 0) Guard(() => Scene.Playing = !Scene.Playing); }
        void IViewportInputTarget.RequestDelete() { if (Selected != null || SelectedUnresolved != null) RequestRemove(); }
        void IViewportInputTarget.Save() => Guard(() => Save());
        void IViewportInputTarget.Undo() => Guard(Undo);
        void IViewportInputTarget.Redo() => Guard(Redo);
        void IViewportInputTarget.SetGizmo(int key) => SetGizmoMode((GizmoMode)(key - 1));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // ----- Picking -----
        internal struct SceneHit
        {
            public MmdSceneService.SceneObject Item;
            public Vector3 Point;        // world space
            public float Distance;       // along the ray, world units
            public int PmxMaterial;      // PMX material index of the hit triangle, -1 when unknown
        }
        internal MmdSceneService.SceneObject PickAt(Vector2 screen) => RaycastScene(screen).Item;
        // Nearest visible object whose skinned mesh triangles the screen ray hits.
        // Each renderer's world bounds is tested first; candidate meshes are baked at
        // their current pose, so picking follows animation. Triangles are double-sided.
        internal SceneHit RaycastScene(Vector2 screen)
        {
            var result = new SceneHit { PmxMaterial = -1, Distance = float.PositiveInfinity };
            var camera = Camera.main;
            if (camera == null || !PointerInViewport(screen)) return result;
            var ray = camera.ScreenPointToRay(screen);
            foreach (var item in Scene.Objects)
            {
                if (!item.Visible) continue;
                foreach (var renderer in item.SkinnedRenderers)
                {
                    if (!renderer.enabled || renderer.sharedMesh == null) continue;
                    if (!renderer.bounds.IntersectRay(ray, out var enter) || enter >= result.Distance) continue;
                    if (RayHitsBakedMesh(renderer, ray, result.Distance, out var distance, out var subMesh))
                    {
                        result.Item = item; result.Distance = distance; result.Point = ray.GetPoint(distance);
                        result.PmxMaterial = item.StylerOrCreate.PmxIndexOf(renderer, subMesh);
                    }
                }
            }
            return result;
        }
        private bool RayHitsBakedMesh(SkinnedMeshRenderer renderer, Ray ray, float limit, out float distance, out int subMeshHit)
        {
            distance = limit; subMeshHit = -1;
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
                    if (t > 0 && t < distance) { distance = t; hit = true; subMeshHit = sub; }
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
    }
}
