using B4XMcpServer.Engine;
using B4XMcpServer.Models;
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
using System.Threading.Tasks;
using ContextFileMode = B4XMcpServer.Models.FileMode;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class ProjectTools
    {
        // Regex timeout protects against catastrophic backtracking on untrusted input.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

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

            bool isB4A = projectFile != null && string.Equals(Path.GetExtension(projectFile), ".b4a", StringComparison.OrdinalIgnoreCase);

            // Warning 1: Where does the main code live?
            if (!hasMainBas)
            {
                string? mainModule = projectFile != null ? Path.GetFileName(projectFile) : "the .b4a/.b4j file";
                string entryPointHint = isB4A ? "Activity_Create, etc." : "AppStart, etc.";
                warnings.Add($"⚠️ CRITICAL: This project does NOT have a Main.bas file. The main {(isB4A ? "activity" : "module")} code lives inside '{mainModule}'. " +
                              $"DO NOT create Main.bas. DO NOT look for Main.bas. All Subs (Process_Globals, Globals, {entryPointHint}) go in the project file itself.");
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
            // #Region Activity Attributes is Android/Activity-specific (B4A only) — B4J (Form) and
            // B4i (Page) projects never have it, so only mention it for B4A to avoid misleading
            // the caller into thinking a B4J/B4i project is missing something.
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

            var result = new
            {
                projectRoot = root,
                projectFile,
                fileCount = files.Count,
                files = files.Select(f => new { path = f.Path, name = f.Name, kind = f.Kind }),
                warnings = warnings
            };

            return JsonSerializer.Serialize(result, JsonOptions.Default);
        }

        [McpServerTool, Description("Returns the full text content of a file (B4X module .bas, project file .b4a/.b4j/.b4i, or any other text file). For .bas files, strips the IDE metadata header automatically. For .b4a/.b4j files, returns the clean source code only.")]
        public static string GetFileContent(
            [Description("Absolute path to the file to read.")] string filePath)
        {
            PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            bool isProjectFile = ext == ".b4a" || ext == ".b4j" || ext == ".b4i";
            bool isBasFile = ext == ".bas";

            if (isProjectFile || isBasFile)
                return CodeUtils.ReadTextSafely(filePath);

            return File.ReadAllText(filePath);
        }

        [McpServerTool, Description("Writes (overwrites) a file with the given content. This replaces the entire file, so read it first with get_file_content if you need to preserve parts of it. Typically used to save an edited B4X module back to disk. BLOCKED for existing .b4a/.b4j/.b4i project files to prevent IDE metadata corruption — use edit_sub for code, enable_library/disable_library for libraries, write_manifest for manifest, and register_layout_in_project/register_module_in_project for layouts/modules. Creates a .bak backup first if the file already exists.")]
        public static string WriteFile(
            [Description("Absolute path to the file to write.")] string filePath,
            [Description("The full new content of the file.")] string content)
        {
            PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

            // For destructive writes, try to keep them inside the project root.
            string? projectRoot = ProjectScanner.FindProjectRoot(PathSecurity.GetDirectoryForProjectRoot(filePath));
            if (projectRoot != null)
                PathSecurity.ValidateWithinBaseDirectory(filePath, projectRoot, nameof(filePath));

            // CRITICAL: block CREATING a new Main.bas when the parent directory
            // already has a B4X project file. The .b4a/.b4j IS the Main module —
            // adding a separate Main.bas instantiates duplicate Main and corrupts
            // the project (compile errors, IDE confusion). AI assistants keep
            // trying this; this guard makes it a hard error rather than a soft
            // warning that the AI scrolls past. Editing an existing Main.bas
            // that the human explicitly authored is still allowed (File.Exists
            // check below distinguishes the two cases).
            if (PathSecurity.IsForbiddenMainBas(filePath, out var blockReason))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = blockReason,
                    hints = new[]
                    {
                        "The .b4a/.b4j/.b4i file IS the project's Main module — Activity_Create / Process_Globals / AppStart live in its source code section.",
                        "To add a Sub to the Main module, use edit_sub on the project file (NOT write_file).",
                        "Call get_project_structure first to confirm which files exist; if Main.bas is not listed, the main code goes in the project file.",
                        "If you previously corrupted the project by creating Main.bas, remove it manually after restoring the project file from its .bak backup."
                    }
                }, JsonOptions.Default);
            }

            // Direct writes to existing B4X project files corrupt the IDE metadata header.
            // Only specialized tools are allowed to touch these files.
            if (File.Exists(filePath) && PathSecurity.IsMainProjectFile(filePath))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "❌ CRITICAL: Direct modification of .b4a/.b4j/.b4i project files using write_file is blocked to prevent IDE metadata corruption.",
                    hints = new[]
                    {
                        "To add/remove libraries, use enable_library or disable_library ONLY.",
                        "To edit code (Subs) in the project file, use edit_sub.",
                        "To edit the B4A manifest, use write_manifest.",
                        "To register a new layout or module, use register_layout_in_project or register_module_in_project."
                    }
                }, JsonOptions.Default);
            }

            string? backupPath = null;
            if (File.Exists(filePath))
            {
                backupPath = filePath + ".bak";
                File.Copy(filePath, backupPath, overwrite: true);
            }

            File.WriteAllText(filePath, content);
            CacheManager.Invalidate(filePath);
            return JsonSerializer.Serialize(new
            {
                success = true,
                path = filePath,
                backup = backupPath,
                bytesWritten = System.Text.Encoding.UTF8.GetByteCount(content)
            });
        }

        [McpServerTool, Description("Compiles a B4X project (B4A, B4J, or B4i) using the platform-correct builder selected automatically from the project file extension.\n\n" +
            "*** CRITICAL: This is the ONLY way to compile. NEVER run shell commands (dir, cd, type, cat, B4ABuilder.exe, etc.). If compilation fails, this tool returns the exact errors with file names, line numbers, and source lines. READ THEM and fix the code — do not try to debug by running commands manually. ***")]
        public static async Task<string> CompileProject(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")] string projectPath,
            [Description("Timeout in seconds. Default 300.")] int timeoutSeconds = 300)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            string? projectFile = File.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectFile(projectPath);
            if (projectFile == null)
                return ToolResponse.Error(
                    $"No .b4a/.b4j/.b4i project file found for '{projectPath}'.",
                    hints: new[] { "Pass the project folder path, not a file that doesn't exist.", "Confirm the project file is at the project root, not nested in a subfolder." });

            // ── PRE-COMPILE VALIDATION ────────────────────────────────────
            var preCheckErrors = ValidateProjectBeforeCompile(projectFile);
            if (preCheckErrors.Count > 0)
            {
                return ToolResponse.Error(
                    "PRE-COMPILE VALIDATION FAILED — the project file is structurally broken and would fail to compile or even open in the IDE.",
                    hints: preCheckErrors.ToArray(),
                    nextSteps: new[] { "Fix each issue with write_file or edit_sub, then call compile_project again." });
            }
            // ──────────────────────────────────────────────────────────────

            var builderPath = BuilderLocator.LocateBuilder(projectFile);
            if (builderPath == null)
                return ToolResponse.Error(
                    "Could not locate the matching builder for this project type.",
                    hints: new[] { "Create b4x_context_config.json next to the project file with a 'builder_path' key." });

            var buildResult = await BuilderRunner.RunBuildAsync(builderPath, projectFile, timeoutSeconds);

            if (buildResult.TryGetValue("fatal_error", out var fatal) && fatal != null)
                return ToolResponse.Error(
                    $"BUILD SYSTEM ERROR: {fatal}",
                    hints: new[] { "Do NOT try to run the builder manually — use compile_project instead." });

            bool success = buildResult.TryGetValue("success", out var s) && s is bool sb && sb;

            if (!success)
            {
                var formattedErrors = BuildFormatter.Format(buildResult);
                return ToolResponse.Error(
                    "COMPILATION FAILED.",
                    hints: new[] { "DO NOT run shell commands. Read the structured errors in `data.buildErrors`, fix the code with write_file or edit_sub, then call compile_project again." },
                    nextSteps: new[] { "Read the embedded build errors, fix the listed file:line:message, then call compile_project again." },
                    data: new { buildErrors = formattedErrors })
                    .Replace("\"data\": null", $"\"data\": {{ \"buildErrors\": {System.Text.Json.JsonSerializer.Serialize(formattedErrors, JsonOptions.Default)} }}");
            }

            return ToolResponse.Success(
                data: new { builder = builderPath, message = "No errors." },
                hints: new[] { "Use run_project or launch_debug to deploy the freshly-built binary." });
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

            // Use the same encoding-detection cascade as everywhere else in the codebase —
            // File.ReadAllText assumes UTF-8/ASCII and would misread a windows-1252 project
            // file, same underlying issue that was fixed in CodeUtils.
            string raw = CodeUtils.DecodeFileWithFallback(projectFile);
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);

            if (markerIdx < 0)
            {
                errors.Add("❌ CRITICAL: The project file is corrupted — it's missing its internal section separator. The file cannot be compiled. Restore it from the .bak backup.");
                return errors;
            }

            string headerSection = raw.Substring(0, markerIdx);
            string codeSection = raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n');

            bool isB4A = string.Equals(Path.GetExtension(projectFile), ".b4a", StringComparison.OrdinalIgnoreCase);

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
                int actualModules = header.Keys.Count(k => Regex.IsMatch(k, @"^Module\d+$", RegexOptions.None, RegexTimeout));
                if (actualModules != expectedModules)
                    errors.Add($"❌ NumberOfModules={expectedModules} but found {actualModules} ModuleN entries. Update NumberOfModules to {actualModules}.");
            }
            else if (!header.ContainsKey("NumberOfModules"))
            {
                int actualModules = header.Keys.Count(k => Regex.IsMatch(k, @"^Module\d+$", RegexOptions.None, RegexTimeout));
                if (actualModules > 0)
                    errors.Add($"❌ Missing NumberOfModules key. Add: NumberOfModules={actualModules}");
            }

            // Check 2: NumberOfLibraries must match actual LibraryN entries
            if (header.TryGetValue("NumberOfLibraries", out var numLibStr) && int.TryParse(numLibStr, out int expectedLibs))
            {
                int actualLibs = header.Keys.Count(k => Regex.IsMatch(k, @"^Library\d+$", RegexOptions.None, RegexTimeout));
                if (actualLibs != expectedLibs)
                    errors.Add($"❌ NumberOfLibraries={expectedLibs} but found {actualLibs} LibraryN entries. Update NumberOfLibraries to {actualLibs}.");
            }

            // Check 3: NumberOfFiles must match actual FileN entries
            if (header.TryGetValue("NumberOfFiles", out var numFilesStr) && int.TryParse(numFilesStr, out int expectedFiles))
            {
                int actualFiles = header.Keys.Count(k => Regex.IsMatch(k, @"^File\d+$", RegexOptions.None, RegexTimeout));
                if (actualFiles != expectedFiles)
                    errors.Add($"❌ NumberOfFiles={expectedFiles} but found {actualFiles} FileN entries. Update NumberOfFiles to {actualFiles}.");
            }

            // Check 4: Module numbering must be sequential starting from 1
            var moduleNumbers = header.Keys
                .Where(k => Regex.IsMatch(k, @"^Module\d+$", RegexOptions.None, RegexTimeout))
                .Select(k => int.Parse(Regex.Match(k, @"\d+", RegexOptions.None, RegexTimeout).Value))
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
            foreach (var kv in header.Where(kv => Regex.IsMatch(kv.Key, @"^Module\d+$", RegexOptions.None, RegexTimeout)))
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

            // Check 9: #Region Project Attributes MUST be in source code section (all project types).
            // #Region Activity Attributes, however, is Android/Activity-specific — B4J (desktop/
            // server, uses Form) and B4i (uses Page) projects never have this region, so requiring
            // it there is a false positive that blocks compilation for no reason.
            bool hasProjectAttrs = codeSection.Contains("#Region  Project Attributes", StringComparison.OrdinalIgnoreCase) ||
                                   codeSection.Contains("#Region Project Attributes", StringComparison.OrdinalIgnoreCase);

            if (!hasProjectAttrs)
                errors.Add("🛑 FATAL: #Region Project Attributes block is MISSING from the source code section. This block is REQUIRED (#ApplicationLabel, #VersionCode, etc.). Restore it from the .bak backup file.");

            if (isB4A)
            {
                bool hasActivityAttrs = codeSection.Contains("#Region  Activity Attributes", StringComparison.OrdinalIgnoreCase) ||
                                        codeSection.Contains("#Region Activity Attributes", StringComparison.OrdinalIgnoreCase);

                if (!hasActivityAttrs)
                    errors.Add("🛑 FATAL: #Region Activity Attributes block is MISSING from the source code section. This block is REQUIRED (#FullScreen, #IncludeTitle, etc.). Restore it from the .bak backup file.");
            }

            return errors;
        }

        [McpServerTool, Description("Decodes a B4X visual layout file into readable JSON: control hierarchy, types, positions (resolved from the correct screen variant, not the misleading top-level template defaults), and properties like text/hint/tag/drawable. Works for both .bal (B4A) and .bjl (B4J) — they share the exact same binary format.")]
        public static string GetLayoutStructure(
            [Description("Absolute path to the .bal or .bjl layout file.")] string layoutPath)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!File.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            // Cache the decoded JSON by mtime — .bal decoding is the most expensive
            // read-only tool on large layouts and AI sessions frequently re-query the
            // same layout across turns while iterating on edits.
            if (CacheManager.TryGetByMtime<string>(layoutPath, out var cached) && cached != null)
                return cached;

            var data = File.ReadAllBytes(layoutPath);
            var decoded = BalDecoder.Decode(data);
            CacheManager.SetByMtime(layoutPath, decoded);
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
        public static async Task<string> GetFullContext(GetFullContextRequest request)
        {
            var projectPath = request.ProjectPath;
            var focusFile = request.FocusFile;
            var focusSub = request.FocusSub;
            var runCompile = request.RunCompile;
            var task = request.Task;

            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));
            if (focusFile != null)
                PathSecurity.ValidateAbsolutePath(focusFile, nameof(focusFile));

            string? root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

            var scanned = ProjectScanner.ScanProject(root);

            // Smart default: when the caller doesn't specify FocusFile, pick the most
            // recently modified source file. This is usually what the AI is "looking
            // at" — saves an entire round-trip of asking "which file is active?".
            string? effectiveFocusFile = focusFile;
            string? autoFocusedFromMtime = null;
            if (string.IsNullOrEmpty(effectiveFocusFile))
            {
                // Smart default: pick the most recently modified source file. Mark it so
                // we can prepend a banner — silently expanding one file from skeleton to
                // full could otherwise balloon the response and surprise the caller.
                autoFocusedFromMtime = scanned
                    .Where(f => f.Kind == "bas" || f.Kind == "b4a" || f.Kind == "b4j" || f.Kind == "b4i")
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f.Path))
                    .Select(f => f.Path)
                    .FirstOrDefault();
                effectiveFocusFile = autoFocusedFromMtime;
            }

            var projectFiles = scanned.Select(f =>
            {
                var pf = new ProjectFile(f.Path) { Kind = f.Kind, Included = true };
                bool isFocus = effectiveFocusFile != null &&
                    string.Equals(Path.GetFullPath(f.Path), Path.GetFullPath(effectiveFocusFile), StringComparison.OrdinalIgnoreCase);
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
                        var buildResult = await BuilderRunner.RunBuildAsync(builderPath, projectFile);
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

            // If we auto-focused via mtime, prepend a one-line banner so the caller
            // understands why one file is in 'full' mode without them asking. This is
            // cheaper than introducing a separate JSON response field for a tool whose
            // output is markdown.
            if (autoFocusedFromMtime != null)
            {
                string banner = $"> ℹ️ Auto-focused on `{Path.GetFileName(autoFocusedFromMtime)}` (most recently modified source file). Pass `FocusFile` explicitly to override or `FocusFile=\"\"` to keep all files in skeleton mode.\n\n";
                bundle = banner + bundle;
            }

            return bundle;
        }

        // Note on parameter shape: EditSub uses individual parameters (filePath,
        // subName, newCode) rather than a single `EditSubRequest` object — same
        // convention as the rest of the line/file-editing trio (EditLine,
        // InsertLine, DeleteLine, ReplaceLines). This avoids the ModelContextProtocol
        // SDK quirk where single-request-object methods force callers to wrap their
        // JSON args as `{"request": {...}}`, which is non-obvious and easy to get
        // wrong from the AI client side. (Audit fix 2025: this WAS bitten — every
        // flat-JSON caller (e.g. AI agents, this audit harness) hit
        // "Missing a value for required parameter 'request'" because EditSub alone
        // used the wrapped shape. All sister tools already use flat params.)
        [McpServerTool, Description("Replaces the entire body of a single Sub in a B4X module in-place, without touching the rest of the file. Safe for .b4a/.b4j/.b4i project files because it preserves the IDE metadata header and only edits the source code section. If the Sub isn't found, returns the list of Subs that do exist in the file so the caller can retry with the correct name. Creates a .bak backup first.")]
        public static string EditSub(
            [Description("Absolute path to the .bas/.b4a/.b4j module file.")] string filePath,
            [Description("Exact name of the Sub to replace (case-insensitive).")] string subName,
            [Description("The full new source of the Sub, including its 'Sub ...' header line and matching 'End Sub' line.")] string newCode)
        {
            PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

            // For destructive writes, keep them inside the project root.
            string? projectRoot = ProjectScanner.FindProjectRoot(filePath);
            if (projectRoot != null)
                PathSecurity.ValidateWithinBaseDirectory(filePath, projectRoot, nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            // Use the encoding-detecting reader (header-preserving variant), not
            // File.ReadAllText, so modules saved in windows-1252 (common on Spanish/Windows
            // locales) aren't silently corrupted — same underlying fix as
            // CodeUtils.ReadTextSafely's encoding-detection bug, but keeping the IDE header
            // intact since we need to reassemble it below.
            string raw = CodeUtils.DecodeFileWithFallback(filePath);

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
                var suggestion = SuggestClosestSubName(subName, available);
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Sub '{subName}' not found in {Path.GetFileName(filePath)}.",
                    didYouMean = suggestion,
                    availableSubs = available,
                    hint = suggestion != null
                        ? $"Did you mean '{suggestion}'? B4X Sub names are matched case-insensitively, so check the spelling rather than the casing."
                        : "Run analyze_module on this file to see the full Sub list with line numbers."
                }, JsonOptions.Default);
            }

            var lines = codeSection.Replace("\r\n", "\n").Split('\n').ToList();
            int startIdx = target.StartLine - 1;
            int endIdx = target.EndLine.Value - 1;

            if (startIdx < 0 || endIdx >= lines.Count || startIdx > endIdx)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Internal error: Sub line range out of bounds after parsing."
                }, JsonOptions.Default);

            var newLines = newCode.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            lines.RemoveRange(startIdx, endIdx - startIdx + 1);
            lines.InsertRange(startIdx, newLines);

            var updatedCodeSection = string.Join("\n", lines);
            var finalContent = markerIdx >= 0 ? header + "\r\n" + updatedCodeSection : updatedCodeSection;

            string backupPath = filePath + ".bak";
            File.Copy(filePath, backupPath, overwrite: true);

            File.WriteAllText(filePath, finalContent);
            CacheManager.Invalidate(filePath);

            return JsonSerializer.Serialize(new
            {
                success = true,
                filePath,
                backup = backupPath,
                subReplaced = target.Name,
                originalLineRange = new { start = target.StartLine, end = target.EndLine },
            newLineCount = newLines.Length
        });
    }

    // Note on parameter shape: EditLine uses individual parameters (filePath, lineNumber,
    // newContent, expectedText) rather than a single `EditLineRequest` object. This
    // matches the convention used by every other multi-arg tool in this codebase
    // (CompileProject, WriteFile, GetManifest, WriteManifest, etc.). It also avoids
    // a ModelContextProtocol SDK quirk where single-request-object methods require
    // the caller to wrap their JSON args as `{"request": {...}}`, which is non-obvious
    // and easy to get wrong from the AI client side.
    [McpServerTool, Description("Replaces a single line in a B4X source file (.bas, .b4a, .b4j, .b4i, or any text file) by its 1-based line number, without touching the rest of the file. Use this for surgical fixes where you know exactly which line to change (e.g. fixing a typo on line 42). For .b4a/.b4j/.b4i project files the IDE metadata header is preserved automatically. Pass expectedText as an atomic safety check to abort if the line has shifted. Creates a .bak backup before writing. For replacing an entire Sub use edit_sub instead; for replacing the whole file use write_file.")]
    public static string EditLine(
        [Description("Absolute path to the .bas/.b4a/.b4j/.b4i file (or any text file) to edit.")] string filePath,
        [Description("1-based line number to replace. For .b4a/.b4j/.b4i project files this counts from the FIRST LINE OF THE SOURCE CODE SECTION (after the @EndOfDesignText@ header), which matches the line numbers returned by get_file_content and analyze_module. For .bas files and other text files this counts from line 1 of the file.")] int lineNumber,
        [Description("The new content for the line. Pass an empty string to BLANK the line (it becomes an empty line — line count is preserved). To replace one line with multiple lines, embed newline characters (\\n) in the string — each newline becomes its own line.")] string newContent,
        [Description("Optional safety check: the current line at lineNumber must exactly match this string (whitespace and all). If it does not, the edit is aborted before the file is touched — no backup is overwritten, no write happens. Use this when you're worried the file may have changed between your read and your write (e.g. another tool ran in between).")] string? expectedText = null)
    {
        PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

        // File existence check BEFORE ProjectScanner.FindProjectRoot — the scanner
        // walks up looking for project markers and may throw on non-existent paths,
        // which would surface to the AI as a generic MCP error instead of our clean
        // ToolResponse.Error envelope. Doing File.Exists first keeps the error path
        // a structured envelope regardless of where the file would have lived.
        if (!File.Exists(filePath))
            return ToolResponse.Error(
                $"File not found: {filePath}",
                hints: new[] { "Run get_project_structure to list every file in the project.", "Use absolute paths only — relative paths are rejected." });

        // For destructive writes, keep them inside the project root.
        string? projectRoot = ProjectScanner.FindProjectRoot(filePath);
        if (projectRoot != null)
            PathSecurity.ValidateWithinBaseDirectory(filePath, projectRoot, nameof(filePath));

        // Use the encoding-detecting reader (header-preserving variant), not
        // File.ReadAllText, so files saved in windows-1252 (common on Spanish/Windows
        // locales) aren't silently corrupted — same rationale as EditSub.
        string raw = CodeUtils.DecodeFileWithFallback(filePath);

        const string marker = "@EndOfDesignText@";
        int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);

        // For .b4a/.b4j/.b4i, split off the IDE header so line numbers map to what
        // get_file_content returned (the code section). For other files, the header
        // is empty and the line numbering matches the file directly.
        string header = markerIdx >= 0 ? raw.Substring(0, markerIdx + marker.Length) : string.Empty;
        string editableSection = markerIdx >= 0
            ? raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n')
            : raw;
        bool fileHasHeader = markerIdx >= 0;

        // Normalize line endings to \n for splitting, then split. We restore CRLF at
        // write time to preserve the file's original style (B4X IDE standard on Windows).
        var lines = editableSection.Replace("\r\n", "\n").Split('\n').ToList();

        // Split produces a trailing empty element when the file ends with \n
        // ("line1\nline2\n" -> ["line1", "line2", ""]). We report totalLines from the
        // count EXCLUDING that empty so range validation matches what get_file_content
        // returns, but keep the array intact so reassembly preserves the file's
        // original trailing newline. Removing the empty element here would silently
        // drop the trailing \n on every write.
        int totalLines = lines.Count;
        if (totalLines > 0 && lines[^1].Length == 0)
            totalLines--;

        if (lineNumber < 1 || lineNumber > totalLines)
            return ToolResponse.Error(
                $"LineNumber {lineNumber} is out of range. The {(fileHasHeader ? "code section" : "file")} has {totalLines} editable line(s).",
                data: new { lineNumber, totalLines, fileHasHeader },
                hints: new[] { "LineNumber is 1-based and must be in [1, totalLines].", "Use get_file_content to see current line numbers, or analyze_module to find Sub boundaries." },
                nextSteps: new[] { "Read the file again with get_file_content to confirm the current line numbers, then retry." });

        int targetIdx = lineNumber - 1;
        string originalLine = lines[targetIdx];

        // Atomic safety check: if ExpectedText was provided, verify the line hasn't shifted.
        // This happens BEFORE the .bak is created, so a failed check leaves no trace.
        if (expectedText != null)
        {
            string currentLine = originalLine.TrimEnd('\r');
            if (!string.Equals(currentLine, expectedText, StringComparison.Ordinal))
                return ToolResponse.Error(
                    $"Line {lineNumber} does not match ExpectedText. The file has likely changed since you read it — edit aborted, no changes written, no backup touched.",
                    data: new { lineNumber, currentLine, expectedText },
                    hints: new[] { "Re-read the file with get_file_content to see the current line, then retry with the updated line number or ExpectedText.", "If you're not sure which line to edit, use analyze_module to see Sub boundaries and line ranges." },
                    nextSteps: new[] { "Call get_file_content, find the correct line, then call edit_line again with the right line number." });
        }

        // Apply the edit (newContent may itself contain embedded newlines — each becomes
        // its own line in the output). The split normalizes any embedded CRLF to \n.
        var newLines = newContent.Replace("\r\n", "\n").Split('\n');
        lines[targetIdx] = newContent;

        // Reassemble: header (no trailing newline — header already ends with the marker)
        // + CRLF + code section. Matches EditSub's write style so .b4a/.b4j files stay
        // byte-identical except for the edited line.
        var updatedEditable = string.Join("\n", lines);
        string finalContent = fileHasHeader
            ? header + "\r\n" + updatedEditable
            : updatedEditable;

        // Create .bak AFTER all validation passes. This guarantees the backup is only
        // created for edits that actually succeed — unlike EditSub which creates the
        // backup before line-range checks (harmless in practice, but cleaner this way).
        string backupPath = filePath + ".bak";
        File.Copy(filePath, backupPath, overwrite: true);

        File.WriteAllText(filePath, finalContent);
        CacheManager.Invalidate(filePath);

        return ToolResponse.Success(
            data: new
            {
                filePath,
                lineNumber,
                fileHadHeader = fileHasHeader,
                originalLine,
                newLine = newContent,
                // Empty NewContent blanks the line in place (line count preserved) — the
                // 1-vs-0 distinction is meaningful enough that callers reading this field
                // (e.g. an AI deciding whether to retry or move on) shouldn't be misled.
                linesInserted = string.IsNullOrEmpty(newContent) ? 0 : newLines.Length,
                linesReplaced = 1,
                backup = backupPath,
                totalEditableLines = totalLines
            },
            hints: new[] { "If the edit broke something, restore from backup by copying <file>.bak back to the original file.", "Embedded newlines in NewContent were each inserted as their own line." },                nextSteps: new[] { "Call compile_project to verify the line edit doesn't break the build.", "Use get_file_content to visually confirm the change." });
    }

    // Note on parameter shape: InsertLine uses individual parameters (filePath, lineNumber,
    // newContent) rather than a single request object — same convention as EditLine,
    // WriteFile, GetManifest, etc. See the EditLine note above for the SDK binding rationale.
    [McpServerTool, Description("Inserts new content as one or more lines at a given 1-based position in a B4X source file (.bas, .b4a, .b4j, .b4i, or any text file), shifting all subsequent lines down. Use this for adding new Subs, Dim declarations, or comments above existing lines without disturbing surrounding code. lineNumber=1 inserts at the top of the file (or top of the code section for .b4a/.b4j/.b4i); lineNumber=totalLines+1 appends after the last existing line. For .b4a/.b4j/.b4i project files the IDE metadata header is preserved automatically. newContent may contain embedded newlines (\\n) — each becomes its own inserted line. Creates a .bak backup before writing. For replacing an existing line use edit_line; for replacing an entire Sub use edit_sub; for rewriting the whole file use write_file.")]
    public static string InsertLine(
        [Description("Absolute path to the .bas/.b4a/.b4j/.b4i file (or any text file) to edit.")] string filePath,
        [Description("1-based position to insert AT. Must be in [1, totalLines+1]. 1 inserts at the very top, totalLines+1 appends after the last existing line. For .b4a/.b4j/.b4i project files this counts from the FIRST LINE OF THE SOURCE CODE SECTION (after the @EndOfDesignText@ header).")] int lineNumber,
        [Description("Content to insert. May contain embedded newline characters (\\n) — each becomes its own inserted line. An empty string inserts one blank line.")] string newContent)
    {
        PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

        // Same File.Exists-first pattern as EditLine — keeps non-existent-file errors
        // as a clean envelope instead of letting ProjectScanner throw into the MCP transport.
        if (!File.Exists(filePath))
            return ToolResponse.Error(
                $"File not found: {filePath}",
                hints: new[] { "Run get_project_structure to list every file in the project.", "Use absolute paths only — relative paths are rejected." });

        // For destructive writes, keep them inside the project root.
        string? projectRoot = ProjectScanner.FindProjectRoot(filePath);
        if (projectRoot != null)
            PathSecurity.ValidateWithinBaseDirectory(filePath, projectRoot, nameof(filePath));

        // Encoding-detecting read; header-preserving so we can reassemble it below.
        string raw = CodeUtils.DecodeFileWithFallback(filePath);

        const string marker = "@EndOfDesignText@";
        int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);

        string header = markerIdx >= 0 ? raw.Substring(0, markerIdx + marker.Length) : string.Empty;
        string editableSection = markerIdx >= 0
            ? raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n')
            : raw;
        bool fileHasHeader = markerIdx >= 0;

        var lines = editableSection.Replace("\r\n", "\n").Split('\n').ToList();

        // Same off-by-one fix as EditLine: totalLines excludes the trailing empty that
        // Split produces for files ending in \n, but the array keeps it so reassembly
        // preserves the file's trailing newline.
        int totalLines = lines.Count;
        if (totalLines > 0 && lines[^1].Length == 0)
            totalLines--;

        // Insertion range is [1, totalLines + 1] — totalLines+1 means "append at end".
        // A 0-line file still allows lineNumber=1 (insert as the only line).
        if (lineNumber < 1 || lineNumber > totalLines + 1)
            return ToolResponse.Error(
                $"LineNumber {lineNumber} is out of range for an insertion. Valid range is [1, {totalLines + 1}] (1 inserts at top, {totalLines + 1} appends at end).",
                data: new { lineNumber, minValid = 1, maxValid = totalLines + 1, totalEditableLines = totalLines, fileHasHeader },
                hints: new[] { "LineNumber for insertion is 1-based and must be in [1, totalLines + 1].", "Use get_file_content to see current line numbers." },
                nextSteps: new[] { "Read the file again to confirm line counts, then retry with a valid insertion position." });

        int insertIdx = lineNumber - 1;

        // Split newContent into the lines to insert. Allow embedded \n so a single call
        // can insert a multi-line block (e.g. a complete Sub). For empty newContent
        // Split returns [""], which inserts one blank line — consistent with the
        // semantic that "insert at N with empty content" creates an empty row.
        var newLines = newContent.Replace("\r\n", "\n").Split('\n');
        lines.InsertRange(insertIdx, newLines);

        var updatedEditable = string.Join("\n", lines);
        string finalContent = fileHasHeader
            ? header + "\r\n" + updatedEditable
            : updatedEditable;

        // Backup AFTER validation — same atomicity guarantee as EditLine.
        string backupPath = filePath + ".bak";
        File.Copy(filePath, backupPath, overwrite: true);

        File.WriteAllText(filePath, finalContent);
        CacheManager.Invalidate(filePath);

        return ToolResponse.Success(
            data: new
            {
                filePath,
                lineNumber,
                fileHadHeader = fileHasHeader,
                newLine = newContent,
                linesInserted = newLines.Length,
                totalEditableLines = totalLines,
                newTotalLines = totalLines + newLines.Length
            },
            hints: new[] { "If the insertion broke something, restore from backup by copying <file>.bak back to the original file.", "Embedded newlines in newContent were each inserted as their own line at the requested position." },
            nextSteps: new[] { "Call compile_project to verify the insertion doesn't break the build.", "Use get_file_content to visually confirm the change." });
    }

    // Note on parameter shape: DeleteLine uses individual parameters (filePath, lineNumber)
    // rather than a single request object — same convention as EditLine and InsertLine.
    // See the EditLine note above for the SDK binding rationale.
    [McpServerTool, Description("Removes a single line from a B4X source file (.bas, .b4a, .b4j, .b4i, or any text file) by its 1-based line number, shifting all subsequent lines up. Use this for removing obsolete Subs, comment lines, or dead code without disturbing the surrounding context. For .b4a/.b4j/.b4i project files the IDE metadata header is preserved automatically. lineNumber must be in [1, totalLines]. Creates a .bak backup before writing. For replacing an existing line use edit_line; for adding new lines use insert_line; for removing an entire Sub use edit_sub; for rewriting the whole file use write_file.")]
    public static string DeleteLine(
        [Description("Absolute path to the .bas/.b4a/.b4j/.b4i file (or any text file) to edit.")] string filePath,
        [Description("1-based line number to remove. Must be in [1, totalLines]. For .b4a/.b4j/.b4i project files this counts from the FIRST LINE OF THE SOURCE CODE SECTION (after the @EndOfDesignText@ header).")] int lineNumber)
    {
        PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

        // Same File.Exists-first pattern as EditLine / InsertLine — keeps non-existent-file
        // errors as a clean envelope instead of letting ProjectScanner throw.
        if (!File.Exists(filePath))
            return ToolResponse.Error(
                $"File not found: {filePath}",
                hints: new[] { "Run get_project_structure to list every file in the project.", "Use absolute paths only — relative paths are rejected." });

        // For destructive writes, keep them inside the project root.
        string? projectRoot = ProjectScanner.FindProjectRoot(filePath);
        if (projectRoot != null)
            PathSecurity.ValidateWithinBaseDirectory(filePath, projectRoot, nameof(filePath));

        // Encoding-detecting read; header-preserving so we can reassemble it below.
        string raw = CodeUtils.DecodeFileWithFallback(filePath);

        const string marker = "@EndOfDesignText@";
        int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);

        string header = markerIdx >= 0 ? raw.Substring(0, markerIdx + marker.Length) : string.Empty;
        string editableSection = markerIdx >= 0
            ? raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n')
            : raw;
        bool fileHasHeader = markerIdx >= 0;

        var lines = editableSection.Replace("\r\n", "\n").Split('\n').ToList();

        // Same off-by-one fix as EditLine / InsertLine: totalLines excludes the trailing
        // empty that Split produces for files ending in \n, but the array keeps it so
        // reassembly preserves the file's trailing newline.
        int totalLines = lines.Count;
        if (totalLines > 0 && lines[^1].Length == 0)
            totalLines--;

        if (lineNumber < 1 || lineNumber > totalLines)
            return ToolResponse.Error(
                $"LineNumber {lineNumber} is out of range. The {(fileHasHeader ? "code section" : "file")} has {totalLines} line(s); nothing to delete.",
                data: new { lineNumber, totalEditableLines = totalLines, fileHasHeader },
                hints: new[] { "LineNumber is 1-based and must be in [1, totalLines].", "Use get_file_content to see current line numbers before deleting.", "Note: delete_line refuses to delete past the last line (use insert_line instead to append)." },
                nextSteps: new[] { "Read the file again to confirm line counts, then retry with a valid line number." });

        // 0-based index for the line being removed.
        int targetIdx = lineNumber - 1;
        string removedLine = lines[targetIdx];

        // Shift every subsequent line up by one.
        lines.RemoveAt(targetIdx);

        var updatedEditable = string.Join("\n", lines);
        string finalContent = fileHasHeader
            ? header + "\r\n" + updatedEditable
            : updatedEditable;

        // Backup AFTER validation — same atomicity guarantee as EditLine / InsertLine.
        string backupPath = filePath + ".bak";
        File.Copy(filePath, backupPath, overwrite: true);

        File.WriteAllText(filePath, finalContent);
        CacheManager.Invalidate(filePath);

        return ToolResponse.Success(
            data: new
            {
                filePath,
                lineNumber,
                fileHadHeader = fileHasHeader,
                removedLine,
                linesRemoved = 1,
                totalEditableLines = totalLines,
                newTotalLines = totalLines - 1
            },
            hints: new[] { "If the deletion broke something, restore from backup by copying <file>.bak back to the original file.", "Subsequent lines shifted up automatically — no other positions changed." },
            nextSteps: new[] { "Call compile_project to verify the deletion doesn't break the build.", "Use get_file_content to visually confirm the change." });
    }

    // Note on parameter shape: ReplaceLines uses individual parameters (filePath, startLine,
    // endLine, newContent) — same convention as the rest of the line-level trio.
    [McpServerTool, Description("Replaces a CONTIGUOUS RANGE of lines [startLine, endLine] in a B4X source file (.bas, .b4a, .b4j, .b4i, or any text file) with new content, in the spirit of edit_line but spanning multiple lines. The range is inclusive on both ends. For .b4a/.b4j/.b4i project files the IDE metadata header is preserved automatically. newContent may contain embedded newlines (\\n) — each becomes its own inserted line. Pass newContent=\"\" to BLANK the range IN PLACE — every line in [startLine, endLine] becomes an empty string, line count preserved, no shift. This MATCHES edit_line's empty=blank semantic so replace_lines(N, N, \"\") is exactly equivalent to edit_line(N, \"\"). When newContent has more lines than the original range, lines after shift DOWN; when fewer, they shift UP; when equal, no shift. For single-line precision use edit_line; for inserting new lines use insert_line; for deleting a range of lines use delete_line N times.")]
    public static string ReplaceLines(
        [Description("Absolute path to the .bas/.b4a/.b4j/.b4i file (or any text file) to edit.")] string filePath,
        [Description("1-based START of the inclusive range to replace. Must be in [1, totalLines].")] int startLine,
        [Description("1-based END of the inclusive range to replace. Must be >= startLine and <= totalLines.")] int endLine,
        [Description("Content that REPLACES the range. May contain embedded newline characters (\\n) — each becomes its own inserted line. An empty string DELETES the range (no blank line inserted — lines after endLine shift up).")] string newContent)
    {
        PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

        // Same File.Exists-first pattern as EditLine / InsertLine / DeleteLine.
        if (!File.Exists(filePath))
            return ToolResponse.Error(
                $"File not found: {filePath}",
                hints: new[] { "Run get_project_structure to list every file in the project.", "Use absolute paths only — relative paths are rejected." });

        // For destructive writes, keep them inside the project root.
        string? projectRoot = ProjectScanner.FindProjectRoot(filePath);
        if (projectRoot != null)
            PathSecurity.ValidateWithinBaseDirectory(filePath, projectRoot, nameof(filePath));

        // Encoding-detecting read; header-preserving so we can reassemble it below.
        string raw = CodeUtils.DecodeFileWithFallback(filePath);

        const string marker = "@EndOfDesignText@";
        int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);

        string header = markerIdx >= 0 ? raw.Substring(0, markerIdx + marker.Length) : string.Empty;
        string editableSection = markerIdx >= 0
            ? raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n')
            : raw;
        bool fileHasHeader = markerIdx >= 0;

        var lines = editableSection.Replace("\r\n", "\n").Split('\n').ToList();

        // Same off-by-one fix as the trio: totalLines excludes the trailing empty from
        // Split, but the array keeps it so reassembly preserves the file's trailing newline.
        int totalLines = lines.Count;
        if (totalLines > 0 && lines[^1].Length == 0)
            totalLines--;

        // Range validation: startLine in [1, totalLines], endLine in [startLine, totalLines].
        if (startLine < 1 || startLine > totalLines || endLine < startLine || endLine > totalLines)
            return ToolResponse.Error(
                $"Invalid range [{startLine}, {endLine}]. Must satisfy 1 ≤ startLine ≤ endLine ≤ totalLines ({totalLines}).",
                data: new { startLine, endLine, totalEditableLines = totalLines, fileHasHeader },
                hints: new[] { "startLine and endLine are 1-based and inclusive.", "endLine must be >= startLine.", "Both must be within [1, totalLines].", "To delete a single line, use delete_line; to insert, use insert_line." },
                nextSteps: new[] { "Read the file again with get_file_content to see exact line numbers, then retry with a valid range." });

        int startIdx = startLine - 1;
        int rangeSize = endLine - startLine + 1;

        // Snapshot the removed range so the AI can inspect it (useful for undo /
        // verifying "I just deleted exactly these N lines").
        var removedRange = new List<string>(rangeSize);
        for (int i = 0; i < rangeSize; i++)
            removedRange.Add(lines[startIdx + i]);

        // Single unified mutation shape (RemoveRange + InsertRange) for both branches —
        // simpler and harder to get wrong than two divergent branches. Empty newContent
        // blanks the range IN PLACE by inserting rangeSize empty strings at the splice
        // position, so replace_lines(N, N, "") == edit_line(N, "") — line count preserved,
        // no shift, no insertion of trailing blanks.
        // No TrimEnd: matches edit_line/insert_line exactly so the trio stays consistent
        // — caller submitting "x\n" gets the same behavior across all three tools.
        bool isEmpty = newContent.Length == 0;
        string[] newLines = isEmpty
            ? Enumerable.Repeat(string.Empty, rangeSize).ToArray()
            : newContent.Replace("\r\n", "\n").Split('\n');
        lines.RemoveRange(startIdx, rangeSize);
        lines.InsertRange(startIdx, newLines);

        var updatedEditable = string.Join("\n", lines);
        string finalContent = fileHasHeader
            ? header + "\r\n" + updatedEditable
            : updatedEditable;

        // Backup AFTER validation — same atomicity guarantee as the trio.
        string backupPath = filePath + ".bak";
        File.Copy(filePath, backupPath, overwrite: true);

        File.WriteAllText(filePath, finalContent);
        CacheManager.Invalidate(filePath);

        return ToolResponse.Success(
            data: new
            {
                filePath,
                startLine,
                endLine,
                fileHadHeader = fileHasHeader,
                removedRange,
                newContent,
                linesRemoved = rangeSize,
                // newLines.Length is the single source of truth: empty newContent yields
                // rangeSize empty strings (net line-count change = rangeSize - rangeSize = 0,
                // i.e. blank-in-place); non-empty yields the raw split count (line-count
                // change = splitcount - rangeSize, i.e. proportional shift).
                linesInserted = newLines.Length,
                totalEditableLines = totalLines,
                newTotalLines = totalLines - rangeSize + newLines.Length
            },
            hints: new[] { "If the replace broke something, restore from backup by copying <file>.bak back to the original file.", "Embedded newlines in newContent were each inserted as their own line at the start of the range.", "Pass newContent=\"\" to delete the range entirely (no blank line inserted)." },
            nextSteps: new[] { "Call compile_project to verify the replace doesn't break the build.", "Use get_file_content to visually confirm the change." });
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

            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            if (string.IsNullOrWhiteSpace(pattern))
                throw new ArgumentException("Pattern must not be empty.");

            string? root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

            Regex regex;
            try
            {
                // A timeout is essential here: `pattern` comes straight from the MCP caller
                // (the AI), and an unbounded Regex against untrusted patterns is vulnerable to
                // catastrophic backtracking (e.g. `(a+)+b`) — without a timeout, one bad pattern
                // can hang this tool call, and with it the whole MCP server process, indefinitely.
                regex = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Invalid regex pattern: {ex.Message}");
            }

            var files = ProjectScanner.ScanProject(root)
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
                        timeoutWarning = $"Pattern took too long against a line in {f.Path}:{i + 1} and was stopped early (possible catastrophic backtracking). Results below are partial — try a simpler/more specific pattern.";
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

        [McpServerTool, Description("Parses a B4X project file's project metadata into structured JSON: app type, version, referenced libraries, module list, included files, and every other raw key=value setting.")]
        public static string GetProjectConfig(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file.")] string projectPath)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            string? projectFile = File.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectFile(projectPath);
            if (projectFile == null)
                throw new FileNotFoundException($"No .b4a/.b4j/.b4i project file found for '{projectPath}'.");

            // Cache the parsed config JSON by file mtime — config reads happen on every
            // context-build / validate_event_handlers / compile, so saving the regex
            // header parse adds up on large projects.
            string cacheKey = $"project-config:{projectFile}";
            if (CacheManager.TryGetByMtime<string>(projectFile, out var cachedConfig) && cachedConfig != null)
                return cachedConfig;

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

            var libraries = rawSettings.Where(kv => Regex.IsMatch(kv.Key, @"^Library\d+$", RegexOptions.None, RegexTimeout)).Select(kv => kv.Value).OrderBy(v => v).ToList();
            var modules = rawSettings.Where(kv => Regex.IsMatch(kv.Key, @"^Module\d+$", RegexOptions.None, RegexTimeout)).Select(kv => kv.Value).ToList();
            var includedFiles = rawSettings.Where(kv => Regex.IsMatch(kv.Key, @"^File\d+$", RegexOptions.None, RegexTimeout)).Select(kv => kv.Value).ToList();

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

            var configJson = JsonSerializer.Serialize(result, JsonOptions.Default);
            CacheManager.SetByMtime(projectFile, configJson);
            return configJson;
        }

        [McpServerTool, Description("Analyzes a single B4X module (.bas): lists every Sub (name, parameters, return type, public/private, event handler detection), every Type declaration, and Globals presence. Also reports structural parse issues without compiling.")]
        public static string AnalyzeModule(
            [Description("Absolute path to the .bas module file.")] string filePath)
        {
            PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

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
                    RegexOptions.IgnoreCase, RegexTimeout),
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
            }, JsonOptions.Default);

            CacheManager.SetByMtime(filePath, result);
            CacheManager.Store(cacheKey, result);

            return result;
        }

        [McpServerTool, Description("Statically validates every event handler Sub in a B4X project against the event signatures declared in the referenced libraries. Reports parameter count, name, and type mismatches (e.g. Int vs Double) that cause runtime crashes like java.lang.IllegalArgumentException. Also infers control types from Dim declarations and layout files.")]
        public static string ValidateEventHandlers(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file.")] string projectPath)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            string? root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
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
        public static string ValidateProject(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")] string projectPath)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

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
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Lists every layout file (.bal/.bjl/.bil) in a project with basic metadata: screen variants and top-level control count.")]
        public static string ListLayouts(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")] string projectPath)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

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
                    // Per-file mtime cache — same rationale as GetLayoutStructure.
                    string? decodedJson;
                    if (!CacheManager.TryGetByMtime<string>(f.Path, out decodedJson) || decodedJson == null)
                    {
                        var data = File.ReadAllBytes(f.Path);
                        decodedJson = BalDecoder.Decode(data);
                        CacheManager.SetByMtime(f.Path, decodedJson);
                    }
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
                JsonOptions.Default);
        }

        private const string ManifestStartMarker = "#Region Manifest Editor";
        private const string ManifestEndMarker = "#End Region";

        [McpServerTool, Description("Extracts the Manifest Editor block from a B4A project file.")]
        public static string GetManifest(
            [Description("Absolute path to the .b4a project file.")] string projectPath)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"File not found: {projectPath}");
            if (!projectPath.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("File must have a .b4a extension.");

            string raw = File.ReadAllText(projectPath);

            var block = ExtractManifestBlock(raw);
            if (block == null)
                return ToolResponse.Error(
                    "No '#Region Manifest Editor' region found in this project.",
                    hints: new[] { "This is a B4A-specific region. It may be missing if the project was never opened in the B4A IDE.", "Use write_manifest to add one." });

            return ToolResponse.Success(new { projectPath, manifest = block });
        }

        [McpServerTool, Description("Replaces the Manifest Editor block in a B4A project file. Creates a .bak backup first.")]
        public static string WriteManifest(
            [Description("Absolute path to the .b4a project file.")] string projectPath,
            [Description("New content for the Manifest Editor block.")] string manifestContent)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

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

            return ToolResponse.Success(
                new { projectPath, backup = projectPath + ".bak" },
                nextSteps: new[] { "Call compile_project to verify the manifest change doesn't break the build." });
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
        [McpServerTool, Description("Creates a new .bas module file with the correct IDE metadata header. The header is REQUIRED for the B4A IDE to open the file without errors. Choose type 'activity' for a screen module or 'class' for a code module.")]
        public static string CreateBasModule(
        [Description("Absolute path to the .bas file to create (e.g. 'C:\\...\\Settings.bas')")] string filePath,
        [Description("Module type: 'activity' (has Activity_Create, LoadLayout) or 'class' (code-only)")] string moduleType = "activity")
        {
            PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

            if (File.Exists(filePath))
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"File already exists: {filePath}",
                    hint = "Use write_file to modify an existing module, or delete it first."
                }, JsonOptions.Default);

            moduleType = moduleType.ToLowerInvariant().Trim();
            bool isActivity = moduleType == "activity";
            string moduleName = Path.GetFileNameWithoutExtension(filePath);

            string header = $@"B4A=true
            Group=Default Group
            ModulesStructureVersion=1
            Type={(isActivity ? "Activity" : "Class")}
            Version=13.5
            @EndOfDesignText@
            ";

            string code = isActivity
            ? $@"#Region  Activity Attributes 
	        #FullScreen: False
	        #IncludeTitle: False
            #End Region

            Sub Process_Globals
            End Sub

            Sub Globals
            End Sub

            Sub Activity_Create(FirstTime As Boolean)
	            Activity.LoadLayout(""{moduleName}"")
            End Sub

            Sub Activity_Resume
            End Sub

            Sub Activity_Pause (UserClosed As Boolean)
            End Sub
            "
                            : $@"Sub Process_Globals
            End Sub

            Sub Globals
            End Sub
            ";

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(filePath, header + code);

            // Discover the enclosing .b4a/.b4j/.b4i so the nextSteps hint is actionable;
            // if we can't find one (caller passed a folder we don't recognise) we fall back
            // to a project-agnostic hint rather than embedding a broken path.
            string? projectFile = ProjectScanner.FindProjectFile(dir ?? Path.GetDirectoryName(filePath) ?? ".");
            var nextSteps = new List<string>
            {
                "Call compile_project to verify the new module compiles cleanly."
            };
            if (projectFile != null)
                nextSteps.Insert(0, $"Call register_module_in_project(projectFile='{projectFile}', moduleName='{moduleName}') to add this module to the project metadata.");

            return JsonSerializer.Serialize(new
            {
                success = true,
                filePath,
                moduleName,
                moduleType = isActivity ? "Activity" : "Class",
                nextSteps
            }, JsonOptions.Default);
        }

        // ── Levenshtein-based "did you mean" for EditSub ───────────────────
        // When the caller asks for a Sub that doesn't exist, return the closest
        // match by edit distance so the AI can self-correct on the first retry
        // instead of dumping a long sub list and asking the user to pick.

        private static string? SuggestClosestSubName(string requested, List<string> available)
        {
            if (available.Count == 0 || string.IsNullOrEmpty(requested)) return null;
            string lowerReq = requested.ToLowerInvariant();
            string? best = null;
            int bestDist = int.MaxValue;
            foreach (var name in available)
            {
                int d = LevenshteinDistance(lowerReq, name.ToLowerInvariant());
                if (d < bestDist)
                {
                    bestDist = d;
                    best = name;
                }
            }
            // Threshold: suggest only if the closest match is within ~33% of the
            // requested name length (min 2 edits) — otherwise it's noise.
            int threshold = Math.Max(2, requested.Length / 3);
            return bestDist <= threshold ? best : null;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;
            var prev = new int[b.Length + 1];
            var curr = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[b.Length];
        }
    }
}