using System;
using System.Collections.Generic;

namespace B4XMcpServer.Engine
{
    public static class ReferenceProvider
    {
        public static List<Location> ProvideReferences(string[] lines, string word, int wordLineNo, string fileName)
        {
            var ret = new List<Location>();
            if (string.IsNullOrEmpty(word)) return ret;

            var info = DefinitionProvider.FindDefinitionPosition(lines, word, wordLineNo, fileName);
            int startLine = 0, endLine = lines.Length;

            switch (info.Scope)
            {
                case KeywordScope.Local:
                    var boundary = DocumentAnalysisEngine.FindLocalSubBoundary(lines, wordLineNo);
                    startLine = boundary.Start; endLine = boundary.End;
                    goto case KeywordScope.Global;
                case KeywordScope.Global:
                    for (int line = startLine; line < endLine; line++)
                    {
                        string text = lines[line].Trim().ToLowerInvariant();
                        if (text.StartsWith("'")) continue;
                        int idx = text.IndexOf(word.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                            ret.Add(new Location(line, idx, word.Length));
                    }
                    break;
            }

            return ret;
        }
    }
}
