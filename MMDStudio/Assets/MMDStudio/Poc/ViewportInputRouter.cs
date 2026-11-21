using UnityEngine;

namespace MmdStudio.Poc
{
    // One frame of raw viewport input (Input System screen coordinates: origin bottom-left).
    internal struct ViewportInputFrame
    {
        public Vector2 Screen, Delta;
        public float ScrollNotches;
        public bool OrbitHeld, PanHeld, LeftHeld;
        public bool FrameKey, PlayKey, DeleteKey, CancelKey, SaveKey, FocusKey, UndoKey, RedoKey;
        public int GizmoKey; // 1 move, 2 rotate, 3 scale

        // Reads mouse and keyboard; false when there is no mouse.
        public static bool ReadDevices(out ViewportInputFrame frame)
        {
            frame = default;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return false;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            var shift = keyboard != null && keyboard.shiftKey.isPressed;
            var ctrl = keyboard != null && keyboard.ctrlKey.isPressed;
            var right = mouse.rightButton.isPressed;
            var scroll = mouse.scroll.ReadValue().y;
            bool Pressed(UnityEngine.InputSystem.Controls.KeyControl key) => keyboard != null && key.wasPressedThisFrame;
            frame.Screen = mouse.position.ReadValue(); frame.Delta = mouse.delta.ReadValue();
            // Windows reports 120 per wheel notch unless the Input System normalizes it.
            frame.ScrollNotches = Mathf.Abs(scroll) >= 10 ? scroll / 120 : scroll;
            frame.OrbitHeld = right && !shift; frame.PanHeld = mouse.middleButton.isPressed || (right && shift);
            frame.LeftHeld = mouse.leftButton.isPressed;
            frame.FrameKey = Pressed(keyboard?.fKey); frame.PlayKey = Pressed(keyboard?.spaceKey);
            frame.DeleteKey = Pressed(keyboard?.deleteKey); frame.CancelKey = Pressed(keyboard?.escapeKey);
            frame.SaveKey = ctrl && keyboard.sKey.wasPressedThisFrame;
            frame.GizmoKey = keyboard == null || ctrl ? 0 : keyboard.wKey.wasPressedThisFrame ? 1 : keyboard.eKey.wasPressedThisFrame ? 2 : keyboard.rKey.wasPressedThisFrame ? 3 : 0;
            frame.FocusKey = keyboard != null && !ctrl && keyboard.hKey.wasPressedThisFrame;
            frame.UndoKey = ctrl && !shift && keyboard.zKey.wasPressedThisFrame;
            frame.RedoKey = ctrl && (keyboard.yKey.wasPressedThisFrame || (shift && keyboard.zKey.wasPressedThisFrame));
            return true;
        }
    }

    // What the router may ask of or do to the studio. MmdSceneWorkspace implements it; the router
    // holds no reference to UI Toolkit, the scene or the camera.
    internal interface IViewportInputTarget
    {
        bool CameraReady { get; }
        bool DialogOpen { get; }
        void CancelDialog();
        bool ConfirmOpen { get; }
        void CancelConfirm();
        bool TextFocused { get; }
        bool FocusView { get; }
        void SetFocusView(bool focused);
        bool Busy { get; }
        bool PointerInViewport(Vector2 screen);
        void ReleaseFocus();
        bool BeginGizmoDrag(Vector2 screen);
        void UpdateGizmoDrag(Vector2 screen);
        void EndGizmoDrag();
        void UpdateGizmoHover(Vector2 screen, bool overViewport);
        void ClickAt(Vector2 screen);
        void Orbit(Vector2 delta);
        void Pan(Vector2 delta);
        void Zoom(float notches);
        void ApplyView();
        void FrameSelection();
        void TogglePlay();
        void RequestDelete();
        void Save();
        void Undo();
        void Redo();
        void SetGizmo(int key);
    }

    // H1-04: viewport input routing, separated from the workspace. Right drag orbits, middle or
    // Shift+right drag pans, wheel zooms, left click (press+release within ClickSlopPixels) picks.
    // Keys: F frames the selection, Space plays/pauses, Delete asks to delete the selection, Esc
    // cancels a dialog, W/E/R switch the gizmo; a left press on a gizmo handle drags it instead of
    // picking. A press must start inside the viewport; a started drag continues across panels.
    // While a text field has focus, wheel and keys are ignored and a viewport press only commits
    // the field. While a dialog is open only Esc is handled (its overlay also blocks the pointer).
    internal sealed class ViewportInputRouter
    {
        public const float ClickSlopPixels = 5;
        private enum Drag { None, Orbit, Pan, Gizmo }
        private Drag drag;
        private bool orbitWasHeld, panWasHeld, leftWasHeld, clickCandidate;
        private Vector2 clickStart;

        public void Route(in ViewportInputFrame input, IViewportInputTarget target)
        {
            var screen = input.Screen;
            var pressed = (input.OrbitHeld && !orbitWasHeld) || (input.PanHeld && !panWasHeld);
            var leftPressed = input.LeftHeld && !leftWasHeld; var leftReleased = !input.LeftHeld && leftWasHeld;
            orbitWasHeld = input.OrbitHeld; panWasHeld = input.PanHeld; leftWasHeld = input.LeftHeld;
            if (!input.OrbitHeld && !input.PanHeld && drag != Drag.Gizmo) drag = Drag.None;
            if (drag == Drag.Gizmo && !input.LeftHeld) { drag = Drag.None; target.EndGizmoDrag(); }
            if (!target.CameraReady) { Cancel(target); return; }
            if (target.DialogOpen)
            {
                Cancel(target);
                if (input.CancelKey) target.CancelDialog();
                return;
            }
            if (target.ConfirmOpen)
            {
                Cancel(target);
                if (input.CancelKey) target.CancelConfirm();
                return;
            }
            var textFocused = target.TextFocused;
            if (!textFocused && input.FocusKey) { target.SetFocusView(!target.FocusView); return; }
            if (target.FocusView && input.CancelKey) { target.SetFocusView(false); return; }
            if (target.Busy) { Cancel(target); return; }
            if ((pressed || leftPressed) && drag == Drag.None && target.PointerInViewport(screen))
            {
                // Release focus so keys typed afterwards are not routed to a panel control.
                target.ReleaseFocus();
                if (textFocused) return; // This press only commits the field.
                if (leftPressed && target.BeginGizmoDrag(screen)) { drag = Drag.Gizmo; return; }
                if (leftPressed) { clickCandidate = true; clickStart = screen; }
                else { drag = input.PanHeld ? Drag.Pan : Drag.Orbit; return; } // The press frame's delta precedes the drag.
            }
            if (clickCandidate && (screen - clickStart).sqrMagnitude > ClickSlopPixels * ClickSlopPixels) clickCandidate = false;
            if (leftReleased && clickCandidate)
            {
                clickCandidate = false;
                target.ClickAt(screen);
            }
            if (!input.LeftHeld) clickCandidate = false;
            if (drag == Drag.Gizmo) { target.UpdateGizmoDrag(screen); return; }
            target.UpdateGizmoHover(screen, target.PointerInViewport(screen));
            if (drag == Drag.Orbit) target.Orbit(input.Delta);
            else if (drag == Drag.Pan) target.Pan(input.Delta);
            else if (!textFocused)
            {
                if (input.ScrollNotches != 0 && target.PointerInViewport(screen)) target.Zoom(input.ScrollNotches);
                if (input.FrameKey) target.FrameSelection();
                if (input.PlayKey) target.TogglePlay();
                if (input.DeleteKey) target.RequestDelete();
                if (input.SaveKey) target.Save();
                if (input.UndoKey) target.Undo();
                if (input.RedoKey) target.Redo();
                if (input.GizmoKey >= 1 && input.GizmoKey <= 3) target.SetGizmo(input.GizmoKey);
            }
            target.ApplyView();
        }

        // Ends any pointer drag (dialogs opening, focus view, busy).
        public void Cancel(IViewportInputTarget target)
        {
            if (drag == Drag.Gizmo) target.EndGizmoDrag();
            drag = Drag.None; clickCandidate = false;
        }
    }
}
