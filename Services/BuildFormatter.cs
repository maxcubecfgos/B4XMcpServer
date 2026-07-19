using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace B4XMcpServer.Services
{
    public static class BuildFormatter
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        // Common known-class names that the B4X compiler lowercases in error messages.
        // When an "Undeclared variable" error matches one of these, we can suggest the
        // correct library. Extend this map as new false-positives are discovered.
        private static readonly Dictionary<string, string[]> KnownClassHints = new(StringComparer.OrdinalIgnoreCase)
        {
            ["colors"] = new[] { "Did you mean 'Colors' (uppercase C)? The B4X compiler lowercases identifiers in errors.", "The Colors class is in the 'jFX' library — verify it's enabled with list_project_libraries." },
            ["fx"] = new[] { "The 'jFX' library may not be enabled. Run list_project_libraries to check." },
            ["xui"] = new[] { "The 'XUI' library may not be enabled. Run list_project_libraries to check." },
            ["bitmapcreator"] = new[] { "The 'XUI' library provides BitmapCreator. Verify XUI is enabled." },
            ["b4xview"] = new[] { "The 'XUI' library provides B4XView. Verify XUI is enabled." },
            ["stringutils"] = new[] { "The 'StringUtils' library may need to be enabled." },
            ["keyvaluestore"] = new[] { "The 'KeyValueStore' library may need to be enabled." },
            ["okhttputils2"] = new[] { "The 'OkHttpUtils2' library may need to be enabled." },
        };

        // Patterns that indicate a missing library rather than a real typo.
        private static readonly Regex UndeclaredVarRe = new Regex(
            @"(?i)undeclared\s+variable\s+'([^']+)'",
            RegexOptions.Compiled, RegexTimeout);

        private static readonly Regex CannotFindSymbolRe = new Regex(
            @"(?i)cannot\s+find\s+symbol",
            RegexOptions.Compiled, RegexTimeout);

        public static string Format(Dictionary<string, object?> buildResult)
        {
            if (buildResult == null) return string.Empty;
            bool success = buildResult.ContainsKey("success") && buildResult["success"] is bool b && b;
            if (success) return string.Empty;

            var sb = new StringBuilder();
            var platform = buildResult.TryGetValue("platform", out var pval) && pval != null ? pval.ToString() : "?";
            var version = buildResult.TryGetValue("version", out var vval) && vval != null ? vval.ToString() : "";
            sb.AppendLine($"## COMPILATION ERRORS ({platform} {version})\n");

            if (buildResult.TryGetValue("errors", out var errsObj) && errsObj is System.Collections.IEnumerable errs)
            {
                foreach (var o in errs)
                {
                    if (o is Dictionary<string, object?> e)
                    {
                        var mod = e.TryGetValue("module", out var mval) && mval != null ? mval.ToString() : "(unknown module)";
                        var lineInfo = e.TryGetValue("b4x_line", out var lval) && lval != null ? $"line {lval}" : "";
                        sb.AppendLine($"### {mod} {lineInfo}".Trim());
                        if (e.TryGetValue("source_line", out var src) && src != null)
                        {
                            sb.AppendLine("```b4x");
                            sb.AppendLine(src.ToString());
                            sb.AppendLine("```");
                        }
                        var message = e.TryGetValue("message", out var mmsg) && mmsg != null ? mmsg.ToString() : string.Empty;
                        sb.AppendLine($"**{message}**");
                        if (e.TryGetValue("symbol", out var sym) && sym != null) sb.AppendLine($"- symbol: {sym}");
                        if (e.TryGetValue("location", out var loc) && loc != null) sb.AppendLine($"- location: {loc}");

                        // ── Semantic hints for common false-positive patterns ──────
                        AppendSemanticHints(sb, message, e);

                        sb.AppendLine();
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Adds actionable hints when the raw compiler error matches known patterns
        /// (e.g. "Undeclared variable 'colors'" → suggest jFX library).
        /// </summary>
        private static void AppendSemanticHints(StringBuilder sb, string? message, Dictionary<string, object?> error)
        {
            if (string.IsNullOrEmpty(message)) return;

            // 1) "Undeclared variable 'X'" — check if X looks like a known class name
            var uvMatch = UndeclaredVarRe.Match(message);
            if (uvMatch.Success)
            {
                string varName = uvMatch.Groups[1].Value;
                if (KnownClassHints.TryGetValue(varName, out var hints))
                {
                    sb.AppendLine("\n💡 **Hints:**");
                    foreach (var hint in hints)
                        sb.AppendLine($"- {hint}");
                }
                else if (char.IsUpper(varName[0]))
                {
                    // The user wrote uppercase but compiler lowercased it — flag it
                    sb.AppendLine($"\n💡 **Hint:** The compiler lowercases identifiers. You wrote '{varName}' (uppercase) — is this a class name? Check that its library is enabled with list_project_libraries.");
                }
                return;
            }

            // 2) "cannot find symbol" in javac errors → likely missing library
            if (CannotFindSymbolRe.IsMatch(message))
            {
                if (error.TryGetValue("symbol", out var symObj) && symObj != null)
                {
                    string sym = symObj.ToString()!;
                    sb.AppendLine($"\n💡 **Hint:** The Java compiler cannot resolve '{sym}'. This usually means a required library is not enabled. Run list_project_libraries and compare with the libraries your code expects.");
                }
                else
                {
                    sb.AppendLine("\n💡 **Hint:** 'cannot find symbol' usually means a required library is missing. Run list_project_libraries to verify.");
                }
            }
        }
    }
}