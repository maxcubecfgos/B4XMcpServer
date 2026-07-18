using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace B4XMcpServer.Engine
{
    public enum TokenType { Keyword, Operator, StringLiteral, Identifier, NumberLiteral, LineEnd }

    public record Token(TokenType Type, string Value, int Line, int Col);

    public record ControlPosition(int Left, int Top, int Width, int Height, bool Visible, double TextSize, string Text, string Image, int HAnchor, int VAnchor, string ParentName, int RawLeft, int RawTop, int RawWidth, int RawHeight);

    public record ScriptResults(Dictionary<string, Dictionary<string, object>> Changes, string? Error = null, List<string>? Warnings = null);

    public partial class ScriptEngine
    {
        private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
        { "if", "then", "else", "else if", "end if", "and", "or", "true", "false", "mod" };

        private static readonly HashSet<string> Builtins = new(StringComparer.OrdinalIgnoreCase)
        { "autoscalerate", "autoscaleall", "portrait", "landscape", "activitysize" };

        private static readonly HashSet<char> OperatorChars = new() { '=', '+', '-', '*', '/', '(', ')', '.', '<', '>', ',', '^' };

        private static readonly HashSet<string> TextSupportingTypes = new(StringComparer.OrdinalIgnoreCase)
        { "metalabel", "metabutton", "metatextfield", "metatextview" };

        private static readonly HashSet<string> ImageSupportingTypes = new(StringComparer.OrdinalIgnoreCase)
        { "metaimageview" };

        private static readonly Dictionary<string, string> PropertyTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = "number", ["top"] = "number", ["width"] = "number", ["height"] = "number",
            ["right"] = "number", ["bottom"] = "number", ["horizontalcenter"] = "number", ["verticalcenter"] = "number",
            ["textsize"] = "number", ["text"] = "string", ["image"] = "string", ["visible"] = "bool",
            ["setleftandright"] = "number", ["settopandbottom"] = "number",
        };

        private static readonly Dictionary<string, int> MethodArgCount = new(StringComparer.OrdinalIgnoreCase)
        {
            ["setleftandright"] = 2, ["settopandbottom"] = 2,
        };

        public static List<List<Token>> Tokenize(string script)
        {
            var lines = script.Split('\n');
            var result = new List<List<Token>>();

            for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
            {
                string raw = lines[lineIdx];
                int commentPos = FindCommentStart(raw);
                string line = commentPos >= 0 ? raw[..commentPos] : raw;
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                var tokens = new List<Token>();
                int i = 0;
                while (i < line.Length)
                {
                    if (line[i] == ' ' || line[i] == '\t') { i++; continue; }
                    int col = i;

                    if (line[i] == '"')
                    {
                        i++;
                        var sb = new StringBuilder();
                        while (i < line.Length && line[i] != '"') sb.Append(line[i++]);
                        if (i < line.Length) i++;
                        tokens.Add(new Token(TokenType.StringLiteral, sb.ToString(), lineIdx, col));
                        continue;
                    }

                    if (char.IsDigit(line[i]) || (line[i] == '.' && i + 1 < line.Length && char.IsDigit(line[i + 1])))
                    {
                        var sb = new StringBuilder();
                        while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.')) sb.Append(line[i++]);
                        if (i < line.Length && (line[i] == 'e' || line[i] == 'E'))
                        {
                            sb.Append(line[i++]);
                            if (i < line.Length && (line[i] == '+' || line[i] == '-')) sb.Append(line[i++]);
                            while (i < line.Length && char.IsDigit(line[i])) sb.Append(line[i++]);
                        }
                        if (i + 3 <= line.Length && line[i..(i + 3)].Equals("dip", StringComparison.OrdinalIgnoreCase))
                        {
                            sb.Append("dip"); i += 3;
                        }
                        tokens.Add(new Token(TokenType.NumberLiteral, sb.ToString(), lineIdx, col));
                        continue;
                    }

                    if (OperatorChars.Contains(line[i]))
                    {
                        if (i + 1 < line.Length)
                        {
                            string two = line[i..(i + 2)];
                            if (two is "<>" or ">=" or "<=" or "=>" or "=<")
                            {
                                tokens.Add(new Token(TokenType.Operator, two, lineIdx, col));
                                i += 2; continue;
                            }
                        }
                        tokens.Add(new Token(TokenType.Operator, line[i].ToString(), lineIdx, col));
                        i++; continue;
                    }

                    if (char.IsLetter(line[i]) || line[i] == '_')
                    {
                        var sb = new StringBuilder();
                        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) sb.Append(line[i++]);
                        string word = sb.ToString();
                        string lower = word.ToLowerInvariant();

                        if (lower is "else" or "end")
                        {
                            int j = i;
                            while (j < line.Length && (line[j] == ' ' || line[j] == '\t')) j++;
                            if (j + 2 <= line.Length && line[j..(j + 2)].Equals("if", StringComparison.OrdinalIgnoreCase))
                            {
                                tokens.Add(new Token(TokenType.Keyword, lower + " if", lineIdx, col));
                                i = j + 2; continue;
                            }
                        }

                        if (Keywords.Contains(lower))
                            tokens.Add(new Token(TokenType.Keyword, lower, lineIdx, col));
                        else
                            tokens.Add(new Token(TokenType.Identifier, word, lineIdx, col));
                        continue;
                    }
                    i++;
                }

                if (tokens.Count > 0)
                {
                    tokens.Add(new Token(TokenType.LineEnd, "", lineIdx, line.Length));
                    result.Add(tokens);
                }
            }
            return result;
        }

        private static int FindCommentStart(string line)
        {
            bool inStr = false;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') inStr = !inStr;
                else if (line[i] == '\'' && !inStr) return i;
            }
            return -1;
        }
    }
}
