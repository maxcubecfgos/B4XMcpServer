using B4XMcpServer.Repositories;
using B4XMcpServer.Services;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace B4XMcpServer.Tools.Project
{
    [McpServerToolType]
    public sealed class CompileTools
    {
        private readonly IFileRepository _fileRepository;
        private readonly IProjectRepository _projectRepository;

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        public CompileTools(IFileRepository fileRepository, IProjectRepository projectRepository)
        {
            _fileRepository = fileRepository;
            _projectRepository = projectRepository;
        }

        [McpServerTool, Description("Compiles a B4X project (B4A, B4J, or B4i) using the platform-correct builder selected automatically from the project file extension.\n\n" +
            "*** CRITICAL: This is the ONLY way to compile. NEVER run shell commands (dir, cd, type, cat, B4ABuilder.exe, etc.). If compilation fails, this tool returns the exact errors with file names, line numbers, and source lines. READ THEM and fix the code — do not try to debug by running commands manually. ***")]
        public async Task<string> CompileProject(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")] string projectPath,
            [Description("Timeout in seconds. Default 300.")] int timeoutSeconds = 300,
            [Description("Delete the Objects/ output folder before building to force a clean rebuild. Default false.")] bool cleanBuild = false)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            string? projectFile = _fileRepository.Exists(projectPath) ? projectPath : _projectRepository.FindProjectFile(projectPath);
            if (projectFile == null)
                return ToolResponse.Error(
                    $"No .b4a/.b4j/.b4i project file found for '{projectPath}'.",
                    hints: new[] { "Pass the project folder path, not a file that doesn't exist.", "Confirm the project file is at the project root, not nested in a subfolder." });

            // ── CLEAN BUILD ──────────────────────────────────────────────
            if (cleanBuild)
            {
                string projectDir = Path.GetDirectoryName(projectFile) ?? ".";
                string objectsDir = Path.Combine(projectDir, "Objects");
                if (Directory.Exists(objectsDir))
                {
                    try
                    {
                        Directory.Delete(objectsDir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        return ToolResponse.Error(
                            $"Failed to clean Objects/ directory: {ex.Message}",
                            hints: new[] { "Close the B4X IDE if it's locking files in Objects/." });
                    }
                }
            }
            // ──────────────────────────────────────────────────────────────

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

            // ── Post-parse sanity check ────────────────────────────────────
            if (success && buildResult.TryGetValue("raw_output", out var raw) && raw is string rawStr)
            {
                int rawErrorCount = 0;
                foreach (var line in rawStr.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Contains("error:", StringComparison.OrdinalIgnoreCase) &&
                        !trimmed.StartsWith("warning:", StringComparison.OrdinalIgnoreCase))
                        rawErrorCount++;
                }

                if (rawErrorCount > 0)
                {
                    success = false;
                    buildResult["success"] = false;
                    var fallbackErrors = new List<Dictionary<string, object?>>();
                    fallbackErrors.Add(new Dictionary<string, object?>
                    {
                        ["message"] = $"{rawErrorCount} error(s) detected in raw compiler output but missed by the parser. Review full build output below.",
                        ["raw"] = rawStr.Length > 2000 ? rawStr.Substring(0, 2000) + "..." : rawStr
                    });
                    buildResult["errors"] = fallbackErrors;
                }
            }
            // ────────────────────────────────────────────────────────────────

            if (!success)
            {
                // ── Adjust line numbers: B4X compiler reports lines relative to the
                // source-code section (after @EndOfDesignText@), but users see the
                // full file. Add the header line offset so line numbers match the
                // actual file in the editor. ────────────────────────────────────
                AdjustErrorLineNumbers(buildResult, projectFile);

                var formattedErrors = BuildFormatter.Format(buildResult);
                return ToolResponse.Error(
                    "COMPILATION FAILED.",
                    data: new { buildErrors = formattedErrors },
                    hints: new[] { "DO NOT run shell commands. Read the structured errors in `data.buildErrors`, fix the code with write_file or edit_sub, then call compile_project again." },
                    nextSteps: new[] { "Read the embedded build errors, fix the listed file:line:message, then call compile_project again." });
            }

            int errorCount = 0;
            if (buildResult.TryGetValue("errors", out var errsObj) && errsObj is System.Collections.IList errsList)
                errorCount = errsList.Count;

            return ToolResponse.Success(
                data: new { builder = builderPath, message = $"Compilation OK — {errorCount} error(s), 0 warnings." },
                hints: new[] { "If you still see compilation errors in the output, run compile_project again. If it keeps reporting success, the errors may be in a different project file." });
        }

        /// <summary>
        /// Adds the header line count to each error's b4x_line so numbers match the
        /// full file, not just the source-code section after @EndOfDesignText@.
        /// </summary>
        private void AdjustErrorLineNumbers(Dictionary<string, object?> buildResult, string projectFile)
        {
            if (!buildResult.TryGetValue("errors", out var errsObj) || errsObj is not System.Collections.IList errsList)
                return;

            string raw = _fileRepository.ReadTextWithHeader(projectFile);
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            if (markerIdx < 0) return;

            // Count lines before the marker (the header section)
            int headerLineCount = raw.Substring(0, markerIdx).Split('\n').Length;

            foreach (var err in errsList)
            {
                if (err is Dictionary<string, object?> e && e.TryGetValue("b4x_line", out var lineObj) && lineObj != null)
                {
                    // b4x_line may be int or string; handle both
                    if (lineObj is int lineNum)
                    {
                        e["b4x_line"] = lineNum + headerLineCount;
                    }
                    else if (int.TryParse(lineObj.ToString(), out int parsedLine))
                    {
                        e["b4x_line"] = parsedLine + headerLineCount;
                    }
                }
            }
        }

        /// <summary>
        /// Validates project file structure before attempting to compile.
        /// Catches broken headers that would make the project unopenable in the IDE.
        /// </summary>
        private List<string> ValidateProjectBeforeCompile(string projectFile)
        {
            var errors = new List<string>();

            if (!_fileRepository.Exists(projectFile))
            {
                errors.Add($"Project file not found: {projectFile}");
                return errors;
            }

            string raw = _fileRepository.ReadTextWithHeader(projectFile);
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

            // Check 6: Referenced modules must exist on disk
            string projectDir = Path.GetDirectoryName(projectFile) ?? ".";
            foreach (var kv in header.Where(kv => Regex.IsMatch(kv.Key, @"^Module\d+$", RegexOptions.None, RegexTimeout)))
            {
                var moduleName = kv.Value;
                var modulePath = Path.Combine(projectDir, moduleName);

                if (!Path.HasExtension(modulePath))
                    modulePath += ".bas";

                if (!_fileRepository.Exists(modulePath))
                    errors.Add($"❌ Module '{moduleName}' is referenced in {kv.Key} but file not found at: {modulePath}");
            }

            // Check 7: #Region Manifest Editor must be in metadata section only
            if (codeSection.Contains("#Region Manifest Editor", StringComparison.OrdinalIgnoreCase) ||
                codeSection.Contains("#Region  Manifest Editor", StringComparison.OrdinalIgnoreCase))
                errors.Add("❌ FATAL: #Region Manifest Editor found in SOURCE CODE section. It belongs in the PROJECT METADATA section only. Use write_manifest tool to modify it.");

            // Check 8: AddManifestText must be in metadata section only
            if (codeSection.Contains("AddManifestText", StringComparison.OrdinalIgnoreCase))
                errors.Add("❌ AddManifestText found in SOURCE CODE section. Manifest modifications belong in the PROJECT METADATA section only, never in code.");

            // Check 9: #Region Project Attributes MUST be in source code section
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
    }
}
