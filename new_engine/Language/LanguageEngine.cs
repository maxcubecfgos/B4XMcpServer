using System.Collections.Generic;
using System.Linq;

namespace B4XEngineCore
{
    public static class LanguageEngine
    {
        public static DocumentAnalysisResult AnalyzeDocument(
            string[] lines,
            string fullText,
            int? cursorLine = null,
            int? cursorColumn = null,
            string? cursorWord = null,
            string fileName = "",
            AnalysisFlags flags = AnalysisFlags.All)
        {
            var result = new DocumentAnalysisResult();

            DocumentAnalysisEngine.AnalyzeDocumentForFunctionBlocks(lines);
            result.FunctionBlocks = new List<FunctionBlock>(DocumentAnalysisEngine.FunctionBlockList);

            if (flags.HasFlag(AnalysisFlags.Folding))
                result.FoldingRanges = FoldingRangeProvider.ProvideFoldingRanges(lines);

            if (cursorLine.HasValue && cursorColumn.HasValue && cursorWord != null)
            {
                string lineText = lines[cursorLine.Value];

                if (flags.HasFlag(AnalysisFlags.Definition))
                {
                    result.Definition = DefinitionProvider.FindDefinitionPosition(lines, cursorWord, cursorLine.Value, fileName);
                }

                if (flags.HasFlag(AnalysisFlags.Hover))
                {
                    result.HoverText = HoverProvider.ProvideHover(lines, lineText, cursorWord, cursorLine.Value, cursorColumn.Value, fileName);
                }

                if (flags.HasFlag(AnalysisFlags.References))
                {
                    result.References = ReferenceProvider.ProvideReferences(lines, cursorWord, cursorLine.Value, fileName);
                }

                if (flags.HasFlag(AnalysisFlags.Signature))
                {
                    result.SignatureHelp = SignatureHelpProvider.ProvideSignatureHelp(lines, lineText, cursorLine.Value, cursorColumn.Value, fileName);
                }

                if (flags.HasFlag(AnalysisFlags.Completion))
                {
                    result.Completions = CompletionProvider.ProvideCompletionItems(lines, lineText, cursorWord, cursorLine.Value, cursorColumn.Value, fullText);
                }

                if (flags.HasFlag(AnalysisFlags.AutoClose))
                {
                    var currentBlock = DocumentAnalysisEngine.FunctionBlockList
                        .FirstOrDefault(b => b.LineEnd >= cursorLine && b.LineStart <= cursorLine);
                    result.AutoCloseStatement = DocumentAnalysisEngine.GetAutoCloseStatement(lineText, currentBlock, fullText);
                }
            }

            return result;
        }
    }
}
