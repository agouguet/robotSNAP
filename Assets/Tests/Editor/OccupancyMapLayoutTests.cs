using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class OccupancyMapLayoutTests
    {
        [Test]
        public void FitRect_CentersHorizontallyWhenCanvasIsWiderThanTexture()
        {
            Rect rect = OccupancyMapLayout.FitRect(new Rect(0f, 0f, 200f, 100f), 100, 100);

            Assert.That(rect.x, Is.EqualTo(50f).Within(0.001f));
            Assert.That(rect.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(rect.width, Is.EqualTo(100f).Within(0.001f));
            Assert.That(rect.height, Is.EqualTo(100f).Within(0.001f));
        }

        [Test]
        public void FitRect_CentersVerticallyWhenCanvasIsTallerThanTexture()
        {
            Rect rect = OccupancyMapLayout.FitRect(new Rect(0f, 0f, 100f, 200f), 100, 100);

            Assert.That(rect.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(rect.y, Is.EqualTo(50f).Within(0.001f));
            Assert.That(rect.width, Is.EqualTo(100f).Within(0.001f));
            Assert.That(rect.height, Is.EqualTo(100f).Within(0.001f));
        }

        [Test]
        public void FitRect_PreservesTextureAspectRatio()
        {
            Rect rect = OccupancyMapLayout.FitRect(new Rect(0f, 0f, 400f, 100f), 200, 100);

            Assert.That(rect.width / rect.height, Is.EqualTo(2f).Within(0.001f));
            Assert.That(rect.width, Is.EqualTo(200f).Within(0.001f));
        }

        [Test]
        public void FitRect_ReturnsEmptyWhenSizesAreMissing()
        {
            Assert.That(OccupancyMapLayout.FitRect(new Rect(0f, 0f, 100f, 100f), 0, 100), Is.EqualTo(Rect.zero));
            Assert.That(OccupancyMapLayout.FitRect(new Rect(0f, 0f, 100f, 100f), 100, 0), Is.EqualTo(Rect.zero));
            Assert.That(OccupancyMapLayout.FitRect(new Rect(0f, 0f, 0f, 100f), 100, 100), Is.EqualTo(Rect.zero));
        }
    }
}
