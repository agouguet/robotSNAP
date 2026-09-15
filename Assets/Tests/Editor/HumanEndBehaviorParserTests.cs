using NUnit.Framework;
using RobotSNAP.Agents;

namespace RobotSNAP.Tests.Editor
{
    public sealed class HumanEndBehaviorParserTests
    {
        [TestCase("stay", HumanEndBehavior.Stay)]
        [TestCase("Stay in place", HumanEndBehavior.Stay)]
        [TestCase("disappear", HumanEndBehavior.Disappear)]
        [TestCase("despawn", HumanEndBehavior.Disappear)]
        [TestCase("loop", HumanEndBehavior.Loop)]
        [TestCase("Loop Route", HumanEndBehavior.Loop)]
        public void Parse_ReadsYamlValuesAndDisplayNames(string value, HumanEndBehavior expected)
        {
            Assert.That(HumanEndBehaviorParser.Parse(value), Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("nonsense")]
        public void Parse_DefaultsToStay(string value)
        {
            Assert.That(HumanEndBehaviorParser.Parse(value), Is.EqualTo(HumanEndBehavior.Stay));
        }

        [Test]
        public void ToYamlValue_RoundTripsThroughParse()
        {
            foreach (HumanEndBehavior behavior in new[]
                     {
                         HumanEndBehavior.Stay, HumanEndBehavior.Disappear, HumanEndBehavior.Loop
                     })
            {
                Assert.That(HumanEndBehaviorParser.Parse(HumanEndBehaviorParser.ToYamlValue(behavior)),
                    Is.EqualTo(behavior));
            }
        }

        [Test]
        public void ToDisplayName_IsParseableBackToTheEnum()
        {
            foreach (HumanEndBehavior behavior in new[]
                     {
                         HumanEndBehavior.Stay, HumanEndBehavior.Disappear, HumanEndBehavior.Loop
                     })
            {
                Assert.That(HumanEndBehaviorParser.Parse(HumanEndBehaviorParser.ToDisplayName(behavior)),
                    Is.EqualTo(behavior));
            }
        }

        [TestCase("SFM", MovementControllerType.SFM)]
        [TestCase("sfm", MovementControllerType.SFM)]
        [TestCase("social force", MovementControllerType.SFM)]
        [TestCase("social forces", MovementControllerType.SFM)]
        [TestCase("SFM (social forces)", MovementControllerType.SFM)]
        [TestCase("External", MovementControllerType.External)]
        [TestCase("external", MovementControllerType.External)]
        [TestCase("external control", MovementControllerType.External)]
        [TestCase("Python API", MovementControllerType.External)]
        [TestCase("External (Python API)", MovementControllerType.External)]
        // The ONNX and hybrid controllers are gone; scenarios saved with those values still load, as SFM.
        [TestCase("ONNX", MovementControllerType.SFM)]
        [TestCase("ONNX (learned)", MovementControllerType.SFM)]
        [TestCase("onnx_prediction", MovementControllerType.SFM)]
        [TestCase("Hybrid", MovementControllerType.SFM)]
        [TestCase("Hybrid (SFM + ONNX)", MovementControllerType.SFM)]
        [TestCase("hybrid", MovementControllerType.SFM)]
        public void MovementController_Parse_ReadsYamlValuesAndDisplayNames(
            string value,
            MovementControllerType expected)
        {
            Assert.That(HumanMovementControllerParser.TryParse(value, out MovementControllerType parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("banana")]
        public void MovementController_Parse_LeavesUnknownValuesToTheConfigAsset(string value)
        {
            Assert.That(HumanMovementControllerParser.TryParse(value, out _), Is.False);
        }

        [Test]
        public void MovementController_WritesTheValueItParsed()
        {
            foreach (MovementControllerType controller in new[]
                     {
                         MovementControllerType.SFM,
                         MovementControllerType.External
                     })
            {
                string yaml = HumanMovementControllerParser.ToYamlValue(controller);
                Assert.That(HumanMovementControllerParser.TryParse(yaml, out MovementControllerType parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(controller));
            }
        }
    }
}
