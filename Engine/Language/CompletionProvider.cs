using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace B4XMcpServer.Engine
{
    public static class CompletionProvider
    {
        public static List<CompletionItem> ProvideCompletionItems(string[] lines, string lineText, string wordToSearch, int lineNumber, int column, string fullText)
        {
            var items = new List<CompletionItem>();
            string linePrefix = DocumentMethods.GetLinePrefix(lineText, column);

            if (DocumentMethods.IsDeclaringTypeName(linePrefix))
            {
                foreach (var typeComp in B4XBaseClassInfo.SystemClassTypeCompletion)
                {
                    if (typeComp.Label.StartsWith(wordToSearch, StringComparison.OrdinalIgnoreCase))
                        items.Add(typeComp);
                }
                return items;
            }

            if (DocumentMethods.IsNamingDeclaration(linePrefix))
                return items;

            var parentMatches = DocumentMethods.GetAllParentObjectMatches(linePrefix);
            if (parentMatches.Count > 0)
            {
                foreach (var m in parentMatches)
                {
                    var kw = Regex.Match(m, @"\w+");
                    if (!kw.Success) continue;
                    var info = DefinitionProvider.FindDefinitionPosition(lines, kw.Value, lineNumber, "");
                    if (!string.IsNullOrEmpty(info.ClassName) &&
                        B4XBaseClassInfo.BaseClassMemberCompletion.TryGetValue(info.ClassName.ToLowerInvariant(), out var members))
                        return members;
                }
                return items;
            }

            bool isProceeding = false;
            if (Regex.IsMatch(lineText, $@"(?:^|\r|\n)[ \t]*((Else[ \t]+)?If[ \t]+|(Select[ \t]+)?Case[ \t]+|For[ \t]+Each[ \t]+[ \t\w]+[ \t]+In[ \t]+)?\b{Regex.Escape(wordToSearch)}\b.*(?:$|\r|\n)", RegexOptions.IgnoreCase))
                isProceeding = true;
            if (!isProceeding && Regex.IsMatch(lineText, $@"(?:^|\r|\n)[ \t]*\b[\w]+[., \t\w]+\b.*(=|<(=)?|>(=)?|<>|\()[ \t]*\b{Regex.Escape(wordToSearch)}", RegexOptions.IgnoreCase))
                isProceeding = true;
            if (!isProceeding) return items;

            foreach (var kw in B4XBaseClassInfo.SystemKeywordCompletion)
            {
                if (kw.Label.StartsWith(wordToSearch, StringComparison.OrdinalIgnoreCase))
                    items.Add(kw);
            }
            foreach (var v in B4XBaseClassInfo.SystemVariableCompletion)
            {
                if (v.Label.StartsWith(wordToSearch, StringComparison.OrdinalIgnoreCase))
                    items.Add(v);
            }

            items.AddRange(FindVariablesAndCreateCompletions(fullText, wordToSearch));
            items.AddRange(FindFunctionsAndCreateCompletions(lines, wordToSearch));

            return items;
        }

        private static List<CompletionItem> FindVariablesAndCreateCompletions(string fullText, string keywordToMatch)
        {
            var result = new List<CompletionItem>();
            var varRegex = new Regex(B4XRegex.VariableDeclarationGlobPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            var matches = varRegex.Matches(fullText);
            foreach (Match match in matches)
            {
                if (match.Groups[1].Value.StartsWith(keywordToMatch, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new CompletionItem(match.Groups[1].Value.Trim(),
                        match.Groups[0].Value.Contains("Const", StringComparison.OrdinalIgnoreCase)
                            ? CompletionItemKind.Constant : CompletionItemKind.Variable,
                        match.Groups[0].Value.Trim(), ""));
                }
            }
            return result;
        }

        private static List<CompletionItem> FindFunctionsAndCreateCompletions(string[] lines, string keywordToMatch)
        {
            var result = new List<CompletionItem>();
            foreach (var line in lines)
            {
                if (line.Trim().StartsWith("'")) continue;
                var funcMatch = Regex.Match(line, @"(?<=Sub +)\b\w+\b", RegexOptions.IgnoreCase);
                if (funcMatch.Success && funcMatch.Value.StartsWith(keywordToMatch, StringComparison.OrdinalIgnoreCase))
                {
                    string detail = line.Trim();
                    int commentIdx = detail.IndexOf("'");
                    if (commentIdx >= 0) detail = detail[..commentIdx].Trim();
                    result.Add(new CompletionItem(funcMatch.Value, CompletionItemKind.Function, detail, ""));
                }
            }
            return result;
        }
    }
}
