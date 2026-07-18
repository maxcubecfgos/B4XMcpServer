using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using B4XMcpServer.Repositories;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;

namespace B4XMcpServer.Tools.Layout
{
    [McpServerToolType]
    public sealed class LayoutProjectTools
    {
        private readonly IFileRepository _fileRepository;

        // Regex timeout protects against catastrophic backtracking on untrusted input.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        public LayoutProjectTools(IFileRepository fileRepository)
        {
            _fileRepository = fileRepository;
        }

        [McpServerTool, Description("Registers a layout file in the project metadata so the IDE and builder recognize it. Adds FileN= and FileGroupN= entries to the project header, updates NumberOfFiles, and creates .bak backup. If the layout is already registered, does nothing.")]
        public string RegisterLayoutInProject(
            [Description("Absolute path to the .b4a or .b4j project file")] string projectPath,
            [Description("Layout file name (e.g. 'Main.bal', 'Settings.bjl'). Can be just the filename or a relative path like 'Files/Main.bal'.")] string layoutFileName)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            if (!_fileRepository.Exists(projectPath))
                throw new FileNotFoundException($"Project file not found: {projectPath}");

            var ext = Path.GetExtension(projectPath).ToLowerInvariant();
            if (ext != ".b4a" && ext != ".b4j" && ext != ".b4i")
                throw new ArgumentException("File must have .b4a, .b4j, or .b4i extension");

            string raw = _fileRepository.ReadTextWithHeader(projectPath);
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);

            if (markerIdx < 0)
                throw new InvalidOperationException("Project file is corrupted: missing internal section separator.");

            string headerSection = raw.Substring(0, markerIdx);
            string codeSection = raw.Substring(markerIdx);

            bool usesCrLf = headerSection.Contains("\r\n");
            string eol = usesCrLf ? "\r\n" : "\n";
            var lines = headerSection.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

            string normalizedName = layoutFileName.Replace('\\', '/').Trim();
            string baseName = Path.GetFileName(normalizedName);

            var fileRegex = new Regex(@"^File(\d+)=(.*)$", RegexOptions.IgnoreCase, RegexTimeout);
            var fileGroupRegex = new Regex(@"^FileGroup(\d+)=(.*)$", RegexOptions.IgnoreCase, RegexTimeout);
            var numberOfFilesRegex = new Regex(@"^NumberOfFiles=(\d+)$", RegexOptions.IgnoreCase, RegexTimeout);

            int maxFileIndex = 0;
            int firstFileGroupIndex = -1;
            bool alreadyRegistered = false;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var fileMatch = fileRegex.Match(line);
                if (fileMatch.Success)
                {
                    int idx = int.Parse(fileMatch.Groups[1].Value);
                    if (idx > maxFileIndex) maxFileIndex = idx;

                    string existingFile = fileMatch.Groups[2].Value.Trim().Replace('\\', '/');
                    string existingBase = Path.GetFileName(existingFile);
                    if (string.Equals(existingBase, baseName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(existingFile, normalizedName, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyRegistered = true;
                    }
                    continue;
                }

                if (firstFileGroupIndex < 0 && fileGroupRegex.IsMatch(line))
                    firstFileGroupIndex = i;
            }

            if (alreadyRegistered)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    projectPath,
                    action = "already_registered",
                    layoutFile = normalizedName
                }, JsonOptions.Default);
            }

            int nextIndex = maxFileIndex + 1;
            string fileLine = $"File{nextIndex}={normalizedName}";

            int insertIndex = firstFileGroupIndex >= 0 ? firstFileGroupIndex : lines.Count;
            lines.Insert(insertIndex, fileLine);

            bool hasGroups = lines.Any(l => fileGroupRegex.IsMatch(l));
            if (hasGroups)
            {
                int groupBlockEnd = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (fileGroupRegex.IsMatch(lines[i]))
                        groupBlockEnd = i + 1;
                }

                if (groupBlockEnd >= 0)
                    lines.Insert(groupBlockEnd, $"FileGroup{nextIndex}=Default Group");
            }

            bool updatedNumberOfFiles = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var nfMatch = numberOfFilesRegex.Match(lines[i]);
                if (nfMatch.Success)
                {
                    lines[i] = $"NumberOfFiles={nextIndex}";
                    updatedNumberOfFiles = true;
                    break;
                }
            }
            if (!updatedNumberOfFiles)
                lines.Insert(0, $"NumberOfFiles={nextIndex}");

            string newHeader = string.Join(eol, lines);
            string newContent = newHeader + codeSection;

            string backupPath = _fileRepository.BackupPath(projectPath) ?? (projectPath + ".bak");
            _fileRepository.WriteText(projectPath, newContent);

            return JsonSerializer.Serialize(new
            {
                success = true,
                projectPath,
                backup = backupPath,
                action = "registered",
                layoutFile = normalizedName,
                entry = $"File{nextIndex}",
                numberOfFiles = nextIndex
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("DEPRECATED — do not use. Registering .bas modules automatically has proven unreliable and can corrupt the project metadata. This tool now returns the exact manual steps the user must follow in the B4X IDE to register a new module safely.")]
        public string RegisterModuleInProject(
            [Description("Ignored — kept only for signature compatibility.")] string projectPath,
            [Description("Ignored — kept only for signature compatibility.")] string moduleName)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Automatic registration of .bas modules is disabled to prevent project corruption.",
                instructions = new[]
                {
                    "1. Open the project in the B4X IDE.",
                    "2. If the module file is not already in the project folder, copy it there first.",
                    "3. In the IDE, right-click the project name in the Files tree and choose 'Add Existing Module', or use Project → Add New Module if you still need to create it.",
                    "4. The IDE will update the project metadata (ModuleN= and NumberOfModules) automatically.",
                    "5. Save the project in the IDE (Ctrl+S).",
                    "6. After the module is registered, you can use get_file_content / edit_sub / analyze_module on it, and compile_project to verify."
                },
                note = "Do NOT edit the project file's ModuleN= or NumberOfModules= entries manually — the IDE must keep these in sync with the actual files."
            }, JsonOptions.Default);
        }
    }
}
