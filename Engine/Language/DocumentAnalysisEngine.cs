using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace B4XMcpServer.Engine
{
    public static class DocumentAnalysisEngine
    {
        public static List<FunctionBlock> FunctionBlockList { get; } = new();

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
