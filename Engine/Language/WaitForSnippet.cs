namespace B4XMcpServer.Engine
{
    public static class WaitForSnippet
    {
        public const string Template = "Wait For ($ResumableSub$) Complete ($Result$ As $ResultType$)";

        public static CompletionItem GetSnippetCompletion()
        {
            return new CompletionItem(
                "Wait For",
                CompletionItemKind.Keyword,
                "Wait For ($ResumableSub$) Complete ($Result$ As $ResultType$)",
                "Creates a resumable sub await pattern.\nTemplate: Wait For (ResumableSub) Complete (Result As ResultType)\nPlaceholders: $ResumableSub$ = sub name, $Result$ = result variable, $ResultType$ = type."
            );
        }

        public static string FormatSnippet(string resumableSub, string resultVar, string resultType)
        {
            return $"Wait For ({resumableSub}) Complete ({resultVar} As {resultType})";
        }
    }
}
