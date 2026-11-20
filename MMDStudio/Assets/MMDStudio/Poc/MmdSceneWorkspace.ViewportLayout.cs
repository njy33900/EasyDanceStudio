using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // V0: full-window 3D with independent floating controls. Visibility is session UI state,
    // deliberately absent from .mmdscene and reset to visible on every launch.
    public sealed partial class MmdSceneWorkspace
    {
        private VisualElement topbar, explorer, inspector, playback, statusBar, viewMenu;
        private Button restoreControls;
        private Label focusNotice;
        private VisualElement loadingCard;
        private Label loadingLabel;
        private Button cancelLoading;
        private System.Threading.CancellationTokenSource loadCancellation;
        private readonly Dictionary<string, bool> panelVisibility = new Dictionary<string, bool>
        {
            { "TopBar", true }, { "Explorer", true }, { "Inspector", true },
            { "PlaybackBar", true }, { "StatusBar", true }, { "GizmoModes", true }
        };
        private readonly Dictionary<string, Button> panelButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> collapsedTabs = new Dictionary<string, Button>();
        private bool viewMenuOpen;
        internal bool FocusView { get; private set; }
        private float focusNoticeUntil;
        private string lastFocusNotice;

        private void BuildViewportLayoutControls()
        {
            TextButton(topbar, "ViewMenuToggle", "보기", () =>
            {
                viewMenuOpen = !viewMenuOpen;
                RefreshPanelVisibility();
            });
            TextButton(topbar, "FocusViewToggle", "집중 보기  H", () => SetFocusView(true));
            HideButton(topbar, "HideTopBar", "TopBar");
            viewMenu = Element(Root, "eds-view-menu"); viewMenu.name = "ViewMenu";
            AddPanelButton("TopBar", "상단 도구 모음");
            AddPanelButton("Explorer", "장면 탐색기");
            AddPanelButton("Inspector", "속성 창");
            AddPanelButton("PlaybackBar", "재생 바");
            AddPanelButton("StatusBar", "상태 바");
            AddPanelButton("GizmoModes", "기즈모 모드 바");
            AddCollapsedTab("Explorer", "탐색기  ▸", "eds-collapsed-tab--explorer");
            AddCollapsedTab("Inspector", "◂  속성", "eds-collapsed-tab--inspector");
            AddCollapsedTab("PlaybackBar", "재생  ▴", "eds-collapsed-tab--playback");
            AddCollapsedTab("StatusBar", "상태  ▴", "eds-collapsed-tab--status");
            AddCollapsedTab("GizmoModes", "기즈모  ▾", "eds-collapsed-tab--gizmo");

            restoreControls = TextButton(Root, "RestoreControls", "도구 모음  ▾", () =>
            {
                if (FocusView) SetFocusView(false);
                else SetPanelVisible("TopBar", true);
            });
            restoreControls.AddToClassList("eds-restore-controls");
            restoreControls.tooltip = "조작 UI 복원 (H)";
            focusNotice = Text(Root, "", "eds-focus-notice"); focusNotice.name = "FocusNotice";
            focusNotice.pickingMode = PickingMode.Ignore;
            loadingCard = Element(Root, "eds-loading-card"); loadingCard.name = "LoadingCard";
            loadingLabel = Text(loadingCard, "", "eds-loading-label"); loadingLabel.name = "LoadingLabel";
            cancelLoading = TextButton(loadingCard, "CancelLoading", "취소", () =>
            {
                if (loadCancellation == null || loadCancellation.IsCancellationRequested) return;
                loadCancellation.Cancel();
                message = "취소 요청됨 · 현재 단계가 끝나면 중단합니다.";
                RefreshLoadingCard(); UpdateStatus();
            });
            viewMenu.BringToFront();
            RefreshPanelVisibility();
        }
        private void AddPanelButton(string name, string label)
        {
            var button = TextButton(viewMenu, "Toggle" + name, label, () => SetPanelVisible(name, !panelVisibility[name]));
            button.AddToClassList("eds-view-menu-row");
            panelButtons.Add(name, button);
        }
        private void AddCollapsedTab(string name, string label, string positionClass)
        {
            var tab = TextButton(Root, "Show" + name, label, () => SetPanelVisible(name, true),
                "eds-collapsed-tab", positionClass);
            tab.tooltip = PanelLabel(name) + " 다시 열기";
            collapsedTabs.Add(name, tab);
        }
        private Button HideButton(VisualElement parent, string name, string panelName)
        {
            var button = TextButton(parent, name, "−", () => SetPanelVisible(panelName, false), "eds-card-hide");
            button.tooltip = "숨기기 · 가장자리 탭에서 다시 표시";
            return button;
        }
        internal bool PanelVisible(string name) => panelVisibility.TryGetValue(name, out var visible) && !FocusView && visible;
        internal void SetPanelVisible(string name, bool visible)
        {
            if (!panelVisibility.ContainsKey(name)) throw new ArgumentException("Unknown panel: " + name, nameof(name));
            if (panelVisibility[name] == visible) return;
            CancelPointerDrag();
            if (!visible && Root?.panel?.focusController?.focusedElement is VisualElement focused) focused.Blur();
            panelVisibility[name] = visible;
            if (name == "TopBar" && !visible) viewMenuOpen = false;
            RefreshPanelVisibility();
        }
        internal void SetFocusView(bool focused)
        {
            if (FocusView == focused) return;
            CancelPointerDrag();
            if (Root?.panel?.focusController?.focusedElement is VisualElement element) element.Blur();
            FocusView = focused; viewMenuOpen = false;
            lastFocusNotice = message;
            RefreshPanelVisibility();
        }
        private void RefreshPanelVisibility()
        {
            if (topbar == null || viewMenu == null) return;
            topbar.style.display = PanelVisible("TopBar") ? DisplayStyle.Flex : DisplayStyle.None;
            explorer.style.display = PanelVisible("Explorer") ? DisplayStyle.Flex : DisplayStyle.None;
            inspector.style.display = PanelVisible("Inspector") ? DisplayStyle.Flex : DisplayStyle.None;
            playback.style.display = PanelVisible("PlaybackBar") ? DisplayStyle.Flex : DisplayStyle.None;
            statusBar.style.display = PanelVisible("StatusBar") ? DisplayStyle.Flex : DisplayStyle.None;
            gizmoBar.style.display = PanelVisible("GizmoModes") ? DisplayStyle.Flex : DisplayStyle.None;
            gizmoOverlay.style.display = PanelVisible("GizmoModes") ? DisplayStyle.Flex : DisplayStyle.None;
            viewportHint.style.display = FocusView ? DisplayStyle.None : DisplayStyle.Flex;
            viewMenu.style.display = viewMenuOpen && PanelVisible("TopBar") ? DisplayStyle.Flex : DisplayStyle.None;
            restoreControls.style.display = FocusView || !PanelVisible("TopBar") ? DisplayStyle.Flex : DisplayStyle.None;
            restoreControls.text = FocusView ? "UI" : "도구 모음  ▾";
            restoreControls.style.width = FocusView ? 34 : 104;
            foreach (var entry in collapsedTabs)
                entry.Value.style.display = !FocusView && !panelVisibility[entry.Key] ? DisplayStyle.Flex : DisplayStyle.None;
            collapsedTabs["GizmoModes"].style.left = PanelVisible("Explorer") ? 260 : 108;
            gizmoBar.style.left = PanelVisible("Explorer") ? 260 : 108;
            viewportHint.style.left = PanelVisible("Explorer") ? 260 : 20;
            foreach (var entry in panelButtons)
                entry.Value.text = (panelVisibility[entry.Key] ? "☑  " : "□  ") + PanelLabel(entry.Key);
            UpdateHighlight();
            RefreshFocusNotice();
            RefreshLoadingCard();
        }
        private void RefreshLoadingCard()
        {
            if (loadingCard == null) return;
            loadingCard.style.display = Busy && loadCancellation != null ? DisplayStyle.Flex : DisplayStyle.None;
            loadingLabel.text = message;
            cancelLoading.SetEnabled(loadCancellation != null && !loadCancellation.IsCancellationRequested);
        }
        private static string PanelLabel(string name)
        {
            switch (name)
            {
                case "TopBar": return "상단 도구 모음";
                case "Explorer": return "장면 탐색기";
                case "Inspector": return "속성 창";
                case "PlaybackBar": return "재생 바";
                case "StatusBar": return "상태 바";
                default: return "기즈모 모드 바";
            }
        }
        private void RefreshFocusNotice()
        {
            if (focusNotice == null) return;
            var critical = Busy || !string.IsNullOrEmpty(Error);
            var notice = !string.IsNullOrEmpty(Error) ? Error : message;
            if (FocusView && notice != lastFocusNotice)
            {
                lastFocusNotice = notice;
                focusNoticeUntil = Time.unscaledTime + 3;
            }
            focusNotice.text = notice;
            focusNotice.style.display = FocusView && (critical || Time.unscaledTime < focusNoticeUntil)
                ? DisplayStyle.Flex : DisplayStyle.None;
        }
        private Rect PanelPixels(VisualElement element)
        {
            var root = Root.worldBound; var box = element.worldBound;
            var x = Screen.width / root.width; var y = Screen.height / root.height;
            return Rect.MinMaxRect((box.xMin - root.xMin) * x, (root.yMax - box.yMax) * y,
                (box.xMax - root.xMin) * x, (root.yMax - box.yMin) * y);
        }
        internal Rect SafeViewportRect()
        {
            var full = new Rect(0, 0, Screen.width, Screen.height);
            if (Root == null || Root.worldBound.width <= 0 || Root.worldBound.height <= 0) return full;
            var left = 12f; var right = Screen.width - 12f;
            var bottom = 12f; var top = Screen.height - 12f;
            if (PanelVisible("Explorer")) left = PanelPixels(explorer).xMax + 12;
            if (PanelVisible("Inspector")) right = PanelPixels(inspector).xMin - 12;
            if (PanelVisible("TopBar")) top = PanelPixels(topbar).yMin - 12;
            if (viewMenuOpen && PanelVisible("TopBar")) top = Mathf.Min(top, PanelPixels(viewMenu).yMin - 12);
            if (PanelVisible("PlaybackBar")) bottom = PanelPixels(playback).yMax + 12;
            else if (PanelVisible("StatusBar")) bottom = PanelPixels(statusBar).yMax + 12;
            if (right - left < 100 || top - bottom < 100) return full;
            return Rect.MinMaxRect(left, bottom, right, top);
        }
    }
}
