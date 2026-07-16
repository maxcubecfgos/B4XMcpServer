using B4XMcpServer.Services;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace B4XMcpServer.Tools
{
    /// <summary>
    /// Workflow orchestration tools. The AI client is encouraged to call
    /// <c>get_workflow_guide</c> at the start of any non-trivial task so it does not
    /// have to guess which tools to use or in which order.
    /// </summary>
    [McpServerToolType]
    public sealed class WorkflowTools
    {
        /// <summary>
        /// Returns a recommended sequence of MCP tool calls for the given task.
        /// This reduces guesswork, avoids redundant calls, and keeps the AI on the
        /// safest/shortest path to a working B4X project.
        /// </summary>
        [McpServerTool, Description(
            "Call this FIRST when you are unsure which B4X tools to use or in what order. " +
            "Describe the task in plain English and (optionally) pass the project path. " +
            "You will get back a detected intent, a confidence level, an explanation, " +
            "and a step-by-step plan of tool calls to execute. Follow the steps in order.")]
        public static string GetWorkflowGuide(
            [Description("Plain-English description of what you want to accomplish, e.g. 'Add a Button to Main layout and handle its click'.")] string task,
            [Description("Optional: absolute path to the B4X project folder or its .b4a/.b4j/.b4i file. If omitted, the guide will be generic and may ask you to provide it.")] string? projectPath = null)
        {
            if (string.IsNullOrWhiteSpace(task))
                throw new ArgumentException("Task description must not be empty.", nameof(task));

            string? resolvedRoot = null;
            string? resolvedProjectFile = null;
            if (!string.IsNullOrWhiteSpace(projectPath))
            {
                PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));
                resolvedRoot = Directory.Exists(projectPath)
                    ? projectPath
                    : ProjectScanner.FindProjectRoot(projectPath);
                resolvedProjectFile = resolvedRoot != null
                    ? ProjectScanner.FindProjectFile(resolvedRoot)
                    : null;
            }

            var intent = DetectIntent(task);
            var steps = BuildSteps(intent, task, resolvedRoot, resolvedProjectFile);

            var result = new
            {
                detectedIntent = intent.Name,
                confidence = intent.Confidence.ToString().ToLowerInvariant(),
                explanation = intent.Explanation,
                projectRoot = resolvedRoot,
                projectFile = resolvedProjectFile,
                steps,
                requiredInfo = BuildRequiredInfo(intent, resolvedRoot, resolvedProjectFile),
                note = "Follow the steps in order. Each 'tool' is the MCP tool name; 'params' shows the exact parameter names to use. Values in angle brackets (e.g. <path>) must be filled by you before calling the tool."
            };

            return JsonSerializer.Serialize(result, JsonOptions.Default);
        }

        // ── Intent detection ───────────────────────────────────────────

        private sealed class Intent
        {
            public string Name { get; init; } = "unknown";
            public ConfidenceLevel Confidence { get; init; } = ConfidenceLevel.Low;
            public string Explanation { get; init; } = string.Empty;
        }

        private enum ConfidenceLevel { Low, Medium, High }

        private static readonly char[] WordSeparators = new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?' };

        private static Intent DetectIntent(string task)
        {
            var lower = task.ToLowerInvariant();
            var words = lower.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            // Compound / layout + code tasks
            if (ContainsAny(words, "layout", "bal", "bjl", "bil", "designer"))
            {
                if (ContainsAny(words, "add", "insert", "create", "new") &&
                    ContainsAny(words, "button", "label", "edit", "text", "image", "control", "view"))
                {
                    return new Intent
                    {
                        Name = "add_layout_control",
                        Confidence = ConfidenceLevel.High,
                        Explanation = "The task involves adding a UI control to a visual layout."
                    };
                }

                if (ContainsAny(words, "move", "resize", "reposition"))
                {
                    return new Intent
                    {
                        Name = "edit_layout",
                        Confidence = ConfidenceLevel.High,
                        Explanation = "The task involves changing the position or size of an existing layout control."
                    };
                }

                if (ContainsAny(words, "remove", "delete", "eliminate"))
                {
                    return new Intent
                    {
                        Name = "edit_layout",
                        Confidence = ConfidenceLevel.High,
                        Explanation = "The task involves removing controls from a layout."
                    };
                }

                if (ContainsAny(words, "create", "new") && ContainsAny(words, "layout", "file"))
                {
                    return new Intent
                    {
                        Name = "create_layout",
                        Confidence = ConfidenceLevel.High,
                        Explanation = "The task involves creating a new layout file."
                    };
                }

                // Generic layout-related task
                return new Intent
                {
                    Name = "edit_layout",
                    Confidence = ConfidenceLevel.Medium,
                    Explanation = "The task mentions a layout but the exact action is unclear."
                };
            }

            // Library management
            if (ContainsAny(words, "library", "libraries", "enable", "disable", "add library", "remove library"))
            {
                if (ContainsAny(words, "remove", "disable", "delete"))
                {
                    return new Intent
                    {
                        Name = "remove_library",
                        Confidence = ConfidenceLevel.High,
                        Explanation = "The task involves removing/disabling a library from the project."
                    };
                }

                return new Intent
                {
                    Name = "add_library",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "The task involves adding/enabling a library in the project."
                };
            }

            // Module creation
            if (ContainsAny(words, "create", "new") && ContainsAny(words, "module", "bas", "class", "activity"))
            {
                return new Intent
                {
                    Name = "create_module",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "The task involves creating a new .bas module."
                };
            }

            // Code editing
            if (ContainsAny(words, "edit", "modify", "change", "update", "fix", "refactor") &&
                ContainsAny(words, "sub", "function", "method", "code", "module", "bas", "b4a", "b4j"))
            {
                return new Intent
                {
                    Name = "edit_code",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "The task involves editing existing B4X source code."
                };
            }

            // Compilation / debugging
            if (ContainsAny(words, "compile", "build", "error", "fail", "debug"))
            {
                if (ContainsAny(words, "runtime", "crash", "logcat", "device", "adb"))
                {
                    return new Intent
                    {
                        Name = "debug_runtime",
                        Confidence = ConfidenceLevel.High,
                        Explanation = "The task involves investigating a runtime crash or device log."
                    };
                }

                return new Intent
                {
                    Name = "compile_debug",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "The task involves compiling the project or fixing compile errors."
                };
            }

            // Search / exploration
            if (ContainsAny(words, "search", "find", "grep", "where", "lookup"))
            {
                return new Intent
                {
                    Name = "search_code",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "The task involves searching for code patterns across the project."
                };
            }

            // API / language reference
            if (ContainsAny(words, "api", "signature", "method", "property", "library docs", "documentation") &&
                !ContainsAny(words, "edit", "change", "fix"))
            {
                return new Intent
                {
                    Name = "api_reference",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "The task involves looking up B4X API or library documentation."
                };
            }

            // Git operations
            if (ContainsAny(words, "git", "diff", "log", "status", "commit", "branch"))
            {
                return new Intent
                {
                    Name = "git_ops",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "The task involves inspecting git state."
                };
            }

            // Validation
            if (ContainsAny(words, "validate", "structure", "parse", "check"))
            {
                return new Intent
                {
                    Name = "validate_project",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "The task involves validating project structure without compiling."
                };
            }

            // Context / exploration
            if (ContainsAny(words, "explore", "structure", "overview", "context", "understand", "what files"))
            {
                return new Intent
                {
                    Name = "explore_project",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "The task involves understanding the project layout and files."
                };
            }

            // Fallback
            return new Intent
            {
                Name = "explore_project",
                Confidence = ConfidenceLevel.Low,
                Explanation = "Could not determine a specific intent from the task description. Starting with project exploration is the safest default."
            };
        }

        private static bool ContainsAny(HashSet<string> words, params string[] candidates)
        {
            return candidates.Any(c => words.Contains(c));
        }

        // ── Step generation ──────────────────────────────────────────

        private sealed class WorkflowStep
        {
            public int Step { get; init; }
            public string Tool { get; init; } = string.Empty;
            public string Reason { get; init; } = string.Empty;
            public Dictionary<string, object?> Params { get; init; } = new();
        }

        private static List<WorkflowStep> BuildSteps(Intent intent, string task, string? root, string? projectFile)
        {
            // projectFile and task are reserved for future path inference; currently the
            // steps rely on the caller providing explicit paths in placeholders.
            var steps = new List<WorkflowStep>();
            int stepNumber = 1;

            switch (intent.Name)
            {
                case "explore_project":
                    Add(steps, ref stepNumber, "GetProjectStructure", "Discover the project root, project file, and all modules/layouts.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "GetFullContext", "Get a compact, skeleton-level overview of the whole project.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    break;

                case "edit_code":
                    Add(steps, ref stepNumber, "GetProjectStructure", "Confirm project root and locate the target module(s).",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "GetFileContent", "Read the file you need to edit.",
                        new() { ["filePath"] = Placeholder("<filePath>") });
                    Add(steps, ref stepNumber, "AnalyzeModule", "Inspect the Subs in the target module before editing.",
                        new() { ["filePath"] = Placeholder("<filePath>") });
                    Add(steps, ref stepNumber, "EditSub", "Replace the specific Sub with the new implementation.",
                        new()
                        {
                            ["filePath"] = Placeholder("<filePath>"),
                            ["subName"] = Placeholder("<subName>"),
                            ["newCode"] = Placeholder("<newCode>")
                        });
                    Add(steps, ref stepNumber, "CompileProject", "Compile to verify the change.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    break;

                case "add_layout_control":
                    Add(steps, ref stepNumber, "GetProjectStructure", "Confirm the project and locate the layout file.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "ListLayoutControls", "Inspect the current layout controls before adding one.",
                        new() { ["layoutPath"] = Placeholder("<layoutPath>") });
                    Add(steps, ref stepNumber, "LayoutAddControl", "Add the new control to the layout.",
                        new()
                        {
                            ["layoutPath"] = Placeholder("<layoutPath>"),
                            ["controlType"] = Placeholder("<controlType>"),
                            ["controlName"] = Placeholder("<controlName>")
                        });
                    Add(steps, ref stepNumber, "GenerateCodeFromLayout", "Generate the Dim declaration and event Sub skeleton in the target module.",
                        new()
                        {
                            ["layoutPath"] = Placeholder("<layoutPath>"),
                            ["controlName"] = Placeholder("<controlName>"),
                            ["sourcePath"] = Placeholder("<sourcePath>"),
                            ["generate"] = "both"
                        });
                    Add(steps, ref stepNumber, "CompileProject", "Compile to verify the change.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    break;

                case "edit_layout":
                    Add(steps, ref stepNumber, "GetProjectStructure", "Locate the layout file.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "GetLayoutStructure", "Inspect the full layout JSON before modifying it.",
                        new() { ["layoutPath"] = Placeholder("<layoutPath>") });
                    Add(steps, ref stepNumber, "ListLayoutControls", "List controls to confirm names and positions.",
                        new() { ["layoutPath"] = Placeholder("<layoutPath>") });
                    Add(steps, ref stepNumber, "LayoutMoveControl", "Move/resize the control, or LayoutRemoveControl to delete it.",
                        new()
                        {
                            ["layoutPath"] = Placeholder("<layoutPath>"),
                            ["controlName"] = Placeholder("<controlName>")
                        });
                    Add(steps, ref stepNumber, "CompileProject", "Compile to verify the change.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    break;

                case "create_layout":
                    Add(steps, ref stepNumber, "GetProjectStructure", "Confirm project root and choose the layout file path.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "CreateEmptyLayout", "Generate a valid empty layout JSON for the target platform.",
                        new() { ["platform"] = Placeholder("<b4a|b4j>") });
                    Add(steps, ref stepNumber, "WriteLayout", "Write the JSON to a .bal/.bjl file inside the project.",
                        new()
                        {
                            ["layoutPath"] = Placeholder("<layoutPath>"),
                            ["jsonContent"] = Placeholder("<jsonContent>")
                        });
                    Add(steps, ref stepNumber, "RegisterLayoutInProject", "Register the new layout in the project metadata.",
                        new()
                        {
                            ["projectPath"] = Placeholder("<projectPath>"),
                            ["layoutPath"] = Placeholder("<layoutPath>")
                        });
                    Add(steps, ref stepNumber, "CompileProject", "Compile to verify the change.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    break;

                case "add_library":
                    Add(steps, ref stepNumber, "ListAvailableLibraries", "Check that the library exists and is available.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "EnableLibrary", "Add the library to the project metadata.",
                        new()
                        {
                            ["projectFile"] = Placeholder("<projectPath>"),
                            ["libraryName"] = Placeholder("<libraryName>")
                        });
                    Add(steps, ref stepNumber, "GetLibraryDocs", "Read the library API before writing code that uses it.",
                        new()
                        {
                            ["libraryName"] = Placeholder("<libraryName>"),
                            ["projectPath"] = Placeholder("<projectPath>")
                        });
                    Add(steps, ref stepNumber, "CompileProject", "Compile to verify the change.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    break;

                case "remove_library":
                    Add(steps, ref stepNumber, "ListProjectLibraries", "Confirm the library is currently enabled.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "DisableLibrary", "Remove the library from the project metadata.",
                        new()
                        {
                            ["projectFile"] = Placeholder("<projectPath>"),
                            ["libraryName"] = Placeholder("<libraryName>")
                        });
                    Add(steps, ref stepNumber, "CompileProject", "Compile to verify nothing breaks.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    break;

                case "compile_debug":
                    Add(steps, ref stepNumber, "ValidateProject", "Run a fast structural check before invoking the builder.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "CompileProject", "Compile and capture structured errors.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "SearchCode", "If errors mention unknown symbols, search for its definition.",
                        new()
                        {
                            ["projectPath"] = Placeholder("<projectPath>"),
                            ["pattern"] = Placeholder("<symbolPattern>")
                        });
                    break;

                case "debug_runtime":
                    Add(steps, ref stepNumber, "ValidateEventHandlers", "Statically detect event-handler signature mismatches (cheap; covers the most common runtime crash: java.lang.IllegalArgumentException 'argument type mismatch').",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "CompileProject", "Confirm the project compiles cleanly before launching it.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "RunProject", "Launch the compiled app (B4J), capture stdout/stderr and any Java exception with stack trace mapped back to B4X source.",
                        new()
                        {
                            ["projectPath"] = Placeholder("<projectPath>"),
                            ["runTimeoutSec"] = 30
                        });
                    Add(steps, ref stepNumber, "GetRuntimeErrorDetail", "If you already have a raw Java stack trace, map it to B4X file/Sub/line without re-running.",
                        new()
                        {
                            ["stackTrace"] = Placeholder("<pasteJavaStackTrace>"),
                            ["projectPath"] = Placeholder("<projectPath>")
                        });
                    break;

                case "search_code":
                    Add(steps, ref stepNumber, "SearchCode", "Search across .bas modules and optionally the project file.",
                        new()
                        {
                            ["projectPath"] = Placeholder("<projectPath>"),
                            ["pattern"] = Placeholder("<regexPattern>"),
                            ["includeProjectFile"] = true
                        });
                    break;

                case "api_reference":
                    Add(steps, ref stepNumber, "GetCoreApi", "Look up core B4X type signatures.",
                        new() { ["typeName"] = Placeholder("<optionalTypeName>") });
                    Add(steps, ref stepNumber, "GetLibraryDocs", "Or look up a specific library/class.",
                        new()
                        {
                            ["libraryName"] = Placeholder("<libraryName>"),
                            ["typeName"] = Placeholder("<optionalTypeName>"),
                            ["projectPath"] = Placeholder("<projectPath>")
                        });
                    break;

                case "create_module":
                    Add(steps, ref stepNumber, "GetProjectStructure", "Confirm project root and choose the new module path.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "CreateBasModule", "Create the .bas file with the correct IDE header.",
                        new()
                        {
                            ["filePath"] = Placeholder("<filePath>"),
                            ["moduleType"] = Placeholder("<activity|class>")
                        });
                    Add(steps, ref stepNumber, "RegisterModuleInProject", "Register the module in the project metadata.",
                        new()
                        {
                            ["projectPath"] = Placeholder("<projectPath>"),
                            ["modulePath"] = Placeholder("<filePath>")
                        });
                    Add(steps, ref stepNumber, "CompileProject", "Compile to verify the change.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    break;

                case "validate_project":
                    Add(steps, ref stepNumber, "GetProjectStructure", "Discover the project.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "ValidateProject", "Run structural validation on all modules.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    break;

                case "git_ops":
                    Add(steps, ref stepNumber, "GitStatus", "Check current repository state.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "GitDiff", "Review working-tree changes.",
                        new()
                        {
                            ["projectPath"] = Placeholder("<projectPath>"),
                            ["mode"] = "unstaged"
                        });
                    break;

                default:
                    Add(steps, ref stepNumber, "GetProjectStructure", "Start by exploring the project structure.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    break;
            }

            return steps;
        }

        private static void Add(List<WorkflowStep> steps, ref int stepNumber, string tool, string reason, Dictionary<string, object?> parameters)
        {
            steps.Add(new WorkflowStep
            {
                Step = stepNumber++,
                Tool = tool,
                Reason = reason,
                Params = parameters
            });
        }

        private static object Placeholder(string text) => $"{{{text}}}";

        private static List<string> BuildRequiredInfo(Intent intent, string? root, string? projectFile)
        {
            var required = new List<string>();

            if (string.IsNullOrWhiteSpace(root))
                required.Add("Provide a valid absolute projectPath so the guide can resolve the project root and file paths.");

            if (intent.Confidence == ConfidenceLevel.Low)
                required.Add("Clarify the task description so the router can pick a more specific workflow.");

            switch (intent.Name)
            {
                case "add_layout_control":
                case "edit_layout":
                case "create_layout":
                    required.Add("Identify the target layout file (e.g. Main.bal).");
                    break;
                case "edit_code":
                    required.Add("Identify the target module file, the Sub to edit, and the new Sub body (newCode).");
                    break;
                case "debug_runtime":
                    required.Add("For B4J, confirm the project has been compiled (Objects/<name>.jar exists). For B4A, attach an emulator/device before installing.");
                    break;
                case "add_library":
                case "remove_library":
                    required.Add("Identify the exact library name.");
                    break;
                case "create_module":
                    required.Add("Choose the new module file path and type (activity/class).");
                    break;
            }

            return required;
        }
    }
}
