using B4XMcpServer.Engine;
using B4XMcpServer.Repositories;
using B4XMcpServer.Services;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace B4XMcpServer.Tools.Project
{
    internal class CachedParseResult
    {
        public List<B4xParser.ParseIssue> Issues { get; set; } = new();
    }

    [McpServerToolType]
    public sealed class ValidationTools
    {
        private readonly IFileRepository _fileRepository;
        private readonly IProjectRepository _projectRepository;

        public ValidationTools(IFileRepository fileRepository, IProjectRepository projectRepository)
        {
            _fileRepository = fileRepository;
            _projectRepository = projectRepository;
        }

        [McpServerTool, Description("Statically validates every event handler Sub in a B4X project against the event signatures declared in the referenced libraries. Reports parameter count, name, and type mismatches (e.g. Int vs Double) that cause runtime crashes like java.lang.IllegalArgumentException. Also infers control types from Dim declarations and layout files.")]
        public string ValidateEventHandlers(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file.")] string projectPath)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            string? root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

            var result = EventHandlerValidator.Validate(root);

            return JsonSerializer.Serialize(new
            {
                handlersChecked = result.HandlersChecked,
                mismatchCount = result.MismatchCount,
                mismatches = result.Mismatches.Select(m => new
                {
                    file = m.File,
                    sub = m.Sub,
                    controlName = m.ControlName,
                    inferredType = m.InferredType,
                    eventName = m.EventName,
                    expectedSignature = m.ExpectedSignature,
                    actualSignature = m.ActualSignature,
                    library = m.Library,
                    severity = m.Severity,
                    differences = m.Differences,
                    fixHint = m.FixHint
                }),
                warnings = result.Warnings
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Runs the B4X structural parser against every module (.bas) in a project WITHOUT compiling, and reports any structural problems found (unclosed Sub/Type/Region blocks, mismatched End statements). Near-instant sanity check.")]
        public string ValidateProject(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")] string projectPath)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            string? root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

            var basFiles = _projectRepository.ScanProject(root).Where(f => f.Kind == "bas").ToList();

            var results = new List<object>();
            int totalIssues = 0;
            foreach (var f in basFiles)
            {
                if (CacheManager.TryGetByMtime<CachedParseResult>(f.Path, out var cached) && cached != null)
                {
                    if (cached.Issues.Count > 0)
                    {
                        totalIssues += cached.Issues.Count;
                        results.Add(new
                        {
                            file = f.Path,
                            issues = cached.Issues.Select(i => new { line = i.Line, message = i.Message, severity = i.Severity })
                        });
                    }
                    continue;
                }

                string source;
                try { source = CodeUtils.ReadTextSafely(f.Path); }
                catch (Exception ex)
                {
                    results.Add(new { file = f.Path, error = ex.Message });
                    continue;
                }

                // FALLBACK NOTE: This tool detects structural issues (unclosed Subs/Types/Regions,
                // mismatched End statements) — the new_engine's DocumentAnalysisEngine only finds
                // function blocks (Subs) and cannot detect these. Using legacy B4xParser exclusively
                // for issue detection until new_engine is extended with structural diagnostics.
                var (_, issues) = B4xParser.Parse(source);
                CacheManager.SetByMtime(f.Path, new CachedParseResult { Issues = issues });

                if (issues.Count > 0)
                {
                    totalIssues += issues.Count;
                    results.Add(new
                    {
                        file = f.Path,
                        issues = issues.Select(i => new { line = i.Line, message = i.Message, severity = i.Severity })
                    });
                }
            }

            return JsonSerializer.Serialize(new
            {
                filesChecked = basFiles.Count,
                filesWithIssues = results.Count,
                totalIssues,
                results
            }, JsonOptions.Default);
        }
    }
}
