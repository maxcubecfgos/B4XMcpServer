using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using B4XContext.Services;

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

            var (output, _) = RunAdb(adb, "devices -l");
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

            var deviceArg = string.IsNullOrEmpty(deviceSerial) ? "" : $"-s {deviceSerial} ";
            var (output, _) = RunAdb(adb, $"{deviceArg}logcat -d -t {lines} -s B4A:V");
            return JsonSerializer.Serialize(new { lines, output });
        }

        private static (string output, int exitCode) RunAdb(string adbPath, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) throw new Exception("Failed to start adb.exe process.");

            var sb = new StringBuilder();
            sb.Append(proc.StandardOutput.ReadToEnd());
            sb.Append(proc.StandardError.ReadToEnd());
            proc.WaitForExit(15000);
            return (sb.ToString(), proc.ExitCode);
        }
    }
}