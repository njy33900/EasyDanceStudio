using UnityEngine;

namespace MmdStudio.Poc
{
    // H1-04: the selection highlight rendering (a bounds box and a ground ring drawn with line
    // renderers), separated from the workspace. It only draws what it is given.
    internal sealed class SelectionHighlight
    {
        private readonly GameObject root;
        private readonly LineRenderer box, ring;
        private readonly Material material;

        public bool Visible => root != null && root.activeSelf;
        public Bounds Bounds { get; private set; }

        public SelectionHighlight(Transform parent)
        {
            root = new GameObject("Selection Highlight");
            root.transform.SetParent(parent, false);
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            material = new Material(shader) { color = new Color(.15f, .39f, .92f, .95f) };
            box = Line("Bounds", 16, false);
            ring = Line("Ground", 48, true);
            root.SetActive(false);
        }

        private LineRenderer Line(string name, int count, bool loop)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(root.transform, false);
            line.sharedMaterial = material; line.positionCount = count; line.loop = loop; line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            line.startColor = line.endColor = Color.white; line.numCapVertices = 2;
            return line;
        }

        public void Hide() { if (root != null) root.SetActive(false); }

        // bounds: world bounds of the selection; origin: its placement (the ring sits at the
        // bottom of the bounds below it); viewDistance scales the line width.
        public void Show(Bounds bounds, Vector3 origin, float viewDistance)
        {
            if (root == null) return;
            Bounds = bounds; root.SetActive(true);
            var min = bounds.min; var max = bounds.max;
            Vector3 P(float x, float y, float z) => new Vector3(x, y, z);
            var b0 = P(min.x, min.y, min.z); var b1 = P(max.x, min.y, min.z); var b2 = P(max.x, min.y, max.z); var b3 = P(min.x, min.y, max.z);
            var t0 = P(min.x, max.y, min.z); var t1 = P(max.x, max.y, min.z); var t2 = P(max.x, max.y, max.z); var t3 = P(min.x, max.y, max.z);
            box.SetPositions(new[] { b0, b1, b2, b3, b0, t0, t1, b1, t1, t2, b2, t2, t3, b3, t3, t0 });
            var width = Mathf.Clamp(viewDistance * .0025f, .002f, .5f);
            box.widthMultiplier = width; ring.widthMultiplier = width * 1.5f;
            var centre = origin; centre.y = min.y;
            var radius = Mathf.Max(.1f, Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.1f);
            for (var i = 0; i < ring.positionCount; i++)
            {
                var angle = i * Mathf.PI * 2 / ring.positionCount;
                ring.SetPosition(i, centre + new Vector3(Mathf.Cos(angle) * radius, .002f, Mathf.Sin(angle) * radius));
            }
        }

        public void Dispose() { if (material != null) Object.Destroy(material); }
    }
}
