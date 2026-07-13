using ModelContextProtocol.Server;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class LanguageTools
    {
        [McpServerTool, Description("Returns critical B4A/B4J language gotchas and pitfalls that frequently cause hard-to-debug bugs. Call this when starting work on a B4X project or when encountering unexpected behavior. Covers: case-insensitivity, variable shadowing, File.Exists with DirAssets, reserved keywords (Is, Rnd, ATan2), Color component extraction, Application_Error pitfalls, MediaPlayer issues, and more.")]
        public static string GetLanguageGotchas()
        {
            var gotchas = new[]
            {
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
                    title = "File.Exists does NOT work with File.DirAssets",
                    severity = "HIGH",
                    description = "File.Exists(File.DirAssets, filename) always returns False. Assets are bundled inside the APK and cannot be stat'd — only accessed directly.",
                    example = "If File.Exists(File.DirAssets, 'config.json') Then ... — this always skips, even if the file IS in the Files folder.",
                    fix = "Use Try-Catch when loading assets, or maintain a hardcoded/manifest list of known asset names. Never guard asset access with File.Exists."
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
    }
}