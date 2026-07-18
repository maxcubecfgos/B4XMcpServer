using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace B4XEngineCore
{
    public static class SignatureHelpProvider
    {
        public static SignatureInfo? ProvideSignatureHelp(string[] lines, string lineText, int lineNumber, int column, string fileName)
        {
            int funcCallStartIdx = lineText.LastIndexOf('(', column);
            if (funcCallStartIdx < 0) return null;

            int funcNamePos = funcCallStartIdx - 1;
            string funcName = DocumentMethods.GetWordAtPosition(lineText, funcNamePos);
            if (string.IsNullOrEmpty(funcName)) return null;

            string linePrefix = DocumentMethods.GetLinePrefix(lineText, funcNamePos);
            var parentMatches = DocumentMethods.GetAllParentObjectMatches(linePrefix);
            string declaration = "";

            if (parentMatches.Count > 0)
            {
                foreach (var m in parentMatches)
                {
                    var kw = Regex.Match(m, @"\w+");
                    if (!kw.Success) continue;
                    var info = DefinitionProvider.FindDefinitionPosition(lines, kw.Value, lineNumber, fileName);
                    string className = info.ClassName.ToLowerInvariant();
                    if (B4XBaseClassInfo.SystemClassName.Contains(className))
                    {
                        if (B4XBaseClassInfo.BaseClassMemberDeclaration.TryGetValue($"{className}.{funcName.ToLowerInvariant()}", out var decl))
                        {
                            declaration = decl;
                            break;
                        }
                    }
                }
            }
            else
            {
                declaration = DefinitionProvider.GetDeclarationStringFromSearch(lines, funcName, lineNumber, true, false) ?? "";
            }

            if (string.IsNullOrEmpty(declaration)) return null;

            var labelMatch = Regex.Match(declaration, @"(?<=\bsub )(\w+)[^'|\n]+", RegexOptions.IgnoreCase);
            string label = labelMatch.Success ? labelMatch.Groups[1].Value : "";
            string declClean = labelMatch.Success ? labelMatch.Value : declaration;

            var paramMatches = Regex.Matches(declClean, B4XRegex.VariableDeclarationGlobPattern, RegexOptions.IgnoreCase);
            var parameters = new List<SignatureParameter>();
            foreach (Match p in paramMatches)
                parameters.Add(new SignatureParameter(p.Value, ""));

            string subStr = lineText[funcCallStartIdx..column];
            int activeParam = subStr.Count(c => c == ',');
            if (activeParam < parameters.Count)
                label = parameters[activeParam].Label;

            return new SignatureInfo(label, declClean, parameters);
        }
    }
}
