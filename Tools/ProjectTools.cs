using B4XContext.Engine;
using B4XContext.Models;
using B4XContext.Services;
using B4XContext.Utils;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContextFileMode = B4XContext.Models.FileMode;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class ProjectTools
    {
        // Clase auxiliar para cache de ValidateProject
        private class CachedParseResult
        {
            public List<B4xParser.ParseIssue> Issues { get; set; } = new();
        }

        [McpServerTool, Description("Returns the structure of a B4X project (B4A or B4J): the project root, the .b4a/.b4j/.b4i project file, and every module (.bas) and layout (.bal/.bjl/.bil) file found, ignoring build folders (Objects/bin/gen/obj). Accepts either the project folder path or the path to the .b4a/.b4j/.b4i file itself.")]
        public static string GetProjectStructure(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file.")] string projectPath)
        {
            string? root = Directory.Exists(projectPath)
                ? projectPath
                : ProjectScanner.FindProjectRoot(projectPath);

            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'. Pass either the project folder or its .b4a/.b4j/.b4i file.");

            var files = ProjectScanner.ScanProject(root);
            var projectFile = ProjectScanner.FindProjectFile(root);

            // ── WARNINGS ────────────────────────────────────────────────
            var warnings = new List<string>();
            bool hasMainBas = files.Any(f => f.Name.Equals("Main.bas", StringComparison.OrdinalIgnoreCase));

            // Warning 1: Where does the main code live?
            if (!hasMainBas)
            {
                string? mainModule = projectFile != null ? Path.GetFileName(projectFile) : "the .b4a/.b4j file";
                warnings.Add($"⚠️ CRITICAL: This project does NOT have a Main.bas file. The main activity code lives inside '{mainModule}'. " +
                              "DO NOT create Main.bas. DO NOT look for Main.bas. All Subs (Process_Globals, Globals, Activity_Create, etc.) go in the project file itself.");
            }

            // Warning 2: File structure
            if (projectFile != null)
            {
                warnings.Add($"ℹ️ The project file is '{Path.GetFileName(projectFile)}'. It has TWO sections:\n" +
                              "  • PROJECT METADATA — IDE settings: NumberOfModules, Module1=Starter, Library1=core, Build1=, ManifestCode=, etc. NEVER delete or modify except via enable_library/disable_library.\n" +
                              "  • SOURCE CODE — All Subs and logic. This is where you write and edit code.");
            }

            // Warning 3: Starter.bas is hidden
            warnings.Add("ℹ️ Starter.bas exists but is hidden from this file list. It's a system service that handles app lifecycle. " +
                          "Module1=Starter in the project metadata is required. NEVER read, modify, or worry about Starter.bas — it just works.");

            // Warning 4: Sacred regions
            warnings.Add("🛑 UNTOUCHABLE: The #Region Project Attributes and #Region Activity Attributes blocks at the top of the source code section MUST NEVER be modified, moved, or deleted. " +
                          "They contain #ApplicationLabel, #VersionCode, #FullScreen, #IncludeTitle — essential IDE settings.");

            // Warning 5: Manifest in metadata only
            warnings.Add("ℹ️ #Region Manifest Editor and AddManifestText belong in the PROJECT METADATA section only. " +
                          "#Region Project Attributes and #Region Activity Attributes belong in the SOURCE CODE section. Do NOT move them between sections.");
            // ──────────────────────────────────────────────────────────────

            var result = new
            {
                projectRoot = root,
                projectFile,
                fileCount = files.Count,
                files = files.Select(f => new { path = f.Path, name = f.Name, kind = f.Kind }),
                warnings = warnings
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        [McpServerTool, Description("Returns the full text content of a file (B4X module .bas, project file .b4a/.b4j/.b4i, or any other text file). For .bas files, strips the IDE metadata header automatically. For .b4a/.b4j files, returns the clean source code only.")]
        public static string GetFileContent(
            [Description("Absolute path to the file to read.")] string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            bool isProjectFile = ext == ".b4a" || ext == ".b4j" || ext == ".b4i";
            bool isBasFile = ext == ".bas";

            if (isProjectFile || isBasFile)
                return CodeUtils.ReadTextSafely(filePath);

            return File.ReadAllText(filePath);
        }

        [McpServerTool, Description("Writes (overwrites) a file with the given content. This replaces the entire file, so read it first with get_file_content if you need to preserve parts of it. Typically used to save an edited B4X module back to disk.")]
        public static string WriteFile(
            [Description("Absolute path to the file to write.")] string filePath,
            [Description("The full new content of the file.")] string content)
        {
            File.WriteAllText(filePath, content);
            CacheManager.Invalidate(filePath);
            return JsonSerializer.Serialize(new
            {
                success = true,
                path = filePath,
                bytesWritten = System.Text.Encoding.UTF8.GetByteCount(content)
            });
        }

        [McpServerTool, Description("Compiles a B4X project (B4A, B4J, or B4i) using the platform-correct builder selected automatically from the project file extension.\n\n" +
            "*** CRITICAL: This is the ONLY way to compile. NEVER run shell commands (dir, cd, type, cat, B4ABuilder.exe, etc.). If compilation fails, this tool returns the exact errors with file names, line numbers, and source lines. READ THEM and fix the code — do not try to debug by running commands manually. ***")]
        public static string CompileProject(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")] string projectPath,
            [Description("Timeout in seconds. Default 300.")] int timeoutSeconds = 300)
        {
            string? projectFile = File.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectFile(projectPath);
            if (projectFile == null)
                return $"❌ ERROR: No .b4a/.b4j/.b4i project file found for '{projectPath}'.";

            // ── PRE-COMPILE VALIDATION ────────────────────────────────────
            var preCheckErrors = ValidateProjectBeforeCompile(projectFile);
            if (preCheckErrors.Count > 0)
            {
                return $"❌ PRE-COMPILE VALIDATION FAILED\n\nThe project file is structurally broken and would fail to compile or even open in the IDE.\n\n" +
                       $"{string.Join("\n", preCheckErrors)}\n\n" +
                       $"Fix these issues with write_file or edit_sub, then call compile_project again.";
            }
            // ──────────────────────────────────────────────────────────────

            var builderPath = BuilderLocator.LocateBuilder(projectFile);
            if (builderPath == null)
                return $"❌ ERROR: Could not locate the matching builder. Create b4x_context_config.json with \"builder_path\" next to the project file.";

            var buildResult = BuilderRunner.RunBuild(builderPath, projectFile, timeoutSeconds);

            if (buildResult.TryGetValue("fatal_error", out var fatal) && fatal != null)
                return $"❌ BUILD SYSTEM ERROR\n\n{fatal}\n\nDo NOT try to run the builder manually.";

            bool success = buildResult.TryGetValue("success", out var s) && s is bool sb && sb;

            if (!success)
            {
                var formattedErrors = BuildFormatter.Format(buildResult);
                return $"❌ COMPILATION FAILED\n\n" +
                       $"DO NOT run shell commands. Read the errors below, fix the code with write_file or edit_sub, then call compile_project again.\n\n" +
                       $"{formattedErrors}";
            }

            return $"✅ COMPILATION SUCCESSFUL\nBuilder: {builderPath}\nNo errors.";
        }

        /// <summary>
        /// Validates project file structure before attempting to compile.
        /// Catches broken headers that would make the project unopenable in the IDE.
        /// </summary>
        private static List<string> ValidateProjectBeforeCompile(string projectFile)
        {
            var errors = new List<string>();

            if (!File.Exists(projectFile))
            {
                errors.Add($"Project file not found: {projectFile}");
                return errors;
            }

            string raw = File.ReadAllText(projectFile);
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);

            if (markerIdx < 0)
            {
                errors.Add("❌ CRITICAL: The project file is corrupted — it's missing its internal section separator. The file cannot be compiled. Restore it from the .bak backup.");
                return errors;
            }

            string headerSection = raw.Substring(0, markerIdx);
            string codeSection = raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n');

            // Parse header key=value pairs
            var header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in headerSection.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r').Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                header[trimmed.Substring(0, eq).Trim()] = trimmed.Substring(eq + 1).Trim();
            }

            // Check 1: NumberOfModules must match actual ModuleN entries
            if (header.TryGetValue("NumberOfModules", out var numModStr) && int.TryParse(numModStr, out int expectedModules))
            {
                int actualModules = header.Keys.Count(k => Regex.IsMatch(k, @"^Module\d+$"));
                if (actualModules != expectedModules)
                    errors.Add($"❌ NumberOfModules={expectedModules} but found {actualModules} ModuleN entries. Update NumberOfModules to {actualModules}.");
            }
            else if (!header.ContainsKey("NumberOfModules"))
            {
                int actualModules = header.Keys.Count(k => Regex.IsMatch(k, @"^Module\d+$"));
                if (actualModules > 0)
                    errors.Add($"❌ Missing NumberOfModules key. Add: NumberOfModules={actualModules}");
            }

            // Check 2: NumberOfLibraries must match actual LibraryN entries
            if (header.TryGetValue("NumberOfLibraries", out var numLibStr) && int.TryParse(numLibStr, out int expectedLibs))
            {
                int actualLibs = header.Keys.Count(k => Regex.IsMatch(k, @"^Library\d+$"));
                if (actualLibs != expectedLibs)
                    errors.Add($"❌ NumberOfLibraries={expectedLibs} but found {actualLibs} LibraryN entries. Update NumberOfLibraries to {actualLibs}.");
            }

            // Check 3: NumberOfFiles must match actual FileN entries
            if (header.TryGetValue("NumberOfFiles", out var numFilesStr) && int.TryParse(numFilesStr, out int expectedFiles))
            {
                int actualFiles = header.Keys.Count(k => Regex.IsMatch(k, @"^File\d+$"));
                if (actualFiles != expectedFiles)
                    errors.Add($"❌ NumberOfFiles={expectedFiles} but found {actualFiles} FileN entries. Update NumberOfFiles to {actualFiles}.");
            }

            // Check 4: Module numbering must be sequential starting from 1
            var moduleNumbers = header.Keys
                .Where(k => Regex.IsMatch(k, @"^Module\d+$"))
                .Select(k => int.Parse(Regex.Match(k, @"\d+").Value))
                .OrderBy(n => n)
                .ToList();
            for (int i = 0; i < moduleNumbers.Count; i++)
            {
                if (moduleNumbers[i] != i + 1)
                    errors.Add($"❌ Module numbering is not sequential. Expected Module{i + 1} but found Module{moduleNumbers[i]}. Renumber all ModuleN entries sequentially starting from 1.");
            }

            // Check 5: Code section must contain at least one Sub
            if (string.IsNullOrWhiteSpace(codeSection) || !codeSection.Contains("Sub "))
                errors.Add("⚠️ Warning: Source code section is empty or contains no Sub declarations. The app has no executable code.");

            // Check 6: Referenced modules must exist on disk (auto-append .bas if no extension)
            string projectDir = Path.GetDirectoryName(projectFile) ?? ".";
            foreach (var kv in header.Where(kv => Regex.IsMatch(kv.Key, @"^Module\d+$")))
            {
                var moduleName = kv.Value;
                var modulePath = Path.Combine(projectDir, moduleName);

                if (!Path.HasExtension(modulePath))
                    modulePath += ".bas";

                if (!File.Exists(modulePath))
                    errors.Add($"❌ Module '{moduleName}' is referenced in {kv.Key} but file not found at: {modulePath}");
            }

            // Check 7: #Region Manifest Editor must be in metadata section only (NOT in source code)
            if (codeSection.Contains("#Region Manifest Editor", StringComparison.OrdinalIgnoreCase) ||
                codeSection.Contains("#Region  Manifest Editor", StringComparison.OrdinalIgnoreCase))
                errors.Add("❌ FATAL: #Region Manifest Editor found in SOURCE CODE section. It belongs in the PROJECT METADATA section only. Use write_manifest tool to modify it.");

            // Check 8: AddManifestText must be in metadata section only
            if (codeSection.Contains("AddManifestText", StringComparison.OrdinalIgnoreCase))
                errors.Add("❌ AddManifestText found in SOURCE CODE section. Manifest modifications belong in the PROJECT METADATA section only, never in code.");

            // Check 9: #Region Project Attributes and #Region Activity Attributes MUST be in source code section
            bool hasProjectAttrs = codeSection.Contains("#Region  Project Attributes", StringComparison.OrdinalIgnoreCase) ||
                                   codeSection.Contains("#Region Project Attributes", StringComparison.OrdinalIgnoreCase);
            bool hasActivityAttrs = codeSection.Contains("#Region  Activity Attributes", StringComparison.OrdinalIgnoreCase) ||
                                    codeSection.Contains("#Region Activity Attributes", StringComparison.OrdinalIgnoreCase);

            if (!hasProjectAttrs)
                errors.Add("🛑 FATAL: #Region Project Attributes block is MISSING from the source code section. This block is REQUIRED (#ApplicationLabel, #VersionCode, etc.). Restore it from the .bak backup file.");

            if (!hasActivityAttrs)
                errors.Add("🛑 FATAL: #Region Activity Attributes block is MISSING from the source code section. This block is REQUIRED (#FullScreen, #IncludeTitle, etc.). Restore it from the .bak backup file.");

            return errors;
        }

        [McpServerTool, Description("Decodes a B4X visual layout file into readable JSON: control hierarchy, types, positions (resolved from the correct screen variant, not the misleading top-level template defaults), and properties like text/hint/tag/drawable. Works for both .bal (B4A) and .bjl (B4J) — they share the exact same binary format.")]
        public static string GetLayoutStructure(
            [Description("Absolute path to the .bal or .bjl layout file.")] string layoutPath)
        {
            if (!File.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            var data = File.ReadAllBytes(layoutPath);
            var decoded = BalDecoder.Decode(data);
            return decoded;
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
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

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
                            bool success = buildResult.TryGetValue("success", out var s2) && s2 is bool sb2 && sb2;
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

        [McpServerTool, Description("Replaces the entire body of a single Sub in a B4X module in-place, without touching the rest of the file. Locates the Sub by name using the real B4X parser, so partial/skeleton context is enough to safely target it. If the Sub isn't found, returns the list of Subs that do exist in the file so the caller can retry with the correct name.")]
        public static string EditSub(EditSubRequest request)
        {
            var filePath = request.FilePath;
            var subName = request.SubName;
            var newCode = request.NewCode;

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            string raw = File.ReadAllText(filePath);

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
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            var lines = codeSection.Replace("\r\n", "\n").Split('\n').ToList();
            int startIdx = target.StartLine - 1;
            int endIdx = target.EndLine.Value - 1;

            if (startIdx < 0 || endIdx >= lines.Count || startIdx > endIdx)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Internal error: Sub line range out of bounds after parsing."
                }, new JsonSerializerOptions { WriteIndented = true });

            var newLines = newCode.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            lines.RemoveRange(startIdx, endIdx - startIdx + 1);
            lines.InsertRange(startIdx, newLines);

            var updatedCodeSection = string.Join("\n", lines);
            var finalContent = markerIdx >= 0 ? header + "\r\n" + updatedCodeSection : updatedCodeSection;

            File.WriteAllText(filePath, finalContent);
            CacheManager.Invalidate(filePath);

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

        [McpServerTool, Description("Searches for a regex pattern across every .bas module (and optionally the .b4a/.b4j project file) in a B4X project, like grep. Returns each match with its file, line number, and the matching line's text.")]
        public static string SearchCode(SearchCodeRequest request)
        {
            var projectPath = request.ProjectPath;
            var pattern = request.Pattern;

            if (string.IsNullOrWhiteSpace(pattern))
                throw new ArgumentException("Pattern must not be empty.");

            string? root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

            var regex = new Regex(pattern, RegexOptions.IgnoreCase);

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

        [McpServerTool, Description("Parses a B4X project file's project metadata into structured JSON: app type, version, referenced libraries, module list, included files, and every other raw key=value setting.")]
        public static string GetProjectConfig(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file.")] string projectPath)
        {
            string? projectFile = File.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectFile(projectPath);
            if (projectFile == null)
                throw new FileNotFoundException($"No .b4a/.b4j/.b4i project file found for '{projectPath}'.");

            string raw = File.ReadAllText(projectFile);

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

        [McpServerTool, Description("Analyzes a single B4X module (.bas): lists every Sub (name, parameters, return type, public/private, event handler detection), every Type declaration, and Globals presence. Also reports structural parse issues without compiling.")]
        public static string AnalyzeModule(
            [Description("Absolute path to the .bas module file.")] string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            string cacheKey = $"analyze:{filePath}";
            if (CacheManager.TryGetByMtime<string>(filePath, out var cached) && cached != null)
                return cached;

            string source = CodeUtils.ReadTextSafely(filePath);
            var (root, issues) = B4xParser.Parse(source);
            var nodes = B4xParser.FlattenSubsAndTypes(root);

            var subs = nodes.Where(n => n.Kind == "Sub").Select(n => new
            {
                name = n.Name,
                parameters = n.Params,
                returnType = n.ReturnType,
                isPrivate = n.IsPrivate,
                looksLikeEventHandler = Regex.IsMatch(n.Name,
                    @"_(Click|Create|Resume|Pause|CheckedChange|TextChanged|Tick|JobDone|Complete|ItemClick|LongClick|FocusChanged)$",
                    RegexOptions.IgnoreCase),
                startLine = n.StartLine,
                endLine = n.EndLine
            }).ToList();

            var types = nodes.Where(n => n.Kind == "Type")
                .Select(n => new { name = n.Name, startLine = n.StartLine, endLine = n.EndLine }).ToList();

            var result = JsonSerializer.Serialize(new
            {
                filePath,
                hasProcessGlobals = nodes.Any(n => n.Kind == "Process_Globals"),
                hasGlobals = nodes.Any(n => n.Kind == "Globals"),
                hasClassGlobals = nodes.Any(n => n.Kind == "Class_Globals"),
                subCount = subs.Count,
                subs,
                types,
                parseIssues = issues.Select(i => new { line = i.Line, message = i.Message, severity = i.Severity })
            }, new JsonSerializerOptions { WriteIndented = true });

            CacheManager.SetByMtime(filePath, result);
            CacheManager.Store(cacheKey, result);

            return result;
        }

        [McpServerTool, Description("Runs the B4X structural parser against every module (.bas) in a project WITHOUT compiling, and reports any structural problems found (unclosed Sub/Type/Region blocks, mismatched End statements). Near-instant sanity check.")]
        public static string ValidateProject(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")] string projectPath)
        {
            string? root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

            var basFiles = ProjectScanner.ScanProject(root).Where(f => f.Kind == "bas").ToList();

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
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        [McpServerTool, Description("Lists every layout file (.bal/.bjl/.bil) in a project with basic metadata: screen variants and top-level control count.")]
        public static string ListLayouts(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")] string projectPath)
        {
            string? root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

            var layoutFiles = ProjectScanner.ScanProject(root)
                .Where(f => f.Kind == "bal" || f.Kind == "bjl" || f.Kind == "bil").ToList();

            var results = new List<object>();
            foreach (var f in layoutFiles)
            {
                try
                {
                    var data = File.ReadAllBytes(f.Path);
                    var decodedJson = BalDecoder.Decode(data);
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

        private const string ManifestStartMarker = "#Region Manifest Editor";
        private const string ManifestEndMarker = "#End Region";

        [McpServerTool, Description("Extracts the Manifest Editor block from a B4A project file.")]
        public static string GetManifest(
            [Description("Absolute path to the .b4a project file.")] string projectPath)
        {
            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"File not found: {projectPath}");
            if (!projectPath.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("File must have a .b4a extension.");

            string raw = File.ReadAllText(projectPath);

            var block = ExtractManifestBlock(raw);
            if (block == null)
                throw new InvalidOperationException("No 'Manifest Editor' region found in this project.");

            return JsonSerializer.Serialize(new { projectPath, manifest = block });
        }

        [McpServerTool, Description("Replaces the Manifest Editor block in a B4A project file. Creates a .bak backup first.")]
        public static string WriteManifest(
            [Description("Absolute path to the .b4a project file.")] string projectPath,
            [Description("New content for the Manifest Editor block.")] string manifestContent)
        {
            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"File not found: {projectPath}");
            if (!projectPath.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("File must have a .b4a extension.");

            string raw = File.ReadAllText(projectPath);

            int startIdx = raw.IndexOf(ManifestStartMarker, StringComparison.Ordinal);
            if (startIdx < 0)
                throw new InvalidOperationException("No 'Manifest Editor' region found in this project.");

            int endIdx = raw.IndexOf(ManifestEndMarker, startIdx, StringComparison.Ordinal);
            if (endIdx < 0)
                throw new InvalidOperationException("Found '#Region Manifest Editor' but no matching '#End Region'.");

            File.Copy(projectPath, projectPath + ".bak", overwrite: true);

            var before = raw.Substring(0, startIdx + ManifestStartMarker.Length);
            var after = raw.Substring(endIdx);
            var newContent = before + "\r\n" + manifestContent.TrimEnd('\r', '\n') + "\r\n" + after;

            File.WriteAllText(projectPath, newContent);

            return JsonSerializer.Serialize(new { success = true, projectPath, backup = projectPath + ".bak" });
        }

        private static string? ExtractManifestBlock(string raw)
        {
            int startIdx = raw.IndexOf(ManifestStartMarker, StringComparison.Ordinal);
            if (startIdx < 0) return null;
            int contentStart = startIdx + ManifestStartMarker.Length;
            int endIdx = raw.IndexOf(ManifestEndMarker, contentStart, StringComparison.Ordinal);
            if (endIdx < 0) return null;
            return raw.Substring(contentStart, endIdx - contentStart).Trim('\r', '\n');
        }
    }
}