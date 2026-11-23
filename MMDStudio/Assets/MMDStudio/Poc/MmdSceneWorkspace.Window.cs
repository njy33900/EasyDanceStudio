using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // The app window: resizable (Player setting, the UI scales to fit), and full screen through
    // F11, the top bar's "전체 화면" or Unity's own Alt+Enter. Full screen is borderless at the
    // display's resolution; leaving it (F11, Esc, the button) gives back the window size it had.
    // Window size and mode are session state, not part of the scene file.
    public sealed partial class MmdSceneWorkspace
    {
        private Button fullScreenButton;
        private Vector2Int windowedSize;
        private FullScreenMode windowedMode = FullScreenMode.Windowed;
        private bool shownFullScreen;
        // A maximized window (Windows reports MaximizedWindow) is still a window, not full screen.
        internal bool FullScreen => Screen.fullScreenMode == FullScreenMode.FullScreenWindow || Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen;

        private void BuildWindowControls(VisualElement bar)
        {
            fullScreenButton = TextButton(bar, "FullScreenToggle", "전체 화면  F11", () => Guard(ToggleFullScreen));
            fullScreenButton.tooltip = "전체 화면 / 창 모드 (F11 · 전체 화면에서 Esc로 나가기). 창 가장자리를 끌어 크기를 바꿀 수 있습니다.";
        }

        internal void ToggleFullScreen()
        {
            CancelPointerDrag();
            if (!FullScreen)
            {
                windowedSize = new Vector2Int(Screen.width, Screen.height);
                windowedMode = Screen.fullScreenMode; // Windowed or MaximizedWindow: given back as it was
                // The monitor the window is on (not necessarily the primary one).
                var display = Screen.mainWindowDisplayInfo;
                var width = display.width > 0 ? display.width : Screen.currentResolution.width;
                var height = display.height > 0 ? display.height : Screen.currentResolution.height;
                Screen.SetResolution(width, height, FullScreenMode.FullScreenWindow);
                message = "전체 화면입니다. F11 또는 Esc로 창 모드로 돌아갑니다.";
            }
            else
            {
                var size = windowedSize.x >= 320 && windowedSize.y >= 240 ? windowedSize : new Vector2Int(1280, 720);
                Screen.SetResolution(size.x, size.y, windowedMode == FullScreenMode.MaximizedWindow ? FullScreenMode.MaximizedWindow : FullScreenMode.Windowed);
                message = "창 모드로 돌아왔습니다. 창 가장자리를 끌어 크기를 바꿀 수 있습니다.";
            }
            UpdateStatus();
        }

        // The mode changes a frame later (and Alt+Enter changes it outside the app): follow it.
        private void RefreshWindowControls()
        {
            if (fullScreenButton == null || shownFullScreen == FullScreen && fullScreenButton.text.Length > 0) return;
            shownFullScreen = FullScreen;
            fullScreenButton.text = shownFullScreen ? "창 모드  F11" : "전체 화면  F11";
        }
    }
}
