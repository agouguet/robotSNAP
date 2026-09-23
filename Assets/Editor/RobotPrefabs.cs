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

    /// <summary>Name of the ball caster the tool adds to a robot whose own contacts cannot hold it up.</summary>
    private const string SupportCasterNodeName = "SupportCaster";

    /// <summary>
    /// Torque every driven wheel of a base-driven chassis is left with, in N.m.
    ///
    /// It is written on the prefab rather than left to the default of the component so that what a robot
    /// was wired with is readable from the robot. The figure is three newton-metres, and it is the figure
    /// that lets the wheel be seen turning on every robot: measured on this project, a Jackal and a Bibus
    /// left at the fifth of a newton-metre this used to be never turned their wheels at all - they were
    /// dragged along the floor at zero revolutions - and the four roll at the speed their motion implies at
    /// three. None of it reaches the chassis: a driven wheel meets the world through the skid contact this
    /// tool gives it, which passes a hundredth of what the floor does, and the commanded turn answers at 99
    /// percent whether the wheels are left at three newton-metres or at a thousand.
    /// </summary>
    private const float RollingWheelForceLimit = 3f;

    /// <summary>
    /// How much the tyres are allowed to decide, written on every robot. One is the honest figure - the
    /// drive may never ask for more than its tyres grip - and it is the one number to turn down when a
    /// robot has to feel more responsive than physical: see
    /// <see cref="ArticulationWheelController.controlRealism"/>.
    /// </summary>
    private const float ControlRealism = 1f;

    /// <summary>
    /// How fast a robot answers its command, in m/s^2 and rad/s^2. Every robot is given the same figures
    /// on purpose: a user asking a Kuri and a Freight for the same turn should see the same turn, and what
    /// makes the two robots different is what they are - their mass and their geometry - not their
    /// controller. Both are below what the tyres of the lightest robot here can pass, so this is the ramp
    /// a robot takes when nothing is in its way.
    /// </summary>
    private const float DriveLinearAcceleration = 5f;
    private const float DriveAngularAcceleration = 10f;

    /// <summary>Grip written on every robot. It is a tyre figure, not a floor figure.</summary>
    private const float DriveTractionGrip = 0.8f;

    /// <summary>How fast every drive makes up the effort a resistance takes from it. See the component.</summary>
    private const float DriveIntegralGain = 30f;

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
            int resurfaced = ApplyWheelMaterial(root.transform, left, right, rollers);
            int rollingContacts = CountRollingContacts(root.transform, left, right, rollers);
            int cleared = rollingContacts >= 3
                ? LiftGroundSweepingColliders(root.transform, left, right, rollers)
                : 0;
            int bodied = ApplyBodyMaterial(root.transform, left, right, rollers);
            string stance = rollingContacts >= 3
                ? ""
                : $", only {rollingContacts} rolling contact(s), so the base stays on the floor as the model " +
                  "designed it and is given the contact that slides";
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
                   stance +
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

        // The answer to a command is the same on every robot; see the constants above. The effort a robot
        // may apply is not written here: it is derived from that robot's own mass and grip, so a Freight
        // still pushes what a Freight weighs and a Kuri what a Kuri weighs.
        controller.controlRealism = ControlRealism;
        controller.maxLinearAcceleration = DriveLinearAcceleration;
        controller.maxAngularAcceleration = DriveAngularAcceleration;
        controller.tractionGrip = DriveTractionGrip;
        controller.maxDriveForce = 0f;
        controller.tractionLever = 0f;
        controller.driveIntegralGain = DriveIntegralGain;

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
    /// <summary>
    /// Gives every driven wheel a sphere to roll on, which is the one shape the physics rolls smoothly.
    ///
    /// A wheel rolls on the shape it is given, and the meshes a URDF brings do not roll: the Jackal's and
    /// the Bibus's wheels are faceted cylinders, and a chassis carried by those is hammered by one facet
    /// edge per segment of every revolution. Measured on the flat floor of the default scenario with the
    /// robot driven straight at a metre per second, the pitch and the roll of those two robots oscillate at
    /// 0.08 and 0.24 rad/s while the Kuri's and the Freight's - whose wheels the importer left without a
    /// collider and this tool therefore already gave a sphere - sit at 0.0000. It is also why the mesh is
    /// switched off rather than kept beside the sphere: two shapes on one wheel is a wheel that collides
    /// with the floor twice per step.
    ///
    /// The sphere takes the radius of the wheel itself, from its own mesh or its own renderer, so the robot
    /// stands at the height its model says it stands at.
    /// </summary>
    private static int EnsureWheelColliders(List<Transform> left, List<Transform> right)
    {
        int repaired = 0;
        foreach (Transform wheel in All(left, right))
        {
            if (RollsOnASphere(wheel))
                continue;

            float radius = MeasureWheelRadius(wheel);
            if (radius <= 0.005f)
                continue;

            foreach (Collider collider in wheel.GetComponentsInChildren<Collider>(true))
            {
                if (collider is MeshCollider)
                    collider.enabled = false;
            }

            var sphere = wheel.gameObject.AddComponent<SphereCollider>();
            sphere.radius = radius;
            sphere.center = Vector3.zero;
            repaired++;
        }

        return repaired;
    }

    /// <summary>Whether a wheel already rolls on a sphere of its own.</summary>
    private static bool RollsOnASphere(Transform wheel)
    {
        foreach (Collider collider in wheel.GetComponentsInChildren<Collider>(true))
        {
            if (collider is SphereCollider sphere && sphere.enabled)
                return true;
        }

        return false;
    }

    private static IEnumerable<Transform> All(List<Transform> left, List<Transform> right)
    {
        foreach (Transform wheel in left)
            yield return wheel;
        foreach (Transform wheel in right)
            yield return wheel;
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
    private static int ApplyWheelMaterial(
        Transform root,
        List<Transform> left,
        List<Transform> right,
        List<Transform> rollers)
    {
        PhysicsMaterial wheelMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(WheelMaterialPath);
        if (wheelMaterial == null)
        {
            Debug.LogWarning($"[RobotPrefabs] No wheel material at {WheelMaterialPath}: the driven wheels " +
                             "keep whatever contact they came with, and a chassis may under-turn.");
            return 0;
        }

        int resurfaced = 0;
        List<Transform> rolling = RollingLinks(left, right, rollers, root);

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
    /// Everything the model means to slide on the floor: the driven wheels, the casters it declares as
    /// joints, and the ones it built as fixed links - a ball caster is a fixed sphere in more than one
    /// model here, and it still belongs on the floor, sliding.
    ///
    /// The distinction decides everything that follows: a link on this list is given the contact that
    /// slides and is never lifted, and every other link is taken off the floor.
    /// </summary>
    private static List<Transform> RollingLinks(
        List<Transform> left,
        List<Transform> right,
        List<Transform> rollers,
        Transform root)
    {
        var rolling = new List<Transform>(Rollers(left, right, rollers));
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (!rolling.Contains(candidate) && IsRollingName(candidate.name))
                rolling.Add(candidate);
        }

        return rolling;
    }

    private static bool IsRollingName(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.Contains("caster") || lower.Contains("ball") || lower.Contains("roller");
    }

    /// <summary>
    /// True when a collider is one the physics can actually use. A mesh that never came across the importer
    /// leaves a collider that covers nothing, and counting one of those as a contact is how a robot with
    /// nothing to stand on is declared able to stand.
    /// </summary>
    private static bool Covers(Collider collider)
    {
        switch (collider)
        {
            case MeshCollider mesh:
                return mesh.sharedMesh != null && mesh.sharedMesh.vertexCount > 0;
            case SphereCollider sphere:
                return sphere.radius > 0.0001f;
            case CapsuleCollider capsule:
                return capsule.radius > 0.0001f && capsule.height > 0.0001f;
            case BoxCollider box:
                return box.size.x > 0.0001f && box.size.y > 0.0001f && box.size.z > 0.0001f;
            default:
                return collider != null;
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
        Transform root,
        List<Transform> left,
        List<Transform> right,
        List<Transform> rollers)
    {
        List<Transform> rolling = RollingLinks(left, right, rollers, root);
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

    /// <summary>
    /// How many places the robot already has to stand on: the covering colliders of its wheels and casters
    /// that reach the floor.
    ///
    /// Three is the figure that decides whether the body may be lifted. Two contacts make a line, and a
    /// chassis resting on a line is a seesaw: the Freight has two wheels and no caster at all, and lifting
    /// its base left it leaning three degrees back, unable to turn one of the two ways, because one side of
    /// the base was lifting off the floor while the other pressed into it. Its base reaching the floor is
    /// not a modelling accident to be corrected - it is the third leg the model gave it, and the contact it
    /// needs is the one that slides, not a lift.
    /// </summary>
    private static int CountRollingContacts(
        Transform root,
        List<Transform> left,
        List<Transform> right,
        List<Transform> rollers)
    {
        List<Transform> rolling = RollingLinks(left, right, rollers, root);
        if (rolling.Count == 0)
            return 0;

        float floor = float.PositiveInfinity;
        foreach (Transform wheel in rolling)
        {
            foreach (Collider collider in wheel.GetComponentsInChildren<Collider>(true))
            {
                if (collider.enabled && Covers(collider))
                    floor = Mathf.Min(floor, collider.bounds.min.y);
            }
        }

        if (float.IsInfinity(floor))
            return 0;

        float limit = floor + 0.01f;
        int contacts = 0;
        foreach (Transform wheel in rolling)
        {
            foreach (Collider collider in wheel.GetComponentsInChildren<Collider>(true))
            {
                if (collider.enabled && Covers(collider) && collider.bounds.min.y <= limit)
                    contacts++;
            }
        }

        return contacts;
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
        float rise = bottom - collider.bounds.min.y;
        if (rise <= 0f)
            return false;

        // The lift is applied along the frame's own up, so the frame has to be upright: on a tilted one the
        // raise would have to be split across two axes and the shape would no longer match the mesh.
        Vector3 up = frame.up;
        if (up.y <= 0f || Vector3.Dot(up, Vector3.up) < 0.9f)
            return false;

        // A mesh that never came across the importer has nothing to raise, and the shape it was meant to be
        // is unknown: it is left alone and the report says so.
        if (collider is MeshCollider mesh && (mesh.sharedMesh == null || mesh.sharedMesh.vertexCount == 0))
            return false;

        float scale = Mathf.Abs(frame.lossyScale.y);
        float local = rise / (scale > 1e-6f ? scale : 1f);

        // The shape is copied rather than replaced by a box around it. A box was tried first, and it is what
        // a shape must not become: the Freight's base is a cylinder, and the box that stood in for it put a
        // square corner 5 mm below the wheels, so the robot leaned three degrees back and would only turn
        // one way - the corner lifted on one side of a turn and dug in on the other.
        var raised = new GameObject(GroundClearanceNodeName);
        raised.transform.SetParent(frame, false);
        raised.transform.localPosition = Vector3.up * local;
        raised.transform.localRotation = Quaternion.identity;
        raised.transform.localScale = Vector3.one;

        if (!CopyShape(collider, raised))
        {
            Object.DestroyImmediate(raised);
            return false;
        }

        collider.enabled = false;
        return true;
    }

    /// <summary>Copies a collider onto another object, keeping the shape the model described.</summary>
    private static bool CopyShape(Collider source, GameObject target)
    {
        switch (source)
        {
            case MeshCollider mesh:
            {
                var copy = target.AddComponent<MeshCollider>();
                copy.sharedMesh = mesh.sharedMesh;
                copy.convex = mesh.convex;
                copy.sharedMaterial = mesh.sharedMaterial;
                return true;
            }

            case BoxCollider box:
            {
                var copy = target.AddComponent<BoxCollider>();
                copy.center = box.center;
                copy.size = box.size;
                copy.sharedMaterial = box.sharedMaterial;
                return true;
            }

            case SphereCollider sphere:
            {
                var copy = target.AddComponent<SphereCollider>();
                copy.center = sphere.center;
                copy.radius = sphere.radius;
                copy.sharedMaterial = sphere.sharedMaterial;
                return true;
            }

            case CapsuleCollider capsule:
            {
                var copy = target.AddComponent<CapsuleCollider>();
                copy.center = capsule.center;
                copy.radius = capsule.radius;
                copy.height = capsule.height;
                copy.direction = capsule.direction;
                copy.sharedMaterial = capsule.sharedMaterial;
                return true;
            }

            default:
                return false;
        }
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
            // The support caster is a shape an earlier version of this tool added to a robot that turned out
            // not to need one; it goes with the rest, so that a prefab always rebuilds to the same state.
            if (candidate.name != GroundClearanceNodeName && candidate.name != SupportCasterNodeName)
                continue;

            Object.DestroyImmediate(candidate.gameObject);
        }

        // Every collider a previous run took out of the physics comes back, whatever its shape: the tool is
        // the only thing that disables a collider here, and the lift has to be redone from the model's own
        // shape rather than from the shape of a previous lift.
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled)
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

        List<Transform> rolling = RollingLinks(left, right, rollers, root);
        PhysicsMaterial rollingMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(WheelMaterialPath);

        // Anything else that reaches the level of the wheels is a skid - a caster the model built as a fixed
        // link, a bumper low enough to touch - and a skid has to slide, which is what the rolling contact
        // is for. This is what the Bibus was missing: its two casters are fixed joints, they sit on the
        // floor, and the body pass gave them the ordinary contact of a chassis, so a robot that should have
        // pivoted on two slippery balls was held by two gripping ones and would sometimes only turn one way.
        float floor = float.PositiveInfinity;
        foreach (Transform wheel in rolling)
        {
            foreach (Collider collider in wheel.GetComponentsInChildren<Collider>(true))
                floor = Mathf.Min(floor, collider.bounds.min.y);
        }

        float limit = floor + 0.01f;
        int resurfaced = 0;
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled)
                continue;
            if (!Covers(collider))
                continue;

            bool rolls = false;
            for (Transform frame = collider.transform; frame != null; frame = frame.parent)
            {
                if (!rolling.Contains(frame))
                    continue;

                rolls = true;
                break;
            }

            if (rolls)
                continue;

            bool onTheFloor = collider.bounds.min.y <= limit;
            PhysicsMaterial wanted = onTheFloor && rollingMaterial != null ? rollingMaterial : chassisMaterial;
            if (collider.sharedMaterial == wanted)
                continue;

            collider.sharedMaterial = wanted;
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
