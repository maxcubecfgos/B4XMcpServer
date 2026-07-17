using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace B4XMcpServer.Services
{
    /// <summary>
    /// Sibling to <see cref="B4xProjectInstaller"/> that bundles two B4X
    /// reference markdown files into every B4X project the MCP server creates
    /// or updates AGENTS.md for.
    /// <para>
    /// The two files (a YAML-frontmatter skill manifest and its linked
    /// <c>reference.md</c>) are embedded as <c>EmbeddedResource</c> items in the
    /// assembly and ship inside the single-file exe via PublishSingleFile. On
    /// AGENTS.md installation they are copied to
    /// <c>.b4x-mcp/skills/b4x/SKILL.md</c> and
    /// <c>.b4x-mcp/skills/b4x/reference.md</c> under the project root so any
    /// agent can pick them up at session start.
    /// </para>
    /// <para>
    /// Idempotent across re-runs: a sidecar <c>.installed-by-mcp</c> marker is
    /// written alongside the skill, recording the installer's version. If the
    /// marker matches the running executable's version we treat the install as
    /// current and skip; if the marker reports an older version we refresh
    /// (auto-upgrade); if the SKILL.md exists without our marker we leave it
    /// alone — the user authored it. Each detect-then-write path is logged to
    /// <see cref="Console.Error"/> with a clear, actionable message.
    /// </para>
    /// </summary>
    public static class SkillInstaller
    {
        /// <summary>Subdirectory under the project root that holds installed skills.</summary>
        public const string SkillsRoot = ".b4x-mcp/skills";

        /// <summary>Skill name for the bundled B4X reference. Single source of truth.</summary>
        public const string B4xSkillName = "b4x";

        /// <summary>Sidecar marker file name. Hidden on Unix and ignored by most agents.</summary>
        public const string MarkerFileName = ".installed-by-mcp";

        /// <summary>Marker prefix used to distinguish our line (we now record version+timestamp).</summary>
        public const string MarkerPrefix = "B4XMcpServer";

        /// <summary>Embedded-resource name for the B4X skill manifest (YAML frontmatter + brief body).</summary>
        public const string B4xSkillResourceName = "B4XMcpServer.Resources.Skills.B4x.SKILL.md";

        /// <summary>Embedded-resource name for the full B4X reference content.</summary>
        public const string B4xReferenceResourceName = "B4XMcpServer.Resources.Skills.B4x.reference.md";

        /// <summary>
        /// Result of <see cref="TryInstall"/>. <see cref="Installed"/> means we
        /// wrote (or refreshed) the skill; <see cref="AlreadyInstalled"/> is a
        /// fast no-op against a clean install from the same exe version;
        /// <see cref="Skipped"/> means we deliberately did not write — either
        /// the user owns the file at the same path, or the filesystem rejected
        /// us. Callers do not normally need to inspect this — error and skip
        /// cases are both recoverable.
        /// </summary>
        public enum Outcome
        {
            Installed,
            AlreadyInstalled,
            Skipped,
        }

        /// <summary>Convenience: returns the directory the skill is (or would be) installed into.</summary>
        public static string SkillDirectory(string projectDir)
            => Path.Combine(projectDir, SkillsRoot, B4xSkillName);

        /// <summary>Convenience: file path the skill manifest is installed at.</summary>
        public static string SkillManifestPath(string projectDir)
            => Path.Combine(SkillDirectory(projectDir), "SKILL.md");

        /// <summary>Convenience: file path the reference is installed at.</summary>
        public static string SkillReferencePath(string projectDir)
            => Path.Combine(SkillDirectory(projectDir), "reference.md");

        /// <summary>
        /// Installs or refreshes the bundled B4X skill at <paramref name="projectDir"/>.
        /// Idempotent across consecutive runs and across upgrades of the
        /// running executable (auto-upgrade is performed when the sidecar
        /// marker reports an older version).
        /// </summary>
        /// <param name="projectDir">Absolute path of the B4X project directory.</param>
        public static Outcome TryInstall(string projectDir)
        {
            if (string.IsNullOrEmpty(projectDir)) return Outcome.Skipped;

            string skillDir = SkillDirectory(projectDir);
            string targetSkill = Path.Combine(skillDir, "SKILL.md");
            string targetReference = Path.Combine(skillDir, "reference.md");
            string markerPath = Path.Combine(skillDir, MarkerFileName);

            try
            {
                string? installedVersion = ReadInstalledVersion(markerPath);
                string currentVersion = CurrentVersionString();
                bool skillExists = File.Exists(targetSkill);
                bool refExists = File.Exists(targetReference);

                // "Same or newer" semantics, not strict equality:
                //   - identical version   -> skip (no work to do)
                //   - installed newer     -> skip (a newer exe (or a human) put
                //                            this here; running an older exe must
                //                            NOT clobber the newer content)
                //   - installed older     -> refresh (auto-upgrade)
                //   - installed unparseable-> treat as mismatch and refresh
                // Unparseable installed version is safer to refresh than to
                // leave stale content in place.
                if (skillExists && refExists
                    && installedVersion != null
                    && !IsInstalledOlder(installedVersion, currentVersion))
                {
                    return Outcome.AlreadyInstalled;
                }

                if ((skillExists || refExists) && installedVersion == null)
                {
                    // User-authored content at the same path. Refuse to clobber,
                    // but tell the user how to clear the obstruction.
                    Log($"⚠ Skipping '{B4xSkillName}' skill install in {skillDir}: existing " +
                        "SKILL.md or reference.md without our .installed-by-mcp marker. " +
                        "Delete both files (or the marker) to let the installer write them.");
                    return Outcome.Skipped;
                }

                Directory.CreateDirectory(skillDir);

                string skillContent = ReadEmbeddedResource(B4xSkillResourceName);
                string referenceContent = ReadEmbeddedResource(B4xReferenceResourceName);

                WriteAtomic(targetSkill, skillContent);
                WriteAtomic(targetReference, referenceContent);
                WriteAtomic(markerPath, BuildMarker());

                // "Refreshed (was X, now Y)" only makes sense when we actually had a
                // parseable prior version to compare. If the prior marker was
                // missing or unparseable, we did still write — just don't pretend
                // we know what we upgraded from.
                if (installedVersion != null
                    && Version.TryParse(installedVersion, out _)
                    && IsInstalledOlder(installedVersion, currentVersion))
                {
                    Log($"✓ Refreshed bundled '{B4xSkillName}' skill in {skillDir} " +
                        $"(was {installedVersion}, now {currentVersion})");
                }
                else
                {
                    Log($"✓ Installed bundled '{B4xSkillName}' skill in {skillDir}");
                }
                return Outcome.Installed;
            }
            catch (UnauthorizedAccessException ex)
            {
                Log($"⚠ Skill install skipped (permission denied in {skillDir}): {ex.Message}");
                return Outcome.Skipped;
            }
            catch (DirectoryNotFoundException ex)
            {
                Log($"⚠ Skill install skipped (directory not creatable in {skillDir}): {ex.Message}");
                return Outcome.Skipped;
            }
            catch (IOException ex)
            {
                Log($"⚠ Skill install skipped (I/O error in {skillDir}): {ex.Message}");
                return Outcome.Skipped;
            }
            catch (Exception ex)
            {
                Log($"⚠ Skill install failed in {skillDir}: {ex.GetType().Name}: {ex.Message}");
                return Outcome.Skipped;
            }
        }

        /// <summary>
        /// Reads the embedded resource stream into a string. Throws if the
        /// resource is missing — callers should treat that as a build
        /// configuration error and surface it.
        /// </summary>
        private static string ReadEmbeddedResource(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{name}' not found in assembly {asm.GetName().Name}. " +
                    $"Available: {string.Join(", ", asm.GetManifestResourceNames())}");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Writes <paramref name="content"/> to <paramref name="path"/>
        /// atomically: write to a sibling <c>.tmp</c> first, then rename. If the
        /// process is killed mid-write the destination file is never
        /// partially overwritten, so the next run can either replace it cleanly
        /// or leave it alone.
        /// </summary>
        private static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            // UTF-8 without BOM — preferred for cross-platform markdown files;
            // BOM is invisible but breaks some YAML frontmatter parsers.
            File.WriteAllText(tmp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
            {
                // File.Replace is atomic on the same volume and replaces the
                // destination in one filesystem operation on Windows.
                File.Replace(tmp, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        /// <summary>
        /// Reads the version string from the sidecar marker file, or returns
        /// <c>null</c> if no marker exists. Tolerates missing files, empty
        /// files, and unexpected line content — anything we can't parse is
        /// treated as "unknown" so the caller falls back to a fresh install.
        /// </summary>
        private static string? ReadInstalledVersion(string markerPath)
        {
            try
            {
                if (!File.Exists(markerPath)) return null;
                // Marker format: "B4XMcpServer <version> on <UTC timestamp>"
                var firstLine = File.ReadAllLines(markerPath).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(firstLine)) return null;
                var parts = firstLine.Split(' ');
                if (parts.Length < 2) return null;
                if (!parts[0].Equals(MarkerPrefix, StringComparison.Ordinal)) return null;
                return parts[1];
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Builds the sidecar marker line. Format is stable: prefix + version
        /// + timestamp. Anything past the first two tokens is informational.
        /// </summary>
        private static string BuildMarker()
        {
            string version = CurrentVersionString();
            string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            return $"{MarkerPrefix} {version} on {ts}\n";
        }

        /// <summary>
        /// Returns the running executable's assembly version as a four-component
        /// string. Falls back to "0.0.0.0" when no version is set (e.g. local
        /// builds without [AssemblyVersion]) so any real published build will
        /// be considered newer and trigger an upgrade.
        /// </summary>
        private static string CurrentVersionString()
        {
            var asm = Assembly.GetExecutingAssembly();
            return asm.GetName().Version?.ToString() ?? "0.0.0.0";
        }

        /// <summary>
        /// Returns <c>true</c> when the installed version should be treated as
        /// older than <paramref name="current"/> — which the caller interprets
        /// as "skip the install" only in the negation; see below for the full
        /// table.
        /// <para>
        /// Decision matrix (caller does <c>skip = !IsInstalledOlder(...)</c>):
        /// <list type="bullet">
        ///   <item><description><paramref name="installed"/> unparseable &#8594; return <c>true</c> &#8594; skip = false &#8594; refresh. (Treating an unknown marker as "stale" is safer than leaving possibly-bad content in place.)</description></item>
        ///   <item><description><paramref name="current"/> unparseable &#8594; return <c>false</c> &#8594; skip = true &#8594; do not write. (Defensive: the running exe always has a version string, so this should never fire in practice; if it does, we'd rather leave existing content intact than overwrite with a broken write path.)</description></item>
        ///   <item><description>installed &lt; current &#8594; return <c>true</c> &#8594; refresh (auto-upgrade).</description></item>
        ///   <item><description>installed == current &#8594; return <c>false</c> &#8594; skip silently.</description></item>
        ///   <item><description>installed &gt; current &#8594; return <c>false</c> &#8594; skip (downgrade of the running exe must NOT clobber newer content).</description></item>
        /// </list>
        /// </para>
        /// </summary>
        private static bool IsInstalledOlder(string installed, string current)
        {
            if (!Version.TryParse(installed, out var iv)) return true;
            if (!Version.TryParse(current, out var cv)) return false;
            return iv < cv;
        }

        /// <summary>
        /// Diagnostic output goes to <see cref="Console.Error"/> only — never
        /// stdout, which is reserved for MCP JSON-RPC frames and for tool JSON
        /// output during CLI invocation. Mirrors <see cref="B4xProjectInstaller.Log"/>.
        /// </summary>
        private static void Log(string message)
        {
            Console.Error.WriteLine(message);
        }
    }
}
