using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    // H1-04: selection state, framing and the selection highlight (split from MmdSceneWorkspace.cs).
    public sealed partial class MmdSceneWorkspace
    {
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
            message = target == InspectorTarget.Light ? "조명 선택" : target == InspectorTarget.Audio ? "오디오 선택" : "카메라 선택";
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
            bounds = default; var any = false;
            foreach (var item in items) any |= Encapsulate(item, ref bounds, any);
            return any;
        }
        // One object, without allocating (called every frame for the highlight and the DOF focus).
        internal static bool TryBounds(MmdSceneService.SceneObject item, out Bounds bounds)
        {
            bounds = default;
            return Encapsulate(item, ref bounds, false);
        }
        private static bool Encapsulate(MmdSceneService.SceneObject item, ref Bounds bounds, bool started)
        {
            var any = false;
            foreach (var renderer in item.SkinnedRenderers)
            {
                if (renderer == null) continue;
                if (!started && !any) bounds = renderer.bounds; else bounds.Encapsulate(renderer.bounds);
                any = true;
            }
            return any;
        }
        private void Frame(System.Collections.Generic.IEnumerable<MmdSceneService.SceneObject> items)
        {
            if (!TryBounds(items, out var bounds)) return;
            var camera = Camera.main;
            View.FrameInRect(bounds, camera.fieldOfView, Screen.width, Screen.height, SafeViewportRect()); View.Apply(camera);
        }

        private void SelectFromViewport(MmdSceneService.SceneObject item)
        {
            if (item == null) { SelectedId = null; Target = InspectorTarget.Object; Error = ""; message = "선택을 해제했습니다."; Refresh(); }
            else Select(item.Id);
        }

        private SelectionHighlight highlight;
        private void CreateHighlight() => highlight = new SelectionHighlight(transform);
        internal void UpdateHighlight()
        {
            if (highlight == null) return;
            var selected = Selected;
            if (FocusView || Exporting || selected == null || !selected.Visible || !TryBounds(selected, out var bounds)) { highlight.Hide(); return; }
            highlight.Show(bounds, selected.Placement.position, View.Distance);
        }
    }
}
