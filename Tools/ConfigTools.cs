using B4XMcpServer.Utils;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class ConfigTools
    {
        [McpServerTool, Description("Returns the current B4X MCP server configuration including B4A/B4J paths, library directories, ADB path, and whether each value was explicitly set or auto-detected from the B4X IDE ini files.")]
        public string GetConfig()
        {
            var cfg = B4aConfig.Load();
            var sources = B4aConfig.GetSources();

            return JsonSerializer.Serialize(new
            {
                b4aPath = cfg.B4aPath,
                b4jPath = cfg.B4jPath,
                additionalLibrariesPath = cfg.AdditionalLibrariesPath,
                adbPath = cfg.AdbPath,
                projectsRoot = cfg.ProjectsRoot,
                sharedModulesFolder = cfg.SharedModulesFolder,
                javaBin = cfg.JavaBin,
                configFile = B4aConfig.GetConfigPath(),
                b4aIniFile = B4aConfig.GetB4aIniPath(),
                b4jIniFile = B4aConfig.GetB4jIniPath(),
                libraryDirectories = B4aConfig.GetLibraryDirectories(),
                sources,
                warning = string.IsNullOrEmpty(cfg.B4aPath) && string.IsNullOrEmpty(cfg.B4jPath)
                    ? "No B4A or B4J installation detected. Use set_config to configure paths manually."
                    : null
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Updates a single B4X MCP server configuration value. Valid keys: B4aPath, B4jPath, AdditionalLibrariesPath, AdbPath, ProjectsRoot, SharedModulesFolder, JavaBin. Changes are persisted to %APPDATA%/b4x-mcp-server/config.json and take effect immediately (library cache is invalidated).")]
        public string SetConfig(
            [Description("Configuration key to set.")] string key,
            [Description("New value for the key.")] string value)
        {
            return B4aConfig.SetValue(key, value);
        }

        [McpServerTool, Description("Returns which configuration values are explicitly set (via set_config / config.json) vs auto-detected from the B4X IDE b4xV5.ini files. Use this to understand where each value originates.")]
        public string GetConfigSources()
        {
            var sources = B4aConfig.GetSources();
            return JsonSerializer.Serialize(new
            {
                sources,
                note = "Values marked 'explicit (config.json)' were set via set_config. Values marked 'auto (b4xV5.ini)' were detected from the B4X IDE settings."
            }, JsonOptions.Default);
        }
    }
}
