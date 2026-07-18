using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace B4XMcpServer.Engine
{
    public static class HoverProvider
    {
        public static string? ProvideHover(string[] lines, string lineText, string word, int wordLineNo, int column, string fileName)
        {
            if (string.IsNullOrEmpty(word)) return null;

            string linePrefix = DocumentMethods.GetLinePrefix(lineText, column);
            var parentMatches = DocumentMethods.GetAllParentObjectMatches(linePrefix);

            if (parentMatches.Count > 0)
            {
                foreach (var m in parentMatches)
                {
                    var kw = Regex.Match(m, @"\w+");
                    if (!kw.Success) continue;
                    var info = DefinitionProvider.FindDefinitionPosition(lines, kw.Value, wordLineNo, fileName);
                    if (!string.IsNullOrEmpty(info.ClassName) &&
                        B4XBaseClassInfo.BaseClassMemberDeclaration.TryGetValue($"{info.ClassName.ToLowerInvariant()}.{word.ToLowerInvariant()}", out var decl))
                        return decl;
                }
            }

            var defInfo = DefinitionProvider.FindDefinitionPosition(lines, word, wordLineNo, fileName);
            var declStr = DefinitionProvider.GetDeclarationStringFromSearch(lines, word, wordLineNo);
            if (declStr != null)
            {
                string prefix = defInfo.Type switch
                {
                    KeywordType.Variable => defInfo.Scope == KeywordScope.Global ? "(global variable) " :
                                            defInfo.Scope == KeywordScope.Local ? "(local variable) " : "",
                    KeywordType.Parameter => "(parameter) ",
                    _ => "",
                };
                return prefix + declStr;
            }

            return null;
        }
    }
}
