using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using B4XMcpServer.Engine;
using B4XMcpServer.Repositories;
using B4XMcpServer.Services;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;

namespace B4XMcpServer.Tools
{
    /// <summary>
    /// Live runtime-diagnostics MCP tools. Builds (caller-driven) and runs compiled B4X
    /// apps, captures stdout/stderr, parses any unhandled Java exception, and maps the
    /// stack trace back to B4X source files/Subs/lines so the AI can fix the bug
    /// without guessing.
    /// </summary>
    [McpServerToolType]
    public sealed class RuntimeTools
    {
        private readonly IProjectRepository _projectRepository;

        // Output truncation budgets. Long runs of JVM init chatter can easily reach several
        // thousand lines; we keep the head (so the AI sees startup messages) and the tail
        // (where the crash usually lives) and throw the rest away.
        private const int OutputHeadChars = 500;
        private const int OutputTailChars = 4000;

        public RuntimeTools(IProjectRepository projectRepository)
        {
            _projectRepository = projectRepository;
        }

        [McpServerTool, Description(
            "Runs a compiled B4X project and captures stdout/stderr along with any unhandled " +
            "Java exception. The exception's stack trace is automatically mapped back to the " +
            "B4X source file, Sub, and line, with a heuristic cause suggestion. " +
            "Currently supports B4J (java -jar the built Objects/<name>.jar); B4A requires " +
            "DeviceTools.install_apk + get_logcat, and B4i is not supported by this tool.")]
        public async Task<string> RunProject(
            [Description("Absolute path to the B4X project folder, or its .b4j file (B4J only at this time).")] string projectPath,
            [Description("Hard timeout in seconds for the app run. Default 30. The JVM may take a few seconds to start, so prefer ≥ 10.")] int runTimeoutSec = 30,
            [Description("If true, still attempts to run even if compile_project hasn't been called recently. Default false (caller is expected to compile first).")] bool ignoreBuildStatus = false)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            RuntimeLaunchResult result;
            try
            {
                result = await RuntimeLauncher.RunProjectAsync(projectPath, runTimeoutSec);
            }
            catch (NotSupportedException ex)
            {
                return Serialize(new { error = ex.Message });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Serialize(new { error = ex.Message });
            }
            catch (FileNotFoundException ex)
            {
                return Serialize(new { error = ex.Message });
            }

            return FormatLaunchResult(result, projectPath);
        }

        [McpServerTool, Description(
            "Alias for run_project, signals that the AI expects to capture a crash (e.g. when " +
            "investigating a user-reported runtime error). Returns early once an exception is " +
            "captured so the AI can move directly to fixing it.")]
        public async Task<string> LaunchDebug(
            [Description("Absolute path to the B4X project folder or its .b4j file.")] string projectPath,
            [Description("Hard timeout in seconds for the app run. Default 30.")] int runTimeoutSec = 30)
        {
            // Functionally identical to RunProject today. The "debug" framing simply tells
            // the AI/model not to expect a clean exit — same return shape in both cases.
            return await RunProject(projectPath, runTimeoutSec);
        }

        [McpServerTool, Description(
            "Parses a raw Java stack trace (e.g. pasted from the user's console output) and " +
            "maps each frame to the corresponding B4X source file, Sub, and line in the project. " +
            "Use this when you have a stack trace but no live run, or when run_project timing " +
            "out prevents a fresh capture.")]
        public string GetRuntimeErrorDetail(
            [Description("Raw Java stack trace as a string. Can be multi-line. The exception's 'Caused by:' chain is supported — the deepest cause is analyzed.")] string stackTrace,
            [Description("Absolute path to the B4X project folder, or its .b4a/.b4j/.b4i file. Used to resolve Sub names, modules, and B4J debugLine comments.")] string projectPath)
        {
            if (string.IsNullOrWhiteSpace(stackTrace))
                throw new ArgumentException("stackTrace must not be empty.");
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            var parsed = RuntimeLauncher.ParseException(stackTrace);
            if (parsed == null)
                return Serialize(new { error = "No Java exception found in the input. Make sure the input contains a line like 'java.lang.X: msg' or 'at fully.qualified.Name(File.java:line)'." });

            string? root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
            var frames = RuntimeErrorMapper.MapStackTrace(parsed.StackFrames, root ?? projectPath);

            // Apply the heuristic to the deepest B4X-mapped frame (the one closest to user code).
            string? suspectedCause = RuntimeErrorMapper.GetHeuristicCause(parsed.Type, parsed.Message);
            B4xFrame? primaryFrame = frames.LastOrDefault(f => f.SubName != null);
            if (primaryFrame != null && suspectedCause != null)
            {
                // Re-emit as a new instance so we don't mutate shared cached results.
                primaryFrame = new B4xFrame
                {
                    JavaMethod = primaryFrame.JavaMethod,
                    JavaFile = primaryFrame.JavaFile,
                    JavaLine = primaryFrame.JavaLine,
                    ModuleName = primaryFrame.ModuleName,
                    SubName = primaryFrame.SubName,
                    B4xFile = primaryFrame.B4xFile,
                    B4xLine = primaryFrame.B4xLine,
                    SuspectedCause = suspectedCause
                };
            }

            return Serialize(new
            {
                exception = new
                {
                    type = parsed.Type,
                    message = parsed.Message,
                    b4xFrames = frames.Select(f => new
                    {
                        javaMethod = f.JavaMethod,
                        javaFile = f.JavaFile,
                        javaLine = f.JavaLine,
                        moduleName = f.ModuleName,
                        subName = f.SubName,
                        b4xFile = f.B4xFile,
                        b4xLine = f.B4xLine,
                        suspectedCause = f.SuspectedCause
                    }),
                    primaryB4xFrame = primaryFrame == null ? null : new
                    {
                        b4xFile = primaryFrame.B4xFile,
                        b4xSub = primaryFrame.SubName,
                        b4xLine = primaryFrame.B4xLine,
                        suspectedCause = primaryFrame.SuspectedCause
                    },
                    unmappedFrameCount = frames.Count(f => f.SubName == null)
                }
            }, indent: true);
        }

        [McpServerTool, Description(
            "Returns the most recent unhandled exception captured by the last run_project call " +
            "for this project, already mapped to B4X source. Use this when the AI just ran " +
            "the app and wants to dig into the failure without re-running it.")]
        public string GetB4xStackTrace(
            [Description("Absolute path to the B4X project folder, or its .b4j file.")] string projectPath)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            string? root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
            if (root == null)
                return Serialize(new { error = $"Could not determine a B4X project root from '{projectPath}'." });

            string cacheKey = $"runtime:last_exception:{root}";
            if (!CacheManager.TryGetByTtl<RuntimeLaunchResult>(cacheKey, out var cached) || cached == null || cached.Exception == null)
                return Serialize(new { error = "No recent exception captured for this project. Did you call run_project (or launch_debug) first?" });

            return FormatLaunchResult(cached, root, fromCache: true);
        }

        // ── Formatting helpers ───────────────────────────────────────────

        private string FormatLaunchResult(RuntimeLaunchResult result, string projectPath, bool fromCache = false)
        {
            string? suspectedCause = result.Exception == null
                ? null
                : RuntimeErrorMapper.GetHeuristicCause(result.Exception.Type, result.Exception.Message);

            string? root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
            var frames = result.Exception == null
                ? new List<B4xFrame>()
                : RuntimeErrorMapper.MapStackTrace(result.Exception.StackFrames, root ?? projectPath);

            // Attach the overall heuristic cause to the deepest mapped frame (the one closest
            // to user code) so it surfaces at the top of the AI-readable tree alongside the
            // B4X line number, which is what the AI needs to jump to a fix.
            int primaryIdx = -1;
            for (int i = frames.Count - 1; i >= 0; i--)
            {
                if (frames[i].SubName != null) { primaryIdx = i; break; }
            }
            B4xFrame? primary = primaryIdx >= 0 ? frames[primaryIdx] : null;
            if (primary != null && suspectedCause != null)
            {
                var orig = primary;
                var annotated = new B4xFrame
                {
                    JavaMethod = orig.JavaMethod,
                    JavaFile = orig.JavaFile,
                    JavaLine = orig.JavaLine,
                    ModuleName = orig.ModuleName,
                    SubName = orig.SubName,
                    B4xFile = orig.B4xFile,
                    B4xLine = orig.B4xLine,
                    SuspectedCause = suspectedCause
                };
                frames[primaryIdx] = annotated;
                primary = annotated;
            }

            string head, tail; bool truncated;
            SplitOutput(result.Output, out head, out tail, out truncated);

            var payload = new
            {
                platform = result.Platform,
                executable = result.Executable,
                exitCode = result.ExitCode,
                timedOut = result.TimedOut,
                fromCache,
                outputHead = head,
                outputTail = tail,
                outputTruncated = truncated,
                exceptionCaptured = result.Exception != null,
                exception = result.Exception == null ? null : new
                {
                    type = result.Exception.Type,
                    message = result.Exception.Message,
                    b4xFrames = frames.Select(f => new
                    {
                        javaMethod = f.JavaMethod,
                        javaFile = f.JavaFile,
                        javaLine = f.JavaLine,
                        moduleName = f.ModuleName,
                        subName = f.SubName,
                        b4xFile = f.B4xFile,
                        b4xLine = f.B4xLine,
                        suspectedCause = f.SuspectedCause
                    }),
                    primaryB4xFrame = primary == null ? null : new
                    {
                        b4xFile = primary.B4xFile,
                        b4xSub = primary.SubName,
                        b4xLine = primary.B4xLine,
                        suspectedCause = primary.SuspectedCause
                    },
                    unmappedFrameCount = frames.Count(f => f.SubName == null)
                }
            };
            return Serialize(payload, indent: true);
        }

        private static void SplitOutput(string output, out string head, out string tail, out bool truncated)
        {
            if (string.IsNullOrEmpty(output))
            {
                head = tail = "";
                truncated = false;
                return;
            }

            if (output.Length <= OutputHeadChars + OutputTailChars)
            {
                head = output;
                tail = "";
                truncated = false;
                return;
            }

            head = output.Substring(0, OutputHeadChars);
            tail = output.Substring(output.Length - OutputTailChars);
            truncated = true;
        }

        private static string Serialize(object payload, bool indent = false)
        {
            return JsonSerializer.Serialize(payload, indent ? JsonOptions.Default : JsonOptions.Compact);
        }
    }
}
