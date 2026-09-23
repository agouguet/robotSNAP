using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace RobotSNAP.Diagnostics
{
    /// <summary>
    /// Collapses a run of identical messages into its first line plus one periodic summary, so that a
    /// retrying client cannot bury the console and the editor log under the same failure.
    ///
    /// The ROS TCP connector retries its connection once per second and, with no endpoint listening, writes
    /// the same socket exception from its connection thread every time; one session of that is enough to
    /// grow the editor log by hundreds of megabytes while telling the reader nothing the first line did not
    /// already say. The filter keeps the first message of a run exactly as it was - stack trace included,
    /// because the first failure is the one worth reading - counts the repeats instead of forwarding them,
    /// and reports them in a single line naming the count and the first line of the run. The report goes out
    /// when a different message ends the run, and, if the run never ends, once every
    /// <see cref="SummaryIntervalMilliseconds"/>: a run that has already been reported keeps counting, so a
    /// permanent failure stays visible now and then instead of going quiet for the rest of the session.
    ///
    /// Rendering a message serves to recognise it, never to replace it: everything the filter is unsure
    /// about is forwarded untouched.
    /// </summary>
    public sealed class RepeatedLogFilter : ILogHandler
    {
        /// <summary>How long a run that is still going may stay silent before it is reported again.</summary>
        private const long SummaryIntervalMilliseconds = 30000L;

        /// <summary>
        /// The handler every message reaches; a null one is tolerated, because the filter runs on the logging
        /// path and a half-configured logger must not be able to break a call that would otherwise succeed.
        /// </summary>
        private readonly ILogHandler _inner;

        /// <summary>Monotonic clock in milliseconds, injected so a test can drive the reporting interval.</summary>
        private readonly Func<long> _nowMilliseconds;

        /// <summary>
        /// The default clock. A stopwatch rather than a wall clock because the interval is about how long the
        /// reader has been kept in the dark, and an edited system clock has no say in that.
        /// </summary>
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        /// <summary>
        /// Guards the run state. The connector logs from its connection thread while the main thread keeps
        /// logging its own messages, so the book-keeping is shared and has to be updated atomically.
        /// </summary>
        private readonly object _gate = new object();

        private bool _runOpen;
        private LogType _runLogType;
        private string _runText;
        private string _runFirstLine;
        private Object _runContext;

        /// <summary>Repeats counted since the last report, so a report never claims more than it saw.</summary>
        private int _suppressed;

        private long _lastReportMilliseconds;

        /// <summary>Makes <see cref="Install"/> idempotent across the static calls the runtime makes.</summary>
        private static readonly object InstallGate = new object();

        private static bool _installed;

        /// <summary>
        /// Wraps <paramref name="inner"/>, which receives the first message of every run, every message of a
        /// different run, and the periodic summaries.
        /// </summary>
        public RepeatedLogFilter(ILogHandler inner)
            : this(inner, null)
        {
        }

        /// <summary>
        /// Wraps <paramref name="inner"/> against an explicit clock, so that the reporting interval can be
        /// exercised without waiting thirty seconds for it. A null clock falls back to the wall clock.
        /// </summary>
        public RepeatedLogFilter(ILogHandler inner, Func<long> nowMilliseconds)
        {
            _inner = inner;
            _nowMilliseconds = nowMilliseconds;
        }

        /// <summary>
        /// Filters what reaches the console: the first message of a run goes out exactly as it was logged,
        /// the repeats behind it are counted rather than written, and the count is reported when the run ends
        /// or on the reporting interval.
        /// </summary>
        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            try
            {
                string text = Render(format, args);
                bool forward = Collapse(logType, text, context, out string summary, out Object summaryContext);

                Report(summary, logType, summaryContext);

                if (forward)
                    _inner.LogFormat(logType, context, format, args);
            }
            catch
            {
                // Nothing this filter does may fail loudly, so a failure anywhere above falls back on the
                // call it was given.
                ForwardFormat(logType, context, format, args);
            }
        }

        /// <summary>
        /// The same filtering for exceptions, keyed on the exception's type and message: a client retrying a
        /// refused connection raises the same exception once a second, and only the first of them is worth a
        /// stack trace.
        /// </summary>
        public void LogException(Exception exception, Object context)
        {
            try
            {
                string text = Describe(exception);
                bool forward = Collapse(LogType.Exception, text, context, out string summary, out Object summaryContext);

                Report(summary, LogType.Exception, summaryContext);

                if (forward)
                    _inner.LogException(exception, context);
            }
            catch
            {
                ForwardException(exception, context);
            }
        }

        /// <summary>
        /// Installs the filter in front of whatever handler the Unity logger uses, so a client retrying on a
        /// background thread cannot flood the console and the editor log.
        ///
        /// This runs before the first scene loads because the connector opens its connection thread on its
        /// own timing, and it installs at most once: wrapping the filter in a second copy of itself would
        /// leave the outer copy collapsing nothing it could see and forwarding every message twice.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Install()
        {
            lock (InstallGate)
            {
                if (_installed)
                    return;

                try
                {
                    ILogHandler current = Debug.unityLogger.logHandler;
                    if (current is RepeatedLogFilter)
                    {
                        _installed = true;
                        return;
                    }

                    Debug.unityLogger.logHandler = new RepeatedLogFilter(current);
                    _installed = true;
                }
                catch
                {
                    // Without a filter the project logs exactly as it did before installing one, which beats
                    // an exception thrown out of the runtime's own initialisation.
                }
            }
        }

        /// <summary>
        /// Advances the run book-keeping for one message and answers whether that message has to go out
        /// untouched.
        ///
        /// The state is touched under a lock, and the handlers are called by the callers once it is released:
        /// the inner handler is arbitrary code - Unity's own in a normal run - that may log again, and the
        /// filter must not be holding the lock while it does.
        /// </summary>
        /// <param name="logType">The type the message was logged with, part of what makes it the same message.</param>
        /// <param name="text">The rendered message, the other part of its identity.</param>
        /// <param name="context">The object the message was logged against.</param>
        /// <param name="summary">
        /// The line owed to the console, or null when the run has nothing to report. It is written by the
        /// caller, outside the lock, so that a slow handler cannot hold the run while it works.
        /// </param>
        /// <param name="summaryContext">The context the reported run was logged against.</param>
        private bool Collapse(LogType logType, string text, Object context, out string summary, out Object summaryContext)
        {
            summary = null;
            summaryContext = null;

            long now = Now();

            lock (_gate)
            {
                if (_runOpen && _runLogType == logType && string.Equals(_runText, text, StringComparison.Ordinal))
                {
                    _suppressed++;

                    if (now - _lastReportMilliseconds >= SummaryIntervalMilliseconds)
                    {
                        summary = Summarise(_runFirstLine, _suppressed);
                        summaryContext = _runContext;
                        _suppressed = 0;
                        _lastReportMilliseconds = now;
                    }

                    return false;
                }

                // Another message closes the run that was open and opens a new one in its place.
                if (_runOpen && _suppressed > 0)
                {
                    summary = Summarise(_runFirstLine, _suppressed);
                    summaryContext = _runContext;
                }

                _runOpen = true;
                _runLogType = logType;
                _runText = text;
                _runFirstLine = FirstLine(text);
                _runContext = context;
                _suppressed = 0;
                _lastReportMilliseconds = now;

                return true;
            }
        }

        /// <summary>
        /// Writes a report, or does nothing when there is none to write. A report that fails is swallowed:
        /// losing the count of a run is worth less than the next real message reaching the console.
        /// </summary>
        private void Report(string summary, LogType logType, Object context)
        {
            if (summary == null)
                return;

            try
            {
                _inner.LogFormat(logType, context, "{0}", summary);
            }
            catch
            {
                // Nothing to do: the filter never throws over a summary it could not write.
            }
        }

        /// <summary>
        /// The last line of defence: when the filter itself failed, the message it was given still has to
        /// reach the handler, and a failure of that second attempt has to stay inside the filter rather than
        /// escape into the caller's logging path.
        /// </summary>
        private void ForwardFormat(LogType logType, Object context, string format, object[] args)
        {
            try
            {
                _inner.LogFormat(logType, context, format, args);
            }
            catch
            {
                // Swallowed on purpose: a broken handler must not become an exception in the logger.
            }
        }

        /// <summary>As <see cref="ForwardFormat"/>, for an exception.</summary>
        private void ForwardException(Exception exception, Object context)
        {
            try
            {
                _inner.LogException(exception, context);
            }
            catch
            {
                // Swallowed on purpose, as above.
            }
        }

        /// <summary>
        /// Renders a message the way the handler will, because runs are keyed on the text a reader sees
        /// rather than on the format string: two different pairs of format and arguments that print the same
        /// line are the same line in the console, and only the first of them is worth a stack trace.
        /// </summary>
        private static string Render(string format, object[] args)
        {
            if (format == null)
                return string.Empty;

            return args == null || args.Length == 0 ? format : string.Format(format, args);
        }

        /// <summary>
        /// Names an exception by its type and message, so that a repeat of the same failure is recognised
        /// while two different failures stay two entries.
        /// </summary>
        private static string Describe(Exception exception)
        {
            if (exception == null)
                return string.Empty;

            return exception.GetType().FullName + ": " + exception.Message;
        }

        /// <summary>
        /// The first line of a message, which is all a report needs to point at the run it accounts for:
        /// the exceptions of the connector carry their stack on the following lines.
        /// </summary>
        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            int lineBreak = text.IndexOf('\n');
            string line = lineBreak >= 0 ? text.Substring(0, lineBreak) : text;
            return line.TrimEnd('\r', ' ', '\t');
        }

        private static string Summarise(string firstLine, int count) =>
            $"{firstLine} ... repeated {count} more times";

        private long Now()
        {
            Func<long> clock = _nowMilliseconds;
            return clock != null ? clock() : Clock.ElapsedMilliseconds;
        }
    }
}
