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
    public sealed class LayoutFileTools
    {
        private readonly IFileRepository _fileRepository;
        private readonly IProjectRepository _projectRepository;

        public LayoutFileTools(IFileRepository fileRepository, IProjectRepository projectRepository)
        {
            _fileRepository = fileRepository;
            _projectRepository = projectRepository;
        }

        [McpServerTool, Description("Creates a new B4X layout file from scratch with a default root panel and no child controls. The file is written in the IDE-compatible v5 binary format.")]
        public string CreateLayout(
            [Description("Absolute path to the B4X project folder.")] string projectPath,
            [Description("Absolute path to the new layout file (.bal, .bil, or .bjl) to create.")] string layoutPath)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!Directory.Exists(projectPath))
                return JsonSerializer.Serialize(new { success = false, error = $"Project path is not a directory: {projectPath}" }, JsonOptions.Default);

            var ext = Path.GetExtension(layoutPath).ToLowerInvariant();
            if (ext != ".bal" && ext != ".bil" && ext != ".bjl")
                return JsonSerializer.Serialize(new { success = false, error = "File must have .bal, .bil or .bjl extension" }, JsonOptions.Default);

            string? projectRoot = _projectRepository.FindProjectRoot(PathSecurity.GetDirectoryForProjectRoot(layoutPath));
            if (projectRoot != null)
                PathSecurity.ValidateWithinBaseDirectory(layoutPath, projectRoot, nameof(layoutPath));

            if (_fileRepository.Exists(layoutPath))
                return JsonSerializer.Serialize(new { success = false, error = $"Layout file already exists: {layoutPath}" }, JsonOptions.Default);

            var platform = LayoutHelpers.GetPlatform(layoutPath);
            var layout = BuildDefaultLayout(platform);

            LayoutHelpers.EnsureLayoutDirectory(layoutPath);
            var backup = LayoutHelpers.SaveLayoutWithBackup(_fileRepository, layoutPath, layout);

            return JsonSerializer.Serialize(new
            {
                success = true,
                path = layoutPath,
                backup,
                platform = platform.ToString(),
                gridSize = layout.GridSize,
                variants = layout.Variants.Select(v => new { v.Scale, v.Width, v.Height }).ToList()
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Creates an empty B4X layout file by cloning an existing IDE-created layout and removing all of its child controls. Requires at least one existing .bal/.bjl/.bil layout in the project to use as a template.")]
        public string CreateEmptyLayout(
            [Description("Absolute path to the B4X project folder.")] string projectPath,
            [Description("Absolute path to the new empty layout file (.bal, .bil, or .bjl) to create.")] string layoutPath,
            [Description("Optional: preferred existing layout file to use as template. If omitted, the first layout found in the project is used.")] string? templateLayoutPath = null)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!Directory.Exists(projectPath))
                return JsonSerializer.Serialize(new { success = false, error = $"Project path is not a directory: {projectPath}" }, JsonOptions.Default);

            var ext = Path.GetExtension(layoutPath).ToLowerInvariant();
            if (ext != ".bal" && ext != ".bil" && ext != ".bjl")
                return JsonSerializer.Serialize(new { success = false, error = "File must have .bal, .bil or .bjl extension" }, JsonOptions.Default);

            string? projectRoot = _projectRepository.FindProjectRoot(PathSecurity.GetDirectoryForProjectRoot(layoutPath));
            if (projectRoot != null)
                PathSecurity.ValidateWithinBaseDirectory(layoutPath, projectRoot, nameof(layoutPath));

            string? sourceLayout = null;
            if (!string.IsNullOrEmpty(templateLayoutPath))
            {
                if (!_fileRepository.Exists(templateLayoutPath))
                    return JsonSerializer.Serialize(new { success = false, error = $"Template layout not found: {templateLayoutPath}" }, JsonOptions.Default);
                if (!string.Equals(Path.GetExtension(templateLayoutPath), ext, StringComparison.OrdinalIgnoreCase))
                    return JsonSerializer.Serialize(new { success = false, error = $"Template layout extension must match target extension '{ext}'." }, JsonOptions.Default);
                sourceLayout = templateLayoutPath;
            }
            else
            {
                var candidates = _projectRepository.ScanProject(projectPath)
                    .Where(f => f.Kind == "bal" || f.Kind == "bjl" || f.Kind == "bil")
                    .Select(f => f.Path)
                    .Where(f => !f.EndsWith(".bak", StringComparison.OrdinalIgnoreCase));

                sourceLayout = candidates
                    .OrderBy(f => !string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(f => !Path.GetDirectoryName(f)!.EndsWith("Files", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(f => f.Length)
                    .FirstOrDefault();

                if (sourceLayout == null)
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        error = "No existing layout file found in the project to use as a template.",
                        hint = "Create at least one layout manually in the B4X IDE first, then call create_empty_layout again."
                    }, JsonOptions.Default);
            }

            var template = LayoutHelpers.LoadLayout(_fileRepository, sourceLayout);
            var emptyLayout = new LayoutFile
            {
                Version = template.Version,
                GridSize = template.GridSize,
                Variants = template.Variants.ToList(),
                RootControl = ControlRegistry.DeepCloneControlNode(template.RootControl),
                FileReferences = template.FileReferences.ToList(),
                ScriptData = template.ScriptData,
                Flags = template.Flags,
            };
            emptyLayout.RootControl.Children.Clear();
            LayoutHelpers.RebuildManifest(emptyLayout);

            LayoutHelpers.EnsureLayoutDirectory(layoutPath);
            var backup = LayoutHelpers.SaveLayoutWithBackup(_fileRepository, layoutPath, emptyLayout);

            return JsonSerializer.Serialize(new
            {
                success = true,
                path = layoutPath,
                backup,
                template = sourceLayout,
                gridSize = emptyLayout.GridSize,
                variants = emptyLayout.Variants.Select(v => new { v.Scale, v.Width, v.Height }).ToList()
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Reads a B4X layout file and returns its full structure as JSON. Use this to inspect controls, properties, variants, and the manifest before modifying a layout.")]
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

        [McpServerTool, Description("DISABLED. This tool is no longer available because writing a layout from JSON produces binaries incompatible with the B4X IDE. Use create_layout or create_empty_layout instead.")]
        public string WriteLayout(
            [Description("Ignored — kept only for signature compatibility.")] string layoutPath,
            [Description("Ignored — kept only for signature compatibility.")] string jsonContent)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "write_layout is disabled. The internal encoder cannot produce B4X IDE-compatible layout binaries.",
                hint = "Use create_layout to create a layout from scratch, or create_empty_layout to clone an existing IDE-created layout and clear it. Then use layout_add_control / layout_remove_control / layout_move_control to modify it."
            }, JsonOptions.Default);
        }

        private static LayoutFile BuildDefaultLayout(Platform platform)
        {
            const int gridSize = 10;
            var defaultVariant = VariantManager.GetDefaultVariant(platform);
            var root = ControlRegistry.CreateControl("Panel", platform, new HashSet<string>(), 0, 0,
                variantCount: 1, sourceVariantIndex: 0, gridSize: gridSize);

            if (root == null)
                throw new InvalidOperationException("Failed to create default root panel.");

            root.Properties["name"] = new StringRefValue("root");
            root.Properties["eventName"] = new StringRefValue("root");

            return new LayoutFile
            {
                Version = 5,
                GridSize = gridSize,
                Variants = new() { defaultVariant },
                RootControl = root,
                FileReferences = new(),
                ScriptData = null,
                Flags = (true, true),
            };
        }
    }
}
