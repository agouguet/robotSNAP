using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VYaml.Serialization;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// Ranges: what a scenario says it draws, and what a run actually gets.
    ///
    /// The contract these hold is the one a study depends on - a file written before ranges existed still
    /// loads and runs as it did, a file that names a range draws inside it, and the same seed replays the
    /// same draws.
    /// </summary>
    public sealed class ScenarioRandomizationTests
    {
        private const string CreationTabPath = "Assets/UI/Tabs/Scenarios/uxml/ScenarioCreationTab.uxml";

        private static readonly MethodInfo SelectRoute = typeof(ScenarioRouteEditor)
            .GetMethod("SelectRoute", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// The wizard binds its fields to one route at a time, the selected one, so a test that drives the
        /// crowd fields has to select the crowd route first - the robot route is the one a scenario opens on.
        /// </summary>
        private static void SelectRouteAt(ScenarioRouteEditor editor, int index) =>
            SelectRoute.Invoke(editor, new object[] { index });

        /// <summary>
        /// The range control a value is bound to. The wizard keeps them private because only the tab drives
        /// them; a test reaches in the way it already does for <see cref="SelectRoute"/>.
        /// </summary>
        private static ScenarioRangeControl RangeControlOf(ScenarioRouteEditor editor, string fieldName)
        {
            FieldInfo field = typeof(ScenarioRouteEditor).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"The wizard has no {fieldName}.");
            return (ScenarioRangeControl)field.GetValue(editor);
        }

        /// <summary>
        /// A scenario in the shape the project wrote before ranges existed: no <c>*_range</c> key anywhere.
        /// It is spelled out rather than built, because what is under test is the file a previous version
        /// produced, not the one this version would.
        /// </summary>
        private const string LegacyScenarioYaml = @"
scenario_info:
  name: Legacy
  type: Custom
  description: ''
  version: '1.0'
  author: RobotSNAP
  created: '2026-09-11 15:11'
  tags: []
  map: basic/crowd
  location: null
  dataset: null
  preview: ''
  robot_type: Jackal
  duration: 0
points:
  robot_1_start:
    x: 0.0
    y: 0.0
    z: 5.0
    yaw: 180.0
    center: null
    size: null
  robot_1_goal:
    x: 0.0
    y: 0.0
    z: -5.0
    yaw: null
    center: null
    size: null
  pedestrians_start:
    x: -5.0
    y: 0.0
    z: -2.18
    yaw: null
    center: null
    size: null
robots:
  - id: robot_1
    type: jackal
    start: robot_1_start
    goal: robot_1_goal
    waypoints: null
    behavior: normal
    speed: 2.0
robot: null
humans:
  - id: pedestrians
    count: 1
    spawn_window: 0
    spawn:
      type: point
      ref: pedestrians_start
      position: null
      zone: null
      formation: pair
      formation_parameter: 0
      spacing: 0.8
      relative_to: null
    goal:
      type: point
      ref: null
      position:
        x: 5.21
        y: 0.0
        z: -2.18
      zone: null
      target: null
      radius: 5.0
    goals: null
    end_behavior: stay
    group: null
    movement_controller:
      type: SFM
      sfm_params: null
    behavior: normal
    speed: 1.0
    color: null
    personality: null
";

        private static ScenarioData LegacyScenario() =>
            YamlSerializer.Deserialize<ScenarioData>(
                System.Text.Encoding.UTF8.GetBytes(LegacyScenarioYaml));

        /// <summary>A scenario that ranges every value a study randomizes, with fixed values to fall back on.</summary>
        private static ScenarioData RangedScenario()
        {
            return new ScenarioData
            {
                Info = new ScenarioInfo { Name = "Ranged" },
                Points = new Dictionary<string, RefPoint>
                {
                    ["robot_1_start"] = new RefPoint { X = 0f, Y = 0f, Z = 5f, Yaw = 180f },
                    ["robot_1_goal"] = new RefPoint { X = 0f, Y = 0f, Z = -5f }
                },
                Robots = new List<RobotScenarioConfig>
                {
                    new RobotScenarioConfig
                    {
                        Id = "robot_1",
                        Type = "jackal",
                        StartRef = "robot_1_start",
                        GoalRef = "robot_1_goal",
                        Speed = 1.5f,
                        SpeedRange = new ScenarioRange { Min = 1f, Max = 2f },
                        StartYawRange = new ScenarioRange { Min = -30f, Max = 30f }
                    }
                },
                Humans = new List<HumanScenarioConfig>
                {
                    new HumanScenarioConfig
                    {
                        Id = "pedestrians",
                        Count = 3,
                        Speed = 1.1f,
                        SpawnWindow = 2f,
                        CountRange = new ScenarioRange { Min = 2f, Max = 6f },
                        SpeedRange = new ScenarioRange { Min = 0.8f, Max = 1.6f },
                        SpawnWindowRange = new ScenarioRange { Min = 0f, Max = 6f },
                        Spawn = new SpawnConfig
                        {
                            Type = "random",
                            Zone = RefPoint.FromBounds(Vector3.zero, new Vector3(10f, 0f, 10f)),
                            Formation = "scatter",
                            Spacing = 1f,
                            SpacingRange = new ScenarioRange { Min = 0.6f, Max = 1.4f }
                        },
                        Goal = new GoalConfig
                        {
                            Type = "point",
                            Position = Point.FromVector3(new Vector3(0f, 0f, -8f))
                        }
                    }
                }
            };
        }

        // -- an old file ---------------------------------------------------------

        [Test]
        public void ALegacyScenarioCarriesNoRangeAtAll()
        {
            ScenarioData scenario = LegacyScenario();

            Assert.That(scenario.Robots[0].SpeedRange, Is.Null);
            Assert.That(scenario.Robots[0].StartYawRange, Is.Null);
            HumanScenarioConfig human = scenario.Humans[0];
            Assert.That(human.CountRange, Is.Null);
            Assert.That(human.SpeedRange, Is.Null);
            Assert.That(human.SpawnWindowRange, Is.Null);
            Assert.That(human.Spawn.SpacingRange, Is.Null);
        }

        [Test]
        public void ALegacyScenarioResolvesToItselfAndDrawsNothing()
        {
            ScenarioData scenario = LegacyScenario();

            Random.InitState(1234);
            ScenarioData resolved = ScenarioRandomization.Resolve(scenario, out List<string> notes);

            Assert.That(notes, Is.Empty, "A scenario with no range has nothing to report.");
            Assert.That(resolved.Robots[0].Speed, Is.EqualTo(2f));
            Assert.That(resolved.Points["robot_1_start"].Yaw, Is.EqualTo(180f));
            Assert.That(resolved.Humans[0].Count, Is.EqualTo(1));
            Assert.That(resolved.Humans[0].Speed, Is.EqualTo(1f));
            Assert.That(resolved.Humans[0].SpawnWindow, Is.EqualTo(0f));
            Assert.That(resolved.Humans[0].Spawn.Spacing, Is.EqualTo(0.8f));
        }

        [Test]
        public void ALegacyScenarioSpendsNoRandomDraw()
        {
            // The draws of a run are a sequence, and a scenario that randomizes nothing has to leave it
            // untouched, or every other draw of that run would shift.
            Random.InitState(99);
            ScenarioRandomization.Resolve(LegacyScenario(), out _);
            float afterResolve = Random.Range(0f, 1f);

            Random.InitState(99);
            float reference = Random.Range(0f, 1f);

            Assert.That(afterResolve, Is.EqualTo(reference));
        }

        // -- a ranged file -------------------------------------------------------

        [Test]
        public void TheAgentCountIsDrawnBetweenItsBounds()
        {
            var counts = new HashSet<int>();
            for (int seed = 0; seed < 200; seed++)
            {
                Random.InitState(seed);
                ScenarioData resolved = ScenarioRandomization.Resolve(RangedScenario(), out _);

                int count = resolved.Humans[0].Count;
                Assert.That(count, Is.GreaterThanOrEqualTo(2).And.LessThanOrEqualTo(6));
                counts.Add(count);
            }

            Assert.That(counts.Count, Is.GreaterThan(1), "The count has to actually vary between runs.");
            Assert.That(counts, Does.Contain(2), "Both bounds are included in the draw.");
            Assert.That(counts, Does.Contain(6), "Both bounds are included in the draw.");
        }

        [Test]
        public void ARangedCountRescuesARouteWhoseFixedCountIsZero()
        {
            // The case an author writes by accident: the fixed count left at zero, and the range doing the
            // work. The route has to spawn agents, not be dropped because of the zero.
            ScenarioData scenario = RangedScenario();
            scenario.Humans[0].Count = 0;

            Random.InitState(3);
            ScenarioData resolved = ScenarioRandomization.Resolve(scenario, out _);

            Assert.That(resolved.Humans[0].Count, Is.InRange(2, 6));
        }

        [Test]
        public void EveryRangedValueStaysInsideItsBounds()
        {
            Random.InitState(77);
            ScenarioData resolved = ScenarioRandomization.Resolve(RangedScenario(), out List<string> notes);

            Assert.That(resolved.Robots[0].Speed, Is.InRange(1f, 2f));
            Assert.That(resolved.Points["robot_1_start"].Yaw, Is.InRange(-30f, 30f));
            Assert.That(resolved.Humans[0].Speed, Is.InRange(0.8f, 1.6f));
            Assert.That(resolved.Humans[0].SpawnWindow, Is.InRange(0f, 6f));
            Assert.That(resolved.Humans[0].Spawn.Spacing, Is.InRange(0.6f, 1.4f));
            Assert.That(notes, Is.Not.Empty, "What was drawn is reported back to the caller.");
        }

        [Test]
        public void TheSameSeedReplaysTheSameDraws()
        {
            Random.InitState(2026);
            ScenarioData first = ScenarioRandomization.Resolve(RangedScenario(), out _);

            Random.InitState(2026);
            ScenarioData second = ScenarioRandomization.Resolve(RangedScenario(), out _);

            Assert.That(second.Humans[0].Count, Is.EqualTo(first.Humans[0].Count));
            Assert.That(second.Humans[0].Speed, Is.EqualTo(first.Humans[0].Speed));
            Assert.That(second.Humans[0].Spawn.Spacing, Is.EqualTo(first.Humans[0].Spawn.Spacing));
            Assert.That(second.Robots[0].Speed, Is.EqualTo(first.Robots[0].Speed));
            Assert.That(second.Points["robot_1_start"].Yaw, Is.EqualTo(first.Points["robot_1_start"].Yaw));
        }

        [Test]
        public void ResolvingLeavesTheAuthoredScenarioAlone()
        {
            // The loader caches the scenario between runs, so a draw that wrote into it would fix the value
            // for every run that follows.
            ScenarioData authored = RangedScenario();

            Random.InitState(5);
            ScenarioRandomization.Resolve(authored, out _);

            Assert.That(authored.Humans[0].Count, Is.EqualTo(3));
            Assert.That(authored.Robots[0].Speed, Is.EqualTo(1.5f));
            Assert.That(authored.Points["robot_1_start"].Yaw, Is.EqualTo(180f));
            Assert.That(authored.Humans[0].CountRange, Is.Not.Null, "The range is still there to draw again.");
        }

        [Test]
        public void ARangeRoundTripsThroughYaml()
        {
            var config = new HumanScenarioConfig
            {
                Id = "pedestrians",
                Count = 3,
                CountRange = new ScenarioRange { Min = 2f, Max = 6f },
                SpeedRange = new ScenarioRange { Min = 0.8f, Max = 1.6f }
            };

            byte[] yaml = YamlSerializer.Serialize(config).ToArray();
            var readBack = YamlSerializer.Deserialize<HumanScenarioConfig>(yaml);
            string text = System.Text.Encoding.UTF8.GetString(yaml);

            Assert.That(text, Does.Contain("count_range"));
            Assert.That(text, Does.Contain("speed_range"));
            Assert.That(readBack.CountRange.Min, Is.EqualTo(2f));
            Assert.That(readBack.CountRange.Max, Is.EqualTo(6f));
            Assert.That(readBack.SpeedRange.Max, Is.EqualTo(1.6f));
        }

        // -- the wizard ----------------------------------------------------------

        [Test]
        public void TheWizardReadsAndWritesTheRangesOfAScenario()
        {
            VisualTreeAsset tab = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CreationTabPath);
            Assert.That(tab, Is.Not.Null, $"The creation tab UXML is missing at {CreationTabPath}.");
            var editor = new ScenarioRouteEditor(tab.Instantiate());

            editor.Load(RangedScenario());

            var saved = new ScenarioData { Info = new ScenarioInfo { Name = "Ranged" } };
            editor.WriteToScenario(saved);

            Assert.That(saved.Robots[0].SpeedRange, Is.Not.Null, "A robot range survives the wizard.");
            Assert.That(saved.Robots[0].SpeedRange.Min, Is.EqualTo(1f));
            Assert.That(saved.Robots[0].StartYawRange.Max, Is.EqualTo(30f));
            Assert.That(saved.Humans[0].CountRange.Min, Is.EqualTo(2f));
            Assert.That(saved.Humans[0].CountRange.Max, Is.EqualTo(6f));
            Assert.That(saved.Humans[0].SpeedRange.Min, Is.EqualTo(0.8f));
            Assert.That(saved.Humans[0].SpawnWindowRange.Max, Is.EqualTo(6f));
            Assert.That(saved.Humans[0].Spawn.SpacingRange.Max, Is.EqualTo(1.4f));
        }

        [Test]
        public void TheWizardWritesNoRangeForARouteThatDrawsNothing()
        {
            VisualTreeAsset tab = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CreationTabPath);
            Assert.That(tab, Is.Not.Null, $"The creation tab UXML is missing at {CreationTabPath}.");
            VisualElement root = tab.Instantiate();
            var editor = new ScenarioRouteEditor(root);

            editor.Load(LegacyScenario());

            var saved = new ScenarioData { Info = new ScenarioInfo { Name = "Legacy" } };
            editor.WriteToScenario(saved);

            Assert.That(saved.Robots[0].SpeedRange, Is.Null);
            Assert.That(saved.Robots[0].StartYawRange, Is.Null);
            Assert.That(saved.Humans[0].CountRange, Is.Null);
            Assert.That(saved.Humans[0].SpeedRange, Is.Null);
            Assert.That(saved.Humans[0].SpawnWindowRange, Is.Null);
            Assert.That(saved.Humans[0].Spawn.SpacingRange, Is.Null);
        }

        /// <summary>
        /// The wizard end to end: a crowd route is selected, the author asks the run to draw its count between
        /// 2 and 6, and the scenario the wizard hands to the loader says exactly that.
        /// </summary>
        [Test]
        public void TheWizardSavesTheCountRangeTheAuthorAskedFor()
        {
            VisualTreeAsset tab = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CreationTabPath);
            Assert.That(tab, Is.Not.Null, $"The creation tab UXML is missing at {CreationTabPath}.");
            VisualElement root = tab.Instantiate();
            var editor = new ScenarioRouteEditor(root);
            editor.Load(LegacyScenario());
            SelectRouteAt(editor, 1); // routes are listed robot first, then the crowd

            DropdownField mode = root.Q<DropdownField>("Agent count draw mode");
            Assert.That(mode, Is.Not.Null, "The wizard has to offer a range for the agent count.");
            Assert.That(root.Q<FloatField>("Agent count draw min"), Is.Not.Null);
            Assert.That(root.Q<FloatField>("Agent count draw max"), Is.Not.Null);
            Assert.That(mode.choices, Does.Contain(ScenarioRangeControl.FixedChoice));
            Assert.That(mode.choices, Does.Contain(ScenarioRangeControl.RangeChoice));
            Assert.That(mode.value, Is.EqualTo(ScenarioRangeControl.FixedChoice));

            RangeControlOf(editor, "_humanCountRange").SetRange(2f, 6f);

            var saved = new ScenarioData { Info = new ScenarioInfo { Name = "Legacy" } };
            editor.WriteToScenario(saved);

            Assert.That(saved.Humans, Has.Count.EqualTo(1));
            Assert.That(saved.Humans[0].CountRange, Is.Not.Null);
            Assert.That(saved.Humans[0].CountRange.Min, Is.EqualTo(2f));
            Assert.That(saved.Humans[0].CountRange.Max, Is.EqualTo(6f));
            Assert.That(saved.Robots[0].SpeedRange, Is.Null, "Asking for one range leaves the others fixed.");
        }

        [Test]
        public void TheWizardShowsTheRangeAndHidesTheFixedFieldWhileItIsDrawn()
        {
            VisualTreeAsset tab = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CreationTabPath);
            VisualElement root = tab.Instantiate();
            var editor = new ScenarioRouteEditor(root);
            editor.Load(RangedScenario());
            SelectRouteAt(editor, 1);

            DropdownField mode = root.Q<DropdownField>("Agent count draw mode");
            VisualElement fixedRow = root.Q<IntegerField>("HumanCountField").parent;
            ScenarioRangeControl control = RangeControlOf(editor, "_humanCountRange");

            Assert.That(mode.value, Is.EqualTo(ScenarioRangeControl.RangeChoice));
            Assert.That(fixedRow.style.display.value, Is.EqualTo(DisplayStyle.None),
                "The fixed field gives way to the range while the value is drawn.");

            control.SetFixed();

            Assert.That(mode.value, Is.EqualTo(ScenarioRangeControl.FixedChoice));
            Assert.That(fixedRow.style.display.value, Is.Not.EqualTo(DisplayStyle.None));

            control.SetRange(4f, 9f);

            Assert.That(mode.value, Is.EqualTo(ScenarioRangeControl.RangeChoice));
            Assert.That(fixedRow.style.display.value, Is.EqualTo(DisplayStyle.None));
        }
    }
}
