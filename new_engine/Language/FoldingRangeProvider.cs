using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace B4XEngineCore
{
    public static class FoldingRangeProvider
    {
        public static List<FoldingRange> ProvideFoldingRanges(string[] lines)
        {
            var ranges = new List<FoldingRange>();
            var stack = new Stack<int>();
            var blockStartRegex = new Regex(@"^(Private|Public)?\s+Sub\b|^\s*If\b.*\bThen|^\s*For\b|^\s*While\b", RegexOptions.IgnoreCase);
            var blockEndRegex = new Regex(@"^\s*\b(End\s+(Sub|If|While)|Next)\b", RegexOptions.IgnoreCase);
            var commentRegex = new Regex(@"^\s*'");

            for (int i = 0; i < lines.Length; i++)
            {
                string text = lines[i];
                if (commentRegex.IsMatch(text)) continue;
                if (blockStartRegex.IsMatch(text))
                    stack.Push(i);
                else if (blockEndRegex.IsMatch(text) && stack.Count > 0)
                    ranges.Add(new FoldingRange(stack.Pop(), i - 1));
            }

            return ranges;
        }
    }
}
