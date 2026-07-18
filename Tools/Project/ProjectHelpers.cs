using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace B4XMcpServer.Tools.Project
{
    // ── Manifest markers ──
    internal static class ManifestMarkers
    {
        public const string Start = "#Region Manifest Editor";
        public const string End = "#End Region";
    }

    /// <summary>Parsed metadata header fields from a .bas or .b4a/.b4j/.b4i file.</summary>
    internal sealed class ParsedHeader
    {
        public bool HasMarker { get; set; }
        public string? Platform { get; set; }
        public string? Group { get; set; }
        public string? ModulesStructureVersion { get; set; }
        public string? Type { get; set; }
        public string? Version { get; set; }
        public Dictionary<string, string> AllFields { get; set; } = null!;
        public int HeaderLineCount { get; set; }
    }

    /// <summary>
    /// Shared helper methods for the Project tools namespace.
    /// All methods and constants are internal and only used by tool classes in this directory.
    /// </summary>
    internal static class ProjectHelpersShared
    {
        public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Parses the metadata header from a B4X file's raw content.
        /// Returns null if the marker is absent (non-B4X file or corrupted).
        /// </summary>
        public static ParsedHeader? ParseFileHeader(string rawContent)
        {
            const string marker = "@EndOfDesignText@";
            int markerIdx = rawContent.IndexOf(marker, StringComparison.Ordinal);
            if (markerIdx < 0) return null;

            string headerSection = rawContent.Substring(0, markerIdx);
            var lines = headerSection.Split('\n');
            int headerLineCount = lines.Length;

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd('\r').Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                fields[trimmed.Substring(0, eq).Trim()] = trimmed.Substring(eq + 1).Trim();
            }

            string? platform = null;
            if (fields.TryGetValue("B4A", out var b4a) && string.Equals(b4a, "true", StringComparison.OrdinalIgnoreCase)) platform = "B4A";
            else if (fields.TryGetValue("B4J", out var b4j) && string.Equals(b4j, "true", StringComparison.OrdinalIgnoreCase)) platform = "B4J";
            else if (fields.TryGetValue("B4i", out var b4i) && string.Equals(b4i, "true", StringComparison.OrdinalIgnoreCase)) platform = "B4i";

            fields.TryGetValue("Group", out var group);
            fields.TryGetValue("ModulesStructureVersion", out var msv);
            fields.TryGetValue("Type", out var type);
            fields.TryGetValue("Version", out var version);

            return new ParsedHeader
            {
                HasMarker = true,
                Platform = platform,
                Group = group,
                ModulesStructureVersion = msv,
                Type = type,
                Version = version,
                AllFields = fields,
                HeaderLineCount = headerLineCount
            };
        }

        public static void ValidateSequentialNumbering(Dictionary<string, string> fields, string prefix, int expectedCount, List<string> warnings)
        {
            var numKey = $"NumberOf{prefix}s";
            if (!fields.TryGetValue(numKey, out var numStr) || !int.TryParse(numStr, out int declared))
            {
                if (expectedCount > 0)
                    warnings.Add($"⚠️ Missing {numKey} — found {expectedCount} {prefix}N entries. Add: {numKey}={expectedCount}");
                return;
            }
            if (declared != expectedCount)
                warnings.Add($"❌ {numKey}={declared} but found {expectedCount} {prefix}N entries. Update {numKey} to {expectedCount}.");

            var numbers = fields.Keys
                .Where(k => Regex.IsMatch(k, $@"^{Regex.Escape(prefix)}\d+$", RegexOptions.None, RegexTimeout))
                .Select(k => int.Parse(Regex.Match(k, @"\d+", RegexOptions.None, RegexTimeout).Value))
                .OrderBy(n => n)
                .ToList();
            for (int i = 0; i < numbers.Count; i++)
            {
                if (numbers[i] != i + 1)
                {
                    warnings.Add($"❌ {prefix} numbering is not sequential. Expected {prefix}{i + 1} but found {prefix}{numbers[i]}. Renumber all {prefix}N entries sequentially starting from 1.");
                    break;
                }
            }
        }

        /// <summary>
        /// Detects the line range of sacred region blocks (#Region Project Attributes and
        /// #Region Activity Attributes) in the source code section. Returns null if no
        /// sacred region found (non-project file or the region was removed).
        /// </summary>
        public static (int startLine, int endLine, string regionName)? FindSacredRegion(
            string codeSection, string regionName)
        {
            var lines = codeSection.Replace("\r\n", "\n").Split('\n');
            int? regionStart = null;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("#Region", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains(regionName, StringComparison.OrdinalIgnoreCase))
                {
                    regionStart = i + 1;
                }
                else if (regionStart.HasValue && line.StartsWith("#End Region", StringComparison.OrdinalIgnoreCase))
                {
                    return (regionStart.Value, i + 1, regionName);
                }
            }
            return null;
        }

        /// <summary>
        /// Checks whether the given line range [startLine, endLine] overlaps with any
        /// sacred region (#Region Project Attributes or #Region Activity Attributes).
        /// </summary>
        public static string? DetectSacredRegionEdit(List<string> codeLines, int startLine, int endLine, string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".b4a" && ext != ".b4j" && ext != ".b4i") return null;

            string codeSection = string.Join("\n", codeLines);
            var overlaps = new List<string>();

            foreach (var regionName in new[] { "Project Attributes", "Activity Attributes" })
            {
                var region = FindSacredRegion(codeSection, regionName);
                if (region == null) continue;

                if (startLine <= region.Value.endLine && endLine >= region.Value.startLine)
                {
                    overlaps.Add($"#Region {region.Value.regionName} (lines {region.Value.startLine}-{region.Value.endLine})");
                }
            }

            if (overlaps.Count == 0) return null;

            return $"This edit overlaps with SACRED REGION(S): {string.Join(", ", overlaps)}. " +
                   "These blocks contain #ApplicationLabel, #VersionCode, #FullScreen, #IncludeTitle — essential IDE settings. " +
                   "Editing them can corrupt the project and break compilation. Proceed only if absolutely sure.";
        }

        public static string? SuggestClosestSubName(string requested, List<string> available)
        {
            if (available.Count == 0) return null;

            var exact = available.FirstOrDefault(a => string.Equals(a, requested, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            string best = available[0];
            int bestDist = int.MaxValue;
            foreach (var candidate in available)
            {
                int dist = LevenshteinDistance(requested.ToLowerInvariant(), candidate.ToLowerInvariant());
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }

            bool substringMatch = requested.IndexOf(best, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  best.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0;
            if (bestDist <= 3 || substringMatch)
                return best;

            return null;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            int n = a.Length, m = b.Length;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        public static string? ExtractManifestBlock(string raw)
        {
            int startIdx = raw.IndexOf(ManifestMarkers.Start, StringComparison.Ordinal);
            if (startIdx < 0) return null;
            int contentStart = startIdx + ManifestMarkers.Start.Length;
            int endIdx = raw.IndexOf(ManifestMarkers.End, contentStart, StringComparison.Ordinal);
            if (endIdx < 0) return null;
            return raw.Substring(contentStart, endIdx - contentStart).Trim('\r', '\n');
        }
    }
}
