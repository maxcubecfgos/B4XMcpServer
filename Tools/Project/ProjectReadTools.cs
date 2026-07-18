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
    public sealed class ProjectReadTools
    {
        private readonly IFileRepository _fileRepository;
        private readonly IProjectRepository _projectRepository;

        // Regex timeout protects against catastrophic backtracking on untrusted input.
        public ProjectReadTools(IFileRepository fileRepository, IProjectRepository projectRepository)
        {
            _fileRepository = fileRepository;
            _projectRepository = projectRepository;
        }

        [McpServerTool, Description("Returns the structure of a B4X project (B4A or B4J): the project root, the .b4a/.b4j/.b4i project file, and every module (.bas) and layout (.bal/.bjl/.bil) file found, ignoring build folders (Objects/bin/gen/obj). Accepts either the project folder path or the path to the .b4a/.b4j/.b4i file itself.")]
        public string GetProjectStructure(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file.")] string projectPath)
        {
            string? root = Directory.Exists(projectPath)
                ? projectPath
                : _projectRepository.FindProjectRoot(projectPath);

            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'. Pass either the project folder or its .b4a/.b4j/.b4i file.");

            var files = _projectRepository.ScanProject(root);
            var projectFile = _projectRepository.FindProjectFile(root);

            // ── WARNINGS ────────────────────────────────────────────────
            var warnings = new List<string>();
            bool hasMainBas = files.Any(f => f.Name.Equals("Main.bas", StringComparison.OrdinalIgnoreCase));

            bool isB4A = projectFile != null && string.Equals(Path.GetExtension(projectFile), ".b4a", StringComparison.OrdinalIgnoreCase);
            string? mainModuleName = projectFile != null ? Path.GetFileName(projectFile) : "the .b4a/.b4j/.b4i file";
            string entryPointHint = isB4A ? "Activity_Create, Process_Globals, etc." : "AppStart, Process_Globals, etc.";

            // Warning 1: The project file IS the Main module — always, regardless of its name.
            if (hasMainBas)
            {
                warnings.Add($"🛑 CRITICAL CORRUPTION: A 'Main.bas' file exists in the project root, but it is NOT the main module. " +
                              $"The REAL main module is '{mainModuleName}'. In B4X the .b4a/.b4j/.b4i file at the project root IS the Main module — REGARDLESS of its name. " +
                              $"DO NOT read, edit, or interact with 'Main.bas'. Ask the user to delete it, or ignore it completely. " +
                              $"All main Subs ({entryPointHint}) go in the source code section of '{mainModuleName}'.");
            }
            else
            {
                warnings.Add($"ℹ️ MAIN MODULE: The main module is '{mainModuleName}'. All main Subs ({entryPointHint}) live in its source code section. " +
                              $"DO NOT create or look for a 'Main.bas' file — it does not exist and must not be created.");
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
            warnings.Add(isB4A
                ? "🛑 UNTOUCHABLE: The #Region Project Attributes and #Region Activity Attributes blocks at the top of the source code section MUST NEVER be modified, moved, or deleted. " +
                  "They contain #ApplicationLabel, #VersionCode, #FullScreen, #IncludeTitle — essential IDE settings."
                : "🛑 UNTOUCHABLE: The #Region Project Attributes block at the top of the source code section MUST NEVER be modified, moved, or deleted. " +
                  "It contains #ApplicationLabel, #VersionCode — essential IDE settings.");

            // Warning 5: Manifest in metadata only
            warnings.Add(isB4A
                ? "ℹ️ #Region Manifest Editor and AddManifestText belong in the PROJECT METADATA section only. " +
                  "#Region Project Attributes and #Region Activity Attributes belong in the SOURCE CODE section. Do NOT move them between sections."
                : "ℹ️ #Region Project Attributes belongs in the SOURCE CODE section. Do NOT move it between sections.");
            // ──────────────────────────────────────────────────────────────

            var filesOutput = files.Select(f =>
            {
                bool isMainBas = f.Name.Equals("Main.bas", StringComparison.OrdinalIgnoreCase);
                bool isProjectFile = projectFile != null &&
                    string.Equals(f.Path, projectFile, StringComparison.OrdinalIgnoreCase);

                // Parse header for .bas modules to expose type/version/group
                object? headerInfo = null;
                if (f.Kind == "bas" && _fileRepository.Exists(f.Path))
                {
                    try
                    {
                        string raw = _fileRepository.ReadTextWithHeader(f.Path);
                        var parsed = ProjectHelpersShared.ParseFileHeader(raw);
                        if (parsed != null)
                        {
                            headerInfo = new
                            {
                                type = parsed.Type,
                                version = parsed.Version,
                                group = parsed.Group,
                                platform = parsed.Platform
                            };
                        }
                    }
                    catch { /* best-effort: skip header parsing for unreadable files */ }
                }

                return new
                {
                    path = f.Path,
                    name = f.Name,
                    kind = f.Kind,
                    role = isMainBas ? "corruption" : (isProjectFile ? "main_module" : "module"),
                    header = headerInfo
                };
            });

            int usableFileCount = files.Count(f => !f.Name.Equals("Main.bas", StringComparison.OrdinalIgnoreCase));

            var result = new
            {
                projectRoot = root,
                projectFile,
                mainModule = mainModuleName,
                fileCount = files.Count,
                usableFileCount,
                files = filesOutput,
                warnings = warnings
            };

            return JsonSerializer.Serialize(result, JsonOptions.Default);
        }

        [McpServerTool, Description("Returns the full text content of a file (B4X module .bas, project file .b4a/.b4j/.b4i, or any other text file). For .bas and project files (.b4a/.b4j/.b4i), strips the IDE metadata header automatically ONLY when the @EndOfDesignText@ marker is present; if the marker is missing, returns the full content including any header. ALSO returns parsed header metadata (platform, type, version, group, modulesStructureVersion) for .bas/.b4a/.b4j/.b4i files so you know the module's structure without a separate analyze_module call.")]
        public string GetFileContent(
            [Description("Absolute path to the file to read.")] string filePath)
        {
            PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

            if (!_fileRepository.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            bool isProjectFile = ext == ".b4a" || ext == ".b4j" || ext == ".b4i";
            bool isBasFile = ext == ".bas";

            if (isProjectFile || isBasFile)
            {
                string raw = _fileRepository.ReadTextWithHeader(filePath);
                var parsed = ProjectHelpersShared.ParseFileHeader(raw);
                // Strip header from raw content in memory (avoid second disk read)
                string content = parsed != null && parsed.HasMarker
                    ? raw.Substring(raw.IndexOf("@EndOfDesignText@", StringComparison.Ordinal) + "@EndOfDesignText@".Length).TrimStart('\r', '\n')
                    : raw;

                if (parsed != null)
                {
                    int libraryCount = parsed.AllFields.Keys.Count(k => Regex.IsMatch(k, @"^Library\d+$", RegexOptions.None, ProjectHelpersShared.RegexTimeout));
                    int moduleCount = parsed.AllFields.Keys.Count(k => Regex.IsMatch(k, @"^Module\d+$", RegexOptions.None, ProjectHelpersShared.RegexTimeout));
                    int fileCount = parsed.AllFields.Keys.Count(k => Regex.IsMatch(k, @"^File\d+$", RegexOptions.None, ProjectHelpersShared.RegexTimeout));

                    var numberingWarnings = new List<string>();
                    ProjectHelpersShared.ValidateSequentialNumbering(parsed.AllFields, "Library", libraryCount, numberingWarnings);
                    ProjectHelpersShared.ValidateSequentialNumbering(parsed.AllFields, "Module", moduleCount, numberingWarnings);
                    ProjectHelpersShared.ValidateSequentialNumbering(parsed.AllFields, "File", fileCount, numberingWarnings);

                    // Build the explicit numbered lines. Line numbers are FILE-LINE (1-based
                    // across the whole file including the IDE metadata header).
                    var contentLines = content.Replace("\r\n", "\n").Split('\n');
                    int numberedCount = contentLines.Length;
                    if (numberedCount > 0 && contentLines[^1].Length == 0)
                        numberedCount--;
                    var numberedLines = new object[numberedCount];
                    for (int i = 0; i < numberedCount; i++)
                    {
                        int fileLine = parsed.HeaderLineCount + 1 + i;
                        numberedLines[i] = new { line = fileLine, text = contentLines[i] };
                    }
                    int totalFileLines = parsed.HeaderLineCount + numberedCount;

                    return JsonSerializer.Serialize(new
                    {
                        filePath,
                        lineNumbering = "file",
                        totalLines = totalFileLines,
                        lineOffset = parsed.HeaderLineCount,
                        header = new
                        {
                            platform = parsed.Platform,
                            type = parsed.Type,
                            version = parsed.Version,
                            group = parsed.Group,
                            modulesStructureVersion = parsed.ModulesStructureVersion,
                            headerLineCount = parsed.HeaderLineCount,
                            libraryCount = libraryCount > 0 ? libraryCount : (int?)null,
                            moduleCount = moduleCount > 0 ? moduleCount : (int?)null,
                            fileCount = fileCount > 0 ? fileCount : (int?)null,
                            numberOfLibraries = parsed.AllFields.TryGetValue("NumberOfLibraries", out var nl) ? nl : null,
                            numberOfModules = parsed.AllFields.TryGetValue("NumberOfModules", out var nm) ? nm : null,
                            numberOfFiles = parsed.AllFields.TryGetValue("NumberOfFiles", out var nf) ? nf : null,
                            headerIntegrityWarnings = numberingWarnings.Count > 0 ? numberingWarnings : null
                        },
                        lines = numberedLines,
                        _reminder = "The header section (before @EndOfDesignText@) is SACRED. NEVER modify header fields except via enable_library/disable_library/library tools. Line numbers throughout this response (and in edit_line/insert_line/replace_lines/edit_sub/analyze_module) are FILE-LINE: 1-based from the first line of the FILE, including the IDE metadata header.",
                        content
                    }, JsonOptions.Default);
                }

                return content;
            }

            return _fileRepository.ReadText(filePath);
        }

        public sealed class SearchCodeRequest
        {
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")]
            public string ProjectPath { get; set; } = string.Empty;

            [Description(".NET regular expression to search for, matched against each line of every .bas file (case-insensitive).")]
            public string Pattern { get; set; } = string.Empty;

            [Description("Also search inside the .b4a/.b4j/.b4i project file's code section in addition to .bas modules. Default true.")]
            public bool IncludeProjectFile { get; set; } = true;

            [Description("Maximum number of matches to return. Default 200.")]
            public int MaxResults { get; set; } = 200;
        }

        [McpServerTool, Description("Searches for a regex pattern across every .bas module (and optionally the .b4a/.b4j project file) in a B4X project, like grep. Returns each match with its file, line number, and the matching line's text.")]
        public string SearchCode(SearchCodeRequest request)
        {
            var projectPath = request.ProjectPath;
            var pattern = request.Pattern;

            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            if (string.IsNullOrWhiteSpace(pattern))
                throw new ArgumentException("Pattern must not be empty.");

            string? root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Invalid regex pattern: {ex.Message}");
            }

            var files = _projectRepository.ScanProject(root)
                .Where(f => f.Kind == "bas" || (request.IncludeProjectFile && (f.Kind == "b4a" || f.Kind == "b4j" || f.Kind == "b4i")))
                .ToList();

            var matches = new List<object>();
            var filesSearched = 0;
            string? timeoutWarning = null;
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
                    bool isMatch;
                    try
                    {
                        isMatch = regex.IsMatch(lines[i]);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        timeoutWarning = $"Pattern took too long against a line in {f.Path}:{i + 1} and was stopped early.";
                        break;
                    }
                    if (isMatch)
                    {
                        matches.Add(new { file = f.Path, line = i + 1, text = lines[i].TrimEnd('\r').Trim() });
                        if (matches.Count >= request.MaxResults) break;
                    }
                }
                if (timeoutWarning != null) break;
            }

            return JsonSerializer.Serialize(new
            {
                pattern,
                filesSearched,
                matchCount = matches.Count,
                truncated = matches.Count >= request.MaxResults,
                timeoutWarning,
                matches
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Returns the full project configuration from the project file (.b4a/.b4j/.b4i) header. Returns libraries, modules, resource files, app type, package name, version info, and other header metadata. For analyzing a single .bas module structure use analyze_module instead of this tool.")]
        public string GetProjectConfig(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file. For inspecting a single .bas module use analyze_module instead — this tool's projectPath is the project root, not a module file.")] string projectPath)
        {
            // Round-3 polish: relative paths are resolved by Exists + FindProjectFile below,
            // matching get_project_structure's leniency.
            string? projectFile = _fileRepository.Exists(projectPath) ? projectPath : _projectRepository.FindProjectFile(projectPath);
            if (projectFile == null)
                throw new FileNotFoundException($"No .b4a/.b4j/.b4i project file found for '{projectPath}'.");

            // Cache the parsed config JSON by file mtime.
            string cacheKey = $"project-config:{projectFile}";
            if (CacheManager.TryGetByMtime<string>(projectFile, out var cachedConfig) && cachedConfig != null)
                return cachedConfig;

            string raw = _fileRepository.ReadTextWithHeader(projectFile);

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

            var libraries = rawSettings.Where(kv => Regex.IsMatch(kv.Key, @"^Library\d+$", RegexOptions.None, ProjectHelpersShared.RegexTimeout)).Select(kv => kv.Value).OrderBy(v => v).ToList();
            var modules = rawSettings.Where(kv => Regex.IsMatch(kv.Key, @"^Module\d+$", RegexOptions.None, ProjectHelpersShared.RegexTimeout)).Select(kv => kv.Value).ToList();
            var includedFiles = rawSettings.Where(kv => Regex.IsMatch(kv.Key, @"^File\d+$", RegexOptions.None, ProjectHelpersShared.RegexTimeout)).Select(kv => kv.Value).ToList();

            var result = new
            {
                projectFile,
                appType = rawSettings.TryGetValue("AppType", out var at) ? at : null,
                version = rawSettings.TryGetValue("Version", out var ver) ? ver : null,
                packageName = rawSettings.TryGetValue("Package", out var pkg) ? pkg : null,
                versionName = rawSettings.TryGetValue("VersionName", out var vn) ? vn : null,
                versionCode = rawSettings.TryGetValue("VersionCode", out var vc) ? vc : null,
                numberOfLibraries = rawSettings.TryGetValue("NumberOfLibraries", out var nol) ? nol : null,
                numberOfModules = rawSettings.TryGetValue("NumberOfModules", out var nom) ? nom : null,
                numberOfFiles = rawSettings.TryGetValue("NumberOfFiles", out var nof) ? nof : null,
                libraries,
                modules,
                resourceFiles = includedFiles,
                rawSettings
            };

            var json = JsonSerializer.Serialize(result, JsonOptions.Default);
            CacheManager.SetByMtime(projectFile, json);
            return json;
        }
    }
}
