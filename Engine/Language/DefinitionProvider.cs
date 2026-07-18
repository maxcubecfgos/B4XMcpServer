using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace B4XMcpServer.Engine
{
    public static class DefinitionProvider
    {
        public static KeywordInfo FindDefinitionPosition(string[] lines, string word, int wordLineNo, string fileName, List<FunctionBlock>? functionBlocks = null)
        {
            var ret = new KeywordInfo { KeywordName = word, ModuleName = fileName };
            string lineText = lines[wordLineNo].Trim();
            if (lineText.StartsWith("'")) return ret;

            int idxBefore = lineText.IndexOf(word, StringComparison.OrdinalIgnoreCase) - 1;
            if (idxBefore >= 0 && idxBefore < lineText.Length && lineText[idxBefore] == '.')
            {
                ret.Scope = KeywordScope.CodeSpace;
                return ret;
            }

            var localResult = FindLocalVariableDefinition(lines, word, wordLineNo);
            if (localResult.DefinitionLine.HasValue)
                return localResult;

            var globalResult = FindGlobalDefinition(lines, word, fileName);
            if (globalResult.DefinitionLine.HasValue)
                return globalResult;

            var systemVar = B4XBaseClassInfo.SystemVariableCompletion
                .FirstOrDefault(v => v.Label.Equals(word, StringComparison.OrdinalIgnoreCase));
            if (systemVar != null)
            {
                ret.DefinitionLine = wordLineNo;
                ret.DefinitionColumn = lineText.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                ret.Scope = KeywordScope.CodeSpace;
                ret.Type = KeywordType.Variable;
                var decl = systemVar.Detail;
                var match = Regex.Match(decl, B4XRegex.VariableMatchPattern(word), RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                    ret.ClassName = match.Groups[1].Value;
            }

            return ret;
        }

        private static KeywordInfo FindLocalVariableDefinition(string[] lines, string word, int lineNo)
        {
            var ret = new KeywordInfo { KeywordName = word };
            var boundary = DocumentAnalysisEngine.FindLocalSubBoundary(lines, lineNo);

            if (boundary.Start >= boundary.End || boundary.End == 0)
                return ret;

            for (int line = boundary.Start; line < boundary.End; line++)
            {
                string lineText = lines[line];
                if (lineText.Trim().StartsWith("'")) continue;

                var varMatch = Regex.Match(lineText, B4XRegex.VariableMatchPattern(word), RegexOptions.IgnoreCase);
                if (varMatch.Success)
                {
                    ret.DefinitionLine = line;
                    ret.DefinitionColumn = lineText.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                    ret.Scope = KeywordScope.Local;
                    ret.Type = Regex.IsMatch(lineText, B4XRegex.DeclarationMatchPattern(word), RegexOptions.IgnoreCase)
                        ? KeywordType.Variable : KeywordType.Parameter;
                    if (varMatch.Groups.Count > 1) ret.ClassName = varMatch.Groups[1].Value;
                    return ret;
                }
            }

            return ret;
        }

        private static KeywordInfo FindGlobalDefinition(string[] lines, string word, string fileName)
        {
            var ret = new KeywordInfo { KeywordName = word, ModuleName = fileName };

            for (int line = 0; line < lines.Length; line++)
            {
                string lineText = lines[line];
                string lowerText = lineText.Trim().ToLowerInvariant();
                if (lowerText.StartsWith("'")) continue;

                if (ret.ModuleTypeValue == ModuleType.Undefined)
                {
                    if (lowerText.Contains("sub class_globals")) ret.ModuleTypeValue = ModuleType.Class;
                    if (lowerText.Contains("sub process_globals")) ret.ModuleTypeValue = ModuleType.StaticCode;
                }

                if (lowerText.Contains($"sub {word}_")) continue;

                var funcMatch = Regex.Match(lineText, B4XRegex.FunctionMatchPattern(word), RegexOptions.IgnoreCase);
                var varMatch = Regex.Match(lineText, B4XRegex.VariableMatchPattern(word), RegexOptions.IgnoreCase);

                if (funcMatch.Success || varMatch.Success)
                {
                    ret.DefinitionLine = line;
                    ret.DefinitionColumn = lineText.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                    ret.Scope = KeywordScope.Global;
                    if (funcMatch.Success) ret.Type = KeywordType.Sub;
                    if (varMatch.Success)
                    {
                        ret.Type = Regex.IsMatch(lineText, B4XRegex.DeclarationMatchPattern(word), RegexOptions.IgnoreCase)
                            ? KeywordType.Variable : KeywordType.Parameter;
                        if (varMatch.Groups.Count > 1) ret.ClassName = varMatch.Groups[1].Value;
                    }
                    return ret;
                }
            }

            return ret;
        }

        public static string? GetDeclarationStringFromSearch(string[] lines, string word, int wordLineNo, bool isFunctionSearch = true, bool isVariableSearch = true)
        {
            var info = FindDefinitionPosition(lines, word, wordLineNo, "");
            if (!info.DefinitionLine.HasValue) return null;
            return GetDeclarationStringFromSameLine(lines, word, info.DefinitionLine.Value, isFunctionSearch, isVariableSearch);
        }

        private static string? GetDeclarationStringFromSameLine(string[] lines, string word, int matchingLineNum, bool isFunctionSearch, bool isVariableSearch)
        {
            if (matchingLineNum < 0 || matchingLineNum >= lines.Length) return null;
            string text = lines[matchingLineNum].Trim();
            string lowerText = text.ToLowerInvariant();

            var varMatch = Regex.Match(text, B4XRegex.VariableMatchPattern(word), RegexOptions.IgnoreCase);

            if (lowerText.Contains($"sub {word}", StringComparison.OrdinalIgnoreCase))
            {
                if (isFunctionSearch) return text;
            }
            else if (varMatch.Success && isVariableSearch)
            {
                if (!Regex.IsMatch(text, B4XRegex.DeclarationMatchPattern(word), RegexOptions.IgnoreCase))
                {
                    int paramPos = lowerText.IndexOf($"{word} as", StringComparison.OrdinalIgnoreCase);
                    if (paramPos >= 0)
                    {
                        int end = text.IndexOf(',', paramPos);
                        if (end < 0) end = text.IndexOf(')', paramPos);
                        return end >= 0 ? text[paramPos..end] : text[paramPos..];
                    }
                }
                return varMatch.Value;
            }

            return null;
        }
    }
}
