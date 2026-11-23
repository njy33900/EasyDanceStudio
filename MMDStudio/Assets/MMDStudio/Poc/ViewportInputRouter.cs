using UnityEngine;

namespace MmdStudio.Poc
{
    // One frame of raw viewport input (Input System screen coordinates: origin bottom-left).
    internal struct ViewportInputFrame
    {
        public Vector2 Screen, Delta;
        public float ScrollNotches;
        // Right button without Shift (dolly), middle button or Shift+right (pan), left button.
        public bool DollyHeld, PanHeld, LeftHeld;
        // The window lost the keyboard/mouse focus this frame (Alt+Tab, another window clicked).
        public bool FocusLost;
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
            frame.DollyHeld = right && !shift; frame.PanHeld = mouse.middleButton.isPressed || (right && shift);
            frame.LeftHeld = mouse.leftButton.isPressed;
            frame.FocusLost = !Application.isFocused;
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
        // Camera moves; the router calls them only with a non-zero amount, so a press and release
        // without movement never moves the camera (or releases a camera VMD).
        void Orbit(Vector2 delta);
        void Pan(Vector2 delta);
        void Dolly(float pixels);
        void Zoom(float notches, Vector2 screen);
        void ApplyView();
        void FrameSelection();
        void TogglePlay();
        void RequestDelete();
        void Save();
        void Undo();
        void Redo();
        void SetGizmo(int key);
    }

    // H1-04 / v0.61: viewport input routing, separated from the workspace.
    //   Left click (press and release within ClickSlopPixels): pick; empty space clears.
    //   Left drag (beyond ClickSlopPixels): orbit around the pivot; the release does not pick.
    //   Right drag: dolly (up = closer, down = farther; horizontal movement is ignored).
    //   Middle or Shift+right drag: pan.  Wheel: zoom toward the surface under the cursor.
    // Keys: F frames the selection, Space plays/pauses, Delete asks to delete the selection, Esc
    // cancels a dialog, W/E/R switch the gizmo; a left press on a gizmo handle drags it instead.
    // A press must start inside the viewport (not on a card, tab or dialog); a started drag
    // continues across panels. Only one button drives at a time: presses while another is held
    // are ignored. A drag ends on its button's release, focus loss, a dialog, or busy (loading,
    // output). While a text field has focus, wheel and keys are ignored and a viewport press only
    // commits the field. While a dialog is open only Esc is handled (its overlay blocks the pointer).
    internal sealed class ViewportInputRouter
    {
        public const float ClickSlopPixels = 5;
        private enum Drag { None, Orbit, Dolly, Pan, Gizmo }
        private Drag drag;
        private bool dollyWasHeld, panWasHeld, leftWasHeld, clickCandidate;
        private Vector2 clickStart;
        internal bool Dragging => drag != Drag.None;

        public void Route(in ViewportInputFrame input, IViewportInputTarget target)
        {
            var screen = input.Screen;
            var leftPressed = input.LeftHeld && !leftWasHeld; var leftReleased = !input.LeftHeld && leftWasHeld;
            var dollyPressed = input.DollyHeld && !dollyWasHeld; var panPressed = input.PanHeld && !panWasHeld;
            dollyWasHeld = input.DollyHeld; panWasHeld = input.PanHeld; leftWasHeld = input.LeftHeld;
            // A drag ends with its own button.
            if (drag == Drag.Gizmo && !input.LeftHeld) { drag = Drag.None; target.EndGizmoDrag(); }
            else if ((drag == Drag.Orbit && !input.LeftHeld) || (drag == Drag.Dolly && !input.DollyHeld) || (drag == Drag.Pan && !input.PanHeld)) drag = Drag.None;
            if (input.FocusLost || !target.CameraReady) { Cancel(target); return; }
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
            if ((leftPressed || dollyPressed || panPressed) && drag == Drag.None && !clickCandidate && target.PointerInViewport(screen))
            {
                // Release focus so keys typed afterwards are not routed to a panel control.
                target.ReleaseFocus();
                if (textFocused) return; // This press only commits the field.
                if (leftPressed && target.BeginGizmoDrag(screen)) { drag = Drag.Gizmo; return; }
                if (leftPressed) { clickCandidate = true; clickStart = screen; }
                else { drag = panPressed ? Drag.Pan : Drag.Dolly; return; } // The press frame's delta precedes the drag.
            }
            var orbitDelta = input.Delta;
            if (clickCandidate)
            {
                if (!input.LeftHeld)
                {
                    clickCandidate = false;
                    if (leftReleased) target.ClickAt(screen);
                }
                else if ((screen - clickStart).sqrMagnitude > ClickSlopPixels * ClickSlopPixels)
                {
                    // Beyond the click slop the press becomes an orbit. It catches up with the
                    // movement held back while it could still have been a click.
                    clickCandidate = false; drag = Drag.Orbit; orbitDelta = screen - clickStart;
                }
            }
            if (drag == Drag.Gizmo) { target.UpdateGizmoDrag(screen); return; }
            target.UpdateGizmoHover(screen, drag == Drag.None && target.PointerInViewport(screen));
            if (drag == Drag.Orbit) { if (orbitDelta != Vector2.zero) target.Orbit(orbitDelta); }
            else if (drag == Drag.Dolly) { if (input.Delta.y != 0) target.Dolly(input.Delta.y); }
            else if (drag == Drag.Pan) { if (input.Delta != Vector2.zero) target.Pan(input.Delta); }
            else if (!textFocused)
            {
                if (input.ScrollNotches != 0 && target.PointerInViewport(screen)) target.Zoom(input.ScrollNotches, screen);
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

        // Ends any pointer drag (dialogs opening, focus view, busy, focus loss).
        public void Cancel(IViewportInputTarget target)
        {
            if (drag == Drag.Gizmo) target.EndGizmoDrag();
            drag = Drag.None; clickCandidate = false;
        }
    }
}
