using NUnit.Framework;
using RobotSNAP;
using RobotSNAP.Core;
using RobotSNAP.ROS;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Reflection;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The speed control of the simulation top bar.
    ///
    /// It shows the scale the session's configuration holds and it writes a new one through the same
    /// router command a Python client sends, so the two ways of setting the speed cannot disagree. A
    /// dropdown raises its callback through the panel it is part of, and the tree of an edit-mode test has
    /// none, so these cases call the entry point that callback hands its value to
    /// (<see cref="TimeScaleControl.Choose"/>) with the label the dropdown would raise.
    ///
    /// An edit-mode test runs in an empty scene, so the session the router writes to is stood up by hand: a
    /// Clock and a Supervisor are added for real and given a configuration, the same shape the stop command's
    /// test uses. Everything the test exercises after that is the real code - the router, the write to the
    /// configuration, and the clock the configuration drives.
    /// </summary>
    public sealed class TimeScaleControlTests
    {
        private const string TopBarPath = "Assets/UI/Tabs/Simulator/uxml/SimulationTopBar.uxml";
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();

        private Supervisor _supervisor;
        private Clock _clock;
        private SimulationConfig _config;

        private float _originalTimeScale;
        private float _originalFixedDelta;
        private float _originalMaximumDelta;

        [SetUp]
        public void RememberTheSessionsTiming()
        {
            _originalTimeScale = Time.timeScale;
            _originalFixedDelta = Time.fixedDeltaTime;
            _originalMaximumDelta = Time.maximumDeltaTime;
        }

        [TearDown]
        public void PutTheSessionsTimingBack()
        {
            Time.timeScale = _originalTimeScale;
            Time.fixedDeltaTime = _originalFixedDelta;
            Time.maximumDeltaTime = _originalMaximumDelta;

            foreach (GameObject created in _created)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }

            _created.Clear();

            // The configuration is owned by the test rather than carried by a scene object, so it is destroyed
            // here and not with the supervisor that reads it.
            if (_config != null)
                Object.DestroyImmediate(_config);

            _supervisor = null;
            _clock = null;
            _config = null;
        }

        /// <summary>
        /// A session for the router to write to: the clock first - so the supervisor wires the one it finds
        /// instead of making its own - then the supervisor, then the configuration the router clamps and
        /// applies. A component added in an edit-mode test does not run its lifecycle, so the clock and the
        /// configuration are planted through the fields the supervisor reads.
        /// </summary>
        private void BuildSession()
        {
            _clock = NewObject("time_scale_clock").AddComponent<Clock>();
            _supervisor = NewObject("time_scale_supervisor").AddComponent<Supervisor>();
            Assert.That(Supervisor.Instance, Is.SameAs(_supervisor),
                "the session's supervisor is the one the router finds");

            _config = ScriptableObject.CreateInstance<SimulationConfig>();
            SetField(_supervisor, "_defaultConfig", _config);
            SetField(_supervisor, "_clock", _clock);
        }

        private GameObject NewObject(string name)
        {
            var created = new GameObject(name);
            _created.Add(created);
            return created;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, Private);
            Assert.That(field, Is.Not.Null, $"the field '{name}' the session is planted through still exists");
            field.SetValue(target, value);
        }

        private static DropdownField BuildDropdown()
        {
            VisualTreeAsset topBar = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TopBarPath);
            Assert.That(topBar, Is.Not.Null, $"The simulation top bar is missing at {TopBarPath}.");

            DropdownField dropdown = topBar.Instantiate().Q<DropdownField>("TimeScaleDropdown");
            Assert.That(dropdown, Is.Not.Null, "The top bar carries the speed control.");
            return dropdown;
        }

        [Test]
        public void ThePresetsAreTheSpeedsASessionIsRunAt()
        {
            Assert.That(TimeScaleControl.Presets, Is.EqualTo(new[] { 1f, 2f, 5f, 10f, 25f, 50f, 100f }));
        }

        [Test]
        public void TheControlShowsTheScaleTheConfigurationHolds()
        {
            DropdownField dropdown = BuildDropdown();
            float applied = 1f;
            var control = new TimeScaleControl(dropdown, new SimulationCommandRouter(), () => applied);

            // What a Python client setting nothing a preset would leave: the control has to say seven, not
            // round it to the nearest speed it happens to offer.
            applied = 7f;
            control.Refresh();

            Assert.That(dropdown.value, Is.EqualTo("x7"), "a scale that is not a preset is shown as it is");
            Assert.That(dropdown.choices, Does.Contain("x7"),
                "the shown scale has to be a choice of the list, or the dropdown has no value to show");

            applied = 10f;
            control.Refresh();

            Assert.That(dropdown.value, Is.EqualTo("x10"), "the control follows the configuration, not the last click");
            Assert.That(dropdown.choices, Does.Not.Contain("x7"),
                "a scale the session no longer runs at stops being offered");
        }

        [Test]
        public void ChoosingASpeedWritesTheConfigurationThroughTheRouter()
        {
            BuildSession();

            DropdownField dropdown = BuildDropdown();
            var control = new TimeScaleControl(dropdown, new SimulationCommandRouter(), () => _config.TimeScale);

            control.Choose("x10");

            // The session under test paces freely, so the configuration applies what it was asked for. A
            // lockstep period can clamp it, and the message says so instead of pretending.
            Assert.That(_config.TimeScale, Is.EqualTo(10f).Within(1e-3f),
                "the speed a selection asks for is the one the configuration holds, written through the router");
            Assert.That(Time.timeScale, Is.EqualTo(_config.TimeScale).Within(1e-3f),
                "and the configuration is the scale Unity runs at");
            Assert.That(_clock.TimeScale, Is.EqualTo(_config.TimeScale).Within(1e-3f),
                "the clock every stream is stamped from follows the same scale");
            Assert.That(dropdown.value, Is.EqualTo("x10"), "the control shows the speed it set");
        }

        [Test]
        public void ASelectionIsReadBackFromItsLabel()
        {
            Assert.That(TimeScaleControl.TryParseScale("x100", out float hundred), Is.True);
            Assert.That(hundred, Is.EqualTo(100f));

            Assert.That(TimeScaleControl.TryParseScale("x7.5", out float sevenAndAHalf), Is.True);
            Assert.That(sevenAndAHalf, Is.EqualTo(7.5f).Within(1e-4f));

            Assert.That(TimeScaleControl.TryParseScale("", out _), Is.False, "an empty label is not a speed");
            Assert.That(TimeScaleControl.TryParseScale("x0", out _), Is.False, "zero is not a speed a session can run at");
        }
    }
}
