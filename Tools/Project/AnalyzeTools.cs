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
using System.Text.RegularExpressions;

namespace B4XMcpServer.Tools.Project
{
    [McpServerToolType]
    public sealed class AnalyzeTools
    {
        private readonly IFileRepository _fileRepository;

        public AnalyzeTools(IFileRepository fileRepository)
        {
            _fileRepository = fileRepository;
        }

        [McpServerTool, Description("Analyzes a SINGLE .bas, .b4a, or .b4j module file: lists every Sub (name, parameters, return type, public/private, event handler detection), every Type declaration, and Globals presence. Also reports structural parse issues without compiling. All returned subs[*].startLine / endLine and types[*].startLine / endLine are FILE-LINE (1-based from the first line of the file); issues[*].Line is also FILE-LINE so it can be passed straight into edit_line / insert_line.")]
        public string AnalyzeModule(
            [Description("Absolute path to a single .bas module file. This tool takes ONE MODULE, not the whole project — for project-wide metadata (libs, NumberOfModules, etc.) use get_project_config with projectPath instead.")] string filePath)
        {
            PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

            if (!_fileRepository.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            string cacheKey = $"analyze:{filePath}";
            if (CacheManager.TryGetByMtime<string>(filePath, out var cached) && cached != null)
                return cached;

            const string analyzeMarker = "@EndOfDesignText@";

            string source = CodeUtils.ReadTextSafely(filePath);
            int analyzeMarkerIdx = source.IndexOf(analyzeMarker, StringComparison.Ordinal);
            int analyzeHeaderLineCount = analyzeMarkerIdx >= 0
                ? source.Substring(0, analyzeMarkerIdx).Split('\n').Length
                : 0;

            // ── PRIMARY: Use new_engine (DocumentAnalysisEngine) for Sub detection ──
            // The new engine is the canonical source of truth for B4X code analysis.
            var lines = source.Replace("\r\n", "\n").Split('\n');
            DocumentAnalysisEngine.AnalyzeDocumentForFunctionBlocks(lines);
            var functionBlocks = DocumentAnalysisEngine.FunctionBlockList;

            // ── FALLBACK: Use old B4xParser for Type declarations, params/return types, parse issues ──
            // new_engine doesn't yet expose Type declarations or parameter/return-type info,
            // so we supplement from the legacy parser. These will be migrated when
            // new_engine's capabilities are extended.
            var (root, issues) = B4xParser.Parse(source);
            var nodes = B4xParser.FlattenSubsAndTypes(root);

            // Build a lookup from old parser results by name and line for supplementing
            // parameters and return type info that new_engine's FunctionBlock doesn't expose.
            var oldSubsByName = nodes
                .Where(n => n.Kind == "Sub")
                .ToLookup(n => n.Name, StringComparer.OrdinalIgnoreCase);

            var subs = functionBlocks.Select(b =>
            {
                // Match by name (case-insensitive) to get params/returnType from old parser
                var oldNode = oldSubsByName[b.FunctionName].FirstOrDefault();
                return new
                {
                    name = b.FunctionName,
                    parameters = oldNode?.Params,
                    returnType = oldNode?.ReturnType,
                    isPrivate = b.FunctionScopeValue == FunctionScope.Private,
                    looksLikeEventHandler = Regex.IsMatch(b.FunctionName,
                        @"_(Click|Create|Resume|Pause|CheckedChange|TextChanged|Tick|JobDone|Complete|ItemClick|LongClick|FocusChanged)$",
                        RegexOptions.IgnoreCase, ProjectHelpersShared.RegexTimeout),
                    startLine = b.LineStart + 1 + analyzeHeaderLineCount,
                    endLine = b.LineEnd + 1 + analyzeHeaderLineCount
                };
            }).ToList();

            var types = nodes
                .Where(n => n.Kind == "Type")
                .Select(n => new { name = n.Name, startLine = n.StartLine + analyzeHeaderLineCount, endLine = (n.EndLine ?? n.StartLine) + analyzeHeaderLineCount }).ToList();

            bool hasGlobals = source.Contains("Sub Globals", StringComparison.OrdinalIgnoreCase) ||
                              source.Contains("Sub Process_Globals", StringComparison.OrdinalIgnoreCase) ||
                              source.Contains("Sub Class_Globals", StringComparison.OrdinalIgnoreCase);

            var parseIssues = issues.Select(i => new { line = i.Line + analyzeHeaderLineCount, message = i.Message, severity = i.Severity }).ToList();

            var result = new
            {
                filePath,
                lineNumbering = "file",
                lineOffset = analyzeHeaderLineCount,
                subCount = subs.Count,
                subs,
                typeCount = types.Count,
                types,
                hasGlobals,
                globalsHeaders = new
                {
                    hasProcessGlobals = source.Contains("Sub Process_Globals", StringComparison.OrdinalIgnoreCase),
                    hasGlobals = source.Contains("Sub Globals", StringComparison.OrdinalIgnoreCase),
                    hasClassGlobals = source.Contains("Sub Class_Globals", StringComparison.OrdinalIgnoreCase)
                },
                issueCount = parseIssues.Count,
                issues = parseIssues,
                _reminder = "Line numbers in subs, types, and issues are FILE-LINE (1-based from the first line of the file including the IDE header). Use these directly with edit_line / insert_line."
            };

            var json = JsonSerializer.Serialize(result, JsonOptions.Default);
            CacheManager.SetByMtime(filePath, json);
            return json;
        }

        [McpServerTool, Description("DEPRECATED — do not use. Creating .bas modules automatically has proven unreliable and can corrupt the project. This tool now returns the exact manual steps the user must follow in the B4X IDE to create and register a new module safely.")]
        public string CreateBasModule(
            [Description("Ignored — kept only for signature compatibility.")] string filePath,
            [Description("Ignored — kept only for signature compatibility.")] string moduleType = "activity")
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Automatic creation of .bas modules is disabled to prevent project corruption.",
                instructions = new[]
                {
                    "1. Open the project in the B4X IDE.",
                    "2. Choose Project → Add New Module from the menu.",
                    "3. Select the module type (Activity, Service, Class, CodeModule).",
                    "4. The IDE will create the .bas file, update the project header (ModuleN=, NumberOfModules=), and handle all file structure automatically.",
                    "5. After the new module appears in the IDE, call get_file_content / analyze_module / edit_sub on it as needed — the MCP tools can then interact with it without any project corruption risk."
                }
            }, JsonOptions.Default);
        }
    }
}
