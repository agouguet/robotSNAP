using System.Collections.Generic;
using System.Text;
using RobotSNAP;
using RobotSNAP.Agents;
using RobotSNAP.ROS;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Gives every robot the project imported the same equipment as the base it shipped with.
///
/// A prefab that comes out of the URDF importer is only a body: links, joints and meshes. What makes it a
/// robot of this application is the same set of components the Freight prefab carries - the Robot, the
/// input controller, the articulation initialisation, the chassis that turns a velocity into wheel speeds,
/// the odometry publisher and the lidar - each one wired to the links of that particular model.
///
/// The wiring is read from the model rather than written down per robot: the wheels are the joints the
/// importer created for them, split into a left side and a right side by where they sit, the base is the
/// root of the articulation, and the lidar sits on the link the model already calls a lidar. A new robot
/// therefore needs no code - drop its prefab in <c>Assets/Prefabs/Robots</c>, add its entry to
/// <see cref="RobotProfiles"/>, and run the two menu items below.
/// </summary>
public static class RobotPrefabs
{
    private const string RobotFolderPath = "Assets/Prefabs/Robots";
    private const string CatalogFolder = "Assets/Resources";
    private const string CatalogPath = CatalogFolder + "/RobotCatalog.asset";

    /// <summary>
    /// The contact a driven wheel scrubs on. A chassis whose wheels grip as hard as the floor holds itself
    /// straight however its two sides are driven, so a driven wheel is given a slippery contact on purpose:
    /// measured on this project, dropping the driven wheels from the floor's 0.6 to this material took the
    /// Kuri from 85 to 97 percent of the turn it was commanded.
    /// </summary>
    private const string WheelMaterialPath = "Assets/Materials/Physics/WheelSkid.physicMaterial";

    /// <summary>
    /// The contact the body of a robot meets the world with - a person, a wall, a kerb. It is an ordinary
    /// floor contact and not the slippery one of a rolling link, because a collision is exactly the thing
    /// that must stay real: a robot that slid along a crowd would answer the question the application
    /// exists to ask with the wrong numbers.
    /// </summary>
    private const string ChassisMaterialPath = "Assets/Materials/Physics/Chassis.physicMaterial";

    /// <summary>
    /// How far above the floor the body of a robot is left, in metres. A model imported from a URDF often
    /// declares a chassis collision that reaches down to the ground - the Freight's base box comes out one
    /// millimetre above it, the Bibus's base plate comes out below the wheels - and a body dragging on the
    /// floor is a sledge: it eats the turn, and it makes every contact the robot has happen through a
    /// surface that is not the one a person touches.
    /// </summary>
    private const float GroundClearance = 0.02f;

    /// <summary>Name of the child the base components live on, the way the base robot authored it.</summary>
    private const string PluginNodeName = "Plugins";

    /// <summary>
    /// Name of the child the tool hangs a raised collider on. The replacement is a child rather than a
    /// second collider on the same object so that a second run of the tool can find what the first one did
    /// and redo it: a lift that is applied twice lifts the same shape twice, which is how the Freight ended
    /// up with twenty-one chassis colliders stacked on top of its own.
    /// </summary>
    private const string GroundClearanceNodeName = "GroundClearance";

    /// <summary>
    /// Torque every driven wheel of a base-driven chassis is left with, in N.m.
    ///
    /// It is written on the prefab rather than left to the default of the component so that what a robot
    /// was wired with is readable from the robot. The figure is a fifth of a newton-metre: enough for the
    /// wheel to spin itself up and be seen turning, far too little for four of them to hold the chassis
    /// back, which is what they did at the two newton-metres they were first given.
    /// </summary>
    private const float RollingWheelForceLimit = 0.2f;

    /// <summary>Name of the link the lidar components are hung from when the model declares no sensor.</summary>
    private const string LaserNodeName = "laser_link";

    [MenuItem("RobotSNAP/Robots/Wire the robot prefabs")]
    public static void WireAll()
    {
        var report = new StringBuilder();
        foreach (string path in PrefabPaths())
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!RobotProfiles.TryFind(name, out RobotProfile profile))
            {
                report.AppendLine($"{name}: not a robot type this build knows, left alone.");
                continue;
            }

            report.AppendLine(Wire(path, profile));
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RobotPrefabs] Wired the robot prefabs:\n" + report);
    }

    [MenuItem("RobotSNAP/Robots/Rebuild the robot catalog")]
    public static void RebuildCatalog()
    {
        var entries = new List<RobotCatalog.Entry>();
        var report = new StringBuilder();

        foreach (string path in PrefabPaths())
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!RobotProfiles.TryFind(name, out RobotProfile profile))
            {
                report.AppendLine($"{name}: not a robot type this build knows, not catalogued.");
                continue;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponentInChildren<Robot>(true) == null)
            {
                report.AppendLine($"{name}: carries no Robot component, not catalogued. Run the wiring first.");
                continue;
            }

            entries.Add(new RobotCatalog.Entry { TypeId = profile.Id, Prefab = prefab });
            report.AppendLine($"{profile.Id} -> {path}");
        }

        EnsureFolder(CatalogFolder);
        RobotCatalog catalog = AssetDatabase.LoadAssetAtPath<RobotCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<RobotCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.SetEntries(entries);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[RobotPrefabs] Wrote {CatalogPath} with {entries.Count} type(s):\n" + report);
    }

    // ==========================================
    //          ONE PREFAB
    // ==========================================

    private static string Wire(string path, RobotProfile profile)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
            return $"{profile.Id}: could not be opened, left alone.";

        try
        {
            Transform baseLink = FindBaseLink(root.transform);
            if (baseLink == null)
                return $"{profile.Id}: no articulation root, left alone. It is not a body this tool can drive.";

            int strays = NeutraliseStrayArticulations(root.transform, baseLink);

            ClearGroundClearance(root.transform);

            List<Transform> left = new List<Transform>();
            List<Transform> right = new List<Transform>();
            SplitWheels(root.transform, left, right);

            List<Transform> rollers = new List<Transform>();
            FindFreeRollers(baseLink, left, right, rollers);

            int repaired = EnsureWheelColliders(left, right);
            int resurfaced = ApplyWheelMaterial(left, right, rollers);
            int cleared = LiftGroundSweepingColliders(left, right, rollers);
            int bodied = ApplyBodyMaterial(root.transform, left, right, rollers);
            Transform plugins = EnsureChild(root.transform, PluginNodeName);
            Transform laser = EnsureLaserLink(root.transform, baseLink, profile);

            var robot = EnsureComponent<Robot>(root);
            SetReference(robot, "baseLink", baseLink.gameObject);
            SetBool(robot, "keepParentAtOrigin", false);

            EnsureComponent<OdometryPublisher>(root);
            SetString(robot.GetComponent<OdometryPublisher>(), "childFrameId", baseLink.name);

            EnsureComponent<RobotInputController>(root);

            var initialization = EnsureComponent<ArticulationBodyInitialization>(plugins.gameObject);
            SetReference(initialization, "robotRoot", root);

            string chassis;
            if (left.Count > 0 && right.Count > 0)
            {
                var wheelController = EnsureComponent<ArticulationWheelController>(plugins.gameObject);
                WireWheelController(wheelController, baseLink, left, right, rollers);
                chassis = $"{left.Count}+{right.Count} wheel(s) on a track of " +
                          $"{wheelController.wheelTrackLength:0.###} m, wheels of {wheelController.wheelRadius:0.####} m" +
                          (rollers.Count > 0 ? $", {rollers.Count} free roller(s)" : "");
            }
            else
            {
                var kinematic = EnsureComponent<KinematicBaseDrive>(plugins.gameObject);
                SetReference(kinematic, "_baseLink", baseLink.GetComponent<ArticulationBody>());
                chassis = "no wheel in the model, so a kinematic base moves it";
            }

            EnsureComponent<AgentDetector>(laser.gameObject);
            EnsureComponent<AgentDetectorROS>(laser.gameObject);
            EnsureComponent<RaycastLaserScanner>(laser.gameObject);
            EnsureComponent<LaserScanPublisher>(laser.gameObject);

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);

            return $"{profile.Id}: base '{baseLink.name}', lidar '{laser.name}', {chassis}" +
                   (repaired > 0 ? $", {repaired} wheel collider(s) rebuilt" : "") +
                   (resurfaced > 0 ? $", {resurfaced} driven wheel(s) given the scrubbing contact" : "") +
                   (cleared > 0 ? $", {cleared} body collider(s) lifted {GroundClearance * 1000f:0} mm off the floor" : "") +
                   (bodied > 0 ? $", {bodied} body collider(s) given the real contact" : "") +
                   (strays > 0 ? $", {strays} stray articulation(s) made inert" : "");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// The transform that carries the root articulation: the topmost body of the chain. It is the one that
    /// can be teleported, which is what placing a robot is.
    ///
    /// A model may declare more than one body at the top - Kuri ships a lone gyro body next to its chassis -
    /// so the one that carries the rest of the robot is the base, and the stub is left where the model put it.
    /// </summary>
    private static Transform FindBaseLink(Transform root)
    {
        ArticulationBody best = null;
        int bestReach = -1;

        foreach (ArticulationBody body in root.GetComponentsInChildren<ArticulationBody>(true))
        {
            bool nested = false;
            for (Transform parent = body.transform.parent; parent != null; parent = parent.parent)
            {
                if (parent.GetComponent<ArticulationBody>() != null)
                {
                    nested = true;
                    break;
                }
            }

            if (nested)
                continue;

            int reach = body.GetComponentsInChildren<ArticulationBody>(true).Length;
            if (best == null || reach > bestReach || (reach == bestReach && body.mass > best.mass))
            {
                best = body;
                bestReach = reach;
            }
        }

        return best != null ? best.transform : null;
    }

    /// <summary>
    /// Stops the bodies at the top of the model that are not the robot from falling out of the world.
    ///
    /// A model can declare more than one body with nothing above it: Kuri ships a lone gyro frame beside
    /// its chassis, both hanging from a footprint that carries no physics at all. Each of those is a
    /// separate articulation, and a separate articulation is a body nothing holds: it ignores the chassis
    /// it belongs to and falls out of the world on its own - measured at 28 000 metres below the scene by
    /// the time anybody looked.
    ///
    /// The component cannot simply be removed: Unity refuses to destroy an ArticulationBody, on a prefab
    /// as much as at runtime - the attempt is made and counted, and the body is still there afterwards. It
    /// is made immovable instead, and left without gravity, which is the state a stub frame belongs in:
    /// it stays exactly where the model put it, it holds nothing up, and it costs the solver nothing.
    /// </summary>
    private static int NeutraliseStrayArticulations(Transform root, Transform baseLink)
    {
        var strays = new List<ArticulationBody>();
        foreach (ArticulationBody body in root.GetComponentsInChildren<ArticulationBody>(true))
        {
            if (body.transform == baseLink)
                continue;

            bool nested = false;
            for (Transform parent = body.transform.parent; parent != null; parent = parent.parent)
            {
                if (parent.GetComponent<ArticulationBody>() != null)
                {
                    nested = true;
                    break;
                }
            }

            if (!nested)
                strays.Add(body);
        }

        foreach (ArticulationBody stray in strays)
        {
            stray.immovable = true;
            stray.useGravity = false;
        }

        return strays.Count;
    }

    /// <summary>
    /// The driven joints of the model, split by the side they sit on. A name is what says "this is a wheel"
    /// - the importer keeps the link names of the URDF - and the sign of the lateral offset is what says
    /// which side.
    /// </summary>
    private static void SplitWheels(Transform root, List<Transform> left, List<Transform> right)
    {
        foreach (ArticulationBody body in root.GetComponentsInChildren<ArticulationBody>(true))
        {
            if (body.jointType == ArticulationJointType.FixedJoint)
                continue;

            Transform link = body.transform;
            if (!IsWheel(link.name))
                continue;

            float lateral = root.InverseTransformPoint(link.position).x;
            if (lateral < 0f)
                left.Add(link);
            else
                right.Add(link);
        }
    }

    private static bool IsWheel(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.Contains("wheel") || lower.StartsWith("swd_");
    }

    /// <summary>
    /// The joints of the model that roll but are driven by nothing: the casters the chassis leans on.
    ///
    /// They are not wheels - nothing commands them - but they are not arms either, and the articulation
    /// initialiser would otherwise brake them with the joint friction of one. A braked caster is a skid:
    /// measured on the Kuri, whose two casters came out at a hundred, they cost a third of the commanded
    /// turn as soon as the drive became an honest force. The name is what tells them apart, the same way
    /// it does for the wheels, because the importer keeps the link names of the URDF.
    /// </summary>
    private static void FindFreeRollers(
        Transform baseLink,
        List<Transform> left,
        List<Transform> right,
        List<Transform> rollers)
    {
        foreach (ArticulationBody body in baseLink.GetComponentsInChildren<ArticulationBody>(true))
        {
            if (body.jointType == ArticulationJointType.FixedJoint)
                continue;

            Transform link = body.transform;
            if (IsWheel(link.name))
                continue;
            if (left.Contains(link) || right.Contains(link))
                continue;

            string lower = link.name.ToLowerInvariant();
            if (lower.Contains("caster") || lower.Contains("ball") || lower.Contains("roller"))
                rollers.Add(link);
        }
    }

    private static void WireWheelController(
        ArticulationWheelController controller,
        Transform baseLink,
        List<Transform> left,
        List<Transform> right,
        List<Transform> rollers)
    {
        bool added = controller.leftWheels == null || controller.leftWheels.Count == 0;

        controller.leftWheel = left[0].GetComponent<ArticulationBody>();
        controller.rightWheel = right[0].GetComponent<ArticulationBody>();

        controller.leftWheels = new List<ArticulationBody>(left.Count);
        foreach (Transform wheel in left)
            controller.leftWheels.Add(wheel.GetComponent<ArticulationBody>());
        controller.rightWheels = new List<ArticulationBody>(right.Count);
        foreach (Transform wheel in right)
            controller.rightWheels.Add(wheel.GetComponent<ArticulationBody>());

        controller.freeRollers = new List<ArticulationBody>(rollers.Count);
        foreach (Transform roller in rollers)
            controller.freeRollers.Add(roller.GetComponent<ArticulationBody>());

        // Every wheeled robot the tool wires is driven from its base rather than through its wheels, and
        // that is a measurement and not a preference: leaving the motion to the wheels was tried on a
        // two-wheel differential as well as on the Jackal's four-wheel skid steer, and both under-turn -
        // 16 to 47 percent of the commanded rate - because a chassis turns by scrubbing a contact that
        // PhysX gives a single friction coefficient for both directions. See ArticulationWheelController
        // for what was tried and what it gave.
        controller.driveModel = ArticulationWheelController.DriveModel.SkidSteerBase;
        controller.rollingWheelForceLimit = RollingWheelForceLimit;

        // Geometry is only written on a chassis that had none: the base robot was tuned by hand on a bench,
        // and re-deriving its track from the meshes would silently retune a robot that already drives well.
        if (!added && controller.wheelTrackLength > 0.001f && controller.wheelRadius > 0.001f)
            return;

        Vector3 leftCentre = AveragePosition(baseLink, left);
        Vector3 rightCentre = AveragePosition(baseLink, right);
        controller.wheelTrackLength = Mathf.Abs(rightCentre.x - leftCentre.x);

        float radius = 0f;
        foreach (Transform wheel in left)
            radius = Mathf.Max(radius, MeasureWheelRadius(wheel));
        foreach (Transform wheel in right)
            radius = Mathf.Max(radius, MeasureWheelRadius(wheel));
        controller.wheelRadius = radius > 0.005f ? radius : 0.05f;
    }

    private static Vector3 AveragePosition(Transform reference, List<Transform> wheels)
    {
        Vector3 sum = Vector3.zero;
        foreach (Transform wheel in wheels)
            sum += reference.InverseTransformPoint(wheel.position);
        return wheels.Count > 0 ? sum / wheels.Count : sum;
    }

    /// <summary>Radius of a wheel, read from the picture of it and then, if the picture has no usable
    /// bounds, from the shape the physics sees.</summary>
    private static float MeasureWheelRadius(Transform wheel)
    {
        bool found = false;
        Bounds bounds = new Bounds();
        foreach (Renderer renderer in wheel.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled)
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (found && bounds.size.y > 0.005f)
            return bounds.size.y * 0.5f;

        foreach (Collider collider in wheel.GetComponentsInChildren<Collider>(true))
        {
            if (collider is SphereCollider sphere)
            {
                float scale = Mathf.Max(Mathf.Abs(sphere.transform.lossyScale.x),
                    Mathf.Abs(sphere.transform.lossyScale.y), Mathf.Abs(sphere.transform.lossyScale.z));
                return sphere.radius * scale;
            }

            if (collider is MeshCollider mesh && mesh.sharedMesh != null && mesh.sharedMesh.vertexCount > 0)
            {
                Vector3 size = mesh.sharedMesh.bounds.size;
                float scale = Mathf.Max(Mathf.Abs(mesh.transform.lossyScale.x),
                    Mathf.Abs(mesh.transform.lossyScale.y), Mathf.Abs(mesh.transform.lossyScale.z));
                return Mathf.Max(size.y, size.z) * 0.5f * scale;
            }
        }

        return 0f;
    }

    /// <summary>
    /// Gives a wheel that the importer left without a shape something to roll on.
    ///
    /// Kuri is the case: its wheels carry a MeshCollider whose mesh never came across, so the collider
    /// covers nothing and the robot drags its body on the floor instead of rolling. The base robot solves
    /// the same problem with a sphere of the radius of the wheel, which is what is done here.
    /// </summary>
    private static int EnsureWheelColliders(List<Transform> left, List<Transform> right)
    {
        int repaired = 0;
        foreach (Transform wheel in All(left, right))
        {
            if (HasUsableCollider(wheel))
                continue;

            float radius = MeasureWheelRadius(wheel);
            if (radius <= 0.005f)
                continue;

            var sphere = wheel.gameObject.AddComponent<SphereCollider>();
            sphere.radius = radius;
            sphere.center = Vector3.zero;
            repaired++;
        }

        return repaired;
    }

    private static IEnumerable<Transform> All(List<Transform> left, List<Transform> right)
    {
        foreach (Transform wheel in left)
            yield return wheel;
        foreach (Transform wheel in right)
            yield return wheel;
    }

    private static bool HasUsableCollider(Transform wheel)
    {
        foreach (Collider collider in wheel.GetComponentsInChildren<Collider>(true))
        {
            if (collider is MeshCollider mesh)
            {
                if (mesh.sharedMesh != null && mesh.sharedMesh.vertexCount > 0)
                    return true;
                continue;
            }

            if (collider != null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gives everything that rolls the slippery contact its scrubbing needs.
    ///
    /// A wheel that grips as hard as the floor cannot be dragged sideways into a turn, and a chassis that
    /// cannot scrub its wheels does not turn. The free casters are in the same position for the opposite
    /// reason: one is meant to swivel and follow, and a sim that gives its contact one friction coefficient
    /// for every direction makes it a skid instead. Both were measured on the Kuri - slippery wheels with
    /// gripping casters gave 85 percent of the commanded turn, slippery casters as well gave 97 - which is
    /// why the material is put on every rolling link and not on the driven ones alone.
    /// </summary>
    private static int ApplyWheelMaterial(List<Transform> left, List<Transform> right, List<Transform> rollers)
    {
        PhysicsMaterial wheelMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(WheelMaterialPath);
        if (wheelMaterial == null)
        {
            Debug.LogWarning($"[RobotPrefabs] No wheel material at {WheelMaterialPath}: the driven wheels " +
                             "keep whatever contact they came with, and a chassis may under-turn.");
            return 0;
        }

        int resurfaced = 0;
        var rolling = new List<Transform>(Rollers(left, right, rollers));

        foreach (Transform wheel in rolling)
        {
            bool touched = false;
            foreach (Collider collider in wheel.GetComponentsInChildren<Collider>(true))
            {
                if (collider.sharedMaterial == wheelMaterial)
                    continue;

                collider.sharedMaterial = wheelMaterial;
                touched = true;
            }

            if (touched)
                resurfaced++;
        }

        return resurfaced;
    }

    /// <summary>Every link of the chassis that rolls on the floor: the driven wheels and the free casters.</summary>
    private static IEnumerable<Transform> Rollers(
        List<Transform> left,
        List<Transform> right,
        List<Transform> rollers)
    {
        var seen = new HashSet<Transform>();
        foreach (Transform wheel in All(left, right))
        {
            if (wheel != null && seen.Add(wheel))
                yield return wheel;
        }

        foreach (Transform roller in rollers)
        {
            if (roller != null && seen.Add(roller))
                yield return roller;
        }
    }

    /// <summary>
    /// Takes the body of a robot off the floor, and tells how many colliders it had to raise.
    ///
    /// A model imported from a URDF does not always respect the ground its wheels define. The Freight comes
    /// out with a chassis collision whose bottom sits one millimetre above the floor, the Bibus with a base
    /// plate seventeen millimetres *below* its wheels: both are then carried by their body instead of by
    /// their wheels, and a body dragged on the floor eats the turn - measured at 3 percent of the commanded
    /// turn on the Freight before anything was done about it.
    ///
    /// The collider is not moved out of the way, because that would take away the contact the robot has
    /// with everything at that height. Its lowest face is raised to <see cref="GroundClearance"/> above the
    /// wheels, its top and its footprint kept, and a shape too thin to survive that is left alone and
    /// reported. The wheels carry the robot, which is what a robot's wheels are for.
    /// </summary>
    private static int LiftGroundSweepingColliders(
        List<Transform> left,
        List<Transform> right,
        List<Transform> rollers)
    {
        var rolling = new List<Transform>(Rollers(left, right, rollers));
        if (rolling.Count == 0)
            return 0;

        float floor = float.PositiveInfinity;
        foreach (Transform wheel in rolling)
        {
            foreach (Collider collider in wheel.GetComponentsInChildren<Collider>(true))
                floor = Mathf.Min(floor, collider.bounds.min.y);
        }

        if (float.IsInfinity(floor))
            return 0;

        // A millimetre of tolerance: a shape authored to sit exactly on the floor comes back from the bounds
        // a hair above or below it, and which side it lands on must not decide whether the robot turns.
        float limit = floor + 0.002f;
        float bottom = floor + GroundClearance;
        int lifted = 0;
        foreach (Transform candidate in rolling[0].root.GetComponentsInChildren<Transform>(true))
        {
            if (rolling.Contains(candidate))
                continue;

            foreach (Collider collider in candidate.GetComponentsInChildren<Collider>(true))
            {
                // The colliders are collected with the children of each transform, so the colliders of a
                // wheel are also reached from the mount it hangs from. Those roll, and must not be lifted.
                if (OnARollingLink(collider, rolling))
                    continue;

                if (collider.bounds.min.y > limit)
                    continue;

                if (Lift(collider, bottom))
                    lifted++;
                else if (collider.enabled)
                    Debug.LogWarning(
                        $"[RobotPrefabs] {candidate.name}/{collider.GetType().Name} reaches the floor " +
                        $"(bottom {collider.bounds.min.y:0.####}, wheels at {floor:0.####}) and could not be " +
                        $"lifted: its frame points up {collider.transform.up}, so it will drag. It is left " +
                        "as the model authored it rather than moved on a guess.");
            }
        }

        return lifted;
    }

    /// <summary>True when the collider hangs, directly or not, from one of the links that roll on the floor.</summary>
    private static bool OnARollingLink(Collider collider, List<Transform> rolling)
    {
        for (Transform frame = collider.transform; frame != null; frame = frame.parent)
        {
            if (rolling.Contains(frame))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Raises the lowest face of one collider to <paramref name="bottom"/>, in the space of the transform
    /// that carries it. False when the shape cannot be raised without guessing - a frame that is not
    /// upright, or a slab thin enough that nothing would be left of it - and the collider is then left
    /// exactly as the model authored it.
    /// </summary>
    private static bool Lift(Collider collider, float bottom)
    {
        // A collider the model or a previous run already took out of the physics is left alone: it covers
        // nothing, and reading its bounds anyway is what made the tool add a collider on every pass.
        if (!collider.enabled)
            return false;

        Transform frame = collider.transform;
        Bounds bounds = collider.bounds;
        float rise = bottom - bounds.min.y;
        if (rise <= 0f)
            return false;

        // A slab thin enough that trimming it would leave nothing is moved bodily instead of trimmed: a base
        // plate belongs above the ground, not inside it, and a plate left with a centimetre of itself is not
        // the shape the model described either.
        bool trim = bounds.size.y - rise > 0.01f;

        // The lift is applied along the frame's own up, so the frame has to be upright: on a tilted one the
        // raise would have to be split across two axes and the shape would no longer match the mesh.
        Vector3 up = frame.up;
        if (up.y <= 0f || Vector3.Dot(up, Vector3.up) < 0.9f)
            return false;

        float scale = Mathf.Abs(frame.lossyScale.y);
        float local = rise / (scale > 1e-6f ? scale : 1f);

        if (collider is BoxCollider box)
        {
            box.center += Vector3.up * (trim ? local * 0.5f : local);
            if (trim)
                box.size = new Vector3(box.size.x, box.size.y - local, box.size.z);
            return true;
        }

        if (collider is MeshCollider mesh)
        {
            if (mesh.sharedMesh == null || mesh.sharedMesh.vertexCount == 0)
                return false;

            Bounds shape = mesh.sharedMesh.bounds;
            var raised = new GameObject(GroundClearanceNodeName);
            raised.transform.SetParent(frame, false);
            raised.transform.localPosition = Vector3.zero;
            raised.transform.localRotation = Quaternion.identity;
            raised.transform.localScale = Vector3.one;

            var replacement = raised.AddComponent<BoxCollider>();
            // The replacement lives in a child of the frame, so the mesh bounds - which are expressed in the
            // frame - can be copied as they are, and the frame's own rotation and scale are the child's.
            replacement.center = new Vector3(
                shape.center.x, shape.center.y + (trim ? local * 0.5f : local), shape.center.z);
            replacement.size = trim
                ? new Vector3(shape.size.x, shape.size.y - local, shape.size.z)
                : shape.size;
            replacement.sharedMaterial = collider.sharedMaterial;
            mesh.enabled = false;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes every collider a previous run of the tool put under the floor, so that the prefab is rebuilt
    /// rather than added to. A raised shape is a child named after it, and it goes; the collider the model
    /// shipped is re-enabled, because a mesh the tool disabled has to come back for the lift to be redone.
    /// </summary>
    private static void ClearGroundClearance(Transform root)
    {
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name != GroundClearanceNodeName)
                continue;

            Object.DestroyImmediate(candidate.gameObject);
        }

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider is MeshCollider && !collider.enabled)
                collider.enabled = true;
        }
    }

    /// <summary>
    /// Gives the body of the robot - everything that is not a rolling link - the contact it meets the world
    /// with. This is the pass that makes a collision a collision: the robot that used to slide along a
    /// crowd, because every surface of it that could touch the floor had been made slippery, now presents a
    /// normal floor contact everywhere above its wheels.
    /// </summary>
    private static int ApplyBodyMaterial(
        Transform root,
        List<Transform> left,
        List<Transform> right,
        List<Transform> rollers)
    {
        // A model with no wheel is not driven on the floor and is not part of this question: the humanoid is
        // carried kinematically, and walking its eight hundred and forty colliders one by one to give them a
        // contact that nothing reads would only bury the report.
        if (left.Count == 0 && right.Count == 0)
            return 0;

        PhysicsMaterial chassisMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(ChassisMaterialPath);
        if (chassisMaterial == null)
        {
            Debug.LogWarning($"[RobotPrefabs] No chassis material at {ChassisMaterialPath}: the body keeps " +
                             "the contact it came with.");
            return 0;
        }

        var rolling = new List<Transform>(Rollers(left, right, rollers));
        int resurfaced = 0;
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            bool rolls = false;
            for (Transform frame = collider.transform; frame != null; frame = frame.parent)
            {
                if (!rolling.Contains(frame))
                    continue;

                rolls = true;
                break;
            }

            if (rolls || collider.sharedMaterial == chassisMaterial)
                continue;

            collider.sharedMaterial = chassisMaterial;
            resurfaced++;
        }

        return resurfaced;
    }

    /// <summary>
    /// The link the lidar rides on: the one the model already names after a sensor, or a fresh link on the
    /// base at the height the profile gives, for a model that ships none.
    /// </summary>
    private static Transform EnsureLaserLink(Transform root, Transform baseLink, RobotProfile profile)
    {
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate == root || candidate.GetComponent<ArticulationBody>() == null)
                continue;

            string lower = candidate.name.ToLowerInvariant();
            if (lower.Contains("lidar") || lower.Contains("laser") || lower.Contains("scan") || lower.Contains("hokuyo"))
                return candidate;
        }

        // A laser link this tool added on a previous run and that ended up under the wrong base - which is
        // what happens when the base itself is detected differently - is removed rather than left behind as a
        // second sensor on a robot that only has room for one. A link of the model always carries its UrdfLink.
        foreach (Transform stray in root.GetComponentsInChildren<Transform>(true))
        {
            if (stray.name != LaserNodeName || stray.parent == baseLink ||
                stray.GetComponent<Unity.Robotics.UrdfImporter.UrdfLink>() != null)
                continue;

            Object.DestroyImmediate(stray.gameObject);
        }

        Transform laser = EnsureChild(baseLink, LaserNodeName);
        laser.localPosition = new Vector3(0f, profile.LidarHeight, 0f);
        laser.localRotation = Quaternion.identity;
        laser.localScale = Vector3.one;
        return laser;
    }

    // ==========================================
    //          SMALL HELPERS
    // ==========================================

    private static IEnumerable<string> PrefabPaths()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { RobotFolderPath });
        var paths = new List<string>();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            // Only the models at the top of the folder: the meshes and the sub-assets the importer wrote
            // under URDF/ are parts of a body, not bodies of their own.
            if (System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') == RobotFolderPath)
                paths.Add(path);
        }

        paths.Sort(System.StringComparer.OrdinalIgnoreCase);
        return paths;
    }

    private static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T existing = target.GetComponent<T>();
        return existing != null ? existing : target.AddComponent<T>();
    }

    private static Transform EnsureChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            return existing;

        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;
        return child.transform;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name))
            AssetDatabase.CreateFolder(parent, name);
    }

    private static void SetReference(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning($"[RobotPrefabs] {target.GetType().Name} has no field '{field}'.");
            return;
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBool(Object target, string field, bool value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
            return;
        property.boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetString(Object target, string field, string value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
            return;
        property.stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
