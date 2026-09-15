using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The pictograms of the view toolbar, drawn with Painter2D. The font has no dependable glyph for
/// pan, zoom or rotate — a missing one renders as an empty square — so the icons are drawn instead,
/// which also keeps them crisp at any size and takes their colour from the button (active or not).
/// </summary>
public sealed class SimulationToolIcon : VisualElement
{
    public enum Shape
    {
        Pointer,
        Move,
        Rotate,
        Zoom,
        Target
    }

    private readonly Shape _shape;

    public SimulationToolIcon(Shape shape)
    {
        _shape = shape;
        pickingMode = PickingMode.Ignore;

        // The icon fills the button it belongs to, whatever its size.
        style.position = Position.Absolute;
        style.left = 0;
        style.right = 0;
        style.top = 0;
        style.bottom = 0;

        generateVisualContent += Draw;
    }

    private void Draw(MeshGenerationContext context)
    {
        Rect rect = contentRect;
        float size = Mathf.Min(rect.width, rect.height);
        if (size < 6f) return;

        var center = new Vector2(rect.width * 0.5f, rect.height * 0.5f);
        float half = size * 0.5f;
        Color color = resolvedStyle.color;

        Painter2D painter = context.painter2D;
        painter.strokeColor = color;
        painter.fillColor = color;
        painter.lineWidth = Mathf.Max(1.4f, size * 0.085f);
        painter.lineCap = LineCap.Round;
        painter.lineJoin = LineJoin.Round;

        switch (_shape)
        {
            case Shape.Pointer:
                DrawPointer(painter, center, half);
                break;
            case Shape.Move:
                DrawMove(painter, center, half);
                break;
            case Shape.Rotate:
                DrawRotate(painter, center, half);
                break;
            case Shape.Zoom:
                DrawZoom(painter, center, half);
                break;
            case Shape.Target:
                DrawTarget(painter, center, half);
                break;
        }
    }

    /// <summary>Classic cursor arrow: the "pick an agent" tool.</summary>
    private static void DrawPointer(Painter2D painter, Vector2 center, float half)
    {
        float u = half * 0.86f;
        float left = center.x - u * 0.5f;

        painter.BeginPath();
        painter.MoveTo(new Vector2(left, center.y - u));
        painter.LineTo(new Vector2(left, center.y + u * 0.72f));
        painter.LineTo(new Vector2(center.x - u * 0.08f, center.y + u * 0.26f));
        painter.LineTo(new Vector2(center.x + u * 0.14f, center.y + u * 0.82f));
        painter.LineTo(new Vector2(center.x + u * 0.36f, center.y + u * 0.7f));
        painter.LineTo(new Vector2(center.x + u * 0.16f, center.y + u * 0.16f));
        painter.LineTo(new Vector2(left + u * 0.72f, center.y + u * 0.22f));
        painter.ClosePath();
        painter.Fill();
    }

    /// <summary>Four-way arrow: the tool that slides the view over the map.</summary>
    private static void DrawMove(Painter2D painter, Vector2 center, float half)
    {
        float u = half * 0.72f;

        painter.BeginPath();
        painter.MoveTo(new Vector2(center.x - u, center.y));
        painter.LineTo(new Vector2(center.x + u, center.y));
        painter.MoveTo(new Vector2(center.x, center.y - u));
        painter.LineTo(new Vector2(center.x, center.y + u));
        painter.Stroke();

        DrawArrowHead(painter, center + Vector2.right * u, Vector2.right, half * 0.34f);
        DrawArrowHead(painter, center + Vector2.left * u, Vector2.left, half * 0.34f);
        DrawArrowHead(painter, center + Vector2.down * u, Vector2.down, half * 0.34f);
        DrawArrowHead(painter, center + Vector2.up * u, Vector2.up, half * 0.34f);
    }

    private static void DrawArrowHead(Painter2D painter, Vector2 tip, Vector2 direction, float length)
    {
        Vector2 back = tip - direction * length;
        var side = new Vector2(-direction.y, direction.x) * length * 0.6f;

        painter.BeginPath();
        painter.MoveTo(tip);
        painter.LineTo(back + side);
        painter.LineTo(back - side);
        painter.ClosePath();
        painter.Fill();
    }

    /// <summary>Open circle with an arrow head: the tool that turns around the scene.</summary>
    private static void DrawRotate(Painter2D painter, Vector2 center, float half)
    {
        float radius = half * 0.62f;
        const float startDegrees = 35f;
        const float endDegrees = 320f;

        painter.BeginPath();
        painter.Arc(center, radius, Angle.Degrees(startDegrees), Angle.Degrees(endDegrees), ArcDirection.Clockwise);
        painter.Stroke();

        float tipDegrees = endDegrees;
        var tip = new Vector2(
            center.x + Mathf.Cos(tipDegrees * Mathf.Deg2Rad) * radius,
            center.y + Mathf.Sin(tipDegrees * Mathf.Deg2Rad) * radius);

        Vector2 tangent = new Vector2(
            -Mathf.Sin(tipDegrees * Mathf.Deg2Rad),
            Mathf.Cos(tipDegrees * Mathf.Deg2Rad));

        DrawArrowHead(painter, tip, tangent, half * 0.4f);
    }

    /// <summary>Magnifier: the tool that zooms in and out.</summary>
    private static void DrawZoom(Painter2D painter, Vector2 center, float half)
    {
        float radius = half * 0.46f;
        var lens = new Vector2(center.x - half * 0.14f, center.y - half * 0.14f);

        painter.BeginPath();
        painter.Arc(lens, radius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
        painter.Stroke();

        painter.BeginPath();
        painter.MoveTo(lens + new Vector2(radius * 0.72f, radius * 0.72f));
        painter.LineTo(lens + new Vector2(radius * 1.85f, radius * 1.85f));
        painter.Stroke();
    }

    /// <summary>Concentric rings: the shortcut that re-frames the followed agent.</summary>
    private static void DrawTarget(Painter2D painter, Vector2 center, float half)
    {
        painter.BeginPath();
        painter.Arc(center, half * 0.68f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
        painter.Stroke();

        painter.BeginPath();
        painter.Arc(center, half * 0.36f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
        painter.Stroke();

        painter.BeginPath();
        painter.Arc(center, half * 0.14f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
        painter.Fill();
    }
}
