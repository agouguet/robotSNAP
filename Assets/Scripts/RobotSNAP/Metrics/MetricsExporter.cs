using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RobotSNAP.Metrics
{
    /// <summary>Where an export landed, so a caller - a log line, a router answer - can name the files.</summary>
    public readonly struct MetricsExportReport
    {
        public readonly string Directory;
        public readonly string SessionFile;
        public readonly string CsvFile;
        public readonly string IndexFile;

        public MetricsExportReport(string directory, string sessionFile, string csvFile, string indexFile)
        {
            Directory = directory;
            SessionFile = sessionFile;
            CsvFile = csvFile;
            IndexFile = indexFile;
        }
    }

    /// <summary>
    /// Writes a session to disk under <c>StreamingAssets/metrics/</c>, in a format Python reads without Unity
    /// and a human reads without Python:
    ///
    ///   <c>metrics_index.json</c>              one entry per session this machine ever exported, newest last,
    ///                                          each naming the session file and the CSV that go with it
    ///   <c>session_&lt;id&gt;.json</c>             every episode of one session, trajectories included
    ///   <c>session_&lt;id&gt;.csv</c>              the same episodes as a table, trajectories left out
    ///
    /// One file per session rather than one per episode, and one flat folder: a training session runs hundreds
    /// of episodes, and a folder holding hundreds of files is a folder nobody can list. The session file is
    /// rewritten whole after every finished episode, so the file on disk is always the session as it stands;
    /// the last rewrite of a session is therefore also its final record, and a reader that opens it mid-run
    /// sees complete episodes only, never a half-written one - the write goes through a temporary file and a
    /// move, so a reader never observes a truncated document either.
    ///
    /// None of this throws: an export that fails - a read-only folder, a full disk - is reported through the
    /// returned report being empty and through the exception message in <c>LastError</c>, because an episode
    /// that has already been recorded and published must not be lost to a file system problem.
    /// </summary>
    public static class MetricsExporter
    {
        /// <summary>Last failure of an export, or null when the last one succeeded.</summary>
        public static string LastError { get; private set; }

        /// <summary>Folder the export writes to when the caller names none: <c>StreamingAssets/metrics</c>.</summary>
        public static string DefaultRoot
            => Path.Combine(Application.streamingAssetsPath, MetricsContract.ExportFolder);

        /// <summary>
        /// Exports the store's current session and updates the index. <paramref name="root"/> overrides the
        /// folder, which is how a test writes to a temporary directory instead of the project.
        /// </summary>
        public static MetricsExportReport Export(MetricsStore store, string root = null)
        {
            if (store == null) return default;

            string directory = string.IsNullOrEmpty(root) ? DefaultRoot : root;
            string sessionFile = Path.Combine(directory, MetricsContract.SessionFileName(store.SessionId));
            string csvFile = Path.Combine(directory, MetricsContract.SessionCsvFileName(store.SessionId));
            string indexFile = Path.Combine(directory, MetricsContract.IndexFileName);

            try
            {
                Directory.CreateDirectory(directory);
                WriteAtomic(sessionFile, store.ToSessionJson());
                WriteAtomic(csvFile, store.ToCsv());
                WriteAtomic(indexFile, MergeIndex(indexFile, store));
                LastError = null;
                return new MetricsExportReport(directory, sessionFile, csvFile, indexFile);
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                Debug.LogWarning($"[MetricsExporter] export to '{directory}' failed: {exception.Message}");
                return default;
            }
        }

        /// <summary>
        /// Exports one episode on its own, so a run can be handed over without the session it belongs to. The
        /// file sits next to the session export, is named after the episode and is written whole, so a reader
        /// of a single run never has to open - or be given - the session's file. The index is deliberately not
        /// touched: it describes sessions, an episode is not one, and a reader of an index a previous version
        /// wrote must go on reading the same shape.
        /// </summary>
        public static MetricsExportReport ExportEpisode(MetricsStore store, string episodeId, string root = null)
        {
            if (store == null) return default;

            EpisodeMetrics episode = store.Get(episodeId);
            if (episode == null)
            {
                LastError = $"no episode '{episodeId}' in session {store.SessionId}";
                return default;
            }

            string directory = string.IsNullOrEmpty(root) ? DefaultRoot : root;
            string file = Path.Combine(directory, "episode_" + episode.Id + ".json");

            try
            {
                Directory.CreateDirectory(directory);
                WriteAtomic(file, JsonConvert.SerializeObject(episode, Formatting.Indented));
                LastError = null;
                return new MetricsExportReport(directory, file, string.Empty, string.Empty);
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                Debug.LogWarning($"[MetricsExporter] export of '{episodeId}' to '{directory}' failed: {exception.Message}");
                return default;
            }
        }

        /// <summary>
        /// Reads the index, replaces the entry of this session and returns the document. The file is the only
        /// record of the sessions a machine exported, so a session exported twice - the store grows after every
        /// episode - updates its own entry instead of appearing twice.
        /// </summary>
        private static string MergeIndex(string indexFile, MetricsStore store)
        {
            var sessions = new List<JObject>();

            if (File.Exists(indexFile))
            {
                try
                {
                    JObject existing = JObject.Parse(File.ReadAllText(indexFile));
                    if (existing["sessions"] is JArray entries)
                    {
                        foreach (JToken entry in entries)
                        {
                            if (entry is JObject entryObject &&
                                (string)entryObject["id"] != store.SessionId)
                            {
                                sessions.Add(entryObject);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // An index a previous version wrote in another shape is rebuilt rather than allowed to stop
                    // an export: the session files are the record, the index is only the way to find them.
                }
            }

            sessions.Add(new JObject
            {
                ["id"] = store.SessionId,
                ["started_at"] = store.StartedAt,
                ["exported_at"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["episode_count"] = store.Count,
                ["file"] = MetricsContract.SessionFileName(store.SessionId),
                ["csv"] = MetricsContract.SessionCsvFileName(store.SessionId),
            });

            var document = new JObject
            {
                ["updated_at"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["sessions"] = new JArray(sessions),
            };
            return document.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Writes through a temporary file and moves it into place, so a reader that opens the destination while
        /// it is being rewritten sees either the previous complete document or the new one, never a half-written
        /// one. The temporary name is derived from the destination, so two exports cannot collide on it.
        /// </summary>
        private static void WriteAtomic(string path, string content)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, content);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temporary, path);
        }
    }
}
