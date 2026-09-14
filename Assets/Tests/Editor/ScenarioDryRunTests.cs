using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class ScenarioDryRunTests
    {
        private static ScenarioDryRun.Route Route(float speed, params Vector2[] points) =>
            new("Human route 1", points, speed);

        [Test]
        public void PathLength_AddsEverySegment()
        {
            var points = new List<Vector2> { new(0f, 0f), new(3f, 0f), new(3f, 4f) };

            Assert.That(ScenarioDryRun.PathLength(points), Is.EqualTo(7f).Within(0.001f));
            Assert.That(ScenarioDryRun.PathLength(null), Is.EqualTo(0f));
            Assert.That(ScenarioDryRun.PathLength(new List<Vector2> { Vector2.zero }), Is.EqualTo(0f));
        }

        [Test]
        public void Analyse_ReportsAnOkRouteWithItsLengthAndTime()
        {
            var routes = new List<ScenarioDryRun.Route>
            {
                Route(1f, new Vector2(0f, 0f), new Vector2(10f, 0f))
            };

            List<DryRunFinding> findings = ScenarioDryRun.Analyse(routes);

            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(findings[0].Severity, Is.EqualTo(DryRunSeverity.Ok));
            Assert.That(findings[0].Message, Does.Contain("10 m"));
            Assert.That(findings[0].Message, Does.Contain("10 s"));
        }

        [Test]
        public void Analyse_FlagsRoutesThatWouldNotRun()
        {
            var routes = new List<ScenarioDryRun.Route>
            {
                Route(1f, new Vector2(0f, 0f)),
                Route(0f, new Vector2(0f, 0f), new Vector2(5f, 0f)),
                Route(1f, new Vector2(2f, 2f), new Vector2(2f, 2f)),
                Route(1f, new Vector2(0f, 0f), new Vector2(0.1f, 0f)),
                Route(0.2f, new Vector2(0f, 0f), new Vector2(200f, 0f))
            };

            List<DryRunFinding> findings = ScenarioDryRun.Analyse(routes);

            Assert.That(HasFinding(findings, DryRunSeverity.Error, "at least one objective"), Is.True);
            Assert.That(HasFinding(findings, DryRunSeverity.Error, "speed"), Is.True);
            Assert.That(HasFinding(findings, DryRunSeverity.Error, "same spot"), Is.True);
            Assert.That(HasFinding(findings, DryRunSeverity.Warning, "barely move"), Is.True);
            Assert.That(HasFinding(findings, DryRunSeverity.Warning, "about 1000 s"), Is.True);
        }

        private static bool HasFinding(List<DryRunFinding> findings, DryRunSeverity severity, string fragment) =>
            findings.Any(finding => finding.Severity == severity && finding.Message.Contains(fragment));

        [Test]
        public void Analyse_KeepsAHealthyRouteSilent()
        {
            List<DryRunFinding> findings = ScenarioDryRun.Analyse(new List<ScenarioDryRun.Route>
            {
                Route(1f, new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 3f))
            });

            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(findings[0].Severity, Is.EqualTo(DryRunSeverity.Ok));
            Assert.That(findings[0].Message, Does.Contain("7 m"));
        }

        [Test]
        public void Analyse_HandlesNoRouteAtAll()
        {
            Assert.That(ScenarioDryRun.Analyse(null), Is.Empty);
            Assert.That(ScenarioDryRun.Analyse(new List<ScenarioDryRun.Route>()), Is.Empty);
        }

        [Test]
        public void Describe_ReportsTheLongestWalk()
        {
            var routes = new List<ScenarioDryRun.Route>
            {
                Route(1f, new Vector2(0f, 0f), new Vector2(3f, 0f)),
                Route(2f, new Vector2(0f, 0f), new Vector2(20f, 0f))
            };

            Assert.That(ScenarioDryRun.Describe(routes), Is.EqualTo("Dry run: 2 route(s), longest 20 m ≈ 10 s at 2 m/s."));
            Assert.That(ScenarioDryRun.Describe(new List<ScenarioDryRun.Route>()), Is.EqualTo("Dry run: no route to walk."));
        }
    }
}
