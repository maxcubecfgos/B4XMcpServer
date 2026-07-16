using System.Text.RegularExpressions;
using B4XMcpServer.Models;
using B4XMcpServer.Services;

namespace B4XMcpServer.Repositories
{
    /// <summary>
    /// Default project repository. Wraps ProjectScanner and centralizes
    /// project-file metadata parsing.
    /// </summary>
    public sealed class ProjectRepository : IProjectRepository
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        public string? FindProjectRoot(string? startPath)
        {
            return ProjectScanner.FindProjectRoot(startPath);
        }

        public string? FindProjectFile(string? projectRoot)
        {
            return ProjectScanner.FindProjectFile(projectRoot);
        }

        public List<ProjectFile> ScanProject(string? projectRoot)
        {
            return ProjectScanner.ScanProject(projectRoot);
        }

        public ProjectConfig GetConfig(string projectFile)
        {
            var raw = CodeUtils.ReadTextSafely(projectFile);
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            string headerSection = markerIdx >= 0 ? raw.Substring(0, markerIdx) : raw;

            var rawSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lineRaw in headerSection.Split('\n'))
            {
                var line = lineRaw.TrimEnd('\r').Trim();
                if (string.IsNullOrEmpty(line)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                rawSettings[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            var libraries = rawSettings
                .Where(kv => Regex.IsMatch(kv.Key, @"^Library\d+$", RegexOptions.None, RegexTimeout))
                .Select(kv => kv.Value)
                .OrderBy(v => v)
                .ToList();

            var modules = rawSettings
                .Where(kv => Regex.IsMatch(kv.Key, @"^Module\d+$", RegexOptions.None, RegexTimeout))
                .Select(kv => kv.Value)
                .ToList();

            var includedFiles = rawSettings
                .Where(kv => Regex.IsMatch(kv.Key, @"^File\d+$", RegexOptions.None, RegexTimeout))
                .Select(kv => kv.Value)
                .ToList();

            return new ProjectConfig
            {
                ProjectFile = projectFile,
                AppType = rawSettings.TryGetValue("AppType", out var at) ? at : null,
                Version = rawSettings.TryGetValue("Version", out var ver) ? ver : null,
                NumberOfModules = rawSettings.TryGetValue("NumberOfModules", out var nm) ? nm : null,
                Libraries = libraries,
                Modules = modules,
                IncludedFiles = includedFiles,
                RawSettings = rawSettings
            };
        }
    }
}
