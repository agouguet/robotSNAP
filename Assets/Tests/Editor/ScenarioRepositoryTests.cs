using System.IO;
using System.Text;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class ScenarioRepositoryTests
    {
        [Test]
        public void Load_ParsesBundledScenarioAndCachesItsMetadata()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Scenarios", "corner.yaml");
            var repository = new ScenarioRepository();

            ScenarioData scenario = repository.Load("corner", path, cache: true);
            ScenarioInfo info = repository.GetInfo("corner", path);

            Assert.That(scenario, Is.Not.Null);
            Assert.That(scenario.Info.Name, Is.EqualTo("Corner"));
            Assert.That(repository.CacheSize, Is.EqualTo(1));
            Assert.That(info, Is.SameAs(scenario.Info));
        }

        [Test]
        public void Clear_RemovesScenarioCache()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Scenarios", "corner.yaml");
            var repository = new ScenarioRepository();
            repository.Load("corner", path, cache: true);

            repository.Clear();

            Assert.That(repository.CacheSize, Is.Zero);
            Assert.That(repository.TryGetScenario("corner", out _), Is.False);
        }

        [Test]
        public void Parse_LoadsOrderedRobotAndHumanGoals()
        {
            const string yaml = @"scenario_info:
  name: Route test
  map: basic/corner
points:
  robot_start: {x: 0, y: 0, z: 0}
  robot_mid: {x: 1, y: 0, z: 0}
  robot_goal: {x: 2, y: 0, z: 0}
  human_start: {x: 0, y: 0, z: 1}
  human_goal_1: {x: 1, y: 0, z: 1}
  human_goal_2: {x: 2, y: 0, z: 1}
robot:
  start: robot_start
  waypoints: [robot_mid]
  goal: robot_goal
humans:
  - id: independent_route
    count: 1
    spawn: {type: point, ref: human_start}
    goal: {type: point, ref: human_goal_1}
    goals:
      - {type: point, ref: human_goal_2}
";
            var repository = new ScenarioRepository();

            ScenarioData scenario = repository.Parse(Encoding.UTF8.GetBytes(yaml), "route-test");

            Assert.That(scenario.Robot.WaypointRefs, Is.EqualTo(new[] { "robot_mid" }));
            Assert.That(scenario.Humans[0].Goal.Reference, Is.EqualTo("human_goal_1"));
            Assert.That(scenario.Humans[0].Goals[0].Reference, Is.EqualTo("human_goal_2"));
        }
    }
}
