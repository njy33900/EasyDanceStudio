using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H1-04: viewport input routing — device polling, the per-frame input router and mesh picking
    // (split from MmdSceneWorkspace.cs).
    public sealed partial class MmdSceneWorkspace
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
            bool leftHeld = false, bool playKey = false, bool deleteKey = false, bool cancelKey = false, bool saveKey = false, int gizmoKey = 0, bool focusKey = false,
            bool undoKey = false, bool redoKey = false)
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
                if (undoKey) Guard(Undo);
                if (redoKey) Guard(Redo);
                if (gizmoKey >= 1 && gizmoKey <= 3) SetGizmoMode((GizmoMode)(gizmoKey - 1));
            }
            View.Apply(camera);
        }
        private void CancelPointerDrag()
        {
            if (drag == Drag.Gizmo) EndGizmoDrag();
            drag = Drag.None; clickCandidate = false;
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
                keyboard != null && !keyboard.ctrlKey.isPressed && keyboard.hKey.wasPressedThisFrame,
                keyboard != null && keyboard.ctrlKey.isPressed && !keyboard.shiftKey.isPressed && keyboard.zKey.wasPressedThisFrame,
                keyboard != null && keyboard.ctrlKey.isPressed && (keyboard.yKey.wasPressedThisFrame || (keyboard.shiftKey.isPressed && keyboard.zKey.wasPressedThisFrame)));
        }
    }
}
