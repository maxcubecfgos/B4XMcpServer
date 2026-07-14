using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace B4XMcpServer.Utils
{
    public static class B4aConfig
    {
        private static readonly string B4aIniPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Anywhere Software", "Basic4android", "b4xV5.ini");

        private static readonly string B4jIniPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Anywhere Software", "Basic4j", "b4xV5.ini");

        /// <summary>
        /// Returns all library directories: B4A Libraries, B4J Libraries, and AdditionalLibrariesFolder from ini.
        /// </summary>
        public static List<string> GetLibraryDirectories()
        {
            var dirs = new List<string>();

            // B4A
            var b4aIni = ParseIni(B4aIniPath);
            if (b4aIni.TryGetValue("AdditionalLibrariesFolder", out var additional) && !string.IsNullOrEmpty(additional))
                dirs.Add(additional);

            // B4A built-in from Program Files
            var b4aDefault = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Anywhere Software", "B4A", "Libraries");
            var b4aDefaultX86 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Anywhere Software", "B4A", "Libraries");
            if (Directory.Exists(b4aDefault)) dirs.Add(b4aDefault);
            else if (Directory.Exists(b4aDefaultX86)) dirs.Add(b4aDefaultX86);

            // B4J
            var b4jIni = ParseIni(B4jIniPath);
            if (b4jIni.TryGetValue("AdditionalLibrariesFolder", out var b4jAdditional) && !string.IsNullOrEmpty(b4jAdditional))
                if (!dirs.Contains(b4jAdditional)) dirs.Add(b4jAdditional);

            var b4jDefault = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Anywhere Software", "B4J", "Libraries");
            if (Directory.Exists(b4jDefault) && !dirs.Contains(b4jDefault)) dirs.Add(b4jDefault);

            // Project-local Libraries folder (if we have a project root)
            // This is discovered at call time, not here.

            return dirs.Where(Directory.Exists).Distinct().ToList();
        }

        /// <summary>
        /// Finds library directories including a project-local Libraries folder.
        /// </summary>
        public static List<string> GetLibraryDirectories(string? projectRoot)
        {
            var dirs = GetLibraryDirectories();

            if (!string.IsNullOrEmpty(projectRoot))
            {
                var localLib = Path.Combine(projectRoot, "Libraries");
                if (Directory.Exists(localLib) && !dirs.Contains(localLib))
                    dirs.Insert(0, localLib); // Project-local takes priority
            }

            return dirs;
        }

        private static Dictionary<string, string> ParseIni(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return result;

            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(";") || trimmed.StartsWith("#") || !trimmed.Contains("="))
                    continue;
                int idx = trimmed.IndexOf('=');
                var key = trimmed.Substring(0, idx).Trim();
                var value = trimmed.Substring(idx + 1).Trim();
                result[key] = value;
            }
            return result;
        }
    }
}