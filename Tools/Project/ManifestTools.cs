using B4XMcpServer.Repositories;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;

namespace B4XMcpServer.Tools.Project
{
    [McpServerToolType]
    public sealed class ManifestTools
    {
        private readonly IFileRepository _fileRepository;

        public ManifestTools(IFileRepository fileRepository)
        {
            _fileRepository = fileRepository;
        }

        [McpServerTool, Description("Returns the current Manifest Editor block from a B4A project file. This is the content between #Region Manifest Editor and #End Region in the project metadata header.")]
        public string GetManifest(
            [Description("Absolute path to the .b4a project file.")] string projectPath)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            if (!_fileRepository.Exists(projectPath))
                throw new FileNotFoundException($"File not found: {projectPath}");
            if (!projectPath.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("File must have a .b4a extension.");

            string raw = _fileRepository.ReadTextWithHeader(projectPath);

            var block = ProjectHelpersShared.ExtractManifestBlock(raw);
            if (block == null)
                return ToolResponse.Error(
                    "No '#Region Manifest Editor' region found in this project.",
                    hints: new[] { "This is a B4A-specific region. It may be missing if the project was never opened in the B4A IDE.", "Use write_manifest to add one." });

            return ToolResponse.Success(new { projectPath, manifest = block });
        }

        [McpServerTool, Description("Replaces the Manifest Editor block in a B4A project file. Creates a .bak backup first.")]
        public string WriteManifest(
            [Description("Absolute path to the .b4a project file.")] string projectPath,
            [Description("New content for the Manifest Editor block.")] string manifestContent)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            if (!_fileRepository.Exists(projectPath))
                throw new FileNotFoundException($"File not found: {projectPath}");
            if (!projectPath.EndsWith(".b4a", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("File must have a .b4a extension.");

            string raw = _fileRepository.ReadTextWithHeader(projectPath);

            int startIdx = raw.IndexOf(ManifestMarkers.Start, StringComparison.Ordinal);
            if (startIdx < 0)
                throw new InvalidOperationException("No 'Manifest Editor' region found in this project.");

            int endIdx = raw.IndexOf(ManifestMarkers.End, startIdx, StringComparison.Ordinal);
            if (endIdx < 0)
                throw new InvalidOperationException("Found '#Region Manifest Editor' but no matching '#End Region'.");

            _fileRepository.Copy(projectPath, projectPath + ".bak", overwrite: true);

            var before = raw.Substring(0, startIdx + ManifestMarkers.Start.Length);
            var after = raw.Substring(endIdx);
            var newContent = before + "\r\n" + manifestContent.TrimEnd('\r', '\n') + "\r\n" + after;

            _fileRepository.WriteText(projectPath, newContent);

            return ToolResponse.Success(
                new { projectPath, backup = projectPath + ".bak" },
                nextSteps: new[] { "Call compile_project to verify the manifest change doesn't break the build." });
        }
    }
}
