using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using B4XMcpServer.Utils;

namespace B4XMcpServer.Services
{
    /// <summary>
    /// Captured, parsed headline of a Java exception found inside a process's combined output.
    /// </summary>
    public sealed class RuntimeException
    {
        public string Type { get; init; } = "";
        public string Message { get; init; } = "";
        public List<string> StackFrames { get; init; } = new();
    }

    /// <summary>
    /// Result of a runtime launch: the executable used, whether it ended normally or by
    /// crashing with an unhandled Java exception, and the full output (already captured
    /// by ProcessRunner with a hard timeout).
    /// </summary>
    public sealed class RuntimeLaunchResult
    {
        public string Platform { get; init; } = "B4J";
        public string? Executable { get; init; }
        public int ExitCode { get; init; }
        public bool TimedOut { get; init; }
        public string Output { get; init; } = "";
        public RuntimeException? Exception { get; init; }
    }

    /// <summary>
    /// Launches compiled B4X projects and captures runtime exceptions.
    /// Phase 1 supports B4J (java -jar). B4A and B4i are deliberately not implemented here;
    /// DeviceTools already covers adb-based B4A logcat capture when needed.
    /// </summary>
    public static class RuntimeLauncher
    {
        // Regex timeout protects against catastrophic backtracking on crafted/large outputs.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        // Matches the first line of a Java exception: "java.lang.X: msg" or "java.lang.X" alone.
        // Group 1 = fully-qualified type, Group 2 = message (optional).
        // Anchored with Multiline so each line is tested individually; we'll start scanning from
        // the LAST match (deepest "Caused by" is the actual root cause).
        private static readonly Regex ExceptionHeaderRegex = new Regex(
            @"^\s*(?:Exception in thread ""[^""]+""\s+)?([A-Za-z_][\w.]*?Exception)(?:[:\s]+(.*?))?$",
            RegexOptions.Compiled | RegexOptions.Multiline,
            RegexTimeout);

        // Stack frame: "    at fully.qualified.Method(Class.java:123)" or "(Native Method)" / "(Unknown Source)".
        private static readonly Regex StackFrameRegex = new Regex(
            @"^\s+at\s+([^\s(]+)(?:\(([^)]*)\))?",
            RegexOptions.Compiled,
            RegexTimeout);

        /// <summary>
        /// Builds (if needed) and runs a B4X app, capturing stdout/stderr and any unhandled exception.
        /// </summary>
        /// <param name="projectPath">Path to the project folder or to its .b4a/.b4j/.b4i file.</param>
        /// <param name="timeoutSec">Hard cap on the run, in seconds. The JVM may take a few seconds to spin up.</param>
        public static async Task<RuntimeLaunchResult> RunProjectAsync(string projectPath, int timeoutSec = 30)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            string? root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

            string? projectFile = ProjectScanner.FindProjectFile(root);
            if (projectFile == null)
                throw new FileNotFoundException($"No .b4a/.b4j/.b4i project file found in '{root}'.");

            string ext = Path.GetExtension(projectFile).ToLowerInvariant();

            return ext switch
            {
                ".b4j" => await LaunchB4JAsync(projectFile, timeoutSec),
                ".b4a" => throw new NotSupportedException(
                    "Runtime launching for B4A is not yet implemented in this tool. " +
                    "Use DeviceTools.install_apk + get_logcat to debug a B4A app instead."),
                ".b4i" => throw new NotSupportedException("Runtime launching for B4i is not supported via MCP (requires macOS host)."),
                _ => throw new NotSupportedException($"Unsupported project type '{ext}'.")
            };
        }

        /// <summary>
        /// Launches the B4J jar produced by a successful compile_project. The jar lives in
        /// Objects/&lt;name&gt;.jar and is run from the project root so it can find Files/ assets.
        /// </summary>
        private static async Task<RuntimeLaunchResult> LaunchB4JAsync(string projectFile, int timeoutSec)
        {
            string projectName = Path.GetFileNameWithoutExtension(projectFile);
            string projectDir = Path.GetDirectoryName(projectFile) ?? ".";
            string objectsDir = Path.Combine(projectDir, "Objects");
            string executable;

            // Prefer the jar whose stem matches the project name. If not present, fall back
            // to the first non-b4jlibs jar we can find — B4J's libraries jar is unrelated.
            string preferred = Path.Combine(objectsDir, projectName + ".jar");
            if (File.Exists(preferred))
            {
                executable = preferred;
            }
            else if (!Directory.Exists(objectsDir))
            {
                return Failed($"No compiled output found at '{preferred}'. The project file does not have an Objects/ directory. Run compile_project first.");
            }
            else
            {
                var any = Directory.GetFiles(objectsDir, "*.jar")
                    .FirstOrDefault(j => !j.EndsWith("b4jlibs.jar", StringComparison.OrdinalIgnoreCase));
                if (any == null)
                    return Failed($"No compiled jar found in '{objectsDir}'. Run compile_project first.");
                executable = any;
            }

            // Verify java is available. We only need it on PATH; ProcessRunner will return a
            // Win32Exception-derived message if it isn't, which we rewrite into actionable text.
            try
            {
                using var probe = Process.Start(new ProcessStartInfo
                {
                    FileName = "java",
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (probe == null)
                    return Failed("Java is not available on PATH. Install a JDK (Adoptium / OpenJDK) and retry.");
                probe.WaitForExit(5000);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return Failed("Java is not available on PATH. Install a JDK (Adoptium / OpenJDK) and retry.");
            }

            // Working directory must be the project root so the jar can resolve its Files/ assets
            // and any relative classpath entries. Running with WorkingDirectory set elsewhere would
            // produce "file not found" errors that have nothing to do with the app's actual code.
            var runResult = await ProcessRunner.RunAsync(
                fileName: "java",
                arguments: new[] { "-jar", executable },
                workingDirectory: projectDir,
                timeoutMilliseconds: timeoutSec * 1000);

            var ex = ParseException(runResult.Output);
            var result = new RuntimeLaunchResult
            {
                Platform = "B4J",
                Executable = executable,
                ExitCode = runResult.ExitCode,
                TimedOut = runResult.TimedOut,
                Output = runResult.Output,
                Exception = ex
            };

            // Cache the most recent exception so GetB4xStackTrace can return it without re-running.
            if (ex != null)
                CacheManager.SetByTtl($"runtime:last_exception:{projectDir}", result, ttlSeconds: 600);

            return result;
        }

        private static RuntimeLaunchResult Failed(string message) => new RuntimeLaunchResult
        {
            Platform = "B4J",
            Output = message,
            ExitCode = -1
        };

        /// <summary>
        /// Extracts the LAST Java exception from the combined output. The last one is the
        /// root cause (nested "Caused by:" chains end with whatever actually triggered the failure).
        /// Returns null if no exception is found.
        /// </summary>
        public static RuntimeException? ParseException(string output)
        {
            if (string.IsNullOrEmpty(output)) return null;

            var matches = ExceptionHeaderRegex.Matches(output);
            if (matches.Count == 0) return null;

            // Pick the last exception header — it's the deepest / most informative.
            var header = matches[matches.Count - 1];
            int headerIndex = header.Index;
            int headerLineEnd = headerIndex + header.Length;

            string type = header.Groups[1].Value.Trim();
            string message = header.Groups[2].Success ? header.Groups[2].Value.Trim() : "";

            var frames = new List<string>();
            var subsequent = output.Substring(headerLineEnd);
            foreach (var line in subsequent.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(trimmed)) break;  // blank line ends the trace
                if (StackFrameRegex.IsMatch(trimmed))
                    frames.Add(trimmed.Trim());
                else
                    break;  // first non-frame line ends the trace
            }

            return new RuntimeException
            {
                Type = type,
                Message = message,
                StackFrames = frames
            };
        }
    }
}
