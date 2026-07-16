using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using B4XMcpServer.Services;

namespace B4XMcpServer.Engine
{
    /// <summary>
    /// One parsed Java stack frame with a best-effort mapping to a B4X source location.
    /// </summary>
    public sealed class B4xFrame
    {
        /// <summary>The raw Java method name as it appeared in the trace (e.g. "b4j.example.main._panroot_resize").</summary>
        public string JavaMethod { get; init; } = "";

        /// <summary>The Java source file referenced in the frame (e.g. "main.java"), if present.</summary>
        public string? JavaFile { get; init; }

        /// <summary>The Java line number from the trace, if present.</summary>
        public int? JavaLine { get; init; }

        /// <summary>The B4X module this frame appears to belong to.</summary>
        public string? ModuleName { get; init; }

        /// <summary>The original-case B4X Sub name this frame maps to (best effort).</summary>
        public string? SubName { get; init; }

        /// <summary>The B4X source file (.b4j or .bas) containing the Sub, if known.</summary>
        public string? B4xFile { get; init; }

        /// <summary>The B4X source line, resolved via B4J's debugLine comments when possible.</summary>
        public int? B4xLine { get; init; }

        /// <summary>A heuristic suggestion for the root cause, if we can identify one.</summary>
        public string? SuspectedCause { get; init; }
    }

    /// <summary>
    /// Translates raw Java stack traces (captured by RuntimeLauncher) into B4X source
    /// locations and applies heuristics to suggest likely root causes.
    ///
    /// Strategy:
    ///   1. Disambiguate "package.module._subname" by matching the segment BEFORE the first
    ///      underscore against the project's known module names. The first underscore is the
    ///      B4J generator's separator — everything after it (preserving underscores) is the
    ///      lowercase B4X Sub name.
    ///   2. Match that lowercase name against the lowercased Sub names parsed from every
    ///      .bas/.b4a/.b4j/.b4i file in the project to recover original casing.
    ///   3. For line numbers, read Objects/src/&lt;package-path&gt;/&lt;module&gt;.java (the generated
    ///      Java source B4J emits alongside the jar) and scan backwards from the reported
    ///      Java line for the closest "//BA.debugLineNum = N;" marker — B4J inserts these
    ///      comments at every B4X source line so the mapping back to B4X is exact. We only
    ///      fall back to "same as Java line" when the generated Java source isn't available.
    /// </summary>
    public static class RuntimeErrorMapper
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        // "at fully.qualified.Name(Class.java:123)"  or "at Name(SomeOther.java:Unknown Source)"
        private static readonly Regex FrameFullRegex = new Regex(
            @"^\s+at\s+([^\s(]+)(?:\(([^)]+)\))?",
            RegexOptions.Compiled,
            RegexTimeout);

        // B4J marker: "//BA.debugLineNum = 789;"
        private static readonly Regex DebugLineRegex = new Regex(
            @"//BA\.debugLineNum\s*=\s*(\d+)",
            RegexOptions.Compiled,
            RegexTimeout);

        /// <summary>
        /// Maps each Java stack frame string to a B4xFrame with best-effort B4X location.
        /// </summary>
        public static List<B4xFrame> MapStackTrace(IEnumerable<string> rawFrames, string projectRoot)
        {
            var result = new List<B4xFrame>();

            // Build a lookup table of lowercased-sub-name → (OriginalName, ModuleName, B4xFile).
            Index? index = TryBuildSubIndex(projectRoot);
            var packagedJavaDir = ResolveObjectsSrcDir(projectRoot);

            foreach (var raw in rawFrames)
            {
                result.Add(MapSingleFrame(raw, index, packagedJavaDir));
            }

            return result;
        }

        private sealed class Index
        {
            // Keyed on lowercased subname-with-underscores (which is exactly what B4J
            // appends to "<module>._" in the generated Java method name).
            public Dictionary<string, SubEntry> Subs { get; init; } = new(StringComparer.OrdinalIgnoreCase);

            // B4X files at the project root (e.g. "main.b4j") or .bas modules in any folder.
            // Keyed on lowercased filename-without-extension for quick "is this <module>.bas
            // somewhere in the project?" checks.
            public Dictionary<string, string> ModulePath { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class SubEntry
        {
            public string OriginalName { get; init; } = "";
            public string ModuleName { get; init; } = "";
            public string FilePath { get; init; } = "";
        }

        private static Index? TryBuildSubIndex(string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot))
                return null;

            var index = new Index();
            var files = ProjectScanner.ScanProject(projectRoot)
                .Where(f => f.Kind is "bas" or "b4a" or "b4j" or "b4i")
                .ToList();

            foreach (var f in files)
            {
                string source;
                try { source = CodeUtils.ReadTextSafely(f.Path); }
                catch { continue; }

                var (root, _) = B4xParser.Parse(source);
                if (root == null) continue;

                // ModuleName comes from the filename without extension. For .bas it might
                // shadow a same-named .b4j file, but that's fine — both can declare subs.
                string moduleFromPath = Path.GetFileNameWithoutExtension(f.Path) ?? "";

                foreach (var node in B4xParser.FlattenSubsAndTypes(root))
                {
                    if (node.Kind != "Sub") continue;

                    // The Java lowercased name uses underscores preserved; we mirror that here.
                    string lookupKey = node.Name.ToLowerInvariant();
                    index.Subs[lookupKey] = new SubEntry
                    {
                        OriginalName = node.Name,
                        // Prefer the module name from the filename (more reliable than the parser
                        // node name for B4X modules), but fall back to the parser name if needed.
                        ModuleName = string.IsNullOrEmpty(moduleFromPath) ? (root.Name ?? "") : moduleFromPath,
                        FilePath = f.Path
                    };
                }

                // Track this as a candidate module-to-file mapping.
                if (!string.IsNullOrEmpty(moduleFromPath))
                    index.ModulePath[moduleFromPath] = f.Path;
            }

            return index;
        }

        /// <summary>
        /// Returns the path to Objects/src/ — that's where B4B files (B4J's generated Java)
        /// live. Returns null if Objects/ doesn't exist or we can't determine the package path.
        /// </summary>
        private static string? ResolveObjectsSrcDir(string projectRoot)
        {
            string srcDir = Path.Combine(projectRoot, "Objects", "src");
            return Directory.Exists(srcDir) ? srcDir : null;
        }

        private static B4xFrame MapSingleFrame(string rawFrame, Index? index, string? objectsSrc)
        {
            var match = FrameFullRegex.Match(rawFrame);
            if (!match.Success)
            {
                return new B4xFrame
                {
                    JavaMethod = rawFrame.Trim(),
                    SuspectedCause = null
                };
            }

            string javaMethod = match.Groups[1].Value;
            string fileAndLine = match.Groups[2].Success ? match.Groups[2].Value : "";
            string? javaFile = null;
            int? javaLine = null;

            // Frame part is "main.java:782" or "Native Method" or "Unknown Source".
            if (fileAndLine.EndsWith(".java", StringComparison.OrdinalIgnoreCase))
            {
                int colon = fileAndLine.LastIndexOf(':');
                if (colon > 0)
                {
                    javaFile = fileAndLine.Substring(0, colon);
                    if (int.TryParse(fileAndLine.Substring(colon + 1), out int n))
                        javaLine = n;
                }
            }

            string? moduleName = null;
            string? subName = null;
            string? b4xFile = null;

            if (index != null && javaMethod.Contains('_'))
            {
                // Parse "b4j.example.main._panroot_resize" → module "main", subname "panroot_resize".
                // The separator is FIRST underscore after the dots. Everything before it (minus
                // the package prefix) is the module; everything after is the lowercased Sub name.
                string afterDots = javaMethod;

                // Drop package segments one by one until we find a segment that, combined with
                // what follows, matches a known B4X Sub. We try the longest possible module
                // name first (longest match wins), so we don't accidentally clip an underscore-
                // bearing module name.
                string[] segments = javaMethod.Split('.');
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    string candidateModule = segments[i];
                    string tail = string.Join(".", segments.Skip(i + 1));
                    int underscoreIdx = tail.IndexOf('_');
                    if (underscoreIdx < 0) continue;

                    string expectedSub = tail.Substring(underscoreIdx + 1);
                    if (index.Subs.TryGetValue(expectedSub, out var entry))
                    {
                        moduleName = entry.ModuleName;
                        subName = entry.OriginalName;
                        b4xFile = entry.FilePath;
                        break;
                    }
                }
            }

            // Resolve B4X line via Java debug comments when we can locate the generated Java source.
            // When we can't find a //BA.debugLineNum marker, leave b4xLine null rather than
            // reporting the Java line as the B4X line: B4J's Java expansion ratio (often 2-5x)
            // would mislead the AI into editing a line that doesn't exist in the .b4j file.
            int? b4xLine = null;
            if (objectsSrc != null && javaFile != null && javaLine.HasValue)
            {
                b4xLine = ResolveB4xLineFromJavaDebugComment(objectsSrc, javaFile, javaLine.Value);
            }

            return new B4xFrame
            {
                JavaMethod = javaMethod,
                JavaFile = javaFile,
                JavaLine = javaLine,
                ModuleName = moduleName,
                SubName = subName,
                B4xFile = b4xFile,
                B4xLine = b4xLine,
                SuspectedCause = null  // per-frame heuristic belongs to the caller
            };
        }

        /// <summary>
        /// Reads Objects/src/&lt;javaFile.path&gt; and scans backwards from <paramref name="javaLine"/>
        /// for the nearest "//BA.debugLineNum = N;" marker. If we find it, returns N.
        /// </summary>
        private static int? ResolveB4xLineFromJavaDebugComment(string objectsSrc, string javaFile, int javaLine)
        {
            // The Java frame usually only reports the bare filename ("main.java"). B4J stores
            // its generated source under Objects/src/<package_path>/<file>, where the package
            // path is the runtime package with dots converted to directory separators.
            // We try the most likely locations in order: file at objectsSrc root, then under
            // any subdirectory of objectsSrc.
            string? fullPath = LocateJavaSource(objectsSrc, javaFile);
            if (fullPath == null) return null;

            string[] lines;
            try { lines = File.ReadAllLines(fullPath); }
            catch { return null; }

            if (javaLine < 1 || javaLine > lines.Length) return null;

            // Scan backwards up to 200 lines — exceeding that almost always means we haven't
            // found a marker because of macro-generated code, in which case the proportional
            // fallback in the caller handles it better than an arbitrary stop.
            int from = Math.Min(javaLine - 1, lines.Length - 1);
            int to = Math.Max(0, from - 200);
            for (int i = from; i >= to; i--)
            {
                var m = DebugLineRegex.Match(lines[i]);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int b4xLine))
                    return b4xLine;
            }

            return null;
        }

        private static string? LocateJavaSource(string objectsSrc, string javaFileName)
        {
            // 1. Direct child of objectsSrc — e.g. Objects/src/main.java
            string direct = Path.Combine(objectsSrc, javaFileName);
            if (File.Exists(direct)) return direct;

            // 2. Anywhere under objectsSrc — handles Objects/src/b4j/example/main.java etc.
            try
            {
                foreach (var f in Directory.EnumerateFiles(objectsSrc, javaFileName, SearchOption.AllDirectories))
                    return f;
            }
            catch { /* fall through */ }

            return null;
        }

        /// <summary>
        /// Produces a one-line suggested cause for well-known Java exception types + messages.
        /// Returns null when no heuristic applies (the AI should still see the raw exception).
        /// </summary>
        public static string? GetHeuristicCause(string exceptionType, string exceptionMessage)
        {
            if (string.IsNullOrEmpty(exceptionType)) return null;

            if (exceptionType.EndsWith("IllegalArgumentException", StringComparison.OrdinalIgnoreCase))
            {
                if (exceptionMessage.Contains("argument type mismatch", StringComparison.OrdinalIgnoreCase))
                    return "Event handler signature mismatch: the B4J generator passes typed parameters from the library, but this Sub's parameter types do not match the library event declaration (common case: Int instead of Double). Run validate_event_handlers on the project to find the exact mismatch.";
                return "IllegalArgumentException: a method received an argument of the wrong type. Likely a signature mismatch or an uninitialized Dim.";
            }
            if (exceptionType.EndsWith("NullPointerException", StringComparison.OrdinalIgnoreCase))
                return "Null reference: a control or object was used before being assigned, or an event fired before its handler was wired. Check Dim declarations and load order.";
            if (exceptionType.EndsWith("ClassCastException", StringComparison.OrdinalIgnoreCase))
                return "Type mismatch: a variable was used as the wrong B4X/Java type. Often happens when a control was loaded as the wrong type in the layout file.";
            if (exceptionType.EndsWith("ArrayIndexOutOfBoundsException", StringComparison.OrdinalIgnoreCase))
                return "Array index out of bounds. Likely an off-by-one, an uninitialized array, or a Sub that runs before the array is sized.";
            if (exceptionType.EndsWith("NumberFormatException", StringComparison.OrdinalIgnoreCase))
                return "Number parsing failed on a string input. Likely a non-numeric EditText value passed to Val/ParseInt.";
            if (exceptionType.EndsWith("ArithmeticException", StringComparison.OrdinalIgnoreCase))
                return "Arithmetic error (e.g. divide by zero).";
            if (exceptionType.EndsWith("StackOverflowError", StringComparison.OrdinalIgnoreCase))
                return "Infinite recursion in a Sub. Check for self-calls in a base case that never fires.";

            return null;
        }
    }
}
