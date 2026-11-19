using UnityEngine;
using UnityEngine.UIElements;

namespace MmdStudio.Poc
{
    public enum MmdIconKind { NewFile, Open, Save, SaveAs, Frame, SkipStart, Play, Pause, Stop, Cube, Character, Stage, Light, Camera, Chevron, Plus }

    // Line icons drawn with UI Toolkit's vector API (no icon font is available in the Player).
    // Geometry uses a 24-unit grid scaled to the element size.
    public sealed class MmdIcon : VisualElement
    {
        private MmdIconKind kind;
        private Color colour;
        public MmdIconKind Kind { get => kind; set { if (kind != value) { kind = value; MarkDirtyRepaint(); } } }
        public Color Colour { get => colour; set { colour = value; MarkDirtyRepaint(); } }

        public MmdIcon(MmdIconKind kind, float size = 18, Color? colour = null)
        {
            this.kind = kind; this.colour = colour ?? new Color(.24f, .24f, .27f);
            pickingMode = PickingMode.Ignore;
            style.width = size; style.height = size; style.flexShrink = 0;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            var size = Mathf.Min(contentRect.width, contentRect.height);
            if (size <= 0) return;
            var s = size / 24f;
            Vector2 P(float x, float y) => new Vector2(x * s, y * s);
            var p = context.painter2D;
            p.strokeColor = colour; p.fillColor = colour; p.lineWidth = Mathf.Max(1.2f, 1.7f * s);
            p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            void Line(params Vector2[] points)
            {
                p.BeginPath(); p.MoveTo(points[0]);
                for (var i = 1; i < points.Length; i++) p.LineTo(points[i]);
                p.Stroke();
            }
            void Shape(bool fill, params Vector2[] points)
            {
                p.BeginPath(); p.MoveTo(points[0]);
                for (var i = 1; i < points.Length; i++) p.LineTo(points[i]);
                p.ClosePath(); if (fill) p.Fill(); else p.Stroke();
            }
            void Circle(float x, float y, float r, bool fill = false)
            {
                p.BeginPath(); p.Arc(P(x, y), r * s, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath();
                if (fill) p.Fill(); else p.Stroke();
            }
            switch (kind)
            {
                case MmdIconKind.NewFile:
                    Shape(false, P(6, 3), P(14, 3), P(19, 8), P(19, 21), P(6, 21));
                    Line(P(14, 3), P(14, 8), P(19, 8)); Line(P(12.5f, 11.5f), P(12.5f, 17.5f)); Line(P(9.5f, 14.5f), P(15.5f, 14.5f));
                    break;
                case MmdIconKind.Open:
                    Shape(false, P(3, 6), P(9, 6), P(11, 8), P(19, 8), P(19, 10), P(6, 10), P(3, 19));
                    Shape(false, P(3, 19), P(6, 10), P(22, 10), P(19, 19));
                    break;
                case MmdIconKind.Save:
                case MmdIconKind.SaveAs:
                    Shape(false, P(4, 4), P(16, 4), P(20, 8), P(20, 20), P(4, 20));
                    Line(P(8, 4), P(8, 9), P(15, 9), P(15, 4)); Shape(false, P(7.5f, 14), P(16.5f, 14), P(16.5f, 20), P(7.5f, 20));
                    if (kind == MmdIconKind.SaveAs) { Circle(19, 19, 4.2f, true); p.strokeColor = Color.white; p.lineWidth = 1.4f * s; Line(P(19, 16.8f), P(19, 21.2f)); Line(P(16.8f, 19), P(21.2f, 19)); }
                    break;
                case MmdIconKind.Frame:
                    Line(P(4, 9), P(4, 4), P(9, 4)); Line(P(15, 4), P(20, 4), P(20, 9)); Line(P(20, 15), P(20, 20), P(15, 20)); Line(P(9, 20), P(4, 20), P(4, 15));
                    Circle(12, 12, 2.5f);
                    break;
                case MmdIconKind.SkipStart:
                    Line(P(6, 5), P(6, 19)); Shape(false, P(18, 5), P(9, 12), P(18, 19));
                    break;
                case MmdIconKind.Play:
                    Circle(12, 12, 9); Shape(true, P(10, 8), P(16, 12), P(10, 16));
                    break;
                case MmdIconKind.Pause:
                    Circle(12, 12, 9); Line(P(10, 8.5f), P(10, 15.5f)); Line(P(14, 8.5f), P(14, 15.5f));
                    break;
                case MmdIconKind.Stop:
                    Shape(false, P(5, 5), P(19, 5), P(19, 19), P(5, 19));
                    break;
                case MmdIconKind.Cube:
                    Shape(false, P(12, 2.5f), P(20.5f, 7), P(20.5f, 17), P(12, 21.5f), P(3.5f, 17), P(3.5f, 7));
                    Line(P(3.5f, 7), P(12, 11.5f), P(20.5f, 7)); Line(P(12, 11.5f), P(12, 21.5f));
                    break;
                case MmdIconKind.Character:
                    Circle(12, 8, 3.5f);
                    p.BeginPath(); p.Arc(P(12, 21), 7 * s, Angle.Degrees(180), Angle.Degrees(360)); p.Stroke();
                    break;
                case MmdIconKind.Stage:
                    Shape(false, P(2.5f, 17), P(7, 9), P(17, 9), P(21.5f, 17)); Line(P(12, 9), P(12, 17)); Line(P(4.8f, 13), P(19.2f, 13));
                    break;
                case MmdIconKind.Light:
                    Circle(12, 12, 3.8f);
                    for (var i = 0; i < 8; i++)
                    {
                        var a = i * Mathf.PI / 4; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Line(P(12 + d.x * 6.5f, 12 + d.y * 6.5f), P(12 + d.x * 9, 12 + d.y * 9));
                    }
                    break;
                case MmdIconKind.Camera:
                    Shape(false, P(3, 8), P(8, 8), P(9.5f, 5.5f), P(14.5f, 5.5f), P(16, 8), P(21, 8), P(21, 19), P(3, 19));
                    Circle(12, 13.2f, 3.4f);
                    break;
                case MmdIconKind.Chevron:
                    Line(P(7, 10), P(12, 15), P(17, 10));
                    break;
                case MmdIconKind.Plus:
                    Line(P(12, 5), P(12, 19)); Line(P(5, 12), P(19, 12));
                    break;
            }
        }
    }
}
