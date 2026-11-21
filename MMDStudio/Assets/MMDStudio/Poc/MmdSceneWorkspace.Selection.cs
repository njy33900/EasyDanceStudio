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
            message = target == InspectorTarget.Light ? "조명 선택" : "카메라 선택";
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

        private void SelectFromViewport(MmdSceneService.SceneObject item)
        {
            if (item == null) { SelectedId = null; Target = InspectorTarget.Object; Error = ""; message = "선택을 해제했습니다."; Refresh(); }
            else Select(item.Id);
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
    }
}
