using System;
using System.Collections.Generic;
using System.Globalization;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Names the groups of the scenario editor without depending on Unity.
    ///
    /// The scenario editor no longer asks for a group identifier: it allocates one through
    /// <see cref="NextId"/> and only shows the readable spelling of that identifier. A group written by
    /// hand keeps its own spelling, so both directions leave anything that does not follow the generated
    /// pattern untouched.
    /// </summary>
    public static class GroupNaming
    {
        /// <summary>Préfixe des identifiants alloués automatiquement.</summary>
        public const string Prefix = "group_";

        /// <summary>Préfixe lisible, accepté à la lecture comme à l'écriture (la lecture ignore la casse).</summary>
        private const string DisplayPrefix = "Group";

        /// <summary>"group_3" -> "Group 3". Un identifiant écrit à la main qui ne suit pas le motif est rendu tel quel
        /// (espaces autour retirés). null / blanc -> "".</summary>
        public static string ToDisplayName(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
                return string.Empty;

            string trimmed = groupId.Trim();
            if (!TryReadNumber(trimmed, out int number))
                return trimmed;

            return DisplayPrefix + " " + Format(number);
        }

        /// <summary>"Group 3" -> "group_3" (insensible à la casse, espaces tolérés). Une étiquette qui ne suit pas le
        /// motif est rendue telle quelle, seulement nettoyée. null / blanc -> "".</summary>
        public static string ToGroupId(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return string.Empty;

            string trimmed = displayName.Trim();
            if (!TryReadNumber(trimmed, out int number))
                return trimmed;

            return Prefix + Format(number);
        }

        /// <summary>Plus petit identifiant "group_N" libre parmi ceux déjà utilisés, en partant de 1.
        /// Les identifiants non conformes au motif sont ignorés pour le calcul.</summary>
        public static string NextId(IEnumerable<string> usedIds)
        {
            var used = new HashSet<int>();

            if (usedIds != null)
            {
                foreach (string id in usedIds)
                {
                    if (TryReadNumber(id, out int number))
                        used.Add(number);
                }
            }

            int candidate = 1;
            while (used.Contains(candidate))
                candidate++;

            return Prefix + Format(candidate);
        }

        /// <summary>
        /// Reads the number of a generated identifier or of its readable spelling. The separator has to be
        /// there, so <c>group1</c> stays hand-written; the digits have to be canonical, so <c>group_0</c> and
        /// <c>group_007</c> stay hand-written too. Every other input is a manual name.
        /// </summary>
        private static bool TryReadNumber(string value, out int number)
        {
            number = 0;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            if (trimmed.Length <= DisplayPrefix.Length)
                return false;

            if (string.Compare(trimmed, 0, DisplayPrefix, 0, DisplayPrefix.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return false;

            int separatorEnd = DisplayPrefix.Length;
            while (separatorEnd < trimmed.Length && IsSeparator(trimmed[separatorEnd]))
                separatorEnd++;

            if (separatorEnd == DisplayPrefix.Length)
                return false;

            int digitsEnd = separatorEnd;
            while (digitsEnd < trimmed.Length && trimmed[digitsEnd] >= '0' && trimmed[digitsEnd] <= '9')
                digitsEnd++;

            if (digitsEnd == separatorEnd || digitsEnd != trimmed.Length)
                return false;

            if (trimmed[separatorEnd] == '0')
                return false;

            int parsed = 0;
            for (int index = separatorEnd; index < digitsEnd; index++)
            {
                int digit = trimmed[index] - '0';
                if (parsed > (int.MaxValue - digit) / 10)
                    return false;

                parsed = parsed * 10 + digit;
            }

            number = parsed;
            return true;
        }

        private static bool IsSeparator(char value) => value == '_' || char.IsWhiteSpace(value);

        private static string Format(int number) => number.ToString(CultureInfo.InvariantCulture);
    }
}
