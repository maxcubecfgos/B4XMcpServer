using B4XMcpServer.Engine;
using B4XMcpServer.Repositories;
using B4XMcpServer.Services;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace B4XMcpServer.Tools.Project
{
    [McpServerToolType]
    public sealed class LayoutViewTools
    {
        private readonly IFileRepository _fileRepository;
        private readonly IProjectRepository _projectRepository;

        public LayoutViewTools(IFileRepository fileRepository, IProjectRepository projectRepository)
        {
            _fileRepository = fileRepository;
            _projectRepository = projectRepository;
        }

        [McpServerTool, Description("Decodes a B4X visual layout file into readable JSON: control hierarchy, types, positions (resolved from the correct screen variant, not the misleading top-level template defaults), and properties like text/hint/tag/drawable. Works for both .bal (B4A) and .bjl (B4J) — they share the exact same binary format.")]
        public string GetLayoutStructure(
            [Description("Absolute path to the .bal or .bjl layout file.")] string layoutPath)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!_fileRepository.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            // Cache the decoded JSON by mtime — .bal decoding is the most expensive
            // read-only tool on large layouts and AI sessions frequently re-query the
            // same layout across turns while iterating on edits.
            if (CacheManager.TryGetByMtime<string>(layoutPath, out var cached) && cached != null)
                return cached;

            var data = _fileRepository.ReadBytes(layoutPath);
            var json = LayoutJsonTransform.LayoutToJson(data);
            CacheManager.SetByMtime(layoutPath, json);
            return json;
        }

        [McpServerTool, Description("Lists every layout file (.bal/.bjl/.bil) in a project with basic metadata: screen variants and top-level control count.")]
        public string ListLayouts(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")] string projectPath)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            string? root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

            var layoutFiles = _projectRepository.ScanProject(root)
                .Where(f => f.Kind == "bal" || f.Kind == "bjl" || f.Kind == "bil").ToList();

            var results = new List<object>();
            foreach (var f in layoutFiles)
            {
                try
                {
                    string? decodedJson;
                    if (!CacheManager.TryGetByMtime<string>(f.Path, out decodedJson) || decodedJson == null)
                    {
                        var data = _fileRepository.ReadBytes(f.Path);
                        decodedJson = LayoutJsonTransform.LayoutToJson(data);
                        CacheManager.SetByMtime(f.Path, decodedJson);
                    }
                    using var doc = JsonDocument.Parse(decodedJson);
                    var rootEl = doc.RootElement;

                    var variants = rootEl.TryGetProperty("variants", out var v) ? v.Clone() : default;
                    int kidCount = 0;
                    if (rootEl.TryGetProperty("layoutTree", out var tree) &&
                        tree.TryGetProperty("kids", out var kids) &&
                        kids.ValueKind == JsonValueKind.Array)
                    {
                        kidCount = kids.GetArrayLength();
                    }

                    results.Add(new
                    {
                        file = f.Path,
                        kind = f.Kind,
                        variants,
                        topLevelControlCount = kidCount
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new { file = f.Path, error = ex.Message });
                }
            }

            return JsonSerializer.Serialize(new { layoutCount = results.Count, layouts = results },
                JsonOptions.Default);
        }
    }
}
