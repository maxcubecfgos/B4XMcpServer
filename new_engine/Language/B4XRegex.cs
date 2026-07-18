using System.Text.RegularExpressions;

namespace B4XEngineCore
{
    public static partial class B4XRegex
    {
        public const string EndOfWord = @"\b(?<=\w)";
        public const string StartOfWord = @"\b(?=\w)";
        public const string StartOfSub = @"(?:^|\r|\n)\s*(Public\s+|Private\s+)?\bSub\s+(\w+)\b.*(?:$|\r|\n)";
        public const string EndOfSub = @"(?:^|\r|\n)\s*\bEnd\s+Sub\b.*(?:$|\r|\n)";
        public const string StartOfIf = @"(?:^|\r|\n)[ \t]*\bIf[ \t]+\w+\b.*\bThen\b.*(?:$|\r|\n)";
        public const string InlineIf = @"(?:^|\r|\n)[ \t]*\bIf[ \t]+\w+\b.*\bThen[ \t]+\w+\b.*(?:$|\r|\n)";
        public const string EndOfIf = @"(?:^|\r|\n)[ \t]*\bEnd[ \t]+If\b.*(?:$|\r|\n)";
        public const string StartOfFor = @"(?:^|\r|\n)\s*\b(For\s+Each|For)\s+\w+\b.*(?:$|\r|\n)";
        public const string EndOfFor = @"(?:^|\r|\n)\s*\bNext\b.*(?:$|\r|\n)";
        public const string StartOfSelect = @"(?:^|\r|\n)\s*\b(Select Case)\s+\w+\b.*(?:$|\r|\n)";
        public const string EndOfSelect = @"(?:^|\r|\n)\s*\bEnd\s+Select\b.*(?:$|\r|\n)";
        public const string StartOfTry = @"(?:^|\r|\n)\s*\b(Try)\b.*(?:$|\r|\n)";
        public const string EndOfTry = @"(?:^|\r|\n)\s*\bEnd\s+Try\b.*(?:$|\r|\n)";

        public static string VariableMatchPattern(string word) => $@"{StartOfWord}{word} As (\w+)";
        public static string DeclarationMatchPattern(string word) => $@"(?:Dim|Public|Private|Const|For Each) {word}";
        public static string FunctionMatchPattern(string word) => $@"Sub {word}{EndOfWord}";

        public static string VariableDeclarationGlobPattern => $@"{StartOfWord}(\w+) +As +(\w+){EndOfWord}";

        public static RegexOptions DefaultFlags => RegexOptions.IgnoreCase | RegexOptions.Multiline;
    }
}
