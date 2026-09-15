using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// One agent's sensor footprint on the simulation minimap: a filled circular sector with a thin
/// outline for a directional sensor (<see cref="SetCone"/>), or a full disc with a short heading
/// needle for an omnidirectional one (<see cref="SetRing"/>).
///
/// Ownership: the creator owns positioning and sizing. This element only paints, and it sizes itself
/// to twice the radius it is given, so the owner can centre it on a map point with
/// <c>left = x - radius</c> and <c>top = y - radius</c>.
///
/// Heading convention: callers pass the heading in the minimap's screen convention, that is degrees
/// measured clockwise from the +X axis in a coordinate system where Y grows downward. The angle is
/// used as given and never converted here.
/// </summary>
public sealed class MinimapVisionCone : VisualElement
{
    /// <summary>Stroke width of the sector outline and of the detection ring, in pixels.</summary>
    private const float OutlineWidth = 1f;

    /// <summary>A full turn or more means the sensor sees all around, so the sector becomes a ring.</summary>
    private const float FullTurnDegrees = 360f;

    /// <summary>Where the heading needle starts, as a fraction of the radius; it ends on the ring.</summary>
    private const float NeedleInnerRatio = 0.35f;

    private float _radius;
    private float _headingDegrees;
    private float _fovDegrees;
    private Color _fill = Color.clear;
    private Color _stroke = Color.clear;

    public MinimapVisionCone()
    {
        // The minimap must not steal clicks meant for whatever it overlays.
        pickingMode = PickingMode.Ignore;

        // The owner places the element with left/top, which only lands correctly out of the flex flow.
        style.position = Position.Absolute;

        generateVisualContent += OnGenerateVisualContent;
    }

    /// <summary>
    /// Shows a sector of <paramref name="fovDegrees"/> centred on <paramref name="headingDegrees"/>,
    /// <paramref name="radius"/> pixels deep. A field of view of a full turn or more is drawn as a
    /// ring, and a field of view of zero or less draws nothing at all.
    /// </summary>
    public void SetCone(float radius, float headingDegrees, float fovDegrees, Color fill, Color stroke)
    {
        SetState(radius, headingDegrees, fovDegrees, fill, stroke);
    }

    /// <summary>
    /// Shows a full disc of <paramref name="radius"/> pixels, for a sensor that sees all around, plus
    /// a needle towards <paramref name="headingDegrees"/> so the agent still shows where it looks.
    /// </summary>
    public void SetRing(float radius, float headingDegrees, Color fill, Color stroke)
    {
        SetState(radius, headingDegrees, FullTurnDegrees, fill, stroke);
    }

    /// <summary>Empties the state, so the element stays painted with nothing until the next call.</summary>
    public void Clear()
    {
        SetState(0f, 0f, 0f, Color.clear, Color.clear);
    }

    /// <summary>
    /// Stores the drawing state, resizes the element to the bounding box it needs and asks for a
    /// repaint. Nothing but the styles is touched here, so the owner keeps control of the position.
    /// </summary>
    private void SetState(float radius, float headingDegrees, float fovDegrees, Color fill, Color stroke)
    {
        _radius = Mathf.Max(0f, radius);
        _headingDegrees = headingDegrees;
        _fovDegrees = fovDegrees;
        _fill = fill;
        _stroke = stroke;

        // A float turns into a length in pixels; a zero radius collapses the element harmlessly.
        float size = 2f * _radius;
        style.width = size;
        style.height = size;

        MarkDirtyRepaint();
    }

    private void OnGenerateVisualContent(MeshGenerationContext context)
    {
        Painter2D painter = context.painter2D;
        if (painter == null)
            return;

        // Nothing to draw without an extent, a span, or a laid-out rect to centre on.
        if (_radius <= 0f || _fovDegrees <= 0f)
            return;

        Rect rect = contentRect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        Vector2 center = rect.center;

        // Butt caps keep the needle exactly on the ring, and round joins avoid miter spikes on sharp
        // sector corners.
        painter.lineWidth = OutlineWidth;
        painter.lineCap = LineCap.Butt;
        painter.lineJoin = LineJoin.Round;

        if (_fovDegrees >= FullTurnDegrees)
            DrawRing(painter, center);
        else
            DrawSector(painter, center);
    }

    /// <summary>Fills the sector, then traces its outline: both straight sides and the arc.</summary>
    private void DrawSector(Painter2D painter, Vector2 center)
    {
        float halfSpan = _fovDegrees * 0.5f;

        painter.BeginPath();
        painter.MoveTo(center);
        painter.Arc(
            center,
            _radius,
            Angle.Degrees(_headingDegrees - halfSpan),
            Angle.Degrees(_headingDegrees + halfSpan),
            ArcDirection.Clockwise);
        painter.ClosePath();

        painter.fillColor = _fill;
        painter.Fill();

        painter.strokeColor = _stroke;
        painter.Stroke();
    }

    /// <summary>Fills the detection disc, outlines it, then draws the heading needle.</summary>
    private void DrawRing(Painter2D painter, Vector2 center)
    {
        painter.fillColor = _fill;
        painter.BeginPath();
        painter.Arc(center, _radius, Angle.Degrees(0f), Angle.Degrees(FullTurnDegrees), ArcDirection.Clockwise);
        painter.ClosePath();
        painter.Fill();

        painter.strokeColor = _stroke;
        painter.BeginPath();
        painter.Arc(center, _radius, Angle.Degrees(0f), Angle.Degrees(FullTurnDegrees), ArcDirection.Clockwise);
        painter.ClosePath();
        painter.Stroke();

        painter.BeginPath();
        painter.MoveTo(PointOnCircle(center, _radius * NeedleInnerRatio));
        painter.LineTo(PointOnCircle(center, _radius));
        painter.Stroke();
    }

    /// <summary>A point of the circle at the current heading, in the screen convention above.</summary>
    private Vector2 PointOnCircle(Vector2 center, float radius)
    {
        float radians = _headingDegrees * Mathf.Deg2Rad;
        return new Vector2(
            center.x + Mathf.Cos(radians) * radius,
            center.y + Mathf.Sin(radians) * radius);
    }
}
