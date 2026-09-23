using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// One agent's sensor footprint on the simulation minimap: a filled circular sector with a thin
/// outline for a directional sensor (<see cref="SetCone"/>), or a full disc with a short heading
/// needle for an omnidirectional one (<see cref="SetRing"/>).
///
/// Both shapes have a third form, <see cref="SetOccluded"/>, which draws the same footprint cut
/// back to what the walls let through: the caller hands in how far each ray of the sector actually
/// reaches, and the sector becomes the silhouette of that.
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

    /// <summary>Half a turn, the sweep a full-circle footprint starts at, so its rays straddle the heading.</summary>
    private const float HalfTurnDegrees = 180f;

    /// <summary>Where the heading needle starts, as a fraction of the radius; it ends on the ring.</summary>
    private const float NeedleInnerRatio = 0.35f;

    private float _radius;
    private float _headingDegrees;
    private float _fovDegrees;
    private Color _fill = Color.clear;
    private Color _stroke = Color.clear;

    /// <summary>Visible length of each ray of an occluded footprint, as a fraction of the radius.</summary>
    private float[] _spans = System.Array.Empty<float>();
    private int _spanCount;

    /// <summary>Whether the shape carries the needle that shows where the agent looks.</summary>
    private bool _headingNeedle = true;

    /// <summary>True once a drawing state has been written, so a repeat of it can be skipped.</summary>
    private bool _painted;

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
    ///
    /// <paramref name="headingNeedle"/> belongs to the caller: a footprint of what somebody detects is not a
    /// direction, and the needle of a pedestrian's disc read as a gaze the model does not have.
    /// </summary>
    public void SetRing(float radius, float headingDegrees, Color fill, Color stroke, bool headingNeedle = true)
    {
        SetState(radius, headingDegrees, FullTurnDegrees, fill, stroke, headingNeedle);
    }

    /// <summary>
    /// Shows the footprint of a sensor whose view is cut by the walls: <paramref name="spans"/> carries, for
    /// each ray of the sector from its first edge to its last, the length that ray reaches as a fraction of
    /// <paramref name="radius"/>. A full turn draws the closed silhouette around the agent, a narrower field
    /// of view the sector between its two edges, with its apex on the agent either way.
    /// </summary>
    public void SetOccluded(
        float radius,
        float headingDegrees,
        float fovDegrees,
        System.Collections.Generic.IReadOnlyList<float> spans,
        Color fill,
        Color stroke,
        bool headingNeedle = true)
    {
        float clampedRadius = Mathf.Max(0f, radius);
        int count = spans?.Count ?? 0;

        // Same economy as SetState: the owner recomputes a silhouette only when its agent moved, and a
        // silhouette that came out identical costs nothing here.
        if (_painted &&
            _spanCount == count &&
            Mathf.Approximately(clampedRadius, _radius) &&
            Mathf.Approximately(headingDegrees, _headingDegrees) &&
            Mathf.Approximately(fovDegrees, _fovDegrees) &&
            headingNeedle == _headingNeedle &&
            SameColours(fill, stroke) &&
            SameSpans(spans, count))
            return;

        _painted = true;
        _radius = clampedRadius;
        _headingDegrees = headingDegrees;
        _fovDegrees = fovDegrees;
        _fill = fill;
        _stroke = stroke;
        _headingNeedle = headingNeedle;
        StoreSpans(spans, count);
        Resize(clampedRadius);
    }

    /// <summary>
    /// Stores the drawing state, resizes the element to the bounding box it needs and asks for a
    /// repaint. Nothing but the styles is touched here, so the owner keeps control of the position.
    /// </summary>
    private void SetState(float radius, float headingDegrees, float fovDegrees, Color fill, Color stroke,
        bool headingNeedle = true)
    {
        float clampedRadius = Mathf.Max(0f, radius);

        // A cone nobody has moved or turned keeps the drawing it already has: the owner calls this every frame
        // for every agent, and a crowd standing still would otherwise rebuild one path per agent per frame on
        // the UI renderer for a picture that did not change. The element's own position is the owner's, and it
        // does not affect the drawing, so it is not part of this comparison.
        if (_painted &&
            Mathf.Approximately(clampedRadius, _radius) &&
            Mathf.Approximately(headingDegrees, _headingDegrees) &&
            Mathf.Approximately(fovDegrees, _fovDegrees) &&
            headingNeedle == _headingNeedle &&
            SameColours(fill, stroke) &&
            _spanCount == 0)
            return;

        _painted = true;
        _radius = clampedRadius;
        _headingDegrees = headingDegrees;
        _fovDegrees = fovDegrees;
        _fill = fill;
        _stroke = stroke;
        _headingNeedle = headingNeedle;

        // A plain sector has no silhouette: leaving the spans behind would keep drawing the old one.
        _spanCount = 0;
        Resize(clampedRadius);
    }

    private bool SameColours(Color fill, Color stroke) =>
        fill.r == _fill.r && fill.g == _fill.g && fill.b == _fill.b && fill.a == _fill.a &&
        stroke.r == _stroke.r && stroke.g == _stroke.g && stroke.b == _stroke.b && stroke.a == _stroke.a;

    private bool SameSpans(System.Collections.Generic.IReadOnlyList<float> spans, int count)
    {
        for (int index = 0; index < count; index++)
        {
            if (!Mathf.Approximately(Mathf.Clamp01(spans[index]), _spans[index]))
                return false;
        }

        return true;
    }

    private void StoreSpans(System.Collections.Generic.IReadOnlyList<float> spans, int count)
    {
        if (_spans.Length < count)
            _spans = new float[count];

        for (int index = 0; index < count; index++)
            _spans[index] = Mathf.Clamp01(spans[index]);

        _spanCount = count;
    }

    /// <summary>A float turns into a length in pixels; a zero radius collapses the element harmlessly.</summary>
    private void Resize(float radius)
    {
        float size = 2f * radius;
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

        if (_spanCount > 1)
            DrawOccluded(painter, center);
        else if (_fovDegrees >= FullTurnDegrees)
            DrawRing(painter, center);
        else
            DrawSector(painter, center);
    }

    /// <summary>
    /// Fills the silhouette of the rays the caller measured, then outlines it. The apex is the agent for a
    /// field of view narrower than a turn, and there is no apex at all for a full one: a sensor that looks
    /// everywhere sees a shape that closes around the agent instead of a wedge radiating from it.
    /// </summary>
    private void DrawOccluded(Painter2D painter, Vector2 center)
    {
        bool fullTurn = _fovDegrees >= FullTurnDegrees;
        float span = fullTurn ? FullTurnDegrees : _fovDegrees;
        float first = fullTurn ? _headingDegrees - HalfTurnDegrees : _headingDegrees - span * 0.5f;
        float step = _spanCount > 1 ? span / (_spanCount - 1) : 0f;

        painter.BeginPath();

        if (!fullTurn)
            painter.MoveTo(center);

        for (int index = 0; index < _spanCount; index++)
        {
            Vector2 point = PointOnCircle(center, _radius * _spans[index], first + step * index);
            if (fullTurn && index == 0)
                painter.MoveTo(point);
            else
                painter.LineTo(point);
        }

        painter.ClosePath();

        // A footprint drawn as an outline - a pedestrian's, whose range covers the whole map - skips the fill
        // rather than laying down a transparent one: with eighty of them the map has to stay readable.
        if (_fill.a > 0f)
        {
            painter.fillColor = _fill;
            painter.Fill();
        }

        if (_stroke.a > 0f)
        {
            painter.strokeColor = _stroke;
            painter.Stroke();
        }

        if (fullTurn && _headingNeedle)
        {
            painter.BeginPath();
            painter.MoveTo(PointOnCircle(center, _radius * NeedleInnerRatio));
            painter.LineTo(PointOnCircle(center, _radius));
            painter.Stroke();
        }
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

        if (_headingNeedle)
        {
            painter.BeginPath();
            painter.MoveTo(PointOnCircle(center, _radius * NeedleInnerRatio));
            painter.LineTo(PointOnCircle(center, _radius));
            painter.Stroke();
        }
    }

    /// <summary>A point of the circle at the current heading, in the screen convention above.</summary>
    private Vector2 PointOnCircle(Vector2 center, float radius) => PointOnCircle(center, radius, _headingDegrees);

    /// <summary>A point of the circle at one angle of the sweep, in the screen convention above.</summary>
    private Vector2 PointOnCircle(Vector2 center, float radius, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(
            center.x + Mathf.Cos(radians) * radius,
            center.y + Mathf.Sin(radians) * radius);
    }
}
