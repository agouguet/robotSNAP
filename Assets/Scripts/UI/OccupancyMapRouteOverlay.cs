using System;
using System.Collections.Generic;
using System.Linq;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Draws metric grid lines and ordered agent routes over a scale-to-fit occupancy image.
/// </summary>
public sealed class OccupancyMapRouteOverlay : VisualElement
{
    public readonly struct RouteVisual
    {
        public RouteVisual(
            IReadOnlyList<Vector2> points,
            Color color,
            bool active,
            string name = null,
            bool planned = false)
        {
            Points = points;
            Color = color;
            Active = active;
            Name = name;
            Planned = planned;
        }

        public IReadOnlyList<Vector2> Points { get; }
        public Color Color { get; }
        public bool Active { get; }
        public string Name { get; }

        /// <summary>True when the points follow a planned walkable path instead of straight waypoint legs.</summary>
        public bool Planned { get; }
    }

    /// <summary>
    /// Where the members of one route will stand at spawn, in world space.
    /// The first slot belongs to the leader, who owns the route.
    /// </summary>
    public readonly struct FormationPreview
    {
        public FormationPreview(IReadOnlyList<Vector2> slots, Color color, bool active)
        {
            Slots = slots;
            Color = color;
            Active = active;
        }

        public IReadOnlyList<Vector2> Slots { get; }
        public Color Color { get; }
        public bool Active { get; }
    }

    private readonly List<RouteVisual> _routes = new();
    private readonly List<FormationPreview> _formations = new();
    private readonly Label _scaleValueLabel;
    private Bounds _worldBounds;
    private Rect _imageRect;
    private bool _showGrid = true;
    private float _gridStep = 1f;

    public OccupancyMapRouteOverlay()
    {
        pickingMode = PickingMode.Ignore;
        AddToClassList("route-map-overlay");
        generateVisualContent += GenerateRouteVisuals;

        // Painter2D cannot draw text, so the scale bar value is a child label.
        _scaleValueLabel = new Label();
        _scaleValueLabel.AddToClassList("map-scale-value");
        _scaleValueLabel.pickingMode = PickingMode.Ignore;
        Add(_scaleValueLabel);
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (_showGrid == value)
                return;
            _showGrid = value;
            UpdateScaleLabel();
            MarkDirtyRepaint();
        }
    }

    public float GridStep => _gridStep;

    public void SetMap(Bounds worldBounds, Rect imageRect)
    {
        _worldBounds = worldBounds;
        _imageRect = imageRect;
        _gridStep = CalculateGridStep(worldBounds.size.x, imageRect.width);
        UpdateScaleLabel();
        MarkDirtyRepaint();
    }

    public void SetRoutes(IEnumerable<RouteVisual> routes)
    {
        _routes.Clear();
        if (routes != null)
            _routes.AddRange(routes.Where(route => route.Points != null));
        MarkDirtyRepaint();
    }

    /// <summary>Sets the spawn layout of every grouped route drawn on the map.</summary>
    public void SetFormations(IEnumerable<FormationPreview> formations)
    {
        _formations.Clear();
        if (formations != null)
            _formations.AddRange(formations.Where(formation => formation.Slots != null));
        MarkDirtyRepaint();
    }

    private void GenerateRouteVisuals(MeshGenerationContext context)
    {
        if (contentRect.width < 1f || contentRect.height < 1f ||
            _imageRect.width < 1f || _imageRect.height < 1f ||
            _worldBounds.size.x <= 0f || _worldBounds.size.z <= 0f)
            return;

        Painter2D painter = context.painter2D;
        if (_showGrid)
            DrawGrid(painter);

        foreach (RouteVisual route in _routes.Where(route => !route.Active))
            DrawRoute(painter, route);
        foreach (RouteVisual route in _routes.Where(route => route.Active))
            DrawRoute(painter, route);

        // Formations are drawn last so their members stay readable above the route lines.
        foreach (FormationPreview formation in _formations.Where(formation => !formation.Active))
            DrawFormation(painter, formation);
        foreach (FormationPreview formation in _formations.Where(formation => formation.Active))
            DrawFormation(painter, formation);
    }

    /// <summary>
    /// Draws the spawn slots of a route: hollow markers for the followers, a filled one for the leader.
    /// Seeing the group before running the scenario avoids discovering the formation only in Play Mode.
    /// </summary>
    private void DrawFormation(Painter2D painter, FormationPreview formation)
    {
        IReadOnlyList<Vector2> slots = formation.Slots;
        float alpha = formation.Active ? 0.95f : 0.35f;
        float lineWidth = formation.Active ? 2f : 1.25f;

        for (int index = 0; index < slots.Count; index++)
        {
            Vector2 center = WorldToLocal(slots[index]);
            bool isLeader = index == 0;
            float radius = isLeader ? 5f : 4.5f;

            Color outer = formation.Color;
            outer.a = alpha;
            painter.strokeColor = outer;
            painter.lineWidth = lineWidth;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            painter.Stroke();

            if (!isLeader)
                continue;

            painter.fillColor = outer;
            painter.BeginPath();
            painter.Arc(center, 2.5f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            painter.Fill();
        }
    }

    private void DrawGrid(Painter2D painter)
    {
        painter.strokeColor = new Color(0.55f, 0.72f, 0.86f, 0.22f);
        painter.lineWidth = 1f;

        float firstX = Mathf.Ceil(_worldBounds.min.x / _gridStep) * _gridStep;
        for (float worldX = firstX; worldX <= _worldBounds.max.x; worldX += _gridStep)
        {
            float x = WorldToLocal(new Vector2(worldX, _worldBounds.min.z)).x;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, _imageRect.yMin));
            painter.LineTo(new Vector2(x, _imageRect.yMax));
            painter.Stroke();
        }

        float firstZ = Mathf.Ceil(_worldBounds.min.z / _gridStep) * _gridStep;
        for (float worldZ = firstZ; worldZ <= _worldBounds.max.z; worldZ += _gridStep)
        {
            float y = WorldToLocal(new Vector2(_worldBounds.min.x, worldZ)).y;
            painter.BeginPath();
            painter.MoveTo(new Vector2(_imageRect.xMin, y));
            painter.LineTo(new Vector2(_imageRect.xMax, y));
            painter.Stroke();
        }

        // A compact scale bar uses the same metric step as the grid, so the value printed
        // above it is the length of one grid cell in metres.
        float scaleWidth = _gridStep / _worldBounds.size.x * _imageRect.width;
        float barY = _imageRect.yMax - 13f;
        float barX = _imageRect.xMin + 12f;

        // Dark plate behind the bar so the white strokes stay readable on a bright occupancy grid.
        painter.fillColor = new Color(0.04f, 0.07f, 0.1f, 0.55f);
        painter.BeginPath();
        painter.MoveTo(new Vector2(barX - 6f, barY - 8f));
        painter.LineTo(new Vector2(barX + scaleWidth + 6f, barY - 8f));
        painter.LineTo(new Vector2(barX + scaleWidth + 6f, barY + 6f));
        painter.LineTo(new Vector2(barX - 6f, barY + 6f));
        painter.ClosePath();
        painter.Fill();

        painter.strokeColor = new Color(0.95f, 0.97f, 1f, 0.9f);
        painter.lineWidth = 2f;
        painter.BeginPath();
        painter.MoveTo(new Vector2(barX, barY - 4f));
        painter.LineTo(new Vector2(barX, barY));
        painter.LineTo(new Vector2(barX + scaleWidth, barY));
        painter.LineTo(new Vector2(barX + scaleWidth, barY - 4f));
        painter.Stroke();
    }

    private void DrawRoute(Painter2D painter, RouteVisual route)
    {
        if (route.Points.Count == 0)
            return;

        Color routeColor = route.Color;
        routeColor.a = route.Active ? 0.95f : 0.38f;
        painter.strokeColor = routeColor;
        painter.lineWidth = route.Active ? 3f : 1.5f;

        if (route.Points.Count > 1)
        {
            painter.BeginPath();
            painter.MoveTo(WorldToLocal(route.Points[0]));
            for (int index = 1; index < route.Points.Count; index++)
                painter.LineTo(WorldToLocal(route.Points[index]));
            painter.Stroke();
        }

        for (int index = 0; index < route.Points.Count; index++)
        {
            Vector2 center = WorldToLocal(route.Points[index]);
            float radius = route.Active ? 8f : 5f;
            Color markerColor = route.Color;
            markerColor.a = route.Active ? 1f : 0.55f;
            painter.fillColor = markerColor;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            painter.Fill();

            if (index == 0)
            {
                painter.strokeColor = Color.white;
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.Arc(center, radius + 2f, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
                painter.Stroke();
            }
        }
    }

    private Vector2 WorldToLocal(Vector2 worldPosition)
    {
        Vector2 imagePosition = OccupancyMapCoordinates.WorldToImageNormalized(worldPosition, _worldBounds);
        return new Vector2(
            _imageRect.xMin + imagePosition.x * _imageRect.width,
            _imageRect.yMin + imagePosition.y * _imageRect.height);
    }

    /// <summary>Prints the metric value of the scale bar drawn by <see cref="DrawGrid"/>.</summary>
    private void UpdateScaleLabel()
    {
        bool hasMap = _showGrid && _imageRect.width >= 1f && _imageRect.height >= 1f &&
                      _worldBounds.size.x > 0f;
        _scaleValueLabel.text = hasMap ? $"{_gridStep:0.##} m" : string.Empty;
        _scaleValueLabel.style.left = _imageRect.xMin + 12f;
        _scaleValueLabel.style.top = _imageRect.yMax - 34f;
    }

    private static float CalculateGridStep(float worldWidth, float pixelWidth)
    {
        if (worldWidth <= 0f || pixelWidth <= 0f)
            return 1f;

        float rawStep = worldWidth * 80f / pixelWidth;
        float magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(rawStep)));
        float normalized = rawStep / magnitude;
        float niceStep = normalized <= 1f ? 1f : normalized <= 2f ? 2f : normalized <= 5f ? 5f : 10f;
        return niceStep * magnitude;
    }
}
