using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using B4XMcpServer.Engine;
using B4XMcpServer.Repositories;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;

namespace B4XMcpServer.Tools.Layout
{
    [McpServerToolType]
    public sealed class LayoutControlTools
    {
        private readonly IFileRepository _fileRepository;

        public LayoutControlTools(IFileRepository fileRepository)
        {
            _fileRepository = fileRepository;
        }

        [McpServerTool, Description("Lists all controls in a layout file with their name, type, position, size, and children hierarchy. Use this to understand the structure before adding, removing, or moving controls.")]
        public string ListLayoutControls(
            [Description("Absolute path to the .bal or .bil layout file")] string layoutPath)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!_fileRepository.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            var layout = LayoutHelpers.LoadLayout(_fileRepository, layoutPath);
            var controls = new List<object>();
            FlattenControls(layout.RootControl, "", controls);

            return JsonSerializer.Serialize(new
            {
                file = layoutPath,
                controlCount = controls.Count,
                controls
            }, JsonOptions.Default);
        }

        private static void FlattenControls(ControlNode node, string parentPath, List<object> result)
        {
            var name = PropertyModel.GetStr(node, "name", "") ?? PropertyModel.GetStr(node, "eventName", "");
            var javaType = PropertyModel.GetStr(node, "javaType", "");
            var csType = PropertyModel.GetStr(node, "csType", "");
            var type = LayoutHelpers.GetShortTypeName(javaType);
            var pos = LayoutHelpers.ReadVariant(node, 0);
            var text = PropertyModel.GetStr(node, "text", "") ?? PropertyModel.GetStr(node, "hintText", "");

            result.Add(new
            {
                name = string.IsNullOrEmpty(name) ? "(root)" : name,
                type,
                javaType,
                csType,
                position = $"{pos.Left}, {pos.Top}",
                size = $"{pos.Width}x{pos.Height}",
                text = string.IsNullOrEmpty(text) ? null : text,
                parentPath = string.IsNullOrEmpty(parentPath) ? null : parentPath,
                childCount = node.Children.Count
            });

            var currentPath = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath} > {name}";
            foreach (var child in node.Children)
                FlattenControls(child, currentPath, result);
        }


    }
}
