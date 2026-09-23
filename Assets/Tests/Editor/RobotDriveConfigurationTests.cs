using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RobotSNAP.Agents;
using UnityEditor;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// How a robot type is driven is configuration, not code: it lives in the prefab, on the "Plugins" child
    /// the wiring tool populates with the components of a body. These tests hold that contract for every type
    /// the build knows - one chassis per prefab, wired to real links, and no type left without a case.
    /// </summary>
    public sealed class RobotDriveConfigurationTests
    {
        private const string RobotFolderPath = "Assets/Prefabs/Robots";
        private const string PluginsNodeName = "Plugins";

        /// <summary>One prefab and the robot type it drives.</summary>
        private readonly struct RobotPrefabCase
        {
            public RobotPrefabCase(string typeId, string prefabPath)
            {
                TypeId = typeId;
                PrefabPath = prefabPath;
            }

            public string TypeId { get; }

            public string PrefabPath { get; }
        }

        /// <summary>The prefabs at the top of the robot folder, read in a stable order.</summary>
        private static List<string> RobotPrefabPaths()
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { RobotFolderPath }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Only the bodies at the top of the folder: what the importer wrote under URDF/ are the parts
                // of a model, not models of their own.
                if (Path.GetDirectoryName(path)?.Replace('\\', '/') == RobotFolderPath)
                    paths.Add(path);
            }

            paths.Sort(System.StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        /// <summary>
        /// Every prefab that drives a robot type this build knows, paired with that type. A prefab whose name
        /// is not a known type is a model the project imported but does not drive, and is skipped.
        /// </summary>
        private static List<RobotPrefabCase> DiscoverRobotPrefabs()
        {
            var cases = new List<RobotPrefabCase>();
            foreach (string path in RobotPrefabPaths())
            {
                if (RobotProfiles.TryFind(Path.GetFileNameWithoutExtension(path), out RobotProfile profile))
                    cases.Add(new RobotPrefabCase(profile.Id, path));
            }

            return cases;
        }

        /// <summary>The prefab of one robot type, when the project ships one for it.</summary>
        private static bool TryFindPrefabFor(RobotProfile profile, out string path)
        {
            foreach (RobotPrefabCase robotCase in DiscoverRobotPrefabs())
            {
                if (robotCase.TypeId == profile.Id)
                {
                    path = robotCase.PrefabPath;
                    return true;
                }
            }

            path = null;
            return false;
        }

        private static IEnumerable<TestCaseData> RobotPrefabCases()
        {
            foreach (RobotPrefabCase robotCase in DiscoverRobotPrefabs())
                yield return new TestCaseData(robotCase.TypeId, robotCase.PrefabPath)
                    .SetName("{m}(" + robotCase.TypeId + ")");
        }

        /// <summary>
        /// A type that ships a prefab and is not covered below is a robot nothing checks. A folder the
        /// discovery cannot read at all has to fail here rather than let the parameterized test report success
        /// with no cases at all.
        /// </summary>
        [Test]
        public void EveryKnownRobotTypeWithAPrefab_IsCoveredByACase()
        {
            List<RobotPrefabCase> cases = DiscoverRobotPrefabs();

            Assert.That(cases, Is.Not.Empty,
                $"No robot prefab was found under {RobotFolderPath}, so no chassis would be checked at all.");

            var covered = new HashSet<string>();
            foreach (RobotPrefabCase robotCase in cases)
            {
                Assert.That(covered.Add(robotCase.TypeId), Is.True,
                    $"Two prefabs claim the robot type '{robotCase.TypeId}', so one of them is not what that " +
                    "type drives.");
            }

            int knownTypes = 0;
            foreach (RobotProfile profile in RobotProfiles.All)
            {
                if (!TryFindPrefabFor(profile, out string path))
                    continue;

                knownTypes++;
                Assert.That(covered, Does.Contain(profile.Id),
                    $"The robot type '{profile.Id}' ships {path} but no case covers it.");
            }

            Assert.That(knownTypes, Is.GreaterThan(0),
                $"None of the types of {nameof(RobotProfiles)} has a prefab under {RobotFolderPath}.");
            Assert.That(cases.Count, Is.EqualTo(knownTypes),
                "Every case has to be a robot type this build knows, with a prefab of its own.");
        }

        /// <summary>
        /// The catalogue binds a type to one body, and one body belongs to one type.
        ///
        /// The project carried two Jackals for a while - the model it imported, and a copy driven through its
        /// own tyres - and the duplicate showed up everywhere a type is listed: twice in the type list of the
        /// scenario editor, twice in the robot catalogue, and twice in the course scene. This is that
        /// regression, held for every type rather than for the one that happened to be duplicated.
        /// </summary>
        [Test]
        public void RobotCatalog_OffersEachTypeOnce_AndClaimsNoBodyTwice()
        {
            RobotCatalog catalog = RobotCatalog.Load();
            Assert.That(catalog, Is.Not.Null,
                $"No {nameof(RobotCatalog)} under Resources, so a scenario naming a type would find no body.");

            var types = new HashSet<string>();
            var bodies = new HashSet<GameObject>();

            foreach (RobotCatalog.Entry entry in catalog.Entries)
            {
                Assert.That(entry.Prefab, Is.Not.Null,
                    $"The catalogue entry '{entry.TypeId}' names no prefab.");

                Assert.That(types.Add(entry.TypeId), Is.True,
                    $"The catalogue lists the type '{entry.TypeId}' twice.");

                Assert.That(bodies.Add(entry.Prefab), Is.True,
                    $"The catalogue points two types at {AssetDatabase.GetAssetPath(entry.Prefab)}, so the " +
                    "interface would offer one robot under two names.");
            }
        }

        /// <summary>
        /// The Jackal this build ships is the one its own model describes: the type resolves to the prefab of
        /// that name, and that prefab drives through its tyres rather than through the skid contact the rest
        /// of the fleet is given. The duplicate the project used to carry - a "jackal_real" next to it - is
        /// gone, and this test is where that decision is written down.
        /// </summary>
        [Test]
        public void TheJackal_IsTheOneThatDrivesThroughItsTyres()
        {
            Assert.That(RobotProfiles.TryFind("jackal", out RobotProfile profile), Is.True,
                "The build no longer knows the robot type 'jackal'.");
            Assert.That(profile.Id, Is.EqualTo("jackal"));
            Assert.That(profile.Mass, Is.EqualTo(16.523f).Within(0.001f),
                "The Jackal's profile has to carry the mass its own model adds up to, which is the mass its " +
                "base link is driven with at run time.");

            RobotCatalog catalog = RobotCatalog.Load();
            Assert.That(catalog, Is.Not.Null, $"No {nameof(RobotCatalog)} under Resources.");

            GameObject prefab = catalog.PrefabFor("jackal");
            Assert.That(prefab, Is.Not.Null, "The catalogue resolves no body for the type 'jackal'.");
            Assert.That(AssetDatabase.GetAssetPath(prefab), Is.EqualTo("Assets/Prefabs/Robots/Jackal.prefab"),
                "The type 'jackal' has to be the prefab of that name: the wiring tool finds a type by the " +
                "name of the body it drives.");

            var drive = prefab.GetComponentInChildren<ArticulationWheelController>(true);
            Assert.That(drive, Is.Not.Null, $"{prefab.name} carries no wheel chassis.");
            Assert.That(drive.driveModel, Is.EqualTo(ArticulationWheelController.DriveModel.TyreForces),
                $"{prefab.name} is not driven through its tyres any more, so it is not the robot this type " +
                "was chosen to be.");

            Assert.That(RobotProfiles.TryFind("jackal_real", out RobotProfile _), Is.False,
                "The duplicated 'jackal_real' type is back; one robot is one type.");
        }

        /// <summary>
        /// A robot type drives through exactly one chassis, and that chassis sits on the "Plugins" child of
        /// the prefab - where the wiring tool puts it, and where the prefab is read from at run time.
        /// </summary>
        [TestCaseSource(nameof(RobotPrefabCases))]
        public void RobotPrefab_DrivesThroughTheChassisOnItsPluginsNode(string typeId, string prefabPath)
        {
            Assert.That(RobotProfiles.TryFind(typeId, out RobotProfile profile), Is.True,
                $"'{typeId}' is not a robot type this build knows.");
            Assert.That(profile.Id, Is.EqualTo(typeId),
                $"{prefabPath} was discovered as '{typeId}' but {nameof(RobotProfiles)} resolves it to " +
                $"'{profile.Id}'.");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"No prefab at {prefabPath}.");

            Transform plugins = FindPluginsNode(prefab);
            Assert.That(plugins, Is.Not.Null,
                $"{prefabPath} has no child named '{PluginsNodeName}', where its chassis belongs.");

            var wheelController = plugins.GetComponent<ArticulationWheelController>();
            var kinematic = plugins.GetComponent<KinematicBaseDrive>();
            int onPlugins = (wheelController != null ? 1 : 0) + (kinematic != null ? 1 : 0);

            Assert.That(onPlugins, Is.EqualTo(1),
                $"{prefabPath}: the '{PluginsNodeName}' node must carry exactly one of " +
                $"{nameof(ArticulationWheelController)} or {nameof(KinematicBaseDrive)}, and it carries " +
                $"{onPlugins}.");

            // One chassis per robot, not merely one on the node: a second drive elsewhere in the tree would be
            // commanded as well.
            Assert.That(prefab.GetComponentsInChildren<IRobotDrive>(true).Length, Is.EqualTo(1),
                $"{prefabPath} carries more than one drive component.");

            if (wheelController != null)
                AssertWheelChassisIsWired(prefabPath, wheelController);
            else
                AssertKinematicChassisIsWired(prefabPath, kinematic);
        }

        /// <summary>
        /// The effort a chassis drive asks for is sized on the whole robot, and not on the link its joints
        /// hang from.
        ///
        /// A base link is one body of a chain: the Jackal's chassis_link is 24.4 kg of a 32.3 kg robot, and
        /// the Kuri's base is 7 of 22.6 - and the two of them declare a moment of inertia about the vertical
        /// of 0.39 and 0.037 against the 6.6 and 1.1 that the chassis they carry actually turns with. A drive
        /// that reads the base link turns those two robots with a torque a sixteenth and a thirtieth of what
        /// stopping them needs, which is exactly the robot that keeps spinning after its key is let go while
        /// the Freight and the Bibus - whose base link happens to carry most of them - come to rest at once.
        /// This test is that regression: the drive has to read a chassis heavier than its base link, and one
        /// that resists a turn more than its base link does.
        /// </summary>
        [TestCaseSource(nameof(RobotPrefabCases))]
        public void RobotPrefab_SizesItsDriveOnTheWholeChassis(string typeId, string prefabPath)
        {
            Assert.That(RobotProfiles.TryFind(typeId, out RobotProfile profile), Is.True,
                $"'{typeId}' is not a robot type this build knows.");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"No prefab at {prefabPath}.");

            // A chassis with no wheel is moved by KinematicBaseDrive, which takes the body it is wired to and
            // has no chain to measure; this test is about the drive that has to size a force.
            if (FindPluginsNode(prefab)?.GetComponent<ArticulationWheelController>() == null)
                return;

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                var drive = instance.GetComponentInChildren<ArticulationWheelController>(true);
                Assert.That(drive, Is.Not.Null,
                    $"{prefabPath} carries no {nameof(ArticulationWheelController)} once instantiated.");

                drive.MeasureChassis();

                ArticulationBody baseLink = BaseLinkOf(drive);
                Assert.That(baseLink, Is.Not.Null,
                    $"{prefabPath}: the wheels of {nameof(ArticulationWheelController)} hang from no base.");

                Assert.That(drive.ChassisMass, Is.GreaterThan(baseLink.mass),
                    $"{prefabPath}: the drive moves {drive.ChassisMass:0.###} kg, which is not more than the " +
                    $"{baseLink.mass:0.###} kg of '{baseLink.name}' alone, so it is sizing its effort on the " +
                    "base link instead of on the robot.");

                Assert.That(drive.ChassisYawInertia, Is.GreaterThan(baseLink.inertiaTensor.y),
                    $"{prefabPath}: the drive reads a yaw inertia of {drive.ChassisYawInertia:0.####} kg.m2, " +
                    $"which is not more than the {baseLink.inertiaTensor.y:0.####} of '{baseLink.name}' alone, " +
                    "so it would brake the robot with a fraction of the torque the turn needs.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>The body the wheels of a chassis hang from: the topmost one above the first left wheel.</summary>
        private static ArticulationBody BaseLinkOf(ArticulationWheelController controller)
        {
            ArticulationBody candidate = controller.leftWheel;
            while (candidate != null)
            {
                ArticulationBody above = candidate.transform.parent != null
                    ? candidate.transform.parent.GetComponentInParent<ArticulationBody>()
                    : null;

                if (above == null)
                    return candidate;

                candidate = above;
            }

            return null;
        }

        /// <summary>What a wheeled chassis needs to turn a velocity into wheel speeds.</summary>
        private static void AssertWheelChassisIsWired(string prefabPath, ArticulationWheelController controller)
        {
            string where = $"{prefabPath}: {nameof(ArticulationWheelController)}";

            Assert.That(controller.leftWheel, Is.Not.Null, $"{where} names no left wheel.");
            Assert.That(controller.rightWheel, Is.Not.Null, $"{where} names no right wheel.");
            Assert.That(controller.leftWheels, Is.Not.Null.And.Not.Empty, $"{where} lists no left wheel.");
            Assert.That(controller.rightWheels, Is.Not.Null.And.Not.Empty, $"{where} lists no right wheel.");

            List<ArticulationBody> left = WheelsOf(controller.leftWheel, controller.leftWheels);
            var seen = new HashSet<ArticulationBody>(left);
            foreach (ArticulationBody wheel in WheelsOf(controller.rightWheel, controller.rightWheels))
            {
                Assert.That(seen.Add(wheel), Is.True,
                    $"{where} drives '{wheel.name}' as both a left wheel and a right wheel; a wheel on both " +
                    "sides is told to spin two ways at once.");
            }

            Assert.That(controller.wheelTrackLength, Is.GreaterThan(0.001f),
                $"{where} has a wheel track of {controller.wheelTrackLength}, which no wheel speed can be " +
                "computed from.");
            Assert.That(controller.wheelRadius, Is.GreaterThan(0.001f),
                $"{where} has a wheel radius of {controller.wheelRadius}, which no wheel speed can be " +
                "computed from.");
            Assert.That(controller.driveForceLimit, Is.GreaterThan(0f),
                $"{where} has a drive force limit of {controller.driveForceLimit}, so its wheels would never " +
                "be driven.");
        }

        /// <summary>A chassis with no wheel is moved by its base, so the prefab has to name that base.</summary>
        private static void AssertKinematicChassisIsWired(string prefabPath, KinematicBaseDrive drive)
        {
            string where = $"{prefabPath}: {nameof(KinematicBaseDrive)}";

            var serialized = new SerializedObject(drive);
            SerializedProperty baseLink = serialized.FindProperty("_baseLink");
            Assert.That(baseLink, Is.Not.Null,
                $"{where} has no serialized field '_baseLink' left; this test reads the field the component " +
                "is wired through.");

            UnityEngine.Object target = baseLink.objectReferenceValue;
            Assert.That(target, Is.Not.Null,
                $"{where} names no articulated base, so the robot would be disabled at its first Awake.");
            Assert.That(target as ArticulationBody, Is.Not.Null,
                $"{where} points '_baseLink' at '{target.name}', which is not an ArticulationBody.");
        }

        /// <summary>
        /// The wheels one side drives, read the way the controller reads them: the list when it holds any, and
        /// the single reference otherwise.
        /// </summary>
        private static List<ArticulationBody> WheelsOf(ArticulationBody single, List<ArticulationBody> list)
        {
            var wheels = new List<ArticulationBody>();
            if (list != null)
            {
                foreach (ArticulationBody wheel in list)
                {
                    if (wheel != null)
                        wheels.Add(wheel);
                }
            }

            if (wheels.Count == 0 && single != null)
                wheels.Add(single);

            return wheels;
        }

        /// <summary>
        /// The child node the chassis is wired on. A prefab with two of them is a prefab whose chassis could be
        /// on the wrong one, so both are reported rather than the first being taken.
        /// </summary>
        private static Transform FindPluginsNode(GameObject prefab)
        {
            Transform found = null;
            foreach (Transform candidate in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (candidate == prefab.transform || candidate.name != PluginsNodeName)
                    continue;

                if (found != null)
                    Assert.Fail($"{prefab.name} has more than one child named '{PluginsNodeName}'.");

                found = candidate;
            }

            return found;
        }
    }
}
