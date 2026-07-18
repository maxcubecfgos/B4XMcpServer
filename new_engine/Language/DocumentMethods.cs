using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace B4XEngineCore
{
    public static class DocumentMethods
    {
        public static string GetWordAtPosition(string line, int column)
        {
            if (string.IsNullOrEmpty(line) || column < 0 || column > line.Length) return "";
            int start = column;
            while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '_')) start--;
            int end = column;
            while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_')) end++;
            return start < end ? line[start..end] : "";
        }

        public static string GetLinePrefix(string line, int column)
        {
            return column >= 0 && column <= line.Length ? line[..column] : "";
        }

        public static List<string> GetAllParentObjectMatches(string linePrefix)
        {
            var matches = new List<string>();
            if (Regex.IsMatch(linePrefix, @"[\s\S]+\.[\w]*", RegexOptions.IgnoreCase))
            {
                var m = Regex.Matches(linePrefix, @"(\w+)\.", RegexOptions.IgnoreCase);
                foreach (Match match in m)
                    matches.Add(match.Value);
            }
            return matches;
        }

        public static bool IsNamingDeclaration(string linePrefix)
        {
            var matches = Regex.Matches(linePrefix, @"\w+|=", RegexOptions.IgnoreCase);
            bool retVal = false;
            foreach (Match match in matches)
            {
                string lower = match.Value.ToLowerInvariant();
                if (lower is "dim" or "private" or "public" or "const" or "sub")
                    retVal = true;
                if (retVal && match.Value == "=")
                    retVal = false;
            }
            return retVal;
        }

        public static bool IsDeclaringTypeName(string linePrefix)
        {
            var matches = Regex.Matches(linePrefix, @"\w+|=", RegexOptions.IgnoreCase);
            if (matches.Count == 0) return false;
            return matches[^1].Value.Equals("as", StringComparison.OrdinalIgnoreCase);
        }
    }
}
