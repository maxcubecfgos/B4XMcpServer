using System;
using System.IO;

namespace B4XMcpServer.Services
{
    public static class AdbLocator
    {
        public static string? LocateAdb()
        {
            foreach (var envVar in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
            {
                var sdk = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrEmpty(sdk))
                {
                    var candidate = Path.Combine(sdk, "platform-tools", "adb.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }

            var common = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
                @"C:\Android\Sdk\platform-tools\adb.exe",
            };
            foreach (var p in common)
            {
                if (File.Exists(p)) return p;
            }

            return null;
        }
    }
}