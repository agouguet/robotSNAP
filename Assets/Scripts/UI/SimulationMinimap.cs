using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using RobotSNAP;
using RobotSNAP.Agents;
using RobotSNAP.Core.Scenario;

/// <summary>
/// Owns the runtime minimap of the simulation overlay: the background image (occupancy grid or live
/// camera render), one coloured dot per agent, and — over an occupancy grid — a vision cone that
/// shows where each agent looks. Positions are recomputed every frame in the pixel space of the dots
/// layer, and the dots and the cones are pooled, so a long run allocates no VisualElement per frame.
/// </summary>
public sealed class SimulationMinimap
{
    private const string DotClass = "minimap-dot";
    private const string RobotClass = "robot";
    private const string HumanClass = "human";
    private const string FocusedClass = "is-focused";

    /// <summary>Used until the panel has been laid out once; the panel content width is ~216px.</summary>
    private const float FallbackMapWidth = 216f;
    private const float MinMapHeight = 140f;
    private const float MaxMapHeight = 260f;

    /// <summary>Pixel drift tolerated before the map height is recomputed after a panel resize.</summary>
    private const float MapWidthTolerance = 0.5f;

    /// <summary>
    /// Rays traced for one occluded footprint. Enough for a wall corner to show as a cut rather than a
    /// straight edge, few enough that a crowd can be traced without the minimap costing the frame.
    /// </summary>
    private const int RobotConeRays = 33;
    private const int HumanConeRays = 25;

    /// <summary>
    /// A footprint smaller than this is drawn anyway: an agent standing a metre from a wall has almost
    /// nothing to see, and a cone that vanished would read as a missing agent.
    /// </summary>
    private const float MinConeRadius = 3f;

    /// <summary>Metres an agent may move, and degrees it may turn, before its footprint is traced again.</summary>
    private const float ConeMoveTolerance = 0.15f;
    private const float ConeTurnTolerance = 4f;

    /// <summary>Metres-to-pixels used while the scene has no occupancy bounds to scale from.</summary>
    private const float FallbackPixelsPerMetre = 4f;

    /// <summary>Seconds between two enumerations of the agents of the scene.</summary>
    private const float AgentEnumerationInterval = 0.25f;

    /// <summary>Pixel distance under which a dot is left where it already is.</summary>
    private const float DotMoveTolerance = 0.5f;

    private static readonly Color RobotConeFill = new Color(0.23f, 0.51f, 0.96f, 0.18f);
    private static readonly Color RobotConeStroke = new Color(0.58f, 0.77f, 0.99f, 0.65f);
    /// <summary>
    /// Outlines only. A pedestrian perceives at the range its social force model uses - ten metres in the
    /// scenarios of this project, which is the whole of the map - so a crowd of filled discs would hide the
    /// map it is drawn on. The contour still carries the shape and the range, and stays legible at eighty.
    /// </summary>
    private static readonly Color HumanConeFill = new Color(0.96f, 0.62f, 0.04f, 0f);
    private static readonly Color HumanConeStroke = new Color(0.99f, 0.83f, 0.30f, 0.40f);

    private readonly VisualElement _panel;
    private readonly VisualElement _map;
    private readonly Image _image;
    private readonly VisualElement _dots;
    private readonly VisualElement _cones;
    private readonly Camera _camera;

    private readonly List<VisualElement> _dotPool = new();
    private readonly List<MinimapVisionCone> _conePool = new();

    /// <summary>Position last written into each pooled dot, so a dot that did not move costs no style write.</summary>
    private readonly List<Vector2> _dotPositions = new();

    /// <summary>
    /// Silhouette of each agent's footprint as the walls of the map leave it, kept between frames: a
    /// footprint is traced when its agent moved or turned, not once per frame per agent.
    /// </summary>
    private readonly Dictionary<BaseAgent, ConeTrace> _coneTraces = new();
    private readonly List<BaseAgent> _staleTraces = new();

    /// <summary>Agents of the scene, re-enumerated at <see cref="AgentEnumerationInterval"/>.</summary>
    private Robot[] _robots = System.Array.Empty<Robot>();
    private HumanAgent[] _humans = System.Array.Empty<HumanAgent>();
    private float _nextAgentEnumeration;

    /// <summary>The robot scanner is read from the agent, so it is cached per robot rather than per frame.</summary>
    private Robot _scannerOwner;
    private RaycastLaserScanner _robotScanner;

    private Bounds _worldBounds;
    private Transform _focus;

    /// <summary>True when <see cref="SetEnvironment"/> handed us an occupancy texture, false for camera mode.</summary>
    private bool _useOccupancy;

    /// <summary>Texture currently painted behind the dots, kept to detect a render texture created late.</summary>
    private Texture _appliedTexture;

    /// <summary>Texture whose aspect drives the map height, so a panel resize can be caught in Tick.</summary>
    private Texture _aspectSource;
    private float _appliedMapWidth = float.NaN;

    private bool _inert;

    /// <summary>
    /// Builds the minimap from the simulation overlay. Any missing element or a missing camera turns the
    /// instance inert instead of throwing, because the overlay is rebuilt independently of this class.
    /// </summary>
    public SimulationMinimap(VisualElement root, Camera minimapCamera)
    {
        _camera = minimapCamera;

        if (root == null)
        {
            Debug.LogWarning("SimulationMinimap: root VisualElement is null, the minimap stays inert.");
            _inert = true;
            return;
        }

        _panel = root.Q<VisualElement>("MinimapPanel");
        _map = root.Q<VisualElement>("MinimapMap");
        _image = root.Q<Image>("MinimapImage");
        _dots = root.Q<VisualElement>("MinimapDots");

        if (_panel == null)
        {
            Debug.LogWarning("SimulationMinimap: 'MinimapPanel' not found in the simulation overlay, the minimap stays inert.");
            _inert = true;
            return;
        }

        if (_map == null || _image == null || _dots == null)
        {
            Debug.LogWarning(
                "SimulationMinimap: missing element among 'MinimapMap', 'MinimapImage', 'MinimapDots', the minimap stays inert.");
            _inert = true;
            return;
        }

        if (_camera == null)
        {
            Debug.LogWarning("SimulationMinimap: no minimap camera assigned, the minimap stays inert.");
            _inert = true;
            return;
        }

        // The cones sit between the map picture and the dots: under the agent marks, over the walls.
        _cones = new VisualElement { pickingMode = PickingMode.Ignore };
        _cones.style.position = Position.Absolute;
        _cones.style.left = 0f;
        _cones.style.top = 0f;
        _cones.style.right = 0f;
        _cones.style.bottom = 0f;
        _map.Insert(_map.IndexOf(_dots), _cones);
    }

    /// <summary>
    /// Chooses the background source: a non-null occupancy texture switches to grid mode, a null one
    /// falls back to the render texture of the minimap camera.
    /// </summary>
    public void SetEnvironment(Texture2D occupancy, Bounds worldBounds)
    {
        if (_inert)
            return;

        _worldBounds = worldBounds;
        _useOccupancy = occupancy != null;
        ApplyBackground(_useOccupancy ? occupancy : _camera.targetTexture);
        SyncCameraEnabled(IsPanelVisible());
    }

    /// <summary>Marks the agent whose transform is given as the followed one, so its dot gets highlighted.</summary>
    public void SetFocus(Transform target)
    {
        if (_inert)
            return;

        // Applied by the next Tick, which runs every frame and already rewrites the dot classes.
        _focus = target;
    }

    /// <summary>Refreshes the background source and the dot positions; called every frame from Update.</summary>
    public void Tick()
    {
        if (_inert)
            return;

        // A collapsed tab still ticks - the overlay is alive, only its content is hidden - so the camera is
        // told what to do before the early return, or it would keep drawing a picture nobody is showing.
        bool visible = IsPanelVisible();
        SyncCameraEnabled(visible);
        if (!visible)
            return;

        float width = _dots.resolvedStyle.width;
        float height = _dots.resolvedStyle.height;
        if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            return;

        if (!_useOccupancy)
            SyncCameraBackground();

        // Keeps the image ratio after the panel is resized, without writing the style every frame.
        RefreshMapHeight();

        RefreshAgents();

        // Resolved once per frame: the dots have to land on the drawn image, not on the map area.
        Rect imageRect = GetDisplayedImageRect(width, height);

        int used = PlaceAgents(_robots, true, imageRect, 0);
        used = PlaceAgents(_humans, false, imageRect, used);

        for (int index = used; index < _dotPool.Count; index++)
            _dotPool[index].style.display = DisplayStyle.None;

        // The live render already shows which way every agent faces; the cones belong to the
        // occupancy grid, where the picture alone says nothing about orientation.
        for (int index = _useOccupancy ? used : 0; index < _conePool.Count; index++)
            _conePool[index].style.display = DisplayStyle.None;
    }

    private int PlaceAgents<TAgent>(IReadOnlyList<TAgent> agents, bool isRobot, Rect imageRect, int used)
        where TAgent : BaseAgent
    {
        foreach (TAgent agent in agents)
        {
            // An agent destroyed during the frame is still listed for one more enumeration.
            if (agent == null)
                continue;

            if (!TryProject(agent.Position, imageRect, out Vector2 position))
                continue;

            VisualElement dot = GetDot(used);
            // Writing a style schedules a layout pass of the panel, so a crowd that stands still would pay for
            // one per agent per frame. Only a dot that moved a visible distance - or one that has just been
            // handed a pool slot - is moved; the position is remembered here rather than read back from the
            // style, which cannot tell "no left yet" from "left at zero".
            if (NeedsMove(used, position))
            {
                dot.style.left = position.x;
                dot.style.top = position.y;
                _dotPositions[used] = position;
            }
            dot.EnableInClassList(RobotClass, isRobot);
            dot.EnableInClassList(HumanClass, !isRobot);
            dot.EnableInClassList(FocusedClass, IsFocused(agent.transform));
            dot.style.display = DisplayStyle.Flex;

            PlaceCone(used, agent, position, imageRect);
            used++;
        }

        return used;
    }

    /// <summary>
    /// Draws the detection footprint of one agent under its dot, at the range that agent really has and cut
    /// back to what the walls of the map let through.
    ///
    /// A robot reads its lidar, so its cone is the sensor's own range and field of view and an
    /// omnidirectional scanner comes out as a ring with a heading needle. A pedestrian reads the perception
    /// radius its social force model uses - and that model looks all around, so a pedestrian's footprint is a
    /// disc, not the narrow cone it used to be drawn as.
    /// </summary>
    private void PlaceCone(int index, BaseAgent agent, Vector2 position, Rect imageRect)
    {
        if (!_useOccupancy)
            return;

        MinimapVisionCone cone = GetCone(index);

        Vector3 forward = agent.Forward;
        float yaw = forward.sqrMagnitude > 0.0001f
            ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg
            : agent.Rotation.eulerAngles.y;

        // The occupancy picture runs +x towards world -X and +y towards world +Z, so a world heading
        // becomes 90 + yaw in the painter's clockwise-from-+x frame.
        float heading = 90f + yaw;

        RaycastLaserScanner scanner = GetScanner(agent);
        float rangeMetres;
        float fovDegrees;
        Vector3 origin;

        if (scanner != null)
        {
            rangeMetres = scanner.range_max;
            fovDegrees = Mathf.Max(1f, Mathf.Abs(scanner.angle_max - scanner.angle_min) * Mathf.Rad2Deg);
            origin = scanner.LaserOrigin;
        }
        else
        {
            rangeMetres = HumanPerceptionRange(agent);
            // The social force model has no field of view: a pedestrian perceives whoever is close enough,
            // whoever it is facing. Drawing a wedge here would claim a blind spot the model does not have.
            fovDegrees = 360f;
            origin = agent.Position;
        }

        if (rangeMetres <= 0.0001f)
        {
            cone.style.display = DisplayStyle.None;
            return;
        }

        // True scale: the drawn footprint is the sensor's own range on this map, with no window dressing in
        // between. The floor only keeps a footprint visible, it never inflates one.
        float pixelsPerMetre = PixelsPerMetre(imageRect);
        float radius = Mathf.Max(MinConeRadius, rangeMetres * pixelsPerMetre);

        cone.style.left = position.x - radius;
        cone.style.top = position.y - radius;

        ConeTrace trace = GetTrace(agent);
        if (NeedsTrace(trace, origin, heading, rangeMetres, fovDegrees))
            TraceFootprint(trace, origin, yaw, rangeMetres, fovDegrees, isRobot: scanner != null);

        if (trace.Spans.Count > 1)
        {
            cone.SetOccluded(
                radius,
                heading,
                fovDegrees,
                trace.Spans,
                scanner != null ? RobotConeFill : HumanConeFill,
                scanner != null ? RobotConeStroke : HumanConeStroke);
        }
        else if (scanner != null)
        {
            if (fovDegrees >= 350f)
                cone.SetRing(radius, heading, RobotConeFill, RobotConeStroke);
            else
                cone.SetCone(radius, heading, fovDegrees, RobotConeFill, RobotConeStroke);
        }
        else
        {
            cone.SetRing(radius, heading, HumanConeFill, HumanConeStroke);
        }

        cone.style.display = DisplayStyle.Flex;
    }

    /// <summary>
    /// Silhouette of one agent's footprint as the walls of the map leave it: for every ray of the field of
    /// view, from the first edge to the last, how far that ray reaches as a fraction of the full range.
    ///
    /// The rays are cast in world space against the obstacle grid the scenario built, the same grid the
    /// agents plan on and the swath of walls the map is drawn from. A scenario whose environment is a prefab
    /// or a Unity scene has no such grid: its footprint is then left empty, and the caller draws the plain
    /// shape, which is all the map can honestly show.
    /// </summary>
    private void TraceFootprint(
        ConeTrace trace,
        Vector3 origin,
        float yaw,
        float rangeMetres,
        float fovDegrees,
        bool isRobot)
    {
        trace.Spans.Clear();
        trace.Origin = origin;
        trace.Heading = 90f + yaw;
        trace.RangeMetres = rangeMetres;
        trace.FovDegrees = fovDegrees;

        OccupancyGrid grid = ScenarioNavigation.Obstacles;
        if (grid == null || !grid.IsValid)
            return;

        int rays = isRobot && fovDegrees < 350f ? RobotConeRays : HumanConeRays;
        bool fullTurn = fovDegrees >= 350f;
        float sweep = fullTurn ? 360f : fovDegrees;
        float firstScreenAngle = trace.Heading - sweep * 0.5f;
        float step = sweep / (rays - 1);
        var from = new Vector2(origin.x, origin.z);

        for (int ray = 0; ray < rays; ray++)
        {
            // Screen angles are the painter's; the ray is cast in the world, whose x/z plane the picture
            // mirrors, so the world direction of a screen angle is that angle turned back by the quarter turn
            // the occupancy convention introduces.
            float worldAngle = (firstScreenAngle + step * ray - 90f) * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Sin(worldAngle), Mathf.Cos(worldAngle));
            float distance = grid.DistanceToWall(from, direction, rangeMetres);
            trace.Spans.Add(Mathf.Clamp01(distance / rangeMetres));
        }
    }

    /// <summary>The radius, in metres, at which a pedestrian perceives the other agents around it.</summary>
    private static float HumanPerceptionRange(BaseAgent agent)
    {
        var movement = agent != null ? agent.GetComponent<HumanMovement>() : null;
        return movement != null ? movement.PerceptionRadius : 0f;
    }

    /// <summary>The trace of one agent, created on first use and reused for the whole run.</summary>
    private ConeTrace GetTrace(BaseAgent agent)
    {
        if (_coneTraces.TryGetValue(agent, out ConeTrace trace))
            return trace;

        trace = new ConeTrace();
        _coneTraces[agent] = trace;
        return trace;
    }

    /// <summary>
    /// True when a footprint has to be traced again: it has never been traced, its agent moved far enough or
    /// turned far enough for the walls to fall differently, or its sensor changed - a robot rebuilt with
    /// another profile keeps its agent object.
    /// </summary>
    private static bool NeedsTrace(ConeTrace trace, Vector3 origin, float heading, float rangeMetres, float fovDegrees)
    {
        // Empty means "no silhouette", either because this agent has never been traced or because the map has
        // no obstacle grid to trace against: both have to be looked at again, and the attempt is cheap.
        if (trace.Spans.Count == 0)
            return true;

        if (!Mathf.Approximately(trace.RangeMetres, rangeMetres) || !Mathf.Approximately(trace.FovDegrees, fovDegrees))
            return true;

        if ((trace.Origin - origin).sqrMagnitude > ConeMoveTolerance * ConeMoveTolerance)
            return true;

        return Mathf.Abs(Mathf.DeltaAngle(trace.Heading, heading)) > ConeTurnTolerance;
    }

    /// <summary>
    /// Silhouette of one agent's footprint: where it stood when it was traced, where it looked, what its
    /// sensor reaches, and one visible length per ray.
    /// </summary>
    private sealed class ConeTrace
    {
        public Vector3 Origin = new Vector3(float.NaN, float.NaN, float.NaN);
        public float Heading = float.NaN;
        public float RangeMetres;
        public float FovDegrees;
        public readonly List<float> Spans = new();
    }

    /// <summary>The lidar of the robot, cached: it is the only sensor the cone reads.</summary>
    private RaycastLaserScanner GetScanner(BaseAgent agent)
    {
        if (agent is not Robot robot)
            return null;

        if (robot != _scannerOwner)
        {
            _scannerOwner = robot;
            _robotScanner = robot.GetLaserScanner();
        }

        return _robotScanner;
    }

    /// <summary>Map scale, from the world bounds the occupancy grid was built with.</summary>
    private float PixelsPerMetre(Rect imageRect)
    {
        float span = _worldBounds.size.x;
        if (span <= 0.0001f || imageRect.width <= 0f)
            return FallbackPixelsPerMetre;

        return imageRect.width / span;
    }

    /// <summary>Projects a world position onto the dots layer, or reports that it has no sensible pixel.</summary>
    private bool TryProject(Vector3 worldPosition, Rect imageRect, out Vector2 position)
    {
        if (_useOccupancy)
        {
            // Same convention as the scenario editor: the image's left/top edges are world max X/min Z,
            // so its normalized y already grows downwards, unlike a camera viewport y.
            Vector2 normalized = OccupancyMapCoordinates.WorldToImageNormalized(
                new Vector2(worldPosition.x, worldPosition.z), _worldBounds);
            position = new Vector2(
                imageRect.x + normalized.x * imageRect.width,
                imageRect.y + normalized.y * imageRect.height);
            return true;
        }

        Vector3 viewport = _camera.WorldToViewportPoint(worldPosition);

        // Behind the camera the viewport coordinates are mirrored, so drawing them would be wrong.
        if (viewport.z <= 0f)
        {
            position = default;
            return false;
        }

        // Outside the rendered frame the agent is simply not on the picture.
        if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
        {
            position = default;
            return false;
        }

        position = new Vector2(
            imageRect.x + viewport.x * imageRect.width,
            imageRect.y + (1f - viewport.y) * imageRect.height);
        return true;
    }

    /// <summary>
    /// Area actually covered by the scale-to-fit background. UI Toolkit centres that image, and the panel
    /// height is clamped, so the drawn rect is not always the whole map area.
    /// </summary>
    private Rect GetDisplayedImageRect(float width, float height)
    {
        var area = new Rect(0f, 0f, width, height);
        Texture texture = _appliedTexture;
        if (texture == null || texture.width <= 0 || texture.height <= 0)
            return area;

        Rect fitted = OccupancyMapLayout.FitRect(area, texture.width, texture.height);
        return fitted.width > 0f && fitted.height > 0f ? fitted : area;
    }

    /// <summary>The focus transform can be the agent root or an inner part such as the robot base link.</summary>
    private bool IsFocused(Transform agentTransform)
    {
        if (_focus == null || agentTransform == null)
            return false;

        return agentTransform == _focus || _focus.IsChildOf(agentTransform) || agentTransform.IsChildOf(_focus);
    }

    private void ApplyBackground(Texture texture)
    {
        if (texture == null)
        {
            // No source to paint: the map keeps its panel colour and the dots fall back to the whole area.
            _image.style.backgroundImage = StyleKeyword.None;
            _image.RemoveFromClassList("is-grid");
            _appliedTexture = null;
            _aspectSource = null;
            return;
        }

        if (_useOccupancy)
        {
            _image.style.backgroundImage = Background.FromTexture2D((Texture2D)texture);
            // The occupancy grid is a black-and-white picture; the class tints it into the app palette.
            _image.AddToClassList("is-grid");
        }
        else
        {
            _image.style.backgroundImage = Background.FromRenderTexture((RenderTexture)texture);
            _image.RemoveFromClassList("is-grid");
        }

        _appliedTexture = texture;
        _aspectSource = texture;
        _appliedMapWidth = float.NaN;
        RefreshMapHeight();
    }

    private void SyncCameraBackground()
    {
        RenderTexture target = _camera.targetTexture;
        if (target == null || target == _appliedTexture)
            return;

        // The simulation creates the render texture after the overlay, so it is picked up when it appears.
        ApplyBackground(target);
    }

    /// <summary>
    /// Keeps the render-texture camera on only while its output is what the panel shows.
    ///
    /// The minimap paints either the occupancy grid carried by the scenario or the live render of that
    /// camera. With a grid - the case of every scenario authored in the app - the camera drew the whole
    /// crowd into a thousand-pixel texture that no element on screen ever read, and it cost about a third
    /// of the frame time of a large crowd. The camera follows the source now, not the scene.
    /// </summary>
    private void SyncCameraEnabled(bool panelVisible)
    {
        if (_camera == null)
            return;

        bool needed = panelVisible && !_useOccupancy;
        if (_camera.enabled != needed)
            _camera.enabled = needed;
    }

    /// <summary>
    /// Sizes the map height from the source ratio. Only the height moves: the panel owns the width.
    /// </summary>
    private void RefreshMapHeight()
    {
        Texture texture = _aspectSource;
        if (texture == null || texture.width <= 0 || texture.height <= 0)
            return;

        float width = _map.resolvedStyle.width;
        if (float.IsNaN(width) || width <= 0f)
            width = FallbackMapWidth;

        if (!float.IsNaN(_appliedMapWidth) && Mathf.Abs(width - _appliedMapWidth) < MapWidthTolerance)
            return;

        _appliedMapWidth = width;
        float aspect = (float)texture.width / texture.height;
        _map.style.height = Mathf.Clamp(width / aspect, MinMapHeight, MaxMapHeight);
    }

    private VisualElement GetDot(int index)
    {
        if (index < _dotPool.Count)
            return _dotPool[index];

        var dot = new VisualElement { pickingMode = PickingMode.Ignore };
        dot.AddToClassList(DotClass);
        _dots.Add(dot);
        _dotPool.Add(dot);
        // A dot that has never been placed has no position: a NaN makes the first write always happen.
        _dotPositions.Add(new Vector2(float.NaN, float.NaN));
        return dot;
    }

    /// <summary>
    /// Re-enumerates the agents of the scene at a fixed rate.
    ///
    /// The two <c>FindObjectsByType</c> calls walk every object of the scene and allocate an array for the
    /// result; doing that twice per frame is what the minimap added to a crowd. Only the *set* of agents is
    /// cached - a dot reads the live position of the agent it points at, so the map still moves at the frame
    /// rate, and an agent that appears joins it within a quarter of a second.
    /// </summary>
    private void RefreshAgents()
    {
        if (Time.unscaledTime < _nextAgentEnumeration)
            return;

        _nextAgentEnumeration = Time.unscaledTime + AgentEnumerationInterval;
        _robots = UnityEngine.Object.FindObjectsByType<Robot>();
        _humans = UnityEngine.Object.FindObjectsByType<HumanAgent>();

        PruneConeTraces();
    }

    /// <summary>
    /// Drops the footprint kept for an agent the scenario has removed. A new scenario rebuilds the whole cast,
    /// so without this a long session would keep one silhouette per agent that ever lived.
    /// </summary>
    private void PruneConeTraces()
    {
        if (_coneTraces.Count == 0)
            return;

        _staleTraces.Clear();
        foreach (KeyValuePair<BaseAgent, ConeTrace> entry in _coneTraces)
        {
            if (entry.Key == null)
                _staleTraces.Add(entry.Key);
        }

        for (int index = 0; index < _staleTraces.Count; index++)
            _coneTraces.Remove(_staleTraces[index]);
    }

    /// <summary>True when the dot of that pool slot has to be written: it is new, or it moved enough to show.</summary>
    private bool NeedsMove(int index, Vector2 position)
    {
        if (index >= _dotPositions.Count)
            return true;

        Vector2 previous = _dotPositions[index];
        if (float.IsNaN(previous.x) || float.IsNaN(previous.y))
            return true;

        return (previous - position).sqrMagnitude > DotMoveTolerance * DotMoveTolerance;
    }

    /// <summary>Same pooling as the dots: one cone element per agent, reused across frames.</summary>
    private MinimapVisionCone GetCone(int index)
    {
        if (index < _conePool.Count)
            return _conePool[index];

        var cone = new MinimapVisionCone();
        _cones.Add(cone);
        _conePool.Add(cone);
        return cone;
    }

    /// <summary>
    /// A collapsed tab detaches the panel or hides an ancestor: visibility is inherited in USS, but
    /// display is not, so the resolved size is the only cheap signal left for a hidden ancestor.
    /// </summary>
    private bool IsPanelVisible()
    {
        if (_panel.panel == null || !_panel.visible)
            return false;

        if (_panel.resolvedStyle.display == DisplayStyle.None)
            return false;

        return _panel.resolvedStyle.width > 0f && _panel.resolvedStyle.height > 0f;
    }
}
