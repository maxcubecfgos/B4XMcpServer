using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using B4XMcpServer.Repositories;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;

namespace B4XMcpServer.Tools.Layout
{
    [McpServerToolType]
    public sealed class LayoutProjectTools
    {
        private readonly IFileRepository _fileRepository;

        public LayoutProjectTools(IFileRepository fileRepository)
        {
            _fileRepository = fileRepository;
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
