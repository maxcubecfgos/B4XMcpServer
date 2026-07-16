using B4XMcpServer.Repositories;
using B4XMcpServer.Services;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Text.Json;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class DeviceTools
    {
        public DeviceTools(IFileRepository fileRepository, IProjectRepository projectRepository)
        {
        }

        [McpServerTool, Description("Lists Android devices/emulators currently connected and visible to ADB.")]
        public async Task<string> ListDevices()
        {
            var adb = AdbLocator.LocateAdb();
            if (adb == null)
                throw new Exception("adb.exe not found. Set ANDROID_HOME or ANDROID_SDK_ROOT, or install the Android SDK platform-tools.");

            var result = await RunAdbAsync(adb, new[] { "devices", "-l" });
            return JsonSerializer.Serialize(new { raw = result.Output });
        }

        [McpServerTool, Description("Returns recent Android logcat output filtered to the 'B4A' tag (where B4A's Log() calls and unhandled exceptions show up). Use this to catch runtime crashes/errors that a compile-time check can't see — the natural complement to compile_project.")]
        public async Task<string> GetLogcat(
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
            var result = await RunAdbAsync(adb, args);
            return JsonSerializer.Serialize(new { lines, output = result.Output });
        }

        private static async Task<ProcessRunner.Result> RunAdbAsync(string adbPath, IEnumerable<string> arguments)
        {
            return await ProcessRunner.RunAsync(adbPath, arguments, timeoutMilliseconds: 15000);
        }
    }
}