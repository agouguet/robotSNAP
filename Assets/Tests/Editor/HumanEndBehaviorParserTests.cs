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
    }
}
