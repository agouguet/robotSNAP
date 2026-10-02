using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RobotSNAP.Agents;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The forms of the wizard: the collapsible sections, the route point rows and every field
/// of the left and right panels, and the choices their dropdowns display.</summary>
public sealed partial class ScenarioRouteEditor
{

    // ==========================================
    //   STEP 2 DISCLOSURE AND POINT AREAS
    // ==========================================

    /// <summary>
    /// A collapsed section is one click away rather than gone: the chevron and the header carry the state, so
    /// the author always knows there is more to see.
    /// </summary>
    private void ApplySectionState()
    {
        ApplySection(_sectionGlobalHeader, _sectionGlobalContent, _globalExpanded);
        ApplySection(_sectionGroupHeader, _sectionGroupContent, _groupExpanded);
        ApplySection(_sectionBehaviourHeader, _sectionBehaviourContent, _behaviourExpanded);
        ApplySection(_sectionDepartureHeader, _sectionDepartureContent, _departureExpanded);
    }

    private static void ApplySection(Button header, VisualElement content, bool expanded)
    {
        if (header == null || content == null)
            return;

        content.EnableInClassList(HiddenClass, !expanded);
        header.EnableInClassList("expanded", expanded);
        string title = header.userData as string ?? header.text;
        header.text = expanded ? $"▾  {title}" : $"▸  {title}";
    }

    private static void WriteZoneFields(ZoneFields fields, Rect zone)
    {
        fields.CenterX?.SetValueWithoutNotify(RoundCoordinate(zone.center.x));
        fields.CenterZ?.SetValueWithoutNotify(RoundCoordinate(zone.center.y));
        fields.SizeX?.SetValueWithoutNotify(RoundCoordinate(zone.width));
        fields.SizeZ?.SetValueWithoutNotify(RoundCoordinate(zone.height));
    }

    /// <summary>Keeps the numeric fields in step with an area being dragged on the map.</summary>
    private void SyncZoneFieldsNoNotify()
    {
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;

        int index = _zoneDragIndex;
        if (_pointZoneFields.TryGetValue(index, out ZoneFields fields))
            WriteZoneFields(fields, PointArea(active, index) ?? default);
        UpdateAreaBadge(index);
    }

    /// <summary>
    /// The area of one point, or null when that point is fixed. Index 0 is the start of the route, so its area is
    /// the spawn area, and every other index is the arrival area of one objective. A point is one or the other and
    /// never both, which is why one accessor answers for a whole row.
    /// </summary>
    private static Rect? PointArea(RouteDraft route, int index)
    {
        if (route == null || index < 0 || index >= route.Points.Count)
            return null;

        return index == 0
            ? (route.SpawnRandom ? route.SpawnZone : (Rect?)null)
            : route.ZoneAt(index);
    }

    /// <summary>Writes an area back to its point, or turns that point back into a fixed point.</summary>
    private static void SetPointArea(RouteDraft route, int index, Rect? area)
    {
        if (index == 0)
        {
            route.SpawnRandom = area.HasValue;
            if (area.HasValue)
                route.SpawnZone = area.Value;
        }
        else
        {
            NormalizeZones(route);
            route.PointZones[index] = area;
        }

        // A point is a point OR an area: while it is an area the route runs to the centre of that area.
        if (area.HasValue)
            route.Points[index] = area.Value.center;
    }

    private void RegisterZoneFields(int index, ZoneFields fields)
    {
        void OnChanged(ChangeEvent<float> evt)
        {
            if (_updatingFields) return;
            PushUndo($"zone:{_activeRouteIndex}:{index}");
            ApplyZoneFields(index);
        }

        fields.CenterX?.RegisterValueChangedCallback(OnChanged);
        fields.CenterZ?.RegisterValueChangedCallback(OnChanged);
        fields.SizeX?.RegisterValueChangedCallback(OnChanged);
        fields.SizeZ?.RegisterValueChangedCallback(OnChanged);
    }

    /// <summary>Rebuilds one area from its four numeric fields, so typing a value moves exactly its edge.</summary>
    private void ApplyZoneFields(int index)
    {
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;

        if (!_pointZoneFields.TryGetValue(index, out ZoneFields fields))
            return;

        Rect zone = ZoneFromCenterAndSize(
            new Vector2(fields.CenterX.value, fields.CenterZ.value),
            new Vector2(fields.SizeX.value, fields.SizeZ.value));

        SetPointArea(active, index, zone);
        UpdateAreaBadge(index);

        active.RouteModified = true;
        CancelZonePick();
        RefreshOverlay();
    }

    private void UpdateAreaBadge(int index)
    {
        if (!_pointAreaLabels.TryGetValue(index, out Label badge))
            return;

        Rect? area = PointArea(ActiveRoute, index);
        badge.text = area.HasValue && IsUsableZone(area.Value)
            ? DescribeArea(area.Value)
            : "Draw the area on the map";
    }

    private static string DescribeArea(Rect area) => $"Area {area.width:0.#} x {area.height:0.#} m";

    private void RebuildPointRows()
    {
        _routePointsContainer.Clear();
        _pointRows.Clear();
        _pointFields.Clear();
        _pointZoneFields.Clear();
        _pointAreaLabels.Clear();
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;

        for (int index = 0; index < active.Points.Count; index++)
        {
            int capturedIndex = index;
            Vector2 point = active.Points[index];
            Rect? area = PointArea(active, index);
            var row = new VisualElement();
            row.AddToClassList("route-point-row");
            row.EnableInClassList("selected", index == _pendingPointIndex);

            var header = new VisualElement();
            header.AddToClassList("route-point-row-header");
            var name = new Label(RouteMapHitTesting.PointLabel(index));
            name.AddToClassList("route-point-name");
            header.Add(name);

            // The two states of a point live side by side here: the button names the one it will switch to.
            var zoneButton = new Button(() => TogglePointArea(capturedIndex))
            {
                text = area.HasValue ? "Area" : "Point"
            };
            zoneButton.AddToClassList("route-point-zone-button");
            zoneButton.EnableInClassList("on", area.HasValue);
            zoneButton.tooltip = area.HasValue
                ? "Turn this area back into one exact point"
                : "Let each agent draw its own point inside an area";
            header.Add(zoneButton);
            row.Add(header);

            // Coordinates and row actions share one line, so a route of six points still fits without a scrollbar.
            var line = new VisualElement();
            line.AddToClassList("route-point-line");
            if (area.HasValue)
            {
                // An area shows its size instead of the coordinates of a point the author no longer has.
                var summary = new Label(IsUsableZone(area.Value) ? DescribeArea(area.Value) : "Draw the area on the map");
                summary.AddToClassList("route-point-area-summary");
                _pointAreaLabels[index] = summary;
                line.Add(summary);
            }
            else
            {
                FloatField xField = CreateCoordinateField("X", point.x);
                FloatField zField = CreateCoordinateField("Z", point.y);
                xField.RegisterValueChangedCallback(evt => SetPointCoordinate(capturedIndex, true, evt.newValue));
                zField.RegisterValueChangedCallback(evt => SetPointCoordinate(capturedIndex, false, evt.newValue));
                line.Add(xField);
                line.Add(zField);
                _pointFields[index] = new CoordinateFields { X = xField, Z = zField };
            }

            var actions = new VisualElement();
            actions.AddToClassList("route-point-actions");
            if (index > 0)
            {
                Button up = CreateSmallButton("↑", () => MovePoint(capturedIndex, -1));
                Button down = CreateSmallButton("↓", () => MovePoint(capturedIndex, 1));
                Button remove = CreateSmallButton("×", () => RemovePoint(capturedIndex));
                up.SetEnabled(index > 1);
                down.SetEnabled(index < active.Points.Count - 1);
                remove.SetEnabled(active.Points.Count > 2);
                actions.Add(up);
                actions.Add(down);
                actions.Add(remove);
            }
            line.Add(actions);
            row.Add(line);

            if (area.HasValue)
            {
                // The exact numbers stay available for an author who wants a precise area; the map gesture is one
                // click away. Both used to live in the left panel, away from the point they describe.
                var fields = new ZoneFields
                {
                    CenterX = CreateZoneField("Center X"),
                    CenterZ = CreateZoneField("Center Z"),
                    SizeX = CreateZoneField("Size X"),
                    SizeZ = CreateZoneField("Size Z")
                };
                var grid = new VisualElement();
                grid.AddToClassList("route-point-zone-grid");
                grid.Add(fields.CenterX);
                grid.Add(fields.CenterZ);
                grid.Add(fields.SizeX);
                grid.Add(fields.SizeZ);
                _pointZoneFields[index] = fields;
                RegisterZoneFields(index, fields);
                WriteZoneFields(fields, area.Value);
                row.Add(grid);

                var draw = new Button(() => BeginZonePick(capturedIndex)) { text = "Draw area on map" };
                draw.AddToClassList("route-point-draw-button");
                row.Add(draw);
            }

            row.RegisterCallback<PointerDownEvent>(_ => SelectPoint(capturedIndex));
            _pointRows.Add(row);
            _routePointsContainer.Add(row);
        }
    }

    /// <summary>
    /// Turns one point into an area around it, or back into the exact point at the centre of that area. A point is
    /// one or the other, so the two states never show up together on the map.
    /// </summary>
    private void TogglePointArea(int index)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || index < 0 || index >= active.Points.Count)
            return;

        PushUndo($"point-area:{_activeRouteIndex}:{index}");
        Rect? area = PointArea(active, index);
        if (area.HasValue)
        {
            // Back to a point, on the centre of the area the author was looking at, so it does not jump.
            active.Points[index] = area.Value.center;
            SetPointArea(active, index, null);
        }
        else
        {
            Rect created = DefaultZoneAround(active.Points[index]);
            active.Points[index] = created.center;
            SetPointArea(active, index, created);
        }

        active.RouteModified = true;
        active.HasNonSpatialGoal = false;
        _pendingPointIndex = index;
        CancelZonePick();
        RefreshActiveRoute();
        RefreshRouteList();
    }

    private void UpdatePointSelectionHighlight()
    {
        for (int index = 0; index < _pointRows.Count; index++)
            _pointRows[index].EnableInClassList("selected", index == _pendingPointIndex);
    }

    private static FloatField CreateCoordinateField(string label, float value)
    {
        var field = new FloatField(label) { value = RoundCoordinate(value) };
        field.AddToClassList("creation-text-field");
        field.AddToClassList("route-coordinate-field");
        return field;
    }

    private static FloatField CreateZoneField(string label)
    {
        var field = new FloatField(label);
        field.AddToClassList("creation-text-field");
        field.AddToClassList("route-zone-field");
        return field;
    }

    private static Button CreateSmallButton(string text, Action clicked)
    {
        var button = new Button(clicked) { text = text };
        button.AddToClassList("route-point-small-button");
        return button;
    }

    private void SelectPoint(int index)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || index < 0 || index >= active.Points.Count)
            return;
        _pendingPointIndex = index;
        UpdatePointSelectionHighlight();
        SetPlacementInstruction();
        RefreshOverlay();
    }

    private void SetPointCoordinate(int index, bool isX, float value)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || index < 0 || index >= active.Points.Count)
            return;
        value = RoundCoordinate(value);
        Vector2 point = active.Points[index];
        if (Mathf.Abs((isX ? point.x : point.y) - value) < 0.0001f)
            return;
            PushUndo($"coordinate:{_activeRouteIndex}:{index}:{(isX ? "x" : "z")}");
            SetPointPosition(active, index, isX ? new Vector2(value, point.y) : new Vector2(point.x, value));
            active.RouteModified = true;
        active.HasNonSpatialGoal = false;
        RefreshOverlay();
    }

    private void MovePoint(int index, int direction)
    {
        RouteDraft active = ActiveRoute;
        int targetIndex = index + direction;
        if (active == null || index <= 0 || targetIndex <= 0 || targetIndex >= active.Points.Count)
            return;
        PushUndo();
        SwapPoints(active, index, targetIndex);
        active.RouteModified = true;
        _pendingPointIndex = targetIndex;
        RebuildPointRows();
        RefreshOverlay();
    }

    private void RemovePoint(int index)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || index <= 0 || active.Points.Count <= 2)
        {
            _instructionLabel.text = "The start point and one objective must stay on the route.";
            return;
        }
        PushUndo();
        RemovePointAt(active, index);
        active.RouteModified = true;
        _pendingPointIndex = Mathf.Clamp(_pendingPointIndex, 0, active.Points.Count - 1);
        RebuildPointRows();
        SetPlacementInstruction();
        RefreshOverlay();
    }

    private void UpdateHumanDraft(Action<RouteDraft> update)
    {
        if (_updatingFields || ActiveRoute == null || ActiveRoute.IsRobot)
            return;
        update(ActiveRoute);
    }

    private void SetEndBehaviorChoices()
    {
        if (_endBehaviorDropdown.choices.Count == EndBehaviorChoices.Length &&
            !_endBehaviorDropdown.choices.Where((choice, index) => choice != EndBehaviorChoices[index]).Any())
            return;
        _endBehaviorDropdown.choices = new List<string>(EndBehaviorChoices);
    }

    private void SetFormationChoices()
    {
        if (_formationDropdown.choices.Count == FormationChoices.Length &&
            !_formationDropdown.choices.Where((choice, index) => choice != FormationChoices[index]).Any())
            return;
        _formationDropdown.choices = new List<string>(FormationChoices);
    }

    /// <summary>
    /// Exposes the one value the selected formation tunes — a wedge opening, a row stagger, a cluster
    /// radius, a pair front spacing — and hides the field for the formations that have nothing to tune.
    /// An empty field (zero) means "use the natural default", which the label spells out.
    /// </summary>
    private void UpdateFormationParameterField(RouteDraft active)
    {
        bool hasParameter = active != null && GroupFormation.HasParameter(active.Formation);
        _formationParameterLabel.EnableInClassList(HiddenClass, !hasParameter);
        _formationParameterField.EnableInClassList(HiddenClass, !hasParameter);
        if (!hasParameter)
            return;

        _formationParameterLabel.text = GroupFormation.DescribeParameter(active.Formation, active.GroupSpacing);
        _formationParameterField.SetValueWithoutNotify(active.FormationParameter);
    }

    /// <summary>
    /// The controller dropdown exposes display labels while the YAML uses short values; "Inherit from config"
    /// is written as an absent value so the HumanConfig asset keeps deciding.
    /// </summary>
    private static string MovementControllerToDisplay(string value)
    {
        return HumanMovementControllerParser.TryParse(value, out MovementControllerType controller)
            ? HumanMovementControllerParser.ToDisplayName(controller)
            : InheritControllerChoice;
    }

    private static string ParseMovementControllerChoice(string display)
    {
        if (string.IsNullOrWhiteSpace(display) ||
            string.Equals(display.Trim(), InheritControllerChoice, StringComparison.OrdinalIgnoreCase))
            return null;

        return HumanMovementControllerParser.TryParse(display, out MovementControllerType controller)
            ? HumanMovementControllerParser.ToYamlValue(controller)
            : null;
    }

    /// <summary>
    /// The formation dropdown exposes display labels while the YAML uses short values,
    /// so the two mappings are explicit and unknown values fall back to the default pair.
    /// </summary>
    private static string FormationToDisplay(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            string formation = value.Trim().ToLowerInvariant();
            if (formation == "row" || formation == "abreast" || formation == "side-by-side") return "Row";
            if (formation == "column") return "Column";
            if (formation == "wedge") return "Wedge";
            if (formation == "cluster") return "Cluster";
            if (formation == "scatter" || formation == "none" || formation == "independent") return "Independent";
        }

        return "Pair";
    }

    private static string FormationFromDisplay(string display)
    {
        if (!string.IsNullOrWhiteSpace(display))
        {
            string formation = display.Trim().ToLowerInvariant();
            if (formation == "row" || formation == "abreast" || formation == "side-by-side") return "row";
            if (formation == "column") return "column";
            if (formation == "wedge") return "wedge";
            if (formation == "cluster") return "cluster";
            if (formation == "independent") return GroupFormation.ScatterFormation;
        }

        return "pair";
    }

    /// <summary>
    /// <summary>
    /// The dropdown exposes display labels while the YAML uses short values, so the label is
    /// mapped explicitly and the shared parser stays the fallback for anything else.
    /// </summary>
    private static HumanEndBehavior ParseEndBehaviorChoice(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            string label = value.Trim();
            if (string.Equals(label, HumanEndBehaviorParser.ToDisplayName(HumanEndBehavior.Disappear), StringComparison.OrdinalIgnoreCase))
                return HumanEndBehavior.Disappear;
            if (string.Equals(label, HumanEndBehaviorParser.ToDisplayName(HumanEndBehavior.Loop), StringComparison.OrdinalIgnoreCase))
                return HumanEndBehavior.Loop;
            if (string.Equals(label, HumanEndBehaviorParser.ToDisplayName(HumanEndBehavior.Stay), StringComparison.OrdinalIgnoreCase))
                return HumanEndBehavior.Stay;
        }

        return HumanEndBehaviorParser.Parse(value);
    }
}
