using B4XMcpServer.Engine;
using B4XMcpServer.Repositories;
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
    [McpServerToolType]
    public sealed class CodeEditTools
    {
        private readonly IFileRepository _fileRepository;
        private readonly IProjectRepository _projectRepository;

        public CodeEditTools(IFileRepository fileRepository, IProjectRepository projectRepository)
        {
            _fileRepository = fileRepository;
            _projectRepository = projectRepository;
        }

        [McpServerTool, Description("Replaces the entire body of a single Sub in a B4X module in-place, without touching the rest of the file. Safe for .b4a/.b4j/.b4i project files because it preserves the IDE metadata header and only edits the source code section. CRITICAL: newCode MUST include both the 'Sub ...' header line AND the matching 'End Sub' line — if you forget 'End Sub', the Sub will be corrupted. Use this for modifying existing Subs only; to add a NEW Sub use insert_line instead. If the Sub isn't found, returns the list of Subs that do exist in the file so the caller can retry with the correct name. Creates a .bak backup first.")]
        public string EditSub(
            [Description("Absolute path to the .bas/.b4a/.b4j module file.")] string filePath,
            [Description("Exact name of the Sub to replace (case-insensitive).")] string subName,
            [Description("The full new source of the Sub, CRITICAL: must include BOTH the 'Sub ...' header line AND the matching 'End Sub' line. Forgetting 'End Sub' corrupts the module.")] string newCode)
        {
            PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

            if (PathSecurity.IsForbiddenMainBas(filePath, out var blockReason))
            {
                return ToolResponse.Error(
                    blockReason!,
                    hints: new[]
                    {
                        "The .b4a/.b4j/.b4i file IS the project's Main module REGARDLESS of what it is named — adding code there with edit_sub is the supported path, not building Main.bas from scratch here.",
                        "If you previously corrupted the project by creating Main.bas line-by-line, remove it manually after restoring the project file from its .bak backup."
                    });
            }

            string? projectRoot = _projectRepository.FindProjectRoot(filePath);
            if (projectRoot != null)
                PathSecurity.ValidateWithinBaseDirectory(filePath, projectRoot, nameof(filePath));

            if (!_fileRepository.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            string raw = _fileRepository.ReadTextWithHeader(filePath);

            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            string header = markerIdx >= 0 ? raw.Substring(0, markerIdx + marker.Length) : string.Empty;
            string codeSection = markerIdx >= 0
                ? raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n')
                : raw;

            var lines = codeSection.Replace("\r\n", "\n").Split('\n');
            DocumentAnalysisEngine.AnalyzeDocumentForFunctionBlocks(lines);
            var availableSubs = DocumentAnalysisEngine.FunctionBlockList
                .Select(b => b.FunctionName)
                .ToList();
            var target = DocumentAnalysisEngine.FunctionBlockList
                .FirstOrDefault(b => string.Equals(b.FunctionName, subName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                var suggestion = ProjectHelpersShared.SuggestClosestSubName(subName, availableSubs);
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Sub '{subName}' not found in {Path.GetFileName(filePath)}.",
                    didYouMean = suggestion,
                    availableSubs = availableSubs,
                    hint = suggestion != null
                        ? $"Did you mean '{suggestion}'? B4X Sub names are matched case-insensitively, so check the spelling rather than the casing."
                        : "Run analyze_module on this file to see the full Sub list with line numbers."
                }, JsonOptions.Default);
            }

            var linesList = lines.ToList();
            int startIdx = target.LineStart;
            int endIdx = target.LineEnd;

            if (startIdx < 0 || endIdx >= linesList.Count || startIdx > endIdx)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Internal error: Sub line range out of bounds after parsing."
                }, JsonOptions.Default);

            var newCodeNormalized = newCode.Replace("\r\n", "\n");
            if (!newCodeNormalized.Trim().EndsWith("End Sub", StringComparison.OrdinalIgnoreCase) ||
                !newCodeNormalized.Contains("Sub ", StringComparison.OrdinalIgnoreCase))
            {
                return ToolResponse.Error(
                    $"CRITICAL: newCode must include BOTH the 'Sub {subName}' header AND the matching 'End Sub' line. The tool replaces the ENTIRE Sub between its Sub/End Sub boundaries — forgetting 'End Sub' will corrupt the module irreversibly.\n\n" +
                    "✅ Correct format:\n" +
                    $"Sub {subName} (parameters As Type)\n" +
                    "    ' your code here\n" +
                    "End Sub\n\n" +
                    "❌ Wrong (missing End Sub):\n" +
                    $"Sub {subName} (parameters As Type)\n" +
                    "    ' your code here",
                    hints: new[]
                    {
                        $"You are editing Sub '{subName}'. newCode must start with 'Sub {subName}' and end with 'End Sub'.",
                        "If you want to ADD a new Sub, use insert_line instead of edit_sub.",
                        "If you only want to change a few lines inside the Sub, use edit_line with specific line numbers instead of replacing the whole Sub."
                    });
            }

            var newLines = newCodeNormalized.TrimEnd('\n').Split('\n');
            linesList.RemoveRange(startIdx, endIdx - startIdx + 1);
            linesList.InsertRange(startIdx, newLines);

            var updatedCodeSection = string.Join("\n", linesList);
            var finalContent = markerIdx >= 0 ? header + "\r\n" + updatedCodeSection : updatedCodeSection;

            string? backupPath = _fileRepository.BackupPath(filePath);
            _fileRepository.WriteText(filePath, finalContent);

            int headerLineCount = markerIdx >= 0
                ? raw.Substring(0, markerIdx).Split('\n').Length
                : 0;

            return JsonSerializer.Serialize(new
            {
                success = true,
                filePath,
                backup = backupPath,
                subReplaced = target.FunctionName,
                lineNumbering = "file",
                lineOffset = headerLineCount,
                originalLineRange = new
                {
                    start = target.LineStart + 1 + headerLineCount,
                    end = target.LineEnd + 1 + headerLineCount
                },
                newLineCount = newLines.Length
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Replaces a single line in a B4X source file (.bas, .b4a, .b4j, .b4i, or any text file) by its 1-based line number, without touching the rest of the file. Pass an empty string as newContent to DELETE the line (subsequent lines shift up). Use this for surgical fixes where you know exactly which line to change (e.g. fixing a typo on line 42). For .b4a/.b4j/.b4i project files the IDE metadata header is preserved automatically. Pass expectedText as an atomic safety check to abort if the line has shifted. Creates a .bak backup before writing. For replacing an entire Sub use edit_sub instead; for replacing the whole file use write_file.")]
        public string EditLine(
            [Description("Absolute path to the .bas/.b4a/.b4j/.b4i file (or any text file) to edit.")] string filePath,
            [Description("1-based FILE-LINE number to replace. FILE-LINE counts from line 1 of the file, INCLUDING the IDE metadata header at the top of .b4a/.b4j/.b4i files — the same convention used by compile_project error output.")] int lineNumber,
            [Description("The new content for the line. Pass an empty string to DELETE the line (all subsequent lines shift up). To replace one line with multiple lines, embed newline characters (\\n) in the string.")] string newContent,
            [Description("Optional safety check: the current line at lineNumber must exactly match this string (whitespace and all). If it does not, the edit is aborted before the file is touched.")] string? expectedText = null)
        {
            PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

            if (PathSecurity.IsForbiddenMainBas(filePath, out var blockReason))
            {
                return ToolResponse.Error(
                    blockReason!,
                    hints: new[]
                    {
                        "edit_line cannot create a new Main.bas in a B4X project directory — the .b4a/.b4j/.b4i is the Main module.",
                        "To add a Sub to the project's Main, use edit_sub on the project file."
                    });
            }

            if (!_fileRepository.Exists(filePath))
                return ToolResponse.Error(
                    $"File not found: {filePath}",
                    hints: new[] { "Run get_project_structure to list every file in the project.", "Use absolute paths only — relative paths are rejected." });

            string? projectRoot = _projectRepository.FindProjectRoot(filePath);
            if (projectRoot != null)
                PathSecurity.ValidateWithinBaseDirectory(filePath, projectRoot, nameof(filePath));

            string raw = _fileRepository.ReadTextWithHeader(filePath);

            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);

            string header = markerIdx >= 0 ? raw.Substring(0, markerIdx + marker.Length) : string.Empty;
            string editableSection = markerIdx >= 0
                ? raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n')
                : raw;
            bool fileHasHeader = markerIdx >= 0;

            int headerLineCount = fileHasHeader
                ? raw.Substring(0, markerIdx).Split('\n').Length
                : 0;

            int crlfCount = raw.Split("\r\n", StringSplitOptions.None).Length - 1;
            int lfCount = raw.Split('\n', StringSplitOptions.None).Length - 1 - crlfCount;
            string lineEnding = lfCount > crlfCount ? "\n" : "\r\n";

            var lines = editableSection.Replace("\r\n", "\n").Split('\n').ToList();

            int totalLines = lines.Count;
            if (totalLines > 0 && lines[^1].Length == 0)
                totalLines--;

            if (fileHasHeader && lineNumber <= headerLineCount)
                return ToolResponse.Error(
                    "CANNOT EDIT IDE METADATA HEADER VIA edit_line",
                    data: new { lineNumber, editorBoundary = new { min = headerLineCount + 1, max = headerLineCount + totalLines }, lineOffset = headerLineCount, fileHasHeader },
                    hints: new[]
                    {
                        $"Line {lineNumber} falls within the IDE metadata header (file lines 1–{headerLineCount}, ending in @EndOfDesignText@). Header lines are NOT editable via edit_line / insert_line / replace_lines.",
                        "Use enable_library / disable_library for libraries, write_manifest for the B4A manifest, or register_layout_in_project / register_module_in_project for layout/module registration.",
                        $"Use edit_sub / insert_line for code changes inside the source code section. Line numbers MUST be in [{headerLineCount + 1}, {headerLineCount + totalLines}] for editing."
                    },
                    nextSteps: new[] { $"Use get_file_content to see the `lines` array filtered to fileLine > {headerLineCount}, then target one of those lines with edit_line." });

            if (lineNumber < 1 || lineNumber > headerLineCount + totalLines)
                return ToolResponse.Error(
                    $"LineNumber {lineNumber} is out of range. Editable file lines are [{headerLineCount + 1}, {headerLineCount + totalLines}] (line numbers are FILE-LINE: 1-based from the first line of the file, including the IDE metadata header).",
                    data: new { lineNumber, editorBoundary = new { min = headerLineCount + 1, max = headerLineCount + totalLines }, lineOffset = headerLineCount, fileHasHeader },
                    hints: new[] { "Use get_file_content to see the `lines` array and pick a line within the editable range.", "Use analyze_module to find Sub boundaries." },
                    nextSteps: new[] { "Read the file again with get_file_content to confirm the current line numbers, then retry with a lineNumber within the editor boundary." });

            int sourceLine = fileHasHeader ? lineNumber - headerLineCount : lineNumber;
            int targetIdx = sourceLine - 1;
            string originalLine = lines[targetIdx];

            if (expectedText != null)
            {
                if (!string.Equals(originalLine, expectedText, StringComparison.Ordinal))
                {
                    return ToolResponse.Error(
                        "ATOMIC SAFETY CHECK FAILED: The line content has changed since you last read it.",
                        data: new
                        {
                            filePath,
                            lineNumber,
                            expected = expectedText,
                            actual = originalLine,
                            hint = "The file was modified by another tool or process between your read and this write. Read the file again with get_file_content to see the current state."
                        });
                }
            }

            // Sacred region guard
            if (fileHasHeader)
            {
                string? sacredWarning = ProjectHelpersShared.DetectSacredRegionEdit(lines, sourceLine, sourceLine, filePath);
                if (sacredWarning != null)
                {
                    return ToolResponse.Error(
                        "⚠️ SACRED REGION — EDIT BLOCKED",
                        data: new { filePath, lineNumber, sacredRegion = sacredWarning },
                        hints: new[]
                        {
                            "You are attempting to edit INSIDE a sacred region block (#Region Project/Activity Attributes). These contain essential IDE settings.",
                            "Editing lines inside sacred regions can corrupt the project."
                        });
                }
            }

            // Handle delete (empty newContent)
            if (newContent.Length == 0)
            {
                lines.RemoveAt(targetIdx);
            }
            else
            {
                var newLines = newContent.Replace("\r\n", "\n").Split('\n');
                lines.RemoveAt(targetIdx);
                for (int i = newLines.Length - 1; i >= 0; i--)
                    lines.Insert(targetIdx, newLines[i]);
            }

            var updatedEditable = string.Join(lineEnding, lines);
            string finalContent = fileHasHeader
                ? header + lineEnding + updatedEditable
                : updatedEditable;

            string? backupPath = _fileRepository.BackupPath(filePath);
            _fileRepository.WriteText(filePath, finalContent);

            return ToolResponse.Success(
                data: new
                {
                    filePath,
                    backup = backupPath,
                    lineNumber,
                    fileHadHeader = fileHasHeader,
                    newContent,
                    deleted = newContent.Length == 0
                },
                hints: new[] { "Call compile_project to confirm the edit didn't break the build." });
        }

        [McpServerTool, Description("Inserts new content as one or more lines at a given 1-based FILE-LINE position in a B4X source file (.bas, .b4a, .b4j, .b4i, or any text file), shifting all subsequent lines down. Use this for adding new Subs, Dim declarations, or comments above existing lines without disturbing surrounding code. Allowed range is [lineOffset + 1, totalLines + 1] — lineOffset + 1 inserts at the very top of the source code section, totalLines + 1 appends after the last existing line. Insertion at header lines [1, lineOffset] is rejected. newContent may contain embedded newlines (\\n) — each becomes its own inserted line. Creates a .bak backup before writing.")]
        public string InsertLine(
            [Description("Absolute path to the .bas/.b4a/.b4j/.b4i file (or any text file) to edit.")] string filePath,
            [Description("1-based FILE-LINE position to insert AT. Must be in [lineOffset+1, lineOffset+totalLines+1].")] int lineNumber,
            [Description("Content to insert. May contain embedded newline characters (\\n) — each becomes its own inserted line.")] string newContent)
        {
            PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

            if (PathSecurity.IsForbiddenMainBas(filePath, out var blockReason))
            {
                return ToolResponse.Error(
                    blockReason!,
                    hints: new[]
                    {
                        "insert_line cannot create a new Main.bas in a B4X project directory — the .b4a/.b4j/.b4i is the Main module.",
                        "To add a Sub to the project's Main, use edit_sub on the project file."
                    });
            }

            if (!_fileRepository.Exists(filePath))
                return ToolResponse.Error(
                    $"File not found: {filePath}",
                    hints: new[] { "Run get_project_structure to list every file in the project.", "Use absolute paths only — relative paths are rejected." });

            string? projectRoot = _projectRepository.FindProjectRoot(filePath);
            if (projectRoot != null)
                PathSecurity.ValidateWithinBaseDirectory(filePath, projectRoot, nameof(filePath));

            string raw = _fileRepository.ReadTextWithHeader(filePath);

            int crlfCount = raw.Split("\r\n", StringSplitOptions.None).Length - 1;
            int lfCount = raw.Split('\n', StringSplitOptions.None).Length - 1 - crlfCount;
            string lineEnding = lfCount > crlfCount ? "\n" : "\r\n";

            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);

            string header = markerIdx >= 0 ? raw.Substring(0, markerIdx + marker.Length) : string.Empty;
            string editableSection = markerIdx >= 0
                ? raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n')
                : raw;
            bool fileHasHeader = markerIdx >= 0;

            var lines = editableSection.Replace("\r\n", "\n").Split('\n').ToList();

            int totalLines = lines.Count;
            if (totalLines > 0 && lines[^1].Length == 0)
                totalLines--;

            int headerLineCount = fileHasHeader ? raw.Substring(0, markerIdx).Split('\n').Length : 0;

            if (fileHasHeader && lineNumber <= headerLineCount)
                return ToolResponse.Error(
                    "CANNOT INSERT INTO IDE METADATA HEADER VIA insert_line",
                    data: new { lineNumber, editorBoundary = new { min = headerLineCount + 1, max = headerLineCount + totalLines + 1 }, lineOffset = headerLineCount, fileHasHeader },
                    hints: new[]
                    {
                        $"LineNumber {lineNumber} falls within the IDE metadata header (file lines 1–{headerLineCount}). Header rows are NOT insertable via insert_line.",
                        "Use enable_library / disable_library / write_manifest for changes inside the header.",
                        $"For code insertion use a lineNumber within [{headerLineCount + 1}, {headerLineCount + totalLines + 1}]."
                    });

            if (lineNumber < 1 || lineNumber > headerLineCount + totalLines + 1)
                return ToolResponse.Error(
                    $"LineNumber {lineNumber} is out of range for an insertion. Valid range is [1, {totalLines + 1}] (1 inserts at top, {totalLines + 1} appends at end).",
                    data: new { lineNumber, minValid = 1, maxValid = totalLines + 1, totalEditableLines = totalLines, fileHasHeader },
                    hints: new[] { "LineNumber for insertion is 1-based and must be in [1, totalLines + 1].", "Use get_file_content to see current line numbers." },
                    nextSteps: new[] { "Read the file again to confirm line counts, then retry with a valid insertion position." });

            int sourceLine = fileHasHeader ? lineNumber - headerLineCount : lineNumber;
            int insertIdx = sourceLine - 1;

            // Sacred region guard
            if (fileHasHeader)
            {
                string? sacredWarning = ProjectHelpersShared.DetectSacredRegionEdit(lines, sourceLine, sourceLine, filePath);
                if (sacredWarning != null)
                {
                    return ToolResponse.Error(
                        "⚠️ SACRED REGION — INSERT BLOCKED",
                        data: new { filePath, lineNumber, sacredRegion = sacredWarning },
                        hints: new[]
                        {
                            "You are attempting to insert INTO a sacred region block (#Region Project/Activity Attributes). These contain essential IDE settings.",
                            "Inserting lines inside sacred regions can corrupt the project. Add new #attribute lines directly above or below the existing #End Region, or use edit_line to modify an existing attribute value."
                        });
                }
            }

            var newLines = newContent.Replace("\r\n", "\n").Split('\n');
            lines.InsertRange(insertIdx, newLines);

            var updatedEditable = string.Join(lineEnding, lines);
            string finalContent = fileHasHeader
                ? header + lineEnding + updatedEditable
                : updatedEditable;

            string? backupPath = _fileRepository.BackupPath(filePath);
            _fileRepository.WriteText(filePath, finalContent);

            return ToolResponse.Success(
                data: new
                {
                    filePath,
                    lineNumber,
                    fileHadHeader = fileHasHeader,
                    newLine = newContent,
                    insertedAtLine = lineNumber,
                    totalLinesAfter = (fileHasHeader ? headerLineCount : 0) + lines.Count - (lines.Count > 0 && lines[^1].Length == 0 ? 1 : 0)
                },
                hints: new[] { "Call compile_project to confirm the insert didn't break the build." });
        }

        [McpServerTool, Description("Replaces a CONTIGUOUS RANGE of inclusive [startLine, endLine] FILE-LINE numbers in a B4X source file (.bas, .b4a, .b4j, .b4i, or any text file) with new content, in the spirit of edit_line but spanning multiple lines. The range is inclusive on both ends. Allowed range is [lineOffset + 1, totalLines] — header rows are rejected with a hard error. newContent may contain embedded newlines (\\n) — each becomes its own inserted line. Pass newContent=\"\" to DELETE the range entirely.")]
        public string ReplaceLines(
            [Description("Absolute path to the .bas/.b4a/.b4j/.b4i file (or any text file) to edit.")] string filePath,
            [Description("1-based START of the inclusive range to replace.")] int startLine,
            [Description("1-based END of the inclusive range to replace. Must be >= startLine.")] int endLine,
            [Description("Content that REPLACES the range. Pass empty string to DELETE the range.")] string newContent)
        {
            PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

            if (!_fileRepository.Exists(filePath))
                return ToolResponse.Error(
                    $"File not found: {filePath}",
                    hints: new[] { "Run get_project_structure to list every file in the project.", "Use absolute paths only — relative paths are rejected." });

            string? projectRoot = _projectRepository.FindProjectRoot(filePath);
            if (projectRoot != null)
                PathSecurity.ValidateWithinBaseDirectory(filePath, projectRoot, nameof(filePath));

            string raw = _fileRepository.ReadTextWithHeader(filePath);

            int crlfCount = raw.Split("\r\n", StringSplitOptions.None).Length - 1;
            int lfCount = raw.Split('\n', StringSplitOptions.None).Length - 1 - crlfCount;
            string lineEnding = lfCount > crlfCount ? "\n" : "\r\n";

            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);

            string header = markerIdx >= 0 ? raw.Substring(0, markerIdx + marker.Length) : string.Empty;
            string editableSection = markerIdx >= 0
                ? raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n')
                : raw;
            bool fileHasHeader = markerIdx >= 0;

            var lines = editableSection.Replace("\r\n", "\n").Split('\n').ToList();

            int totalLines = lines.Count;
            if (totalLines > 0 && lines[^1].Length == 0)
                totalLines--;

            int headerLineCount = fileHasHeader ? raw.Substring(0, markerIdx).Split('\n').Length : 0;

            if (fileHasHeader && startLine <= headerLineCount)
                return ToolResponse.Error(
                    "CANNOT REPLACE INTO IDE METADATA HEADER VIA replace_lines",
                    data: new { startLine, endLine, editorBoundary = new { min = headerLineCount + 1, max = headerLineCount + totalLines }, lineOffset = headerLineCount, fileHasHeader },
                    hints: new[]
                    {
                        $"Range starts within the IDE metadata header (file lines 1–{headerLineCount}). Header rows are NOT replaceable via replace_lines.",
                        "Use enable_library / disable_library / write_manifest for changes inside the header.",
                        $"For code replacement use startLine within [{headerLineCount + 1}, {headerLineCount + totalLines}]."
                    });

            if (startLine < 1 || endLine < startLine || startLine > headerLineCount + totalLines || endLine > headerLineCount + totalLines)
                return ToolResponse.Error(
                    $"Invalid range [{startLine}, {endLine}]. Must satisfy {headerLineCount + 1} ≤ startLine ≤ endLine ≤ {headerLineCount + totalLines}. Line numbers are FILE-LINE.",
                    data: new { startLine, endLine, editorBoundary = new { min = headerLineCount + 1, max = headerLineCount + totalLines }, lineOffset = headerLineCount, fileHasHeader },
                    hints: new[] { "startLine and endLine are FILE-LINE numbers, 1-based, inclusive.", $"Both must be within [{headerLineCount + 1}, {headerLineCount + totalLines}].", "Use get_file_content to see exact line numbers." },
                    nextSteps: new[] { "Read the file again with get_file_content to confirm the current line numbers, then retry with a valid FILE-LINE range." });

            int sourceStartLine = fileHasHeader ? startLine - headerLineCount : startLine;
            int sourceEndLine = fileHasHeader ? endLine - headerLineCount : endLine;

            int startIdx = sourceStartLine - 1;
            int rangeSize = sourceEndLine - sourceStartLine + 1;

            // Sacred region guard
            if (fileHasHeader)
            {
                string? sacredWarning = ProjectHelpersShared.DetectSacredRegionEdit(lines, sourceStartLine, sourceEndLine, filePath);
                if (sacredWarning != null)
                {
                    var sacredRemoved = new List<string>(rangeSize);
                    for (int i = 0; i < rangeSize; i++)
                        sacredRemoved.Add(lines[startIdx + i]);

                    return ToolResponse.Error(
                        "⚠️ SACRED REGION — REPLACE BLOCKED",
                        data: new { filePath, startLine, endLine, removedRange = sacredRemoved, sacredRegion = sacredWarning },
                        hints: new[]
                        {
                            "This replace overlaps with SACRED REGION blocks (#Region Project/Activity Attributes). These contain essential IDE settings.",
                            "Replacing lines inside sacred regions can corrupt the project and break compilation.",
                            "If you need to change a specific attribute value, use edit_line on that exact line with expectedText for safety."
                        });
                }
            }

            var removedRange = new List<string>(rangeSize);
            for (int i = 0; i < rangeSize; i++)
                removedRange.Add(lines[startIdx + i]);

            string[]? replacedLines = null;
            if (newContent.Length == 0)
            {
                // DELETE the range entirely
                lines.RemoveRange(startIdx, rangeSize);
            }
            else
            {
                var newLinesList = newContent.Replace("\r\n", "\n").Split('\n');
                replacedLines = newLinesList;
                lines.RemoveRange(startIdx, rangeSize);
                lines.InsertRange(startIdx, newLinesList);
            }

            var updatedEditable = string.Join(lineEnding, lines);
            string finalContent = fileHasHeader
                ? header + lineEnding + updatedEditable
                : updatedEditable;

            string? backupPath = _fileRepository.BackupPath(filePath);
            _fileRepository.WriteText(filePath, finalContent);

            return ToolResponse.Success(
                data: new
                {
                    filePath,
                    backup = backupPath,
                    fileHadHeader = fileHasHeader,
                    replacedRange = removedRange,
                    replacedWith = replacedLines,
                    newLineCount = replacedLines?.Length ?? 0
                },
                hints: new[] { "Call compile_project to confirm the replacement didn't break the build." },
                nextSteps: new[] { "The previous contents of the range are in data.replacedRange — you can undo by calling replace_lines again with those original lines." });
        }
    }
}
