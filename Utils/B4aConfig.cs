using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace B4XMcpServer.Utils
{
    /// <summary>
    /// B4X IDE configuration reader with JSON persistence and auto-detection
    /// from b4xV5.ini. Merges three sources (priority order):
    ///   1. Explicit overrides from %APPDATA%/b4x-mcp-server/config.json
    ///   2. Auto-detected values from B4A/B4J b4xV5.ini
    ///   3. Hard-coded fallback paths
    /// </summary>
    public static class B4aConfig
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "b4x-mcp-server");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        private static readonly string B4aIniPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Anywhere Software", "Basic4android", "b4xV5.ini");

        private static readonly string B4jIniPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Anywhere Software", "Basic4j", "b4xV5.ini");

        private static ConfigData? _stored;
        private static ConfigData? _effective;
        private static readonly object _lock = new();

        public sealed class ConfigData
        {
            public string? B4aPath { get; set; }
            public string? B4jPath { get; set; }
            public string? AdditionalLibrariesPath { get; set; }
            public string? AdbPath { get; set; }
            public string? ProjectsRoot { get; set; }
            public string? SharedModulesFolder { get; set; }
            public string? JavaBin { get; set; }
        }

        /// <summary>
        /// Returns the effective configuration (stored overrides + auto-detected).
        /// Cached after first load.
        /// </summary>
        public static ConfigData Load()
        {
            if (_effective != null) return _effective;
            lock (_lock)
            {
                if (_effective != null) return _effective;
                LoadStoredNoLock();
                _effective = MergeWithDefaults(_stored!);
                return _effective;
            }
        }

        private static void LoadStored()
        {
            lock (_lock)
            {
                LoadStoredNoLock();
            }
        }

        private static void LoadStoredNoLock()
        {
            if (!File.Exists(ConfigPath))
            {
                _stored = new ConfigData();
                SaveStored();
            }
            else
            {
                try
                {
                    var json = File.ReadAllText(ConfigPath);
                    _stored = JsonSerializer.Deserialize<ConfigData>(json) ?? new ConfigData();
                }
                catch
                {
                    _stored = new ConfigData();
                }
            }
        }

        private static ConfigData MergeWithDefaults(ConfigData stored)
        {
            var merged = new ConfigData
            {
                B4aPath = stored.B4aPath,
                B4jPath = stored.B4jPath,
                AdditionalLibrariesPath = stored.AdditionalLibrariesPath,
                AdbPath = stored.AdbPath,
                ProjectsRoot = stored.ProjectsRoot,
                SharedModulesFolder = stored.SharedModulesFolder,
                JavaBin = stored.JavaBin,
            };

            // ── Auto-detect from B4A ini ────────────────────────────────
            var b4aIni = ParseIni(B4aIniPath);

            if (string.IsNullOrEmpty(merged.AdditionalLibrariesPath) &&
                b4aIni.TryGetValue("AdditionalLibrariesFolder", out var b4aLibs))
                merged.AdditionalLibrariesPath = b4aLibs;

            if (string.IsNullOrEmpty(merged.AdbPath) &&
                b4aIni.TryGetValue("ToolsFolder", out var toolsFolder))
            {
                var sdkRoot = Path.GetDirectoryName(toolsFolder.TrimEnd('\\', '/'));
                if (sdkRoot != null)
                {
                    var adbCandidate = Path.Combine(sdkRoot, "platform-tools", "adb.exe");
                    if (File.Exists(adbCandidate)) merged.AdbPath = adbCandidate;
                }
            }

            if (string.IsNullOrEmpty(merged.SharedModulesFolder) &&
                b4aIni.TryGetValue("SharedModulesFolder", out var shared))
                merged.SharedModulesFolder = shared;

            if (string.IsNullOrEmpty(merged.JavaBin) &&
                b4aIni.TryGetValue("JavaBin", out var java))
                merged.JavaBin = java;

            // ── Auto-detect B4A/B4J installation paths ───────────────────
            if (string.IsNullOrEmpty(merged.B4aPath))
            {
                var candidate = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Anywhere Software", "B4A");
                if (Directory.Exists(candidate))
                    merged.B4aPath = candidate;
                else
                {
                    candidate = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "Anywhere Software", "B4A");
                    if (Directory.Exists(candidate))
                        merged.B4aPath = candidate;
                }
            }

            if (string.IsNullOrEmpty(merged.B4jPath))
            {
                var candidate = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Anywhere Software", "B4J");
                if (Directory.Exists(candidate))
                    merged.B4jPath = candidate;
            }

            return merged;
        }

        private static void SaveStored()
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(_stored, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>
        /// Saves explicit config overrides and invalidates the cached effective config.
        /// </summary>
        public static void Save(ConfigData config)
        {
            lock (_lock)
            {
                _stored = config;
                SaveStored();
            }
            lock (_lock) { _effective = null; }
            CacheManager.InvalidateByPrefix("libs:");
        }

        /// <summary>
        /// Sets a single configuration value by key name (case-insensitive).
        /// Valid keys: B4aPath, B4jPath, AdditionalLibrariesPath, AdbPath,
        /// ProjectsRoot, SharedModulesFolder, JavaBin.
        /// </summary>
        public static string SetValue(string key, string value)
        {
            lock (_lock)
            {
                if (_stored == null) LoadStoredNoLock();
                var prop = typeof(ConfigData).GetProperty(key,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.IgnoreCase);
                if (prop == null)
                    return $"Unknown key: {key}. Valid: B4aPath, B4jPath, AdditionalLibrariesPath, AdbPath, ProjectsRoot, SharedModulesFolder, JavaBin";
                prop.SetValue(_stored, value);
                SaveStored();
                _effective = null;
                CacheManager.InvalidateByPrefix("libs:");
                return $"OK: {key} = {value}";
            }
        }

        /// <summary>
        /// Returns which config values are explicit overrides vs auto-detected.
        /// </summary>
        public static Dictionary<string, string> GetSources()
        {
            lock (_lock)
            {
                if (_stored == null) LoadStoredNoLock();
                var sources = new Dictionary<string, string>();
                foreach (var key in new[] { "B4aPath", "B4jPath", "AdditionalLibrariesPath", "AdbPath", "ProjectsRoot", "SharedModulesFolder", "JavaBin" })
                {
                    var prop = typeof(ConfigData).GetProperty(key)!;
                    var storedVal = prop.GetValue(_stored)?.ToString();
                    sources[key] = string.IsNullOrEmpty(storedVal) ? "auto (b4xV5.ini)" : "explicit (config.json)";
                }
                return sources;
            }
        }

        /// <summary>
        /// Returns all library directories: B4A Libraries, B4J Libraries, AdditionalLibrariesFolder, and project-local.
        /// </summary>
        public static List<string> GetLibraryDirectories()
        {
            var cfg = Load();
            var dirs = new List<string>();

            if (!string.IsNullOrEmpty(cfg.AdditionalLibrariesPath) && Directory.Exists(cfg.AdditionalLibrariesPath))
                dirs.Add(cfg.AdditionalLibrariesPath);

            if (!string.IsNullOrEmpty(cfg.B4aPath))
            {
                var b4aLibs = Path.Combine(cfg.B4aPath, "Libraries");
                if (Directory.Exists(b4aLibs)) dirs.Add(b4aLibs);
            }

            if (!string.IsNullOrEmpty(cfg.B4jPath))
            {
                var b4jLibs = Path.Combine(cfg.B4jPath, "Libraries");
                if (Directory.Exists(b4jLibs) && !dirs.Contains(b4jLibs)) dirs.Add(b4jLibs);
            }

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

        /// <summary>
        /// Returns the parsed b4xV5.ini values as a dictionary.
        /// </summary>
        public static Dictionary<string, string> GetIniValues(string platform = "b4a")
        {
            var path = platform.Equals("b4j", StringComparison.OrdinalIgnoreCase) ? B4jIniPath : B4aIniPath;
            return ParseIni(path);
        }

        public static string GetConfigPath() => ConfigPath;
        public static string GetB4aIniPath() => B4aIniPath;
        public static string GetB4jIniPath() => B4jIniPath;
        public static string GetConfigDir() => ConfigDir;

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