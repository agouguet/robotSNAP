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
        public RouteVisual(IReadOnlyList<Vector2> points, Color color, bool active)
        {
            Points = points;
            Color = color;
            Active = active;
        }

        public IReadOnlyList<Vector2> Points { get; }
        public Color Color { get; }
        public bool Active { get; }
    }

    private readonly List<RouteVisual> _routes = new();
    private Bounds _worldBounds;
    private Rect _imageRect;
    private bool _showGrid = true;
    private float _gridStep = 1f;

    public OccupancyMapRouteOverlay()
    {
        pickingMode = PickingMode.Ignore;
        AddToClassList("route-map-overlay");
        generateVisualContent += GenerateRouteVisuals;
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (_showGrid == value)
                return;
            _showGrid = value;
            MarkDirtyRepaint();
        }
    }

    public float GridStep => _gridStep;

    public void SetMap(Bounds worldBounds, Rect imageRect)
    {
        _worldBounds = worldBounds;
        _imageRect = imageRect;
        _gridStep = CalculateGridStep(worldBounds.size.x, imageRect.width);
        MarkDirtyRepaint();
    }

    public void SetRoutes(IEnumerable<RouteVisual> routes)
    {
        _routes.Clear();
        if (routes != null)
            _routes.AddRange(routes.Where(route => route.Points != null));
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

        // A compact scale bar uses the same metric step as the grid.
        float scaleWidth = _gridStep / _worldBounds.size.x * _imageRect.width;
        float barY = _imageRect.yMax - 13f;
        float barX = _imageRect.xMin + 12f;
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
