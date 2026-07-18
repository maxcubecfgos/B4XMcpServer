using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using B4XMcpServer.Engine;
using B4XMcpServer.Repositories;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace B4XMcpServer.Tools.Layout
{
    [McpServerToolType]
    public sealed class LayoutCodeTools
    {
        private readonly IFileRepository _fileRepository;

        // Regex timeout protects against catastrophic backtracking on untrusted input.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        public LayoutCodeTools(IFileRepository fileRepository)
        {
            _fileRepository = fileRepository;
        }

        [McpServerTool, Description("Generates B4X code for a layout control: inserts a Dim declaration in the appropriate Globals section and/or appends an event Sub skeleton at the end of the file. Reads the control type from the layout to produce correct type annotations (e.g. Private btn As B4XView). Creates .bak backup before modifying.")]
        public string GenerateCodeFromLayout(
            [Description("Absolute path to the .bal or .bjl layout file")] string layoutPath,
            [Description("Control name from the layout (e.g. 'Button1', 'lblCounter')")] string controlName,
            [Description("Absolute path to the target .bas or .b4a/.b4j file to insert code into")] string sourcePath,
            [Description("What to generate: 'dim' (declaration only), 'event' (Sub skeleton only), or 'both' (default)")] string generate = "both")
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));
            PathSecurity.ValidateAbsolutePath(sourcePath, nameof(sourcePath));

            if (!_fileRepository.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");
            if (!_fileRepository.Exists(sourcePath))
                throw new FileNotFoundException($"Source file not found: {sourcePath}");

            generate = generate.ToLowerInvariant();

            var layout = LayoutHelpers.LoadLayout(_fileRepository, layoutPath);
            var control = LayoutHelpers.FindControl(layout, controlName);
            if (control == null)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Control '{controlName}' not found in layout.",
                    hint = "Use list_layout_controls to see all control names."
                }, JsonOptions.Default);

            string javaType = PropertyModel.GetStr(control, "javaType", "");
            string csType = PropertyModel.GetStr(control, "csType", "");
            string controlType = LayoutHelpers.GetShortTypeName(javaType);
            string eventName = PropertyModel.GetStr(control, "eventName", controlName);

            string raw = _fileRepository.ReadTextWithHeader(sourcePath);

            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            string header = markerIdx >= 0 ? raw.Substring(0, markerIdx + marker.Length) : "";
            string source = markerIdx >= 0 ? raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n') : raw;
            var lines = source.Replace("\r\n", "\n").Split('\n').ToList();

            var results = new List<object>();
            int linesAdded = 0;

            if (generate == "dim" || generate == "both")
            {
                string dimCode = $"Private {controlName} As {controlType}";

                var existingPattern = new Regex($@"^\s*(?:Private|Dim)\s+{Regex.Escape(controlName)}\s+As\s+\S+",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline, RegexTimeout);
                var existingMatch = existingPattern.Match(source);

                if (existingMatch.Success)
                {
                    results.Add(new { action = "dim", status = "already_exists", existing = existingMatch.Value.Trim() });
                }
                else
                {
                    int insertLine = FindGlobalsInsertionLine(lines);
                    if (insertLine >= 0)
                    {
                        lines.Insert(insertLine + 1, $"\t{dimCode}");
                        linesAdded++;
                        results.Add(new { action = "dim", status = "inserted", line = insertLine + 2, code = dimCode });
                    }
                    else
                    {
                        results.Add(new { action = "dim", status = "failed", error = "Could not find Sub Globals, Sub Process_Globals, or Sub Class_Globals" });
                    }
                }
            }

            if (generate == "event" || generate == "both")
            {
                var events = GetDefaultEvents(controlType);

                foreach (var ev in events)
                {
                    string subName = $"{eventName}_{ev.Name}";
                    string subCode = ev.Parameters != null
                        ? $"Sub {subName}({ev.Parameters})\n\t\nEnd Sub"
                        : $"Sub {subName}\n\t\nEnd Sub";

                    var existingSub = new Regex($@"^\s*(?:Private\s+)?Sub\s+{Regex.Escape(subName)}\b",
                        RegexOptions.IgnoreCase | RegexOptions.Multiline, RegexTimeout);

                    if (existingSub.IsMatch(string.Join("\n", lines)))
                    {
                        results.Add(new { action = "event", eventName = ev.Name, subName, status = "already_exists" });
                        continue;
                    }

                    lines.Add("");
                    lines.Add(subCode);
                    linesAdded++;
                    results.Add(new { action = "event", eventName = ev.Name, subName, status = "appended", code = subCode });
                }
            }

            string backupPath = _fileRepository.BackupPath(sourcePath) ?? (sourcePath + ".bak");

            var updatedSource = string.Join("\n", lines);
            var finalContent = markerIdx >= 0 ? header + "\n" + updatedSource : updatedSource;
            _fileRepository.WriteText(sourcePath, finalContent);

            return JsonSerializer.Serialize(new
            {
                success = true,
                sourcePath,
                backup = backupPath,
                controlName,
                controlType,
                eventPrefix = eventName,
                changes = results,
                totalLinesAdded = linesAdded
            }, JsonOptions.Default);
        }

        private static int FindGlobalsInsertionLine(List<string> lines)
        {
            var globalsPattern = new Regex(@"^\s*Sub\s+(Class_Globals|Process_Globals|Globals)\b", RegexOptions.IgnoreCase, RegexTimeout);
            int bestLine = -1;
            int bestPriority = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var match = globalsPattern.Match(lines[i]);
                if (!match.Success) continue;

                int priority = match.Groups[1].Value.ToLowerInvariant() switch
                {
                    "class_globals" => 3,
                    "process_globals" => 2,
                    "globals" => 1,
                    _ => 0
                };

                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    bestLine = i;
                }
            }

            return bestLine;
        }

        private class EventDef
        {
            public string Name { get; set; } = "";
            public string? Parameters { get; set; }
        }

        private static List<EventDef> GetDefaultEvents(string controlType)
        {
            return controlType.ToLowerInvariant() switch
            {
                "button" => new List<EventDef> { new() { Name = "Click" } },
                "label" => new List<EventDef> { new() { Name = "Click" } },
                "edittext" => new List<EventDef>
                {
                    new() { Name = "TextChanged", Parameters = "Old As String, New As String" },
                    new() { Name = "EnterPressed" }
                },
                "checkbox" => new List<EventDef> { new() { Name = "CheckedChange", Parameters = "Checked As Boolean" } },
                "panel" => new List<EventDef> { new() { Name = "Click" }, new() { Name = "Touch", Parameters = "Action As Int, X As Float, Y As Float" } },
                "imageview" => new List<EventDef> { new() { Name = "Click" } },
                "switch" => new List<EventDef> { new() { Name = "CheckedChange", Parameters = "Checked As Boolean" } },
                "slider" => new List<EventDef> { new() { Name = "ValueChanged", Parameters = "Value As Int" } },
                "spinner" => new List<EventDef> { new() { Name = "ItemClick", Parameters = "Position As Int, Value As Object" } },
                "scrollview" => new List<EventDef> { new() { Name = "ScrollChanged", Parameters = "Position As Int" } },
                _ => new List<EventDef>()
            };
        }
    }
}
