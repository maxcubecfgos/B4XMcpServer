using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace B4XMcpServer.Engine
{
    /// <summary>
    /// Parse node kinds for the enhanced AST.
    /// </summary>
    public enum ParseNodeKind { File, Sub, Type, ProcessGlobals, Globals, ClassGlobals }

    /// <summary>
    /// AST node produced by the structural parser.
    /// </summary>
    public class ParseNode
    {
        public ParseNodeKind Kind { get; set; } = ParseNodeKind.File;
        public string Name { get; set; } = "";
        public string? Params { get; set; }
        public string? ReturnType { get; set; }
        public bool IsPrivate { get; set; }
        public int StartLine { get; set; }
        public int? EndLine { get; set; }
        public bool LooksLikeEventHandler { get; set; }
        public List<ParseNode> Children { get; set; } = new();
    }

    /// <summary>
    /// A structural issue found during parsing (unclosed block, etc.).
    /// </summary>
    public class ParseIssue
    {
        public int Line { get; set; }
        public string Message { get; set; } = "";
        public string Severity { get; set; } = "error";
    }

    public static class DocumentAnalysisEngine
    {
        public static List<FunctionBlock> FunctionBlockList { get; } = new();

        /// <summary>
        /// Known B4X event handler name suffixes. Used to detect Sub names that
        /// look like event handlers (e.g. Button1_Click, Timer1_Tick).
        /// </summary>
        private static readonly Regex EventHandlerSuffixRe = new(
            @"_(Click|Create|Resume|Pause|CheckedChange|TextChanged|Tick|JobDone|" +
            @"Complete|ItemClick|LongClick|FocusChanged|ValueChanged|CloseRequest|Resize)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            TimeSpan.FromSeconds(2));

        private static bool IsEventHandlerName(string name)
            => EventHandlerSuffixRe.IsMatch(name);

        /// <summary>
        /// Enhanced parse: returns the AST root node plus any structural issues found.
        /// Uses stack-based tracking for Subs, Types, and Globals blocks.
        /// </summary>
        public static (ParseNode Root, List<ParseIssue> Issues) ParseModule(string source)
        {
            var root = new ParseNode { Kind = ParseNodeKind.File, Name = "root" };
            var issues = new List<ParseIssue>();
            var stack = new Stack<ParseNode>();
            stack.Push(root);

            var lines = source.Replace("\r\n", "\n").Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                // Skip comments and region directives
                if (trimmed.StartsWith("'")) continue;

                // ── Sub declarations ────────────────────────────────────
                var subMatch = Regex.Match(trimmed,
                    @"^\s*((Private|Public)\s+)?Sub\s+(\w+)\s*(\((.*)\))?\s*(As\s+(\w+))?",
                    RegexOptions.IgnoreCase);
                if (subMatch.Success)
                {
                    var node = new ParseNode
                    {
                        Kind = ParseNodeKind.Sub,
                        Name = subMatch.Groups[3].Value,
                        Params = subMatch.Groups[5].Value,
                        ReturnType = subMatch.Groups[7].Value,
                        IsPrivate = subMatch.Groups[2].Success && subMatch.Groups[2].Value.Trim().Equals("Private", StringComparison.OrdinalIgnoreCase),
                        StartLine = i + 1,
                        LooksLikeEventHandler = IsEventHandlerName(subMatch.Groups[2].Value),
                    };
                    stack.Peek().Children.Add(node);
                    stack.Push(node);
                    continue;
                }

                // ── Type declarations ───────────────────────────────────
                var typeMatch = Regex.Match(trimmed,
                    @"^\s*Type\s+(\w+)", RegexOptions.IgnoreCase);
                if (typeMatch.Success)
                {
                    var node = new ParseNode
                    {
                        Kind = ParseNodeKind.Type,
                        Name = typeMatch.Groups[1].Value,
                        StartLine = i + 1,
                    };
                    stack.Peek().Children.Add(node);
                    stack.Push(node);
                    continue;
                }

                // ── Globals blocks ──────────────────────────────────────
                var globalsMatch = Regex.Match(trimmed,
                    @"^\s*(Process_Globals|Globals|Class_Globals)\s*$",
                    RegexOptions.IgnoreCase);
                if (globalsMatch.Success)
                {
                    var kind = globalsMatch.Groups[1].Value switch
                    {
                        "Process_Globals" => ParseNodeKind.ProcessGlobals,
                        "Globals" => ParseNodeKind.Globals,
                        "Class_Globals" => ParseNodeKind.ClassGlobals,
                        _ => ParseNodeKind.Globals,
                    };
                    var node = new ParseNode
                    {
                        Kind = kind,
                        Name = globalsMatch.Groups[1].Value,
                        StartLine = i + 1,
                    };
                    stack.Peek().Children.Add(node);
                    stack.Push(node);
                    continue;
                }

                // ── End Sub / End Type ──────────────────────────────────
                if (Regex.IsMatch(trimmed, @"^\s*End\s+(Sub|Type)\s*$", RegexOptions.IgnoreCase))
                {
                    if (stack.Count > 1)
                    {
                        var top = stack.Pop();
                        top.EndLine = i + 1;
                    }
                    else
                    {
                        issues.Add(new ParseIssue
                        {
                            Line = i + 1,
                            Message = "Unmatched End Sub/Type",
                            Severity = "error"
                        });
                    }
                }
            }

            // ── Report unclosed blocks ──────────────────────────────────
            while (stack.Count > 1)
            {
                var unclosed = stack.Pop();
                issues.Add(new ParseIssue
                {
                    Line = unclosed.StartLine,
                    Message = $"Unclosed {unclosed.Kind} '{unclosed.Name}'",
                    Severity = "error"
                });
            }

            return (root, issues);
        }

        /// <summary>
        /// Flattens all Sub and Type nodes from the parse tree.
        /// </summary>
        public static List<ParseNode> FlattenSubsAndTypes(ParseNode root)
        {
            var result = new List<ParseNode>();
            FlattenRecursive(root, result);
            return result;
        }

        private static void FlattenRecursive(ParseNode node, List<ParseNode> result)
        {
            foreach (var child in node.Children)
            {
                if (child.Kind is ParseNodeKind.Sub or ParseNodeKind.Type)
                    result.Add(child);
                FlattenRecursive(child, result);
            }
        }

        public static void AnalyzeDocumentForFunctionBlocks(string[] lines)
        {
            FunctionBlockList.Clear();
            FunctionBlock? currentBlock = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                var startSubMatch = Regex.Match(line, B4XRegex.StartOfSub, RegexOptions.IgnoreCase);
                if (startSubMatch.Success)
                {
                    currentBlock = new FunctionBlock
                    {
                        LineStart = i,
                        FunctionScopeValue = Regex.IsMatch(line, @"\bPrivate\b", RegexOptions.IgnoreCase)
                            ? FunctionScope.Private : FunctionScope.Public,
                        FunctionName = startSubMatch.Groups[2].Value,
                    };
                }

                var endSubMatch = Regex.Match(line, B4XRegex.EndOfSub, RegexOptions.IgnoreCase);
                if (endSubMatch.Success && currentBlock != null && currentBlock.LineStart >= 0)
                {
                    currentBlock.LineEnd = i;
                    currentBlock.BlockText = string.Join("\n", lines[currentBlock.LineStart..(i + 1)]);
                    FunctionBlockList.Add(currentBlock);
                }
            }
        }

        public static (int Start, int End) FindLocalSubBoundary(string[] lines, int lineNo, KeywordInfo? givenWordInfo = null)
        {
            const string subStartStr = "sub ";
            const string subEndStr = "end sub";
            int start = 0, end = 0;

            for (int line = lineNo; line >= 0; line--)
            {
                string text = lines[line].Trim().ToLowerInvariant();
                if (text.StartsWith("'")) continue;
                if (text.Contains(subEndStr)) return (start, end);
                if (text.Contains(subStartStr))
                {
                    if (text.Contains("sub class_globals") || text.Contains("sub process_globals"))
                    {
                        if (text.Contains("sub class_globals") && givenWordInfo != null)
                            givenWordInfo.ModuleTypeValue = ModuleType.Class;
                        if (text.Contains("sub process_globals") && givenWordInfo != null)
                            givenWordInfo.ModuleTypeValue = ModuleType.StaticCode;
                        return (start, end);
                    }
                    start = line;
                    break;
                }
            }

            for (int line = lineNo + 1; line < lines.Length; line++)
            {
                string text = lines[line].Trim().ToLowerInvariant();
                if (text.StartsWith("'")) continue;
                if (text.Contains(subStartStr)) return (start, end);
                if (text.Contains(subEndStr)) { end = line; break; }
            }

            return (start, end);
        }

        public static int GetStatementDifferenceInBlockText(string blockText, string startPattern, string endPattern)
        {
            int startCount = Regex.Matches(blockText, startPattern, RegexOptions.IgnoreCase).Count;
            int endCount = Regex.Matches(blockText, endPattern, RegexOptions.IgnoreCase).Count;
            return startCount - endCount;
        }

        public static string? GetAutoCloseStatement(string lineText, FunctionBlock? currentBlock, string fullDocumentText)
        {
            var openSubMatch = Regex.Match(lineText, B4XRegex.StartOfSub, RegexOptions.IgnoreCase);
            if (openSubMatch.Success)
            {
                if (currentBlock != null) return null;
                if (GetStatementDifferenceInBlockText(fullDocumentText, B4XRegex.StartOfSub, B4XRegex.EndOfSub) > 0)
                    return "End Sub";
                return null;
            }

            if (currentBlock == null) return null;

            if (Regex.IsMatch(lineText, B4XRegex.StartOfIf, RegexOptions.IgnoreCase))
            {
                int diff = GetStatementDifferenceInBlockText(currentBlock.BlockText, B4XRegex.StartOfIf, B4XRegex.EndOfIf);
                if (diff > 0)
                {
                    int inlineIfCount = Regex.Matches(currentBlock.BlockText, B4XRegex.InlineIf, RegexOptions.IgnoreCase).Count;
                    if (diff - inlineIfCount > 0) return "End If";
                }
            }
            else if (Regex.IsMatch(lineText, B4XRegex.StartOfFor, RegexOptions.IgnoreCase))
            {
                if (GetStatementDifferenceInBlockText(currentBlock.BlockText, B4XRegex.StartOfFor, B4XRegex.EndOfFor) > 0)
                    return "Next";
            }
            else if (Regex.IsMatch(lineText, B4XRegex.StartOfSelect, RegexOptions.IgnoreCase))
            {
                if (GetStatementDifferenceInBlockText(currentBlock.BlockText, B4XRegex.StartOfSelect, B4XRegex.EndOfSelect) > 0)
                    return "End Select";
            }
            else if (Regex.IsMatch(lineText, B4XRegex.StartOfTry, RegexOptions.IgnoreCase))
            {
                if (GetStatementDifferenceInBlockText(currentBlock.BlockText, B4XRegex.StartOfTry, B4XRegex.EndOfTry) > 0)
                    return "Catch\n\tLog(LastException)\nEnd Try";
            }

            return null;
        }
    }
}
