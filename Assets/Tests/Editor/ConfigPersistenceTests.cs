using System;
using System.IO;
using NUnit.Framework;
using RobotSNAP.Core;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The rules a configuration name has to satisfy, and the round trip of a saved profile.
    ///
    /// The class that owns those files is exercised against a folder of its own rather than the one the
    /// application's user owns - which is what <see cref="ConfigPersistence.DirectoryOverride"/> is for -
    /// and every call below still goes through the real methods: a test that re-implemented the path it
    /// checks would prove nothing about the one the application takes.
    /// </summary>
    public sealed class ConfigPersistenceTests
    {
        private string _folder;
        private SimulationConfig _config;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "robotsnap-config-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            ConfigPersistence.DirectoryOverride = _folder;
            _config = ScriptableObject.CreateInstance<SimulationConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            ConfigPersistence.DirectoryOverride = null;

            if (_config != null)
                UnityEngine.Object.DestroyImmediate(_config);

            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }

        // -- what a name may be ----------------------------------------------------------------

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(".json")]
        public void CheckName_RefusesANameThatIsEmptyOnceTrimmed(string typed)
        {
            ConfigNameProblem problem = ConfigPersistence.CheckName(typed, null, out string clean);

            Assert.That(problem, Is.EqualTo(ConfigNameProblem.Empty));
            Assert.That(clean, Is.Empty);
        }

        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("a:b")]
        [TestCase("a*b")]
        [TestCase("a?b")]
        [TestCase("a\"b")]
        [TestCase("a<b")]
        [TestCase("a>b")]
        [TestCase("a|b")]
        [TestCase("a\nb")]
        public void CheckName_RefusesTheCharactersAFileNameCannotCarry(string typed)
        {
            Assert.That(
                ConfigPersistence.CheckName(typed, null, out _),
                Is.EqualTo(ConfigNameProblem.InvalidCharacter));
        }

        [TestCase(".")]
        [TestCase("..")]
        [TestCase(" .. ")]
        public void CheckName_RefusesTheNamesAPathIsBuiltFrom(string typed)
        {
            Assert.That(ConfigPersistence.CheckName(typed, null, out _), Is.EqualTo(ConfigNameProblem.Reserved));
        }

        [Test]
        public void CheckName_HoldsTheCeilingWithoutRefusingIt()
        {
            string past = new string('a', ConfigPersistence.MaxNameLength + 1);
            string at = new string('a', ConfigPersistence.MaxNameLength);

            Assert.That(ConfigPersistence.CheckName(past, null, out _), Is.EqualTo(ConfigNameProblem.TooLong));
            Assert.That(ConfigPersistence.CheckName(at, null, out _), Is.EqualTo(ConfigNameProblem.None));
        }

        /// <summary>
        /// The name that comes back is the one the list shows: the blanks around it and the extension a
        /// profile is stored under are dropped, so what is saved and what is listed are the same word.
        /// </summary>
        [TestCase("  lobby  ", "lobby")]
        [TestCase("lobby.json", "lobby")]
        [TestCase("Lobby.JSON", "Lobby")]
        public void CheckName_AnswersWithTheNameTheListWouldShow(string typed, string expected)
        {
            ConfigNameProblem problem = ConfigPersistence.CheckName(typed, null, out string clean);

            Assert.That(problem, Is.EqualTo(ConfigNameProblem.None));
            Assert.That(clean, Is.EqualTo(expected));
        }

        [Test]
        public void CheckName_RefusesANameASavedConfigurationAlreadyWears()
        {
            var saved = new[] { "Lobby" };

            Assert.That(ConfigPersistence.CheckName("lobby", saved, out _), Is.EqualTo(ConfigNameProblem.Taken));
            Assert.That(ConfigPersistence.CheckName("lobby.json", saved, out _), Is.EqualTo(ConfigNameProblem.Taken));
            Assert.That(ConfigPersistence.CheckName("warehouse", saved, out _), Is.EqualTo(ConfigNameProblem.None));
        }

        // -- what the files do -----------------------------------------------------------------

        [Test]
        public void SaveThenLoad_RestoresEverySettingThroughTheRealPath()
        {
            _config.DefaultScenario = "intersection";
            _config.RosPrefix = "robot_1";
            _config.TimeScale = 3f;
            _config.EnableROS = true;
            _config.RandomSeed = 42;
            _config.Topics.SimulationAgents = "/crowd";

            ConfigPersistence.Save(_config, "round trip");

            Assert.That(ConfigPersistence.GetAvailableNames(), Does.Contain("round trip"));
            Assert.That(File.Exists(Path.Combine(_folder, "round trip.json")), Is.True);

            Assert.That(ConfigPersistence.TryLoad("round trip", out SimulationConfig loaded), Is.True);
            try
            {
                Assert.That(loaded, Is.Not.Null);
                Assert.That(loaded.DefaultScenario, Is.EqualTo("intersection"));
                Assert.That(loaded.RosPrefix, Is.EqualTo("robot_1"));
                Assert.That(loaded.TimeScale, Is.EqualTo(3f));
                Assert.That(loaded.EnableROS, Is.True);
                Assert.That(loaded.RandomSeed, Is.EqualTo(42));
                Assert.That(
                    loaded.Topics.SimulationAgents,
                    Is.EqualTo("/crowd"),
                    "the table of stream names is part of the profile, not a preference of the page");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(loaded);
            }
        }

        [Test]
        public void Delete_RemovesTheProfileAndTheListStopsOfferingIt()
        {
            ConfigPersistence.Save(_config, "temporary");

            Assert.That(ConfigPersistence.Delete("temporary"), Is.True);
            Assert.That(ConfigPersistence.GetAvailableNames(), Does.Not.Contain("temporary"));
            Assert.That(ConfigPersistence.TryLoad("temporary", out _), Is.False);
            Assert.That(
                ConfigPersistence.Delete("temporary"),
                Is.False,
                "removing what is gone is not a second removal");
        }

        [Test]
        public void GetAvailableNames_CountsTwoSpellingsOfOneNameOnce()
        {
            File.WriteAllText(Path.Combine(_folder, "Lobby.json"), "{}");
            File.WriteAllText(Path.Combine(_folder, "lobby.json"), "{}");
            File.WriteAllText(Path.Combine(_folder, "notes.txt"), "not a configuration");

            // A file system that ignores case holds one of the two files; one that does not holds both, and
            // the list still reads them as the one profile they name.
            Assert.That(ConfigPersistence.GetAvailableNames(), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Whatever is typed, the path a profile is written under stays in the folder that owns it: the
        /// structural half of the name rules, checked where it actually matters.
        /// </summary>
        [TestCase("..")]
        [TestCase("../escape")]
        [TestCase("sub/folder")]
        [TestCase("a/../../b")]
        [TestCase("C:\\windows\\evil")]
        public void GetPath_KeepsEveryNameInsideTheConfigurationFolder(string typed)
        {
            string path = ConfigPersistence.GetPath(typed);

            Assert.That(Path.GetDirectoryName(path), Is.EqualTo(_folder));
        }
    }
}
