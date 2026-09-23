using NUnit.Framework;
using RobotSNAP.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The debug switches of the simulation overlay are the only thing that drives the VisualizationManager.
    /// These tests hold that pair together: a switch writes its setting, and a manager nobody has asked
    /// anything of does not run at all, which is what keeps a crowd from paying for a debug view it is not
    /// showing.
    /// </summary>
    public sealed class SimulationVisualizationPanelTests
    {
        private const string OverlayPath = "Assets/UI/Tabs/Simulator/uxml/SimulationOverlay.uxml";

        private static VisualElement BuildOverlay()
        {
            VisualTreeAsset overlay = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(OverlayPath);
            Assert.That(overlay, Is.Not.Null, $"The simulation overlay UXML is missing at {OverlayPath}.");
            return overlay.Instantiate();
        }

        [Test]
        public void ASwitchWritesItsSetting_AndTheManagerOnlyRunsWhileOneIsOn()
        {
            VisualElement root = BuildOverlay();
            var panel = new SimulationVisualizationPanel(root);
            VisualizationManager manager = Object.FindAnyObjectByType<VisualizationManager>();

            try
            {
                Assert.That(manager, Is.Not.Null,
                    "The panel has to find or give the scene a manager to drive.");

                var wireframe = root.Q<Toggle>("VizWireframe");
                var lidar = root.Q<Toggle>("VizRobotLidar");
                Assert.That(wireframe, Is.Not.Null, "The overlay has to carry the wireframe switch.");
                Assert.That(lidar, Is.Not.Null, "The overlay has to carry the lidar switch.");

                Assert.That(wireframe.value, Is.False, "Every switch starts off.");
                Assert.That(manager.enabled, Is.False,
                    "Nothing is being visualized, so the manager has nothing to walk the scene for.");

                Assert.That(panel.ApplySwitch("VizWireframe", true), Is.True,
                    "A switch the overlay carries has to be one the panel knows.");
                panel.Tick();
                Assert.That(wireframe.value, Is.True, "The switch shows the setting it just wrote.");
                Assert.That(manager.wireframeMode, Is.True, "The switch has to write its setting.");
                Assert.That(manager.enabled, Is.True, "A manager with a switch on has to run.");

                panel.ApplySwitch("VizRobotLidar", true);
                panel.ApplySwitch("VizWireframe", false);
                Assert.That(manager.wireframeMode, Is.False);
                Assert.That(manager.showRobotSensorRays, Is.True);
                Assert.That(manager.enabled, Is.True, "One switch still on is enough to keep it running.");

                panel.ApplySwitch("VizRobotLidar", false);
                Assert.That(manager.enabled, Is.False,
                    "The last switch off puts the manager back to sleep.");

                // The other way round: a setting changed by something else shows up on its switch.
                manager.colorHumansByState = true;
                panel.Refresh();
                Assert.That(root.Q<Toggle>("VizHumanColors").value, Is.True,
                    "The panel reads the manager back, so it cannot show a state the scene is not in.");

                Assert.That(panel.ApplySwitch("VizNothingOfTheSort", true), Is.False,
                    "An unknown switch is refused rather than silently ignored.");
            }
            finally
            {
                if (manager != null)
                    Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        public void TheFootprintSwitchDrivesTheMinimap_AndDoesNotWakeTheManager()
        {
            VisualElement root = BuildOverlay();
            var minimap = new SimulationMinimap(root, null);
            var panel = new SimulationVisualizationPanel(root, minimap);
            VisualizationManager manager = Object.FindAnyObjectByType<VisualizationManager>();

            try
            {
                Assert.That(minimap.ShowDetectionFootprints, Is.True,
                    "The footprints are drawn until somebody asks for the map without them.");

                Assert.That(panel.ApplySwitch("VizDetectionFootprints", false), Is.True,
                    "The overlay carries the switch, so the panel has to know it.");
                Assert.That(minimap.ShowDetectionFootprints, Is.False,
                    "The switch of the minimap writes the minimap, not the manager.");
                Assert.That(manager.enabled, Is.False,
                    "The minimap draws its own footprints, so asking for them is no reason to wake the manager.");

                panel.ApplySwitch("VizDetectionFootprints", true);
                Assert.That(minimap.ShowDetectionFootprints, Is.True);
            }
            finally
            {
                if (manager != null)
                    Object.DestroyImmediate(manager.gameObject);
            }
        }

        [Test]
        public void TheOverlayCarriesASwitchForEverySettingThePanelDrives()
        {
            VisualElement root = BuildOverlay();
            var panel = new SimulationVisualizationPanel(root);
            VisualizationManager manager = Object.FindAnyObjectByType<VisualizationManager>();

            try
            {
                Assert.That(manager, Is.Not.Null);

                // Every switch of the panel is on screen, and the ones the panel does not drive - the boxes
                // Debug.DrawLine draws, which the game view never shows - are not offered at all.
                foreach (string name in SimulationVisualizationPanel.SwitchNames)
                {
                    Assert.That(root.Q<Toggle>(name), Is.Not.Null,
                        $"The panel drives '{name}', which the overlay does not carry.");
                }

                var offered = new System.Collections.Generic.List<string>();
                foreach (Toggle toggle in root.Query<Toggle>().ToList())
                {
                    if (toggle.ClassListContains("viz-toggle"))
                        offered.Add(toggle.name);
                }

                Assert.That(offered, Is.EquivalentTo(SimulationVisualizationPanel.SwitchNames),
                    "A switch nobody drives is a line that does nothing, and a setting with no switch is one " +
                    "nobody can reach.");

                panel.Refresh();
            }
            finally
            {
                if (manager != null)
                    Object.DestroyImmediate(manager.gameObject);
            }
        }
    }
}
