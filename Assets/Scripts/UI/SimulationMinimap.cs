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

    /// <summary>Humans have no sensor to read, so their cone uses a plain field of view.</summary>
    private const float HumanConeFov = 70f;
    private const float HumanConeRadius = 15f;

    /// <summary>Bounds on a drawn cone: small enough not to hide the map, large enough to read.</summary>
    private const float MinConeRadius = 11f;
    private const float MaxConeRadius = 34f;

    /// <summary>Metres-to-pixels used while the scene has no occupancy bounds to scale from.</summary>
    private const float FallbackPixelsPerMetre = 4f;

    private static readonly Color RobotConeFill = new Color(0.23f, 0.51f, 0.96f, 0.30f);
    private static readonly Color RobotConeStroke = new Color(0.58f, 0.77f, 0.99f, 0.65f);
    private static readonly Color HumanConeFill = new Color(0.96f, 0.62f, 0.04f, 0.24f);
    private static readonly Color HumanConeStroke = new Color(0.99f, 0.83f, 0.30f, 0.60f);

    private readonly VisualElement _panel;
    private readonly VisualElement _map;
    private readonly Image _image;
    private readonly VisualElement _dots;
    private readonly VisualElement _cones;
    private readonly Camera _camera;

    private readonly List<VisualElement> _dotPool = new();
    private readonly List<MinimapVisionCone> _conePool = new();

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
        if (_inert || !IsPanelVisible())
            return;

        float width = _dots.resolvedStyle.width;
        float height = _dots.resolvedStyle.height;
        if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            return;

        if (!_useOccupancy)
            SyncCameraBackground();

        // Keeps the image ratio after the panel is resized, without writing the style every frame.
        RefreshMapHeight();

        // Unity 6000.4 has no List-filling overload for this call, so the two arrays it returns are the
        // only per-frame cost; the dot pool below is what must not allocate.
        Robot[] robots = UnityEngine.Object.FindObjectsByType<Robot>();
        HumanAgent[] humans = UnityEngine.Object.FindObjectsByType<HumanAgent>();

        // Resolved once per frame: the dots have to land on the drawn image, not on the map area.
        Rect imageRect = GetDisplayedImageRect(width, height);

        int used = PlaceAgents(robots, true, imageRect, 0);
        used = PlaceAgents(humans, false, imageRect, used);

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
            dot.style.left = position.x;
            dot.style.top = position.y;
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
    /// Draws the field of view of one agent under its dot. The robot reads its lidar, so an
    /// omnidirectional scanner comes out as a detection ring with a needle instead of a cone.
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
        float radius;

        if (scanner != null)
        {
            float span = Mathf.Abs(scanner.angle_max - scanner.angle_min) * Mathf.Rad2Deg;
            radius = Mathf.Clamp(scanner.range_max * PixelsPerMetre(imageRect), MinConeRadius, MaxConeRadius);

            cone.style.left = position.x - radius;
            cone.style.top = position.y - radius;

            if (span >= 350f)
                cone.SetRing(radius, heading, RobotConeFill, RobotConeStroke);
            else
                cone.SetCone(radius, heading, Mathf.Max(1f, span), RobotConeFill, RobotConeStroke);
        }
        else
        {
            radius = HumanConeRadius;

            cone.style.left = position.x - radius;
            cone.style.top = position.y - radius;
            cone.SetCone(radius, heading, HumanConeFov, HumanConeFill, HumanConeStroke);
        }

        cone.style.display = DisplayStyle.Flex;
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
        return dot;
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
