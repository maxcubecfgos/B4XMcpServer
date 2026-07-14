using ModelContextProtocol.Server;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class LanguageTools
    {
        [McpServerTool, Description("Returns critical B4A/B4J language gotchas and pitfalls that frequently cause hard-to-debug bugs. Call this when starting work on a B4X project or when encountering unexpected behavior. Covers: case-insensitivity, variable shadowing, File.Exists with DirAssets, reserved keywords (Is, Rnd, ATan2), Color component extraction, Application_Error pitfalls, B4XView API, project file structure rules, and more.")]
        public static string GetLanguageGotchas()
        {
            var gotchas = new[]
            {
                new
                {
                    title = "The .b4a/.b4j file has TWO sections: METADATA and CODE — never mix them",
                    severity = "CRITICAL",
                    description = "Every .b4a/.b4j file has a PROJECT METADATA section (NumberOfModules, Module1=Starter, Library1=core, ManifestCode=, Build1=, etc.) and a SOURCE CODE section (Subs, Types, #Region blocks). Never put code in the metadata section, never put metadata in the code section.",
                    example = "DON'T put AddManifestText in the code. DON'T put Subs or #Region Project Attributes in the metadata section. DO keep them strictly separated.",
                    fix = "Project metadata (Library1=, Module1=, ManifestCode=) stays in its section — use enable_library/disable_library to modify libraries. #Region Project Attributes and #Region Activity Attributes stay at the top of the source code section. #Region Manifest Editor stays in the metadata section."
                },
                new
                {
                    title = "ALWAYS verify method signatures with get_core_api — never guess",
                    severity = "HIGH",
                    description = "B4X core types have specific method names and signatures. Common mistakes: List.Add() is correct but List.AddAll() not AddRange(), String.Length() is a method not a property, Map.Get() not Map.GetValue(). Use get_core_api to verify before writing code.",
                    example = "DON'T: list.AddRange(items), string.Length, map.GetValue(key). DO: list.AddAll(items), string.Length(), map.Get(key).",
                    fix = "Call get_core_api(typeName='List') or get_core_api() to see all signatures. Never guess a B4X method name — verify it first."
                },
                new
                {
                    title = "Designer script properties: use controlName.Property, not controlName.SetProperty",
                    severity = "MEDIUM",
                    description = "In designer scripts (AutoScaleAll, etc.), control properties are accessed via dot notation: Button1.Width, Label1.TextSize. Available properties: Left, Top, Width, Height, Right, Bottom, HorizontalCenter, VerticalCenter, Visible, TextSize, Text, Image. Also available: _c.DipToCurrent, _c.PerXToCurrent, _c.PerYToCurrent.",
                    example = "Button1.Width = 50%x / Label1.TextSize = _c.DipToCurrent(16) / _c.SetLeftAndRight(Button1, 10%x, 90%x)",
                    fix = "Designer scripts go in the scriptData section of the layout JSON. Use get_layout_structure to see existing scripts, write_layout to update them."
                },
                new
                {
                    title = "NEVER modify, move, or delete the #Region Project Attributes or #Region Activity Attributes blocks",
                    severity = "CRITICAL",
                    description = "The #Region Project Attributes and #Region Activity Attributes blocks at the top of the source code section are SACRED. They contain #ApplicationLabel, #VersionCode, #VersionName, #FullScreen, #IncludeTitle — essential IDE settings. Touching them corrupts the project and breaks compilation.",
                    example = "DON'T: write_file replacing the entire file. DON'T: delete or move these regions. DO: Leave them exactly as they are at the top of the source code section.",
                    fix = "These regions are untouchable. The only safe way to modify code is via edit_sub on specific Subs. Never replace the whole file."
                },
                new
                {
                    title = "NEVER create Main.bas unless the project already has one",
                    severity = "CRITICAL",
                    description = "Most B4X projects have the main activity code inside the .b4a/.b4j file itself, NOT in a separate Main.bas. Check get_project_structure first — if Main.bas is not listed, the main code goes in the project file's source code section.",
                    example = "DON'T: write_file('Main.bas', ...) when Main.bas doesn't exist in the project. DO: use edit_sub or write_file on the .b4a/.b4j file's source code section.",
                    fix = "Always call get_project_structure first. If Main.bas is not in the file list, put all Activity_Create, Process_Globals, etc. directly in the .b4a/.b4j file."
                },
                new
                {
                    title = "NEVER put Manifest Editor blocks inside the source code section",
                    severity = "CRITICAL",
                    description = "The #Region Manifest Editor block belongs in the PROJECT METADATA section, NEVER in the source code section. Putting it in the code corrupts the file and breaks compilation.",
                    example = "DON'T write #Region Manifest Editor in the source code. DO use write_manifest tool to modify the manifest safely.",
                    fix = "Use the write_manifest tool to modify the Android manifest. Never manually add #Region Manifest Editor blocks to the source code."
                },
                new
                {
                    title = "NEVER read, modify, or worry about Starter.bas",
                    severity = "CRITICAL",
                    description = "Starter.bas is a system service module that handles app lifecycle (Service_Create, Service_Start, Application_Error). It NEVER needs to be read, modified, or worried about. It is hidden from get_project_structure for this reason.",
                    example = "DON'T: get_file_content('Starter.bas'), analyze_module('Starter.bas'), or worry about Module1=Starter. DO: Ignore it completely — it just works.",
                    fix = "Never call get_file_content, analyze_module, edit_sub, or any tool on Starter.bas. Module1=Starter in the project metadata is required and normal."
                },
                new
                {
                    title = "ALWAYS call compile_project after making changes — NEVER assume code compiles",
                    severity = "CRITICAL",
                    description = "The ONLY way to verify your code works is to call compile_project. The builder catches errors you can't see. If compile_project returns errors, read them carefully and fix exactly what they say — do NOT run shell commands like dir, cd, type, or try to invoke the builder manually.",
                    example = "DON'T: 'The code looks correct, moving on.' DON'T: run B4ABuilder.exe, dir, or cat to debug. DO: Call compile_project, read the output, fix errors, repeat until success.",
                    fix = "After any code change, immediately call compile_project. If it returns ❌ COMPILATION FAILED, read each error (file + line + message) and fix only those specific issues with write_file or edit_sub, then compile again."
                },
                new
                {
                    title = "B4X is completely case-insensitive",
                    severity = "CRITICAL",
                    description = "Variable names differing only in capitalization are THE SAME variable. A local Dim with the same name as a module global (even different case) silently overwrites the global reference.",
                    example = "In DataModule, 'Dim towerList As List' collides with module global 'TowerList'. Calling towerList.Initialize destroys TowerList content too.",
                    fix = "Always use clearly distinct names for local variables vs module globals. E.g. use 'midTowers' instead of 'towerList' when 'TowerList' exists as a global."
                },
                new
                {
                    title = "Application_Error returning True suppresses all exceptions",
                    severity = "CRITICAL",
                    description = "If Application_Error (in Starter.bas) returns True, ALL runtime exceptions are silently swallowed. Bugs become invisible with no crash, no log, just weird behavior.",
                    example = "A NullPointerException in a render loop never shows - Application_Error eats it and the UI just stops updating.",
                    fix = "During development, set Application_Error to return False so crashes are visible. Only return True in production if you have proper logging."
                },
                new
                {
                    title = "B4XView properties: use .Text, .SetColorAndBorder, .SetLayoutAnimated — NOT .Color, .Background, .SetOnClickListener with wrong signatures",
                    severity = "HIGH",
                    description = "B4XView has specific methods. Common mistakes: using .Color instead of .SetColorAndBorder, .Background instead of .SetBitmap, .IsInitialized (doesn't exist on B4XView), .ALIGNMENT_CENTER (use 'CENTER' string), .SetTextAlignment (doesn't exist).",
                    example = "WRONG: btn.Color = xui.Color_Red / RIGHT: btn.SetColorAndBorder(xui.Color_Red, 0, xui.Color_Red, 12dip)",
                    fix = "Use get_library_docs(libraryName='XUI', typeName='B4XView') to see the exact API. Key methods: .Text, .SetColorAndBorder(color, borderWidth, borderColor, cornerRadius), .SetLayoutAnimated(duration, left, top, width, height)."
                },
                new
                {
                    title = "File.Exists does NOT work with File.DirAssets",
                    severity = "HIGH",
                    description = "File.Exists(File.DirAssets, filename) always returns False. Assets are bundled inside the APK and cannot be stat'd — only accessed directly.",
                    example = "If File.Exists(File.DirAssets, 'config.json') Then ... — this always skips, even if the file IS in the Files folder.",
                    fix = "Use Try-Catch when loading assets, or maintain a hardcoded list of known asset names. Never guard asset access with File.Exists."
                },
                new
                {
                    title = "Reserved keywords: Is, ATan2, Rnd",
                    severity = "HIGH",
                    description = "B4X has keywords that look like valid identifiers but are reserved: 'Is' (type-check operator), 'ATan2' (math function), 'Rnd' (random function). Using them as variable/Sub names causes compile errors.",
                    example = "Sub IsReady() or Dim IsActive As Boolean or Dim Rnd As Int — all compile errors.",
                    fix = "Avoid: Is*, ATan2*, Rnd* as identifiers. Use alternatives: IsOk -> Ready, Rnd -> RandVal, etc."
                },
                new
                {
                    title = "NEVER use shell commands — use MCP tools instead",
                    severity = "CRITICAL",
                    description = "Shell commands (dir, cd, type, cat, ls, B4ABuilder.exe, &&, ;, etc.) DO NOT WORK in this environment. The only way to read files is get_file_content, the only way to write is write_file/edit_sub, and the ONLY way to compile is compile_project.",
                    example = "DON'T: type new.b4a, dir, cd && B4ABuilder.exe, ls -la. DO: get_file_content, compile_project, get_project_structure.",
                    fix = "Use the MCP tools provided. If compile_project fails, READ the error message it returns — it contains everything you need to fix the problem."
                },
                new
                {
                    title = "Colors.R/G/B/A component extraction does NOT exist",
                    severity = "MEDIUM",
                    description = "B4X does NOT have Colors.R(), Colors.G(), Colors.B(), Colors.A() functions to extract individual color channels from an Int color.",
                    example = "Dim r As Int = Colors.R(someColor) — compile error.",
                    fix = "Use bit operations: R = Bit.And(Bit.ShiftRight(color, 16), 0xFF), G = Bit.And(Bit.ShiftRight(color, 8), 0xFF), B = Bit.And(color, 0xFF), A = Bit.UnsignedShiftRight(color, 24)."
                },
                new
                {
                    title = "Parameter name must not shadow a module Global",
                    severity = "MEDIUM",
                    description = "If a Sub parameter has the same name as a module-level global (even with different case), it causes unexpected shadowing or compile errors because B4X is case-insensitive.",
                    example = "Sub ProcessData(data As List) in a module that has 'Private Data As List' declared in Process_Globals.",
                    fix = "Always use distinct parameter names that don't match any variable declared in Process_Globals, Globals, or Class_Globals of the same module."
                },
                new
                {
                    title = "Type declarations: fields must be Dim, not Public/Private",
                    severity = "MEDIUM",
                    description = "Inside a Type...End Type block, all fields are public by default and declared with Dim only. Using Public or Private on Type fields is a compile error.",
                    example = "Type Point: Public x As Int — WRONG. Type Point: Dim x As Int — CORRECT.",
                    fix = "Use 'Dim fieldName As Type' for all fields inside Type blocks. No visibility modifiers."
                },
                new
                {
                    title = "For Each on String iterates characters, not lines",
                    severity = "LOW",
                    description = "For Each c As Char In someString iterates individual characters, not lines. To iterate lines, use Split.",
                    example = "For Each line As String In text — if text is a String, this iterates chars, causing type mismatch or unexpected behavior.",
                    fix = "To iterate lines: For Each line As String In Regex.Split(text, CRLF). The CRLF constant is Chr(13) & Chr(10) (defined by the B4X IDE)."
                },
                new
                {
                    title = "Sleep() is blocking — use with care in UI code",
                    severity = "MEDIUM",
                    description = "Sleep(milliseconds) blocks the current Sub entirely for that duration. While sleeping, the UI is frozen. This is fine in non-UI code or very short delays (<100ms), but long Sleep in event handlers freezes the app.",
                    example = "Sub Button_Click: Sleep(2000) — app freezes for 2 seconds. Better: use a Timer for delayed actions.",
                    fix = "For UI delays > 200ms, use a B4XView Timer or CallSubDelayed pattern instead of Sleep. Only use Sleep for sub-100ms animation steps."
                }
            };

            return JsonSerializer.Serialize(new
            {
                count = gotchas.Length,
                gotchas
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        [McpServerTool, Description("Returns the exact signatures for B4X core API: List, Map, Timer, String, Intent, Activity, DateTime, Bit, Regex, Matcher. Use this to verify method names, parameter types, and return types before writing code. Never guess a method signature — check it here first.")]
        public static string GetCoreApi(
    [Description("Optional: filter to a specific type. Valid values: List, Map, Timer, String, Intent, Activity, DateTime, Bit, Regex, Matcher. Omit to return all.")] string? typeName = null)
        {
            var api = new Dictionary<string, object>
            {
                ["List"] = new[] {
            "Add(item As Object)", "AddAll(list As List)", "AddAllAt(index As Int, list As List)",
            "Clear()", "Get(index As Int) As Object", "IndexOf(item As Object) As Int",
            "Initialize()", "Initialize2(array As List)", "InsertAt(index As Int, list As List)",
            "IsInitialized() As Boolean", "RemoveAt(index As Int)", "Set(index As Int, item As Object)",
            "Size As Int", "Sort(ascending As Boolean)", "SortCaseInsensitive(ascending As Boolean)",
            "SortType(fieldName As String, ascending As Boolean)", "SortTypeCaseInsensitive(fieldName As String, ascending As Boolean)"
        },
                ["Map"] = new[] {
            "Initialize()", "Put(key As Object, value As Object) As Object", "Remove(key As Object) As Object",
            "Get(key As Object) As Object", "GetDefault(key As Object, defaultValue As Object) As Object",
            "GetKeyAt(index As Int) As Object", "GetValueAt(index As Int) As Object", "Clear()",
            "ContainsKey(key As Object) As Boolean", "ContainsValue(value As Object) As Boolean",
            "Keys() As IterableList", "Values() As IterableList", "Size As Int", "IsInitialized() As Boolean"
        },
                ["Timer"] = new[] {
            "Initialize(eventName As String, interval As Long)", "IsInitialized() As Boolean",
            "Enabled As Boolean", "Interval As Long"
        },
                ["String"] = new[] {
            "Length() As Int", "IndexOf(searchFor As String) As Int", "IndexOf2(searchFor As String, index As Int) As Int",
            "LastIndexOf(searchFor As String) As Int", "LastIndexOf2(searchFor As String, index As Int) As Int",
            "Trim() As String", "SubString(beginIndex As Int) As String", "SubString2(beginIndex As Int, endIndex As Int) As String",
            "CompareTo(other As String) As Int", "EqualsIgnoreCase(other As String) As Boolean", "CharAt(index As Int) As Char",
            "StartsWith(prefix As String) As Boolean", "EndsWith(suffix As String) As Boolean",
            "Replace(target As String, replacement As String) As String", "ToLowerCase() As String",
            "Contains(searchFor As String) As Boolean", "ToUpperCase() As String", "GetBytes(charset As String) As Byte()"
        },
                ["Activity"] = new[] {
            "AddView(view As View, left As Int, top As Int, width As Int, height As Int)",
            "GetView(index As Int) As View", "RemoveAllViews()", "RemoveViewAt(index As Int)",
            "NumberOfViews As Int", "LoadLayout(layoutFile As String)",
            "AddMenuItem(title As String, eventName As String)", "Title As String", "Finish()",
            "GetAllViewsRecursive() As IterableList"
        },
                ["DateTime"] = new[] {
            "Now As Long", "Date(ticks As Long) As String", "Time(ticks As Long) As String",
            "DateFormat As String", "TimeFormat As String", "DateParse(date As String) As Long",
            "TimeParse(time As String) As Long", "DateTimeParse(date As String, time As String) As Long",
            "GetYear(ticks As Long) As Int", "GetMonth(ticks As Long) As Int", "GetDayOfMonth(ticks As Long) As Int",
            "GetHour(ticks As Long) As Int", "GetMinute(ticks As Long) As Int", "GetSecond(ticks As Long) As Int",
            "Add(ticks As Long, years As Int, months As Int, days As Int) As Long",
            "TicksPerSecond As Long", "TicksPerMinute As Long", "TicksPerHour As Long", "TicksPerDay As Long"
        },
                ["Bit"] = new[] {
            "And(n1 As Int, n2 As Int) As Int", "Or(n1 As Int, n2 As Int) As Int",
            "Xor(n1 As Int, n2 As Int) As Int", "Not(n As Int) As Int",
            "ShiftLeft(n As Int, shift As Int) As Int", "ShiftRight(n As Int, shift As Int) As Int",
            "UnsignedShiftRight(n As Int, shift As Int) As Int", "ToHexString(n As Int) As String",
            "ParseInt(value As String, radix As Int) As Int", "ArrayCopy(...)"
        },
                ["Regex"] = new[] {
            "IsMatch(pattern As String, text As String) As Boolean",
            "Replace(pattern As String, text As String, template As String) As String",
            "Split(pattern As String, text As String) As String()",
            "Matcher(pattern As String, text As String) As Matcher"
        },
                ["Matcher"] = new[] {
            "Find() As Boolean", "Group(index As Int) As String", "GroupCount As Int",
            "Match As String", "GetStart(index As Int) As Int", "GetEnd(index As Int) As Int"
        }
            };

            if (!string.IsNullOrEmpty(typeName) && api.ContainsKey(typeName))
            {
                return JsonSerializer.Serialize(new
                {
                    type = typeName,
                    signatures = api[typeName]
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            return JsonSerializer.Serialize(new
            {
                availableTypes = api.Keys.ToArray(),
                hint = "Pass typeName to get signatures for a specific type. E.g. get_core_api(typeName='List')",
                api
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}