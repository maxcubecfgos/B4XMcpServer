using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace B4XMcpServer.Engine
{
    public enum DiagnosticSeverity { Error, Warning, Information, Hint }

    public record Diagnostic(int Id, string Message, DiagnosticSeverity Severity, int Line, int Column, int Length);

    public static class DiagnosticProvider
    {
        private static readonly List<CompileWarning> CompileWarnings = new()
        {
            new(1, "Unreachable code detected.", DiagnosticSeverity.Warning),
            new(2, "Not all code paths return a value.", DiagnosticSeverity.Warning),
            new(3, "Return type (in Sub signature) should be set explicitly.", DiagnosticSeverity.Warning),
            new(4, "Return value is missing. Default value will be used instead.", DiagnosticSeverity.Warning),
            new(5, "Variable declaration type is missing. String type will be used.", DiagnosticSeverity.Warning),
            new(6, "The following value misses screen units ('dip' or %x / %y): {0}.", DiagnosticSeverity.Warning),
            new(7, "Object converted to String. This is probably a programming mistake.", DiagnosticSeverity.Warning),
            new(8, "Undeclared variable '{0}'.", DiagnosticSeverity.Error),
            new(9, "Unused variable '{0}'.", DiagnosticSeverity.Hint),
            new(10, "Variable '{0}' is never assigned any value.", DiagnosticSeverity.Hint),
            new(11, "Variable '{0}' was not initialized.", DiagnosticSeverity.Warning),
            new(12, "Sub '{0}' is not used.", DiagnosticSeverity.Hint),
            new(13, "Variable '{0}' should be declared in Sub Process_Globals.", DiagnosticSeverity.Warning),
            new(14, "File '{0}' in Files folder was not added to the Files tab. You should either delete it or add it to the project.", DiagnosticSeverity.Warning),
            new(15, "File '{0}' is not used.", DiagnosticSeverity.Hint),
            new(16, "Layout file '{0}' is not used. Are you missing a call to Activity.LoadLayout?", DiagnosticSeverity.Warning),
            new(17, "File '{0}' is missing from the Files tab.", DiagnosticSeverity.Error),
            new(18, "TextSize value should not be scaled as it is scaled internally.", DiagnosticSeverity.Warning),
            new(19, "Empty Catch block. You should at least add Log(LastException.Message).", DiagnosticSeverity.Warning),
            new(20, "View '{0}' was added with the designer. You should not initialize it.", DiagnosticSeverity.Warning),
            new(21, "Cannot access view's dimension before it is added to its parent.", DiagnosticSeverity.Warning),
            new(22, "Types do not match.", DiagnosticSeverity.Error),
            new(23, "Dialogs are not allowed in Sub Activity_Pause. It will be ignored.", DiagnosticSeverity.Warning),
            new(24, "Accessing fields from other modules in Sub Process_Globals can be dangerous as the initialization order is not deterministic.", DiagnosticSeverity.Warning),
            new(25, "Sub '{0}' not found.", DiagnosticSeverity.Error),
            new(26, "Add android:targetSdkVersion=\"19\" to the manifest editor (after minSdkVersion).", DiagnosticSeverity.Warning),
            new(27, "AndroidManifest.xml is read-only or Do not overwrite manifest file option is checked. Use the manifest editor instead.", DiagnosticSeverity.Warning),
            new(28, "It is recommended to use a custom theme or the default theme. Remove SetApplicationAttribute(android:theme, \"@android:style/Theme.Holo\") from the manifest editor.", DiagnosticSeverity.Warning),
            new(29, "This sub should only be used for variables declaration or assignments of primitive values.", DiagnosticSeverity.Warning),
            new(30, "Variable name is the same as a module name. This can cause problems during debugging.", DiagnosticSeverity.Warning),
            new(31, "The recommended value for android:targetSdkVersion is {0} (manifest editor).", DiagnosticSeverity.Warning),
            new(32, "Library '{0}' is not used.", DiagnosticSeverity.Hint),
            new(33, "DoEvents is deprecated. It can lead to stability issues. Use Sleep(0) instead (if really needed).", DiagnosticSeverity.Warning),
            new(34, "Msgbox and other modal dialogs are deprecated. Use the async methods instead.", DiagnosticSeverity.Warning),
            new(35, "Comparison of Object to other types will fail if exact types do not match. Better to put the object on the right side of the comparison.", DiagnosticSeverity.Warning),
            new(36, "Event parameter is missing.", DiagnosticSeverity.Warning),
            new(37, "It is recommended to remove the Starter service in B4XPages projects.", DiagnosticSeverity.Warning),
        };

        private static readonly List<CompileWarning> RuntimeWarnings = new()
        {
            new(1001, "Panel.LoadLayout should only be called after the panel was added to its parent.", DiagnosticSeverity.Warning),
            new(1002, "The same object was added to the list. You should call Dim again to create a new object.", DiagnosticSeverity.Warning),
            new(1003, "Object was already initialized.", DiagnosticSeverity.Warning),
            new(1004, "FullScreen or IncludeTitle properties in layout file do not match the activity attributes settings.", DiagnosticSeverity.Warning),
        };

        public static Diagnostic? GetWarningById(int id)
        {
            var all = new List<CompileWarning>();
            all.AddRange(CompileWarnings);
            all.AddRange(RuntimeWarnings);
            var found = all.Find(w => w.Id == id);
            return found != null ? new Diagnostic(found.Id, found.Message, found.Severity, 0, 0, 0) : null;
        }

        public static string FormatMessage(int id, params string[] args)
        {
            var all = new List<CompileWarning>();
            all.AddRange(CompileWarnings);
            all.AddRange(RuntimeWarnings);
            var found = all.Find(w => w.Id == id);
            if (found == null) return $"Unknown warning #{id}";
            return args.Length > 0 ? string.Format(found.Message, args) : found.Message;
        }

        public static List<Diagnostic> AnalyzeLine(string lineText, int lineNumber)
        {
            var diagnostics = new List<Diagnostic>();

            if (Regex.IsMatch(lineText, @"\bDoEvents\b", RegexOptions.IgnoreCase))
            {
                int col = lineText.IndexOf("DoEvents", StringComparison.OrdinalIgnoreCase);
                diagnostics.Add(new Diagnostic(33, FormatMessage(33), DiagnosticSeverity.Warning, lineNumber, col, 8));
            }

            if (Regex.IsMatch(lineText, @"\bMsgbox\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(lineText, @"^\s*'", RegexOptions.IgnoreCase))
            {
                int col = lineText.IndexOf("Msgbox", StringComparison.OrdinalIgnoreCase);
                diagnostics.Add(new Diagnostic(34, FormatMessage(34), DiagnosticSeverity.Warning, lineNumber, col, 6));
            }

            return diagnostics;
        }

        public static List<Diagnostic> AnalyzeDocument(string[] lines)
        {
            var diagnostics = new List<Diagnostic>();
            for (int i = 0; i < lines.Length; i++)
                diagnostics.AddRange(AnalyzeLine(lines[i], i));
            return diagnostics;
        }

        private record CompileWarning(int Id, string Message, DiagnosticSeverity Severity);
    }
}
