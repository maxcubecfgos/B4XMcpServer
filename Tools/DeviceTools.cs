using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using B4XMcpServer.Services;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class DeviceTools
    {
        [McpServerTool, Description("Lists Android devices/emulators currently connected and visible to ADB.")]
        public static string ListDevices()
        {
            var adb = AdbLocator.LocateAdb();
            if (adb == null)
                throw new Exception("adb.exe not found. Set ANDROID_HOME or ANDROID_SDK_ROOT, or install the Android SDK platform-tools.");

            var (output, _) = RunAdb(adb, new[] { "devices", "-l" });
            return JsonSerializer.Serialize(new { raw = output });
        }

        [McpServerTool, Description("Returns recent Android logcat output filtered to the 'B4A' tag (where B4A's Log() calls and unhandled exceptions show up). Use this to catch runtime crashes/errors that a compile-time check can't see — the natural complement to compile_project.")]
        public static string GetLogcat(
            [Description("Number of lines to return. Default 200.")] int lines = 200,
            [Description("ADB device serial, optional — uses the first connected device if omitted.")] string? deviceSerial = null)
        {
            var adb = AdbLocator.LocateAdb();
            if (adb == null)
                throw new Exception("adb.exe not found. Set ANDROID_HOME or ANDROID_SDK_ROOT, or install the Android SDK platform-tools.");

            var args = new List<string>();
            if (!string.IsNullOrEmpty(deviceSerial))
            {
                args.Add("-s");
                args.Add(deviceSerial);
            }
            args.AddRange(new[] { "logcat", "-d", "-t", lines.ToString(), "-s", "B4A:V" });
            var (output, _) = RunAdb(adb, args);
            return JsonSerializer.Serialize(new { lines, output });
        }

        private static (string output, int exitCode) RunAdb(string adbPath, IEnumerable<string> arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = adbPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            if (proc == null) throw new Exception("Failed to start adb.exe process.");

            var sb = new StringBuilder();
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            if (!proc.WaitForExit(15000))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(2000); // give the OS a moment to finalize the exit code
                }
                catch { /* best-effort kill */ }
                sb.AppendLine("Error: adb command timed out after 15 seconds.");
            }

            int exitCode;
            try { exitCode = proc.ExitCode; }
            catch { exitCode = -1; } // process may not have a valid exit code after a forced kill

            return (sb.ToString(), exitCode);
        }
    }
}