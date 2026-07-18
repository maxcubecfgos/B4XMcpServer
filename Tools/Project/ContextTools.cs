using B4XMcpServer.Models;
using B4XMcpServer.Repositories;
using B4XMcpServer.Services;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ContextFileMode = B4XMcpServer.Models.FileMode;

namespace B4XMcpServer.Tools.Project
{
    public sealed class GetFullContextRequest
    {
        [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j project file.")]
        public string ProjectPath { get; set; } = string.Empty;

        [Description("Optional: path to the file the AI/user is currently focused on.")]
        public string? FocusFile { get; set; }

        [Description("Optional: name of the specific Sub inside FocusFile to keep expanded. If omitted but FocusFile is set, the whole FocusFile is kept expanded instead.")]
        public string? FocusSub { get; set; }

        [Description("If true, compiles the project first and attaches structured errors to the bundle. Default false (compiling can take a while).")]
        public bool RunCompile { get; set; } = false;

        [Description("Optional short description of the current task, included at the top of the bundle.")]
        public string? Task { get; set; }
    }

    [McpServerToolType]
    public sealed class ContextTools
    {
        private readonly IFileRepository _fileRepository;
        private readonly IProjectRepository _projectRepository;

        public ContextTools(IFileRepository fileRepository, IProjectRepository projectRepository)
        {
            _fileRepository = fileRepository;
            _projectRepository = projectRepository;
        }

        [McpServerTool, Description("Builds a single consolidated Markdown context bundle for a B4X project: an ASCII file tree plus every module/layout, in skeleton form (signatures only, bodies collapsed) by default to keep it compact. Pass FocusFile to keep one specific file fully expanded, or FocusFile+FocusSub to keep just that one Sub expanded while the rest of that same file stays collapsed. Optionally compiles first and attaches real errors. This is the token-efficient alternative to dumping the whole project — use get_file_content afterward for any other single file you need in full.")]
        public async Task<string> GetFullContext(GetFullContextRequest request)
        {
            var projectPath = request.ProjectPath;
            var focusFile = request.FocusFile;
            var focusSub = request.FocusSub;
            var runCompile = request.RunCompile;
            var task = request.Task;

            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));
            if (focusFile != null)
                PathSecurity.ValidateAbsolutePath(focusFile, nameof(focusFile));

            string? root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

            var scanned = _projectRepository.ScanProject(root);

            // Smart default: when the caller doesn't specify FocusFile, pick the most
            // recently modified source file.
            string? effectiveFocusFile = focusFile;
            string? autoFocusedFromMtime = null;
            if (string.IsNullOrEmpty(effectiveFocusFile))
            {
                autoFocusedFromMtime = scanned
                    .Where(f => f.Kind == "bas" || f.Kind == "b4a" || f.Kind == "b4j" || f.Kind == "b4i")
                    .OrderByDescending(f => _fileRepository.GetLastWriteTimeUtc(f.Path))
                    .Select(f => f.Path)
                    .FirstOrDefault();
                effectiveFocusFile = autoFocusedFromMtime;
            }

            var projectFiles = scanned.Select(f =>
            {
                var pf = new ProjectFile(f.Path) { Kind = f.Kind, Included = true };
                bool isFocus = effectiveFocusFile != null &&
                    string.Equals(Path.GetFullPath(f.Path), Path.GetFullPath(effectiveFocusFile), StringComparison.OrdinalIgnoreCase);
                pf.Mode = (isFocus && string.IsNullOrEmpty(focusSub)) ? ContextFileMode.Full : ContextFileMode.Skeleton;
                return pf;
            }).ToList();

            string? compileErrorsBlock = null;
            if (runCompile)
            {
                var projectFile = _projectRepository.FindProjectFile(root);
                if (projectFile != null)
                {
                    var builderPath = BuilderLocator.LocateBuilder(projectFile);
                    if (builderPath != null)
                    {
                        var buildResult = await BuilderRunner.RunBuildAsync(builderPath, projectFile);
                        bool fatal = buildResult.TryGetValue("fatal_error", out var f2) && f2 != null;
                        if (!fatal)
                        {
                            bool success = buildResult.TryGetValue("success", out var s2) && s2 is bool sb2 && sb2;
                            if (!success) compileErrorsBlock = BuildFormatter.Format(buildResult);
                        }
                        else
                        {
                            compileErrorsBlock = $"## COMPILATION ERRORS\n\n(Builder failed to run: {buildResult["fatal_error"]})\n";
                        }
                    }
                }
            }

            var bundle = BundleBuilder.BuildMarkdown(
                preamble: null,
                task: task,
                files: projectFiles,
                includeFileTree: true,
                activeCode: null,
                activeFile: focusFile,
                activeSub: focusSub,
                compileErrors: compileErrorsBlock
            );

            // If we auto-focused via mtime, prepend a one-line banner
            if (autoFocusedFromMtime != null)
            {
                string banner = $"> ℹ️ Auto-focused on `{Path.GetFileName(autoFocusedFromMtime)}` (most recently modified source file). Pass `FocusFile` explicitly to override or `FocusFile=\"\"` to keep all files in skeleton mode.\n\n";
                bundle = banner + bundle;
            }

            return bundle;
        }
    }
}
