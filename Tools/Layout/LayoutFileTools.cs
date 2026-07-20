using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using B4XMcpServer.Engine;
using B4XMcpServer.Repositories;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;

namespace B4XMcpServer.Tools.Layout
{
    [McpServerToolType]
    public sealed class LayoutFileTools
    {
        private readonly IFileRepository _fileRepository;

        public LayoutFileTools(IFileRepository fileRepository)
        {
            _fileRepository = fileRepository;
        }

        [McpServerTool, Description("Reads a B4X layout file and returns its full structure as JSON. Use this to inspect controls, properties, variants, and the manifest.")]
        public string ReadLayout(
            [Description("Absolute path to the .bal or .bil layout file")] string layoutPath)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!_fileRepository.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            var data = _fileRepository.ReadBytes(layoutPath);
            var json = LayoutJsonTransform.LayoutToJson(data);
            using var doc = JsonDocument.Parse(json);

            return JsonSerializer.Serialize(new
            {
                success = true,
                layoutPath,
                layout = doc.RootElement
            }, JsonOptions.Default);
        }



    }
}
