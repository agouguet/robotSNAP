using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotSNAP.Core
{
    /// <summary>Why a name typed for a configuration cannot be given to one of its files.</summary>
    public enum ConfigNameProblem
    {
        /// <summary>The name can be used as it is.</summary>
        None,

        /// <summary>Nothing was typed, or only blanks were.</summary>
        Empty,

        /// <summary>The name is longer than <see cref="ConfigPersistence.MaxNameLength"/>.</summary>
        TooLong,

        /// <summary>The name carries a character a file name cannot carry.</summary>
        InvalidCharacter,

        /// <summary>The name is one a path is built from - <c>.</c> or <c>..</c>.</summary>
        Reserved,

        /// <summary>A configuration of that name is already saved.</summary>
        Taken
    }

    /// <summary>
    /// Single filesystem boundary for user-owned simulation configurations.
    /// </summary>
    public static class ConfigPersistence
    {
        private const string ConfigDirectoryName = "Configs";

        /// <summary>File extension a configuration is stored under, and the one a typed name may carry.</summary>
        private const string FileExtension = ".json";

        /// <summary>
        /// Longest name a configuration may carry. The name becomes a file name on every platform the
        /// application runs on, and those limits are lower than the ones of the field it is typed into.
        /// </summary>
        public const int MaxNameLength = 64;

        /// <summary>
        /// The characters a name will never carry. The platform's own invalid set is not enough by itself:
        /// a profile written on one machine is read on another, so what this refuses is the union of what
        /// any of them refuses.
        /// </summary>
        private static readonly HashSet<char> ForbiddenCharacters = BuildForbiddenCharacters();

        /// <summary>
        /// A folder to use in place of the real one, and null - the default - for the real one. It exists
        /// for tests: where the files go is the one thing a test cannot choose for itself, and the
        /// alternative is writing profiles into the folder the application's own user owns.
        /// </summary>
        public static string DirectoryOverride { get; set; }

        public static string ConfigDirectoryPath => string.IsNullOrEmpty(DirectoryOverride)
            ? Path.Combine(Application.persistentDataPath, ConfigDirectoryName)
            : DirectoryOverride;

        public static void Save(SimulationConfig config, string fileName)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            Directory.CreateDirectory(ConfigDirectoryPath);
            config.SaveToJson(GetPath(fileName));
        }

        public static bool TryLoad(string fileName, out SimulationConfig config)
        {
            string path = GetPath(fileName);
            if (!File.Exists(path))
            {
                path = Path.Combine(Application.streamingAssetsPath, ConfigDirectoryName, Path.GetFileName(path));
                if (!File.Exists(path))
                {
                    config = null;
                    return false;
                }
            }

            config = SimulationConfig.LoadFromJson(path);
            return config != null;
        }

        /// <summary>
        /// Removes a saved configuration. Only the user's own folder is written to: a file that ships beside
        /// the application belongs to the project, and is reported as not removed rather than deleted.
        /// </summary>
        public static bool Delete(string fileName)
        {
            string path = GetPath(fileName);
            if (!File.Exists(path))
                return false;

            try
            {
                File.Delete(path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static IReadOnlyList<string> GetAvailableNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNamesFromDirectory(ConfigDirectoryPath, names);
            AddNamesFromDirectory(Path.Combine(Application.streamingAssetsPath, ConfigDirectoryName), names);
            return new List<string>(names);
        }

        private static void AddNamesFromDirectory(string directory, ISet<string> names)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "*" + FileExtension))
                names.Add(Path.GetFileNameWithoutExtension(file));
        }

        /// <summary>
        /// Whether a name the user typed can be given to a configuration, and the cleaned form it would be
        /// written under.
        ///
        /// The cleaning is what makes the answer match the list the page shows: the extension is dropped,
        /// blanks around the name are dropped, and the comparison against a saved name ignores case - a
        /// file system that ignores it would otherwise hold two profiles that read as one.
        /// </summary>
        public static ConfigNameProblem CheckName(
            string name,
            IEnumerable<string> existingNames,
            out string cleanName)
        {
            cleanName = (name ?? string.Empty).Trim();

            // A name typed with the extension a profile is stored under still means the profile, and the
            // list shows names without it - "x.json" beside "x" would read as two of the same.
            if (cleanName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
                cleanName = cleanName.Substring(0, cleanName.Length - FileExtension.Length).Trim();

            if (cleanName.Length == 0)
                return ConfigNameProblem.Empty;

            if (cleanName.Length > MaxNameLength)
                return ConfigNameProblem.TooLong;

            if (cleanName == "." || cleanName == "..")
                return ConfigNameProblem.Reserved;

            foreach (char character in cleanName)
            {
                if (character < ' ' || character == '\u007f' || ForbiddenCharacters.Contains(character))
                    return ConfigNameProblem.InvalidCharacter;
            }

            // Defence in depth: a name that does not survive being read back as a file name would have been
            // resolved somewhere else. Nothing above should let one through, and this is what says so.
            if (!string.Equals(Path.GetFileName(cleanName), cleanName, StringComparison.Ordinal))
                return ConfigNameProblem.InvalidCharacter;

            if (existingNames != null)
            {
                foreach (string existing in existingNames)
                {
                    if (string.Equals(existing, cleanName, StringComparison.OrdinalIgnoreCase))
                        return ConfigNameProblem.Taken;
                }
            }

            return ConfigNameProblem.None;
        }

        public static string GetPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("A configuration file name is required.", nameof(fileName));

            string safeName = Path.GetFileName(fileName);
            if (!safeName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) safeName += FileExtension;
            return Path.Combine(ConfigDirectoryPath, safeName);
        }

        private static HashSet<char> BuildForbiddenCharacters()
        {
            var forbidden = new HashSet<char>(Path.GetInvalidFileNameChars());
            forbidden.UnionWith(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '\0' });
            return forbidden;
        }
    }
}
