using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using B4XMcpServer.Models;
using B4XMcpServer.Engine;
using FileMode = B4XMcpServer.Models.FileMode;

namespace B4XMcpServer.Services
{
    public static class BundleBuilder
    {
        public static string BuildMarkdown(
            string? preamble,
            string? task,
            IEnumerable<ProjectFile> files,
            bool includeFileTree = true,
            string? activeCode = null,
            string? activeFile = null,
            string? activeSub = null,
            string? compileErrors = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Context Bundle");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(compileErrors))
            {
                sb.AppendLine(compileErrors);
                sb.AppendLine("---");
            }

            sb.AppendLine("## PREAMBLE / CONTEXT");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(preamble) ? "(none)" : preamble);
            sb.AppendLine();

            sb.AppendLine("## TASK / QUERY");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(task) ? "(none)" : task);
            sb.AppendLine();

            if (includeFileTree)
            {
                sb.AppendLine("## FILE TREE");
                sb.AppendLine();
                var tree = BuildAsciiTree(files.Select(f => f.Path));
                sb.AppendLine("```");
                sb.AppendLine(tree);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            sb.AppendLine("## FILES");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(activeCode))
            {
                var tag = activeCode.TrimStart().StartsWith("{") || activeCode.TrimStart().StartsWith("[") ? "json" : "b4x";
                var title = "PRIMARY ACTIVE TARGET";
                if (!string.IsNullOrEmpty(activeSub)) title += $" - Sub {activeSub}";
                if (!string.IsNullOrEmpty(activeFile)) title += $" ({System.IO.Path.GetFileName(activeFile)})";
                sb.AppendLine($"\n### {title}\n```{tag}\n{activeCode}\n```");
            }

            foreach (var f in files.Where(f => f.Included))
            {
                sb.AppendLine($"### {f.Name}   ({f.Mode})");
                sb.AppendLine();
                try
                {
                    if (f.Kind == "bal" || f.Kind == "bjl" || f.Kind == "bil")
                    {
                        var data = System.IO.File.ReadAllBytes(f.Path);
                        var json = LayoutJsonTransform.LayoutToJson(data);
                        var tag = f.Mode == FileMode.Skeleton ? "text" : "json";
                        sb.AppendLine("```" + tag);
                        sb.AppendLine(json);
                        sb.AppendLine("```");
                    }
                    else
                    {
                        var txt = CodeUtils.ReadTextSafely(f.Path);
                        if (f.Mode == FileMode.Skeleton)
                        {
                            var keep = new List<string>();
                            if (!string.IsNullOrEmpty(activeSub) && !string.IsNullOrEmpty(activeFile) && System.IO.Path.GetFullPath(activeFile) == System.IO.Path.GetFullPath(f.Path))
                                keep.Add(activeSub);
                            var skeleton = BuildSkeleton(txt, keep);
                            sb.AppendLine("```b4x");
                            sb.AppendLine(skeleton);
                            sb.AppendLine("```");
                        }
                        else
                        {
                            sb.AppendLine("```b4x");
                            sb.AppendLine(txt);
                            sb.AppendLine("```");
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"(Could not read file: {ex.Message})");
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static string BuildAsciiTree(IEnumerable<string> paths)
        {
            var grouped = paths.Select(p => p.Replace('\\', '/')).ToList();
            var sb = new StringBuilder();
            foreach (var p in grouped.OrderBy(p => p))
            {
                sb.AppendLine(p);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Builds a skeleton of a B4X module using B4XMcpServer.Engine function blocks
        /// and regex scanning for Type declarations. Sub bodies are collapsed
        /// except for names in keepFullNames (Globals/PG/CG and the active sub).
        /// Leading comments are preserved before each collapsed block.
        /// </summary>
        private static string BuildSkeleton(string source, List<string> keepFullNames)
        {
            var keepSet = new HashSet<string>(keepFullNames.Select(n => n.ToLowerInvariant()));
            var lines = source.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0) return source;

            // 1. Get function blocks (Subs, including Globals/Process_Globals/Class_Globals)
            DocumentAnalysisEngine.AnalyzeDocumentForFunctionBlocks(lines);
            var blocks = DocumentAnalysisEngine.FunctionBlockList
                .Select(b => new BlockInfo
                {
                    StartLine = b.LineStart,
                    EndLine = b.LineEnd,
                    Name = b.FunctionName,
                    Kind = "Sub",
                })
                .ToList();

            // 2. Scan for Type declarations (use regex to avoid false matches like "Dim x As Type")
            var typePattern = new System.Text.RegularExpressions.Regex(@"^\s*Type\s+(\w+)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                var m = typePattern.Match(lines[i]);
                if (!m.Success) continue;
                string typeName = m.Groups[1].Value;
                int endLine = i;
                for (int j = i; j < lines.Length; j++)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(lines[j], @"^\s*End\s+Type\b",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        endLine = j;
                        break;
                    }
                }
                blocks.Add(new BlockInfo { StartLine = i, EndLine = endLine, Name = typeName, Kind = "Type" });
                i = endLine;
            }

            blocks = blocks.OrderBy(b => b.StartLine).ToList();

            var outLines = new List<string>();
            int lastEmittedLine = 0;

            foreach (var block in blocks)
            {
                int start = Math.Max(0, Math.Min(block.StartLine, lines.Length - 1));
                int end = Math.Max(start, Math.Min(block.EndLine, lines.Length - 1));

                // Emit lines between last block and this block
                for (int idx = lastEmittedLine; idx < start; idx++)
                    outLines.Add(lines[idx]);

                // Capture leading comments (contiguous lines starting with ' before the block)
                var leadingComments = new List<string>();
                for (int idx = start - 1; idx >= 0; idx--)
                {
                    var trimmed = lines[idx].Trim();
                    if (trimmed.StartsWith("'")) leadingComments.Insert(0, lines[idx]);
                    else if (string.IsNullOrWhiteSpace(lines[idx]))
                    {
                        // Blank line: comment block boundary, stop
                        break;
                    }
                    else break;
                }

                string headerLine = lines[start];
                bool alwaysFull = block.Kind == "Sub" && (block.Name.Equals("Process_Globals", StringComparison.OrdinalIgnoreCase) ||
                                                            block.Name.Equals("Globals", StringComparison.OrdinalIgnoreCase) ||
                                                            block.Name.Equals("Class_Globals", StringComparison.OrdinalIgnoreCase));
                bool keepFull = alwaysFull || block.Kind == "Type" || keepSet.Contains(block.Name.ToLowerInvariant());

                if (keepFull)
                {
                    // Emit leading comments before the block
                    foreach (var comment in leadingComments)
                        outLines.Add(comment);
                    for (int idx = start; idx <= end; idx++)
                        outLines.Add(lines[idx]);
                }
                else
                {
                    foreach (var comment in leadingComments)
                        outLines.Add(comment);
                    outLines.Add(headerLine);
                    int bodyLineCount = Math.Max(0, (end - start + 1) - 2);
                    outLines.Add($"\t'... ({bodyLineCount} lines omitted, use keep_full to see the full body) ...");
                    string closer = block.Kind == "Type" ? "End Type" : "End Sub";
                    outLines.Add(closer);
                }

                lastEmittedLine = end + 1;
            }

            // Emit any remaining lines after last block
            for (int idx = lastEmittedLine; idx < lines.Length; idx++)
                outLines.Add(lines[idx]);

            return string.Join("\n", outLines);
        }

        private class BlockInfo
        {
            public int StartLine { get; set; }
            public int EndLine { get; set; }
            public string Name { get; set; } = "";
            public string Kind { get; set; } = "";
        }
    }
}