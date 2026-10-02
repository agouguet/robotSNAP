using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The power button of the application bar.
    ///
    /// A click is raised by the pointer system through the panel an element belongs to, and an edit-mode
    /// tree has none, so the button is read for what it promises the user - its tooltip, its icon, and no
    /// leftover menu glyph - and the path a click takes is then walked directly, through
    /// <see cref="AppStatusBar.RequestQuit"/>. The effect of the quit is a parameter of that path, which is
    /// what keeps this case from having to stop Play mode or close the editor.
    /// </summary>
    public sealed class AppStatusBarQuitTests
    {
        private const string MainWindowPath = "Assets/UI/MainWindow.uxml";

        private static VisualElement BuildMainWindow()
        {
            VisualTreeAsset window = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainWindowPath);
            Assert.That(window, Is.Not.Null, $"The main window is missing at {MainWindowPath}.");
            return window.Instantiate();
        }

        [Test]
        public void ThePowerButtonAsksTheApplicationToQuit()
        {
            VisualElement root = BuildMainWindow();
            bool quit = false;
            var bar = new AppStatusBar(root, () => quit = true);

            try
            {
                Button power = root.Q<Button>("QuitAppButton");
                Assert.That(power, Is.Not.Null, "the status bar carries the power button");
                Assert.That(power.tooltip, Is.EqualTo("Quit RobotSNAP"), "the role of the button stays readable");
                Assert.That(string.IsNullOrEmpty(power.text), Is.True, "the old menu glyph is gone");
                Assert.That(power.style.backgroundImage.value.texture, Is.Not.Null,
                    "the power icon is drawn on the button");
                Assert.That(power.style.backgroundImage.value.texture.name, Is.EqualTo("power"));

                bar.RequestQuit();

                Assert.That(quit, Is.True, "the power button is the way out of the application");
            }
            finally
            {
                bar.Dispose();
            }
        }
    }
}
