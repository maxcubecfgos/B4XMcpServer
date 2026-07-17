using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace B4XMcpServer.Services
{
    /// <summary>
    /// DI-registered singleton that exposes the bundled B4X development
    /// reference (the same content the AGENTS.md installer drops next to a
    /// B4X project at <c>.b4x-mcp/skills/b4x/reference.md</c>) to MCP tools.
    /// <para>
    /// The reference is embedded as an <c>EmbeddedResource</c> in the
    /// assembly; on first access it is read once and split into named
    /// sections by <c>##</c> headings. Subsequent queries are served from
    /// the in-memory cache, so adding <see cref="GetSection"/>,
    /// <see cref="SearchSections"/>, etc. to a hot path is cheap.
    /// </para>
    /// <para>
    /// This is intentionally a separate type from
    /// <see cref="SkillInstaller"/>: the installer writes the reference to
    /// disk for the AI agent to consume directly; this knowledge base lets
    /// the MCP tools surface structured slices of the same content without
    /// round-tripping through the filesystem.
    /// </para>
    /// </summary>
    public sealed class B4xKnowledgeBase
    {
        /// <summary>
        /// Embedded-resource name of the bundled reference. Must match the
        /// <c>LogicalName</c> declared in <c>B4XMcpServer.csproj</c>.
        /// </summary>
        public const string ReferenceResourceName =
            "B4XMcpServer.Resources.Skills.B4x.reference.md";

        private readonly object _gate = new();
        private string? _rawContent;
        private IReadOnlyList<ReferenceSection>? _sections;

        /// <summary>
        /// Returns the full reference content as a string. Loads on first
        /// call; subsequent calls are O(1).
        /// </summary>
        public string GetRawContent()
        {
            EnsureLoaded();
            return _rawContent!;
        }

        /// <summary>
        /// Returns the section whose heading matches <paramref name="name"/>
        /// (case-insensitive substring match against the leading section
        /// heading), or <c>null</c> if no match. The returned string is the
        /// full section body — heading line plus content — trimmed of trailing
        /// whitespace.
        /// </summary>
        /// <param name="name">
        /// Substring to look for in section headings. Examples that work:
        /// <c>"sqlite"</c> matches <c>"Database (SQLite)"</c>;
        /// <c>"xui"</c> matches <c>"XUI Library (Cross-Platform Foundation)"</c>.
        /// </param>
        public string? GetSection(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            EnsureLoaded();
            var needle = name.Trim();
            // Prefer an exact heading match; fall back to case-insensitive
            // substring so callers can pass a short token like "sqlite".
            var exact = _sections!.FirstOrDefault(s =>
                string.Equals(s.Heading, needle, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact.Content;
            return _sections!.FirstOrDefault(s =>
                s.Heading.Contains(needle, StringComparison.OrdinalIgnoreCase))?.Content;
        }

        /// <summary>
        /// Enumerates the section names in document order. Useful when an
        /// agent wants to discover what's available without guessing.
        /// </summary>
        public IReadOnlyList<string> ListSections()
        {
            EnsureLoaded();
            return _sections!.Select(s => s.Heading).ToList();
        }

        /// <summary>
        /// Searches every section for <paramref name="query"/> (case-insensitive
        /// substring) and returns up to <paramref name="maxResults"/> snippets
        /// — one per matching section — each clipped to a short window around
        /// the first hit so the response stays compact.
        /// </summary>
        public IReadOnlyList<ReferenceSearchHit> SearchSections(
            string query,
            int maxResults = 10,
            int snippetRadius = 160)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<ReferenceSearchHit>();
            EnsureLoaded();
            if (maxResults <= 0) maxResults = 10;
            if (snippetRadius <= 0) snippetRadius = 160;

            var hits = new List<ReferenceSearchHit>();
            foreach (var section in _sections!)
            {
                int idx = section.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                int start = Math.Max(0, idx - snippetRadius);
                int end = Math.Min(section.Content.Length, idx + query.Length + snippetRadius);
                string snippet = section.Content.Substring(start, end - start).Trim();
                if (start > 0) snippet = "…" + snippet;
                if (end < section.Content.Length) snippet += "…";
                hits.Add(new ReferenceSearchHit(section.Heading, snippet));
                if (hits.Count >= maxResults) break;
            }
            return hits;
        }

        private void EnsureLoaded()
        {
            if (_sections is not null) return;
            lock (_gate)
            {
                if (_sections is not null) return;
                string raw = ReadEmbeddedResource();
                _rawContent = raw;
                _sections = ParseSections(raw);
            }
        }

        private static string ReadEmbeddedResource()
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(ReferenceResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{ReferenceResourceName}' not found in assembly " +
                    $"{asm.GetName().Name}. Available: " +
                    $"{string.Join(", ", asm.GetManifestResourceNames())}");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Splits the reference markdown into one <see cref="ReferenceSection"/>
        /// per <c>## </c> heading. The first <c>#</c>-rooted "introduction"
        /// (before the first <c>##</c>) is dropped — the reference is
        /// entirely organised by <c>##</c> sections and we don't surface a
        /// header-only stub.
        /// </summary>
        private static IReadOnlyList<ReferenceSection> ParseSections(string raw)
        {
            // Split on lines that start a level-2 heading. The reference uses
            // h2 (##) for top-level sections and h3 (###) for sub-sections,
            // which we leave embedded inside their parent section.
            var headingRegex = new Regex(@"^##\s+(.+?)\s*$",
                RegexOptions.Multiline | RegexOptions.Compiled);

            var matches = headingRegex.Matches(raw);
            if (matches.Count == 0) return Array.Empty<ReferenceSection>();

            var sections = new List<ReferenceSection>(matches.Count);
            for (int i = 0; i < matches.Count; i++)
            {
                var heading = matches[i].Groups[1].Value.Trim();
                int bodyStart = matches[i].Index + matches[i].Length;
                int bodyEnd = i + 1 < matches.Count ? matches[i + 1].Index : raw.Length;
                string body = raw.Substring(bodyStart, bodyEnd - bodyStart).Trim();
                sections.Add(new ReferenceSection(heading, body));
            }
            return sections;
        }

        /// <summary>One named section of the reference (heading + body).</summary>
        public sealed record ReferenceSection(string Heading, string Content);

        /// <summary>One search hit: the section that matched plus a clipped snippet.</summary>
        public sealed record ReferenceSearchHit(string Section, string Snippet);
    }
}