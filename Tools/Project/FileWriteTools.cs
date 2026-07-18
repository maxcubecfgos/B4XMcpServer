using B4XMcpServer.Repositories;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace B4XMcpServer.Tools.Project
{
    [McpServerToolType]
    public sealed class FileWriteTools
    {
        private readonly IFileRepository _fileRepository;
        private readonly IProjectRepository _projectRepository;

        public FileWriteTools(IFileRepository fileRepository, IProjectRepository projectRepository)
        {
            _fileRepository = fileRepository;
            _projectRepository = projectRepository;
        }

        [McpServerTool, Description("Writes (overwrites) a file with the given content. This replaces the entire file, so read it first with get_file_content if you need to preserve parts of it. Typically used to save an edited B4X module back to disk. BLOCKED for existing .b4a/.b4j/.b4i project files to prevent IDE metadata corruption — use edit_sub for code, enable_library/disable_library for libraries, write_manifest for manifest, and register_layout_in_project/register_module_in_project for layouts/modules. Creates a .bak backup first if the file already exists.")]
        public string WriteFile(
            [Description("Absolute path to the file to write.")] string filePath,
            [Description("The full new content of the file.")] string content)
        {
            PathSecurity.ValidateAbsolutePath(filePath, nameof(filePath));

            // For destructive writes, try to keep them inside the project root.
            string? projectRoot = _projectRepository.FindProjectRoot(PathSecurity.GetDirectoryForProjectRoot(filePath));
            if (projectRoot != null)
                PathSecurity.ValidateWithinBaseDirectory(filePath, projectRoot, nameof(filePath));

            // BLOCK writes to .meta files — they are pure IDE session state.
            if (Path.GetExtension(filePath).Equals(".meta", StringComparison.OrdinalIgnoreCase))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "❌ CRITICAL: Cannot write to .meta files. These contain pure IDE session state (ModuleBookmarks, ModuleBreakpoints, ModuleClosedNodes, NavigationStack, SelectedBuild, VisibleModules) — NONE of this affects compilation. Writing to .meta files at best does nothing useful, at worst desyncs what the IDE shows from what's actually true and looks like corruption to the developer.",
                    hints = new[]
                    {
                        ".meta files are NEVER to be read, written, or modified by automated tools.",
                        "Use get_project_structure for file lists, get_project_config for project metadata, analyze_module for code structure.",
                        "If a .meta file is corrupt, delete it — the IDE will regenerate it on next open."
                    }
                }, JsonOptions.Default);
            }

            // CRITICAL: block CREATING a new Main.bas when the parent directory
            // already has a B4X project file.
            if (PathSecurity.IsForbiddenMainBas(filePath, out var blockReason))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = blockReason,
                    hints = new[]
                    {
                        "The .b4a/.b4j/.b4i file IS the project's Main module — REGARDLESS of what it is named (MiApp.b4a, Project.b4a, MainApp.b4a, anything). Activity_Create / Process_Globals / AppStart live in its source code section.",
                        "To add a Sub to the Main module, use edit_sub on the project file (NOT write_file).",
                        "Call get_project_structure first to confirm which files exist; if Main.bas is not listed, the main code goes in whichever .b4a / .b4j / .b4i sits at the project root.",
                        "If you previously corrupted the project by creating Main.bas, remove it manually after restoring the project file from its .bak backup."
                    }
                }, JsonOptions.Default);
            }

            // Direct writes to existing B4X project files corrupt the IDE metadata header.
            if (_fileRepository.Exists(filePath) && PathSecurity.IsMainProjectFile(filePath))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "❌ CRITICAL: Direct modification of .b4a/.b4j/.b4i project files using write_file is blocked to prevent IDE metadata corruption.",
                    hints = new[]
                    {
                        "To add/remove libraries, use enable_library or disable_library ONLY.",
                        "To edit code (Subs) in the project file, use edit_sub.",
                        "To edit the B4A manifest, use write_manifest.",
                        "To register a new layout or module, use register_layout_in_project or register_module_in_project."
                    }
                }, JsonOptions.Default);
            }

            string? backupPath = null;
            if (_fileRepository.Exists(filePath))
            {
                backupPath = _fileRepository.BackupPath(filePath);
            }

            _fileRepository.WriteText(filePath, content);
            return JsonSerializer.Serialize(new
            {
                success = true,
                path = filePath,
                backup = backupPath,
                bytesWritten = System.Text.Encoding.UTF8.GetByteCount(content)
            });
        }
    }
}
