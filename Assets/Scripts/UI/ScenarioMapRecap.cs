using System;
using System.Collections.Generic;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Read-only map shown on the validation step: the occupancy grid plus every configured route.
/// It shares the drawing overlay with the route editor but never captures pointer input.
/// </summary>
public sealed class ScenarioMapRecap
{
    private const string HiddenClass = "creation-step-hidden";

    private readonly VisualElement _canvas;
    private readonly Image _image;
    private readonly Label _placeholder;
    private readonly Label _legend;
    private readonly VisualElement _legendList;
    private readonly OccupancyMapRouteOverlay _overlay;

    private Texture2D _texture;
    private Bounds _bounds;
    private List<OccupancyMapRouteOverlay.RouteVisual> _routes = new();

    public ScenarioMapRecap(VisualElement root)
    {
        _canvas = root.Q<VisualElement>("ValidationMapCanvas");
        _image = root.Q<Image>("ValidationMapImage");
        _placeholder = root.Q<Label>("ValidationMapPlaceholder");
        _legend = root.Q<Label>("ValidationMapLegend");
        VisualElement overlayHost = root.Q<VisualElement>("ValidationMapOverlayHost");
        _legendList = root.Q<VisualElement>("ValidationMapLegendList");

        if (_canvas == null || _image == null || _placeholder == null || _legend == null ||
            overlayHost == null || _legendList == null)
            throw new InvalidOperationException("The scenario map recap UI is incomplete.");

        _image.scaleMode = ScaleMode.ScaleToFit;
        _overlay = new OccupancyMapRouteOverlay();
        overlayHost.Add(_overlay);
        _canvas.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
    }

    public void SetMap(Texture2D texture, Bounds bounds)
    {
        _texture = texture;
        _bounds = bounds;
        _image.image = texture;
        _placeholder.EnableInClassList(HiddenClass, texture != null);
        Refresh();
    }

    public void SetRoutes(IReadOnlyList<OccupancyMapRouteOverlay.RouteVisual> routes)
    {
        _routes = routes == null
            ? new List<OccupancyMapRouteOverlay.RouteVisual>()
            : new List<OccupancyMapRouteOverlay.RouteVisual>(routes);
        RefreshLegend();
        Refresh();
    }

    public void Refresh()
    {
        _overlay.SetMap(_bounds, GetDisplayedRect());
        _overlay.ShowGrid = true;
        _overlay.SetRoutes(_routes);
        _legend.text = _texture == null || _bounds.size.x <= 0f
            ? "No environment"
            : $"{_routes.Count} route(s) · grid {_overlay.GridStep:0.##} m · " +
              $"map {_bounds.size.x:0.#} × {_bounds.size.z:0.#} m";
    }

    /// <summary>Colour key of the recap: one entry per configured route.</summary>
    private void RefreshLegend()
    {
        if (_legendList == null)
            return;

        _legendList.Clear();
        foreach (OccupancyMapRouteOverlay.RouteVisual route in _routes)
        {
            var entry = new VisualElement();
            entry.AddToClassList("map-legend-entry");

            var swatch = new VisualElement();
            swatch.AddToClassList("map-legend-swatch");
            swatch.style.backgroundColor = route.Color;

            string routeName = string.IsNullOrWhiteSpace(route.Name) ? "Route" : route.Name;
            var name = new Label($"{routeName} · {route.Points.Count} pts");
            name.AddToClassList("map-legend-name");

            entry.Add(swatch);
            entry.Add(name);
            _legendList.Add(entry);
        }
    }

    private Rect GetDisplayedRect() => _texture == null
        ? Rect.zero
        : OccupancyMapLayout.FitRect(_canvas.contentRect, _texture.width, _texture.height);
}
