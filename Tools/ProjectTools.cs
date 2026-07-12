using B4XContext.Engine;
using B4XContext.Models;
using B4XContext.Services;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContextFileMode = B4XContext.Models.FileMode;
using System.Collections.Generic;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class ProjectTools
    {
        [McpServerTool, Description("Returns the structure of a B4X project (B4A or B4J): the project root, the .b4a/.b4j/.b4i project file, and every module (.bas) and layout (.bal/.bjl/.bil) file found, ignoring build folders (Objects/bin/gen/obj). Accepts either the project folder path or the path to the .b4a/.b4j/.b4i file itself.")]
        public static string GetProjectStructure(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file.")] string projectPath)
        {
            string? root = Directory.Exists(projectPath)
                ? projectPath
                : ProjectScanner.FindProjectRoot(projectPath);

            if (root == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Could not determine a B4X project root from '{projectPath}'. Pass either the project folder or its .b4a/.b4j/.b4i file."
                });
            }

            var files = ProjectScanner.ScanProject(root);
            var projectFile = ProjectScanner.FindProjectFile(root);

            var result = new
            {
                projectRoot = root,
                projectFile,
                fileCount = files.Count,
                files = files.Select(f => new { path = f.Path, name = f.Name, kind = f.Kind })
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        [McpServerTool, Description("Returns the full text content of a file (B4X module .bas, project file .b4a/.b4j/.b4i, or any other text file). For .bas/.b4a/.b4j/.b4i files, automatically strips the IDE metadata header (everything up to and including @EndOfDesignText@) since it's pure IDE bookkeeping with no useful content.")]
        public static string GetFileContent(
            [Description("Absolute path to the file to read.")] string filePath)
        {
            if (!File.Exists(filePath))
                return JsonSerializer.Serialize(new { error = $"File not found: {filePath}" });

            try
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                string content = (ext == ".bas" || ext == ".b4a" || ext == ".b4j" || ext == ".b4i")
                    ? CodeUtils.ReadTextSafely(filePath)
                    : File.ReadAllText(filePath);
                return content;
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        [McpServerTool, Description("Writes (overwrites) a file with the given content. This replaces the entire file, so read it first with get_file_content if you need to preserve parts of it. Typically used to save an edited B4X module back to disk.")]
        public static string WriteFile(
            [Description("Absolute path to the file to write.")] string filePath,
            [Description("The full new content of the file.")] string content)
        {
            try
            {
                File.WriteAllText(filePath, content);
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    path = filePath,
                    bytesWritten = System.Text.Encoding.UTF8.GetByteCount(content)
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        [McpServerTool, Description("Compiles a B4X project (B4A or B4J) using the local B4ABuilder.exe/B4JBuilder.exe and returns structured results: success or failure, and if it failed, the list of errors (module name, line number, source line, and message). The correct builder is chosen automatically from the project's file extension (.b4a vs .b4j) — this never mixes up B4A/B4J builders.")]
        public static string CompileProject(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")] string projectPath,
            [Description("Timeout in seconds. Android/JavaFX builds can take a couple of minutes, especially the first build. Default 300.")] int timeoutSeconds = 300)
        {
            string? projectFile = File.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectFile(projectPath);
            if (projectFile == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"No .b4a/.b4j/.b4i project file found for '{projectPath}'."
                });
            }

            var builderPath = BuilderLocator.LocateBuilder(projectFile);
            if (builderPath == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Could not locate the matching B4ABuilder.exe/B4JBuilder.exe for this project. " +
                             "Add a b4x_context_config.json with \"builder_path\" next to the project file, " +
                             "or confirm it's installed under the standard Anywhere Software folder."
                });
            }

            var buildResult = BuilderRunner.RunBuild(builderPath, projectFile, timeoutSeconds);

            if (buildResult.TryGetValue("fatal_error", out var fatal) && fatal != null)
            {
                return JsonSerializer.Serialize(new { success = false, error = fatal.ToString() });
            }

            bool success = buildResult.TryGetValue("success", out var s) && s is bool sb && sb;
            var formattedErrors = success ? null : BuildFormatter.Format(buildResult);

            return JsonSerializer.Serialize(new
            {
                success,
                builderUsed = builderPath,
                errors = formattedErrors
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        [McpServerTool, Description("Decodes a B4X visual layout file into readable JSON: control hierarchy, types, positions (resolved from the correct screen variant, not the misleading top-level template defaults), and properties like text/hint/tag/drawable. Works for both .bal (B4A) and .bjl (B4J) — they share the exact same binary format.")]
        public static string GetLayoutStructure(
            [Description("Absolute path to the .bal or .bjl layout file.")] string layoutPath)
        {
            if (!File.Exists(layoutPath))
                return JsonSerializer.Serialize(new { error = $"Layout file not found: {layoutPath}" });

            try
            {
                var data = File.ReadAllBytes(layoutPath);
                var decoded = BalDecoder.Decode(data, full: true);
                return decoded; // ya viene como JSON serializado
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public sealed class GetFullContextRequest
        {
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")]
            public string ProjectPath { get; set; } = string.Empty;

            [Description("Optional: path to the file the AI/user is currently focused on.")]
            public string? FocusFile { get; set; }

            [Description("Optional: name of the specific Sub inside FocusFile to keep expanded. If omitted but FocusFile is set, the whole FocusFile is kept expanded instead.")]
            public string? FocusSub { get; set; }

            [Description("If true, compiles the project first and attaches structured errors to the bundle. Default false (compiling can take a while).")]
            public bool RunCompile { get; set; } = false;

            [Description("Optional short description of the current task, included at the top of the bundle.")]
            public string? Task { get; set; }
        }

        [McpServerTool, Description("Builds a single consolidated Markdown context bundle for a B4X project: an ASCII file tree plus every module/layout, in skeleton form (signatures only, bodies collapsed) by default to keep it compact. Pass FocusFile to keep one specific file fully expanded, or FocusFile+FocusSub to keep just that one Sub expanded while the rest of that same file stays collapsed. Optionally compiles first and attaches real errors. This is the token-efficient alternative to dumping the whole project — use get_file_content afterward for any other single file you need in full.")]
        public static string GetFullContext(GetFullContextRequest request)
        {
            var projectPath = request.ProjectPath;
            var focusFile = request.FocusFile;
            var focusSub = request.FocusSub;
            var runCompile = request.RunCompile;
            var task = request.Task;

            string? root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            if (root == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Could not determine a B4X project root from '{projectPath}'."
                });
            }

            var scanned = ProjectScanner.ScanProject(root);
            var projectFiles = scanned.Select(f =>
            {
                var pf = new ProjectFile(f.Path) { Kind = f.Kind, Included = true };
                bool isFocus = focusFile != null &&
                    string.Equals(Path.GetFullPath(f.Path), Path.GetFullPath(focusFile), StringComparison.OrdinalIgnoreCase);
                pf.Mode = (isFocus && string.IsNullOrEmpty(focusSub)) ? ContextFileMode.Full : ContextFileMode.Skeleton;
                return pf;
            }).ToList();

            string? compileErrorsBlock = null;
            if (runCompile)
            {
                var projectFile = ProjectScanner.FindProjectFile(root);
                if (projectFile != null)
                {
                    var builderPath = BuilderLocator.LocateBuilder(projectFile);
                    if (builderPath != null)
                    {
                        var buildResult = BuilderRunner.RunBuild(builderPath, projectFile);
                        bool fatal = buildResult.TryGetValue("fatal_error", out var f2) && f2 != null;
                        if (!fatal)
                        {
                            bool success = buildResult.TryGetValue("success", out var s) && s is bool sb && sb;
                            if (!success) compileErrorsBlock = BuildFormatter.Format(buildResult);
                        }
                        else
                        {
                            compileErrorsBlock = $"## COMPILATION ERRORS\n\n(Builder failed to run: {buildResult["fatal_error"]})\n";
                        }
                    }
                }
            }

            var bundle = BundleBuilder.BuildMarkdown(
                preamble: null,
                task: task,
                files: projectFiles,
                includeFileTree: true,
                activeCode: null,
                activeFile: focusFile,
                activeSub: focusSub,
                compileErrors: compileErrorsBlock
            );

            return bundle;
        }
        public sealed class EditSubRequest
        {
            [Description("Absolute path to the .bas/.b4a/.b4j module file.")]
            public string FilePath { get; set; } = string.Empty;

            [Description("Exact name of the Sub to replace (case-insensitive).")]
            public string SubName { get; set; } = string.Empty;

            [Description("The full new source of the Sub, including its 'Sub ...' header line and matching 'End Sub' line.")]
            public string NewCode { get; set; } = string.Empty;
        }

        [McpServerTool, Description("Replaces the entire body of a single Sub in a B4X module in-place, without touching the rest of the file or its IDE metadata header. Locates the Sub by name using the real B4X parser, so partial/skeleton context is enough to safely target it — you don't need the whole file. If the Sub isn't found, returns the list of Subs that do exist in the file so the caller can retry with the correct name.")]
        public static string EditSub(EditSubRequest request)
        {
            var filePath = request.FilePath;
            var subName = request.SubName;
            var newCode = request.NewCode;

            if (!File.Exists(filePath))
                return JsonSerializer.Serialize(new { success = false, error = $"File not found: {filePath}" });

            string raw;
            try
            {
                raw = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = $"Could not read file: {ex.Message}" });
            }

            // IMPORTANTE: leemos el archivo crudo (con su encabezado de metadata del
            // IDE) en vez de usar CodeUtils.ReadTextSafely, que lo recorta. Si
            // escribieramos de vuelta sin ese encabezado, el IDE de B4X dejaria de
            // reconocer el archivo. Lo separamos, editamos solo la parte de codigo,
            // y lo volvemos a unir antes de guardar.
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            string header = markerIdx >= 0 ? raw.Substring(0, markerIdx + marker.Length) : string.Empty;
            string codeSection = markerIdx >= 0
                ? raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n')
                : raw;

            var (root, issues) = B4xParser.Parse(codeSection);
            var nodes = B4xParser.FlattenSubsAndTypes(root);
            var target = nodes.FirstOrDefault(n =>
                n.Kind == "Sub" && string.Equals(n.Name, subName, StringComparison.OrdinalIgnoreCase));

            if (target == null || target.EndLine == null)
            {
                var available = nodes.Where(n => n.Kind == "Sub").Select(n => n.Name).ToList();
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Sub '{subName}' not found in {Path.GetFileName(filePath)}.",
                    availableSubs = available
                });
            }

            var lines = codeSection.Replace("\r\n", "\n").Split('\n').ToList();
            int startIdx = target.StartLine - 1;
            int endIdx = target.EndLine.Value - 1;

            if (startIdx < 0 || endIdx >= lines.Count || startIdx > endIdx)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Internal error: Sub line range out of bounds after parsing."
                });
            }

            var newLines = newCode.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            lines.RemoveRange(startIdx, endIdx - startIdx + 1);
            lines.InsertRange(startIdx, newLines);

            var updatedCodeSection = string.Join("\n", lines);
            var finalContent = markerIdx >= 0 ? header + "\r\n" + updatedCodeSection : updatedCodeSection;

            try
            {
                File.WriteAllText(filePath, finalContent);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = $"Could not write file: {ex.Message}" });
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                filePath,
                subReplaced = target.Name,
                originalLineRange = new { start = target.StartLine, end = target.EndLine },
                newLineCount = newLines.Length
            });
        }
        public sealed class SearchCodeRequest
        {
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")]
            public string ProjectPath { get; set; } = string.Empty;

            [Description(".NET regular expression to search for, matched against each line of every .bas file (case-insensitive).")]
            public string Pattern { get; set; } = string.Empty;

            [Description("Optional: also search inside the .b4a/.b4j project file's code section, not just .bas modules. Default false.")]
            public bool IncludeProjectFile { get; set; } = false;

            [Description("Maximum number of matches to return, to avoid flooding context on overly broad patterns. Default 200.")]
            public int MaxResults { get; set; } = 200;
        }

        [McpServerTool, Description("Searches for a regex pattern across every .bas module (and optionally the .b4a/.b4j project file) in a B4X project, like grep. Returns each match with its file, line number, and the matching line's text. This is a plain text search (it also matches inside string literals and comments), not semantic — use it to quickly locate where something is mentioned, then use get_file_content or get_full_context with focusSub to see it in real context.")]
        public static string SearchCode(SearchCodeRequest request)
        {
            var projectPath = request.ProjectPath;
            var pattern = request.Pattern;

            if (string.IsNullOrWhiteSpace(pattern))
                return JsonSerializer.Serialize(new { error = "Pattern must not be empty." });

            string? root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            if (root == null)
                return JsonSerializer.Serialize(new { error = $"Could not determine a B4X project root from '{projectPath}'." });

            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = $"Invalid regex pattern: {ex.Message}" });
            }

            var files = ProjectScanner.ScanProject(root)
                .Where(f => f.Kind == "bas" || (request.IncludeProjectFile && (f.Kind == "b4a" || f.Kind == "b4j" || f.Kind == "b4i")))
                .ToList();

            var matches = new List<object>();
            var filesSearched = 0;
            foreach (var f in files)
            {
                if (matches.Count >= request.MaxResults) break;
                string content;
                try { content = CodeUtils.ReadTextSafely(f.Path); }
                catch { continue; }
                filesSearched++;

                var lines = content.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        matches.Add(new { file = f.Path, line = i + 1, text = lines[i].TrimEnd('\r').Trim() });
                        if (matches.Count >= request.MaxResults) break;
                    }
                }
            }

            return JsonSerializer.Serialize(new
            {
                pattern,
                filesSearched,
                matchCount = matches.Count,
                truncated = matches.Count >= request.MaxResults,
                matches
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        [McpServerTool, Description("Parses a B4X project file's (.b4a/.b4j/.b4i) IDE metadata header into structured JSON: app type, version, referenced libraries, module list, included files, and every other raw key=value setting from the header. Does not touch the code section.")]
        public static string GetProjectConfig(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file.")] string projectPath)
        {
            string? projectFile = File.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectFile(projectPath);
            if (projectFile == null)
                return JsonSerializer.Serialize(new { error = $"No .b4a/.b4j/.b4i project file found for '{projectPath}'." });

            string raw;
            try
            {
                raw = File.ReadAllText(projectFile);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }

            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            string headerSection = markerIdx >= 0 ? raw.Substring(0, markerIdx) : raw;

            var rawSettings = new Dictionary<string, string>();
            foreach (var lineRaw in headerSection.Split('\n'))
            {
                var line = lineRaw.TrimEnd('\r').Trim();
                if (string.IsNullOrEmpty(line)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                rawSettings[key] = value;
            }

            var libraries = rawSettings.Where(kv => Regex.IsMatch(kv.Key, @"^Library\d+$")).Select(kv => kv.Value).OrderBy(v => v).ToList();
            var modules = rawSettings.Where(kv => Regex.IsMatch(kv.Key, @"^Module\d+$")).Select(kv => kv.Value).ToList();
            var includedFiles = rawSettings.Where(kv => Regex.IsMatch(kv.Key, @"^File\d+$")).Select(kv => kv.Value).ToList();

            var result = new
            {
                projectFile,
                appType = rawSettings.TryGetValue("AppType", out var at) ? at : null,
                version = rawSettings.TryGetValue("Version", out var ver) ? ver : null,
                numberOfModules = rawSettings.TryGetValue("NumberOfModules", out var nm) ? nm : null,
                libraries,
                modules,
                includedFiles,
                rawSettings
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        [McpServerTool, Description("Analyzes a single B4X module (.bas) without needing its full content read separately: lists every Sub (name, parameters, return type, public/private, and whether it looks like an event handler by naming convention e.g. Btn_Click), every Type declaration, and whether Process_Globals/Globals/Class_Globals are present. Also reports any structural parse issues (unclosed blocks, mismatched Type/End Type, etc.) found without compiling.")]
        public static string AnalyzeModule(
            [Description("Absolute path to the .bas module file.")] string filePath)
        {
            if (!File.Exists(filePath))
                return JsonSerializer.Serialize(new { error = $"File not found: {filePath}" });

            string source;
            try { source = CodeUtils.ReadTextSafely(filePath); }
            catch (Exception ex) { return JsonSerializer.Serialize(new { error = ex.Message }); }

            var (root, issues) = B4xParser.Parse(source);
            var nodes = B4xParser.FlattenSubsAndTypes(root);

            var subs = nodes.Where(n => n.Kind == "Sub").Select(n => new
            {
                name = n.Name,
                parameters = n.Params,
                returnType = n.ReturnType,
                isPrivate = n.IsPrivate,
                looksLikeEventHandler = Regex.IsMatch(n.Name, @"_(Click|Create|Resume|Pause|CheckedChange|TextChanged|Tick|JobDone|Complete|ItemClick|LongClick|FocusChanged)$", RegexOptions.IgnoreCase),
                startLine = n.StartLine,
                endLine = n.EndLine
            }).ToList();

            var types = nodes.Where(n => n.Kind == "Type")
                .Select(n => new { name = n.Name, startLine = n.StartLine, endLine = n.EndLine }).ToList();

            var result = new
            {
                filePath,
                hasProcessGlobals = nodes.Any(n => n.Kind == "Process_Globals"),
                hasGlobals = nodes.Any(n => n.Kind == "Globals"),
                hasClassGlobals = nodes.Any(n => n.Kind == "Class_Globals"),
                subCount = subs.Count,
                subs,
                types,
                parseIssues = issues.Select(i => new { line = i.Line, message = i.Message, severity = i.Severity })
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        [McpServerTool, Description("Runs the B4X structural parser against every module (.bas) in a project WITHOUT compiling, and reports any structural problems found (unclosed Sub/Type/Region blocks, mismatched End statements). This is near-instant compared to a real compile — use it as a quick sanity check before compile_project, or right after generating/editing code to catch obvious mistakes early.")]
        public static string ValidateProject(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")] string projectPath)
        {
            string? root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            if (root == null)
                return JsonSerializer.Serialize(new { error = $"Could not determine a B4X project root from '{projectPath}'." });

            var basFiles = ProjectScanner.ScanProject(root).Where(f => f.Kind == "bas").ToList();

            var results = new List<object>();
            int totalIssues = 0;
            foreach (var f in basFiles)
            {
                string source;
                try { source = CodeUtils.ReadTextSafely(f.Path); }
                catch (Exception ex)
                {
                    results.Add(new { file = f.Path, error = ex.Message });
                    continue;
                }

                var (_, issues) = B4xParser.Parse(source);
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
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        [McpServerTool, Description("Lists every layout file (.bal for B4A, .bjl for B4J) in a project with basic metadata: screen variants (dimensions) and top-level control count, without dumping the full decoded tree of each. Use get_layout_structure afterward on a specific one for full detail.")]
        public static string ListLayouts(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")] string projectPath)
        {
            string? root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            if (root == null)
                return JsonSerializer.Serialize(new { error = $"Could not determine a B4X project root from '{projectPath}'." });

            var layoutFiles = ProjectScanner.ScanProject(root)
                .Where(f => f.Kind == "bal" || f.Kind == "bjl" || f.Kind == "bil").ToList();

            var results = new List<object>();
            foreach (var f in layoutFiles)
            {
                try
                {
                    var data = File.ReadAllBytes(f.Path);
                    var decodedJson = BalDecoder.Decode(data, full: true);
                    using var doc = JsonDocument.Parse(decodedJson);
                    var rootEl = doc.RootElement;

                    var variants = rootEl.TryGetProperty("variants", out var v) ? v.Clone() : default;
                    int kidCount = 0;
                    if (rootEl.TryGetProperty("layoutTree", out var tree) &&
                        tree.TryGetProperty("kids", out var kids) &&
                        kids.ValueKind == JsonValueKind.Array)
                    {
                        kidCount = kids.GetArrayLength();
                    }

                    results.Add(new
                    {
                        file = f.Path,
                        kind = f.Kind,
                        variants,
                        topLevelControlCount = kidCount
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new { file = f.Path, error = ex.Message });
                }
            }

            return JsonSerializer.Serialize(new { layoutCount = results.Count, layouts = results },
                new JsonSerializerOptions { WriteIndented = true });
        }
    }
}