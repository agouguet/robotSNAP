using System;
using System.Collections.Generic;
using NUnit.Framework;
using RobotSNAP.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// Guards <see cref="RepeatedLogFilter"/>, the handler that stands between a client retrying once a
    /// second and the editor log: what it keeps, what it collapses, and the fact that it can never take a
    /// logging call down with it.
    /// </summary>
    public sealed class RepeatedLogFilterTests
    {
        private const string ConnectorMessage = "Connection to 127.0.0.1:10000 failed - {0}";

        [Test]
        public void IdenticalMessagesAreForwardedOnceAndReportedWhenTheRunEnds()
        {
            var inner = new RecordingHandler();
            var filter = new RepeatedLogFilter(inner);

            filter.LogFormat(LogType.Error, null, ConnectorMessage, "SocketException");
            filter.LogFormat(LogType.Error, null, ConnectorMessage, "SocketException");
            filter.LogFormat(LogType.Error, null, ConnectorMessage, "SocketException");
            filter.LogFormat(LogType.Error, null, ConnectorMessage, "SocketException");
            filter.LogFormat(LogType.Log, null, "node ready");

            Assert.That(
                inner.Entries,
                Is.EqualTo(
                    new[]
                    {
                        "Connection to 127.0.0.1:10000 failed - SocketException",
                        "Connection to 127.0.0.1:10000 failed - SocketException ... repeated 3 more times",
                        "node ready",
                    }
                )
            );
        }

        [Test]
        public void TwoDifferentMessagesBothReachTheHandler()
        {
            var inner = new RecordingHandler();
            var filter = new RepeatedLogFilter(inner);

            filter.LogFormat(LogType.Error, null, "first");
            filter.LogFormat(LogType.Error, null, "second");

            Assert.That(inner.Entries, Is.EqualTo(new[] { "first", "second" }));
        }

        [Test]
        public void TheSameTextUnderAnotherLogTypeIsAnotherRun()
        {
            // A run is keyed on the log type as well as on the text: an error promoted from a warning is a
            // different line, and collapsing the two would hide a change of severity.
            var inner = new RecordingHandler();
            var filter = new RepeatedLogFilter(inner);

            filter.LogFormat(LogType.Warning, null, "queue full");
            filter.LogFormat(LogType.Error, null, "queue full");
            filter.LogFormat(LogType.Warning, null, "queue full");

            Assert.That(inner.Entries, Is.EqualTo(new[] { "queue full", "queue full", "queue full" }));
        }

        [Test]
        public void AMessageThatReappearsAfterAnotherStartsANewRun()
        {
            var inner = new RecordingHandler();
            var filter = new RepeatedLogFilter(inner);

            filter.LogFormat(LogType.Log, null, "A");
            filter.LogFormat(LogType.Log, null, "A");
            filter.LogFormat(LogType.Log, null, "B");
            filter.LogFormat(LogType.Log, null, "A");

            Assert.That(
                inner.Entries,
                Is.EqualTo(new[] { "A", "A ... repeated 1 more times", "B", "A" })
            );
        }

        [Test]
        public void ExceptionsAreCollapsedByTypeAndMessage()
        {
            var inner = new RecordingHandler();
            var filter = new RepeatedLogFilter(inner);

            filter.LogException(new InvalidOperationException("boom"), null);
            filter.LogException(new InvalidOperationException("boom"), null);
            filter.LogException(new InvalidOperationException("boom"), null);
            filter.LogException(new ArgumentException("boom"), null);

            Assert.That(inner.Exceptions.Count, Is.EqualTo(2));
            Assert.That(inner.Exceptions[0], Is.TypeOf<InvalidOperationException>());
            Assert.That(inner.Exceptions[1], Is.TypeOf<ArgumentException>());
            Assert.That(
                inner.Entries,
                Is.EqualTo(new[] { "System.InvalidOperationException: boom ... repeated 2 more times" })
            );
        }

        [Test]
        public void DistinctMessagesAreNeverLost()
        {
            var inner = new RecordingHandler();
            var filter = new RepeatedLogFilter(inner);

            for (int step = 0; step < 25; step++)
                filter.LogFormat(LogType.Log, null, "step {0}", step);

            Assert.That(inner.Entries.Count, Is.EqualTo(25));
            for (int step = 0; step < 25; step++)
                Assert.That(inner.Entries[step], Is.EqualTo($"step {step}"));
        }

        [Test]
        public void ALongRunIsReportedAgainEveryThirtySeconds()
        {
            var inner = new RecordingHandler();
            long clock = 0;
            var filter = new RepeatedLogFilter(inner, () => clock);

            filter.LogFormat(LogType.Error, null, "boom");
            clock = 10000;
            filter.LogFormat(LogType.Error, null, "boom");
            clock = 31000;
            filter.LogFormat(LogType.Error, null, "boom");
            clock = 62000;
            filter.LogFormat(LogType.Error, null, "boom");
            filter.LogFormat(LogType.Error, null, "boom");

            // The second report counts only what was silenced since the first one, and the run is still
            // running: nothing is ever silent for the rest of the session.
            Assert.That(
                inner.Entries,
                Is.EqualTo(new[] { "boom", "boom ... repeated 2 more times", "boom ... repeated 1 more times" })
            );
        }

        [Test]
        public void ANullHandlerNeverThrows()
        {
            var filter = new RepeatedLogFilter(null);

            Assert.DoesNotThrow(
                () =>
                {
                    filter.LogFormat(LogType.Error, null, "boom {0}", 1);
                    filter.LogFormat(LogType.Error, null, "boom {0}", 1);
                    filter.LogException(new InvalidOperationException("boom"), null);
                    filter.LogException(null, null);
                }
            );
        }

        [Test]
        public void ANullMessageIsForwardedAndCollapsesLikeAnyOther()
        {
            var inner = new RecordingHandler();
            var filter = new RepeatedLogFilter(inner);

            Assert.DoesNotThrow(() => filter.LogFormat(LogType.Log, null, null, null));
            Assert.DoesNotThrow(() => filter.LogFormat(LogType.Log, null, null, null));

            Assert.That(inner.Entries, Is.EqualTo(new[] { string.Empty }));
        }

        [Test]
        public void AMalformedFormatIsForwardedInsteadOfThrowing()
        {
            // Rendering the message is the filter's own step, and it can fail on a format the caller built
            // wrong; the message must still reach the handler rather than be lost.
            var inner = new RecordingHandler();
            var filter = new RepeatedLogFilter(inner);

            Assert.DoesNotThrow(() => filter.LogFormat(LogType.Error, null, "bridge { {0}", "down"));

            Assert.That(inner.Entries, Is.EqualTo(new[] { "bridge { {0}" }));
        }

        [Test]
        public void InstallingTwiceLeavesOneFilterInFrontOfTheLogger()
        {
            ILogHandler previous = Debug.unityLogger.logHandler;

            try
            {
                RepeatedLogFilter.Install();
                ILogHandler installed = Debug.unityLogger.logHandler;

                if (ReferenceEquals(installed, previous))
                    return; // An earlier test already installed it in this session; there is nothing to check.

                Assert.That(installed, Is.InstanceOf<RepeatedLogFilter>());

                RepeatedLogFilter.Install();

                Assert.That(Debug.unityLogger.logHandler, Is.SameAs(installed));
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
        }

        /// <summary>
        /// Stands in for the console: it records what the filter let through, rendered as the console would
        /// show it, so that a test can read the filtered sequence back in order.
        /// </summary>
        private sealed class RecordingHandler : ILogHandler
        {
            private readonly List<string> _entries = new List<string>();
            private readonly List<Exception> _exceptions = new List<Exception>();

            public IReadOnlyList<string> Entries => _entries;

            public IReadOnlyList<Exception> Exceptions => _exceptions;

            public void LogFormat(LogType logType, Object context, string format, params object[] args)
            {
                _entries.Add(Render(format, args));
            }

            public void LogException(Exception exception, Object context)
            {
                _exceptions.Add(exception);
            }

            private static string Render(string format, object[] args)
            {
                if (format == null)
                    return string.Empty;

                try
                {
                    return args == null || args.Length == 0 ? format : string.Format(format, args);
                }
                catch (FormatException)
                {
                    return format;
                }
            }
        }
    }
}
