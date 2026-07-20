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
        private readonly IProjectRepository _projectRepository;

        public WorkflowTools(IProjectRepository projectRepository)
        {
            _projectRepository = projectRepository;
        }

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
        public string GetWorkflowGuide(
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
                    : _projectRepository.FindProjectRoot(projectPath);
                resolvedProjectFile = resolvedRoot != null
                    ? _projectRepository.FindProjectFile(resolvedRoot)
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
                requiredInfo = BuildRequiredInfo(intent, task, resolvedRoot, resolvedProjectFile),
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

            // Layout-related tasks — all layout creation and modification is blocked.
            // Must be done manually by the programmer in the B4X Designer.
            if (ContainsAny(words, "layout", "bal", "bjl", "bil", "designer"))
            {
                return new Intent
                {
                    Name = "layout_blocked",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "Layout creation and modification is blocked. Layout changes must be made in the B4X Designer by the programmer directly."
                };
            }

            // Library management — all library enable/disable is blocked.
            // Must be done manually by the programmer through the IDE.
            if (ContainsAny(words, "library", "libraries", "enable", "disable", "add library", "remove library"))
            {
                return new Intent
                {
                    Name = "library_blocked",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "Library management is blocked. Libraries must be enabled/disabled manually by the programmer through the B4X IDE."
                };
            }

            // Module creation — file creation is blocked.
            // Must be done manually by the programmer in the B4X IDE.
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

            // Pure B4X API/feature lookup: the task names a B4X concept
            // (B4XPages, xCustomListView, ExoPlayer, etc.) and no more
            // specific intent above matched. We do NOT require a question
            // word here — a declarative mention like "add a B4XPages event
            // handler" or "switch from ListView to xCustomListView" should
            // still surface the bundled reference rather than fall through
            // to the generic explore_project fallback. Routed to
            // search_b4x_reference so the answer comes from the bundled
            // master reference rather than guessing.
            if (TryDetectB4xApiName(task) != null)
            {
                return new Intent
                {
                    Name = "b4x_api_lookup",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "The task is a pure lookup of a specific B4X API or language feature. The bundled B4X reference covers this directly."
                };
            }

            // Git operations. The first 6 keywords cover inspection / commit-push
            // flows; we also route here when the task names a repository ("repo"
            // / "repository") or uses "clone" so that git_init / git_clone can
            // be surfaced by the git_ops case below. Note we deliberately do
            // NOT add "init" alone — it overlaps with non-git usage
            // (initialize an object, Initialize() methods, etc.).
            if (ContainsAny(words, "git", "diff", "log", "status", "commit", "branch", "branches",
                            "repo", "repository", "clone"))
            {
                return new Intent
                {
                    Name = "git_ops",
                    Confidence = ConfidenceLevel.High,
                    Explanation = "The task involves git operations."
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

        // ── B4X reference helpers ──────────────────────────────────────

        // Names of B4X APIs / features that, when mentioned in a task
        // description, justify pre-fetching the relevant section of the
        // bundled B4X reference via search_b4x_reference. Substring match
        // (case-insensitive) — a task like "how do I use B4XPages" or
        // "switch to xCustomListView" both trigger. The list is intentionally
        // a flat array: order is irrelevant, and the matches line up with the
        // reference's section naming, not necessarily the runtime API names.
        // Adding a new B4X API? Just append a string here and the workflow
        // guide starts recommending search_b4x_reference automatically.
        private static readonly string[] B4xApiNames =
        {
            "B4XPages", "B4XPage_", "B4XPage", "B4XView", "B4XCanvas", "B4XBitmap", "B4XFont",
            "B4XRect", "B4XPath", "B4XDialog", "B4XDialogs", "B4XCollections",
            "B4XFormatter", "B4XMainPage", "B4XOrderedMap",
            "XUI", "xCustomListView",
            "ResumableSub",
            "ResultSet", "SQL", "DBUtils", "ExecQuery", "ExecNonQuery",
            "Msgbox", "ExoPlayer", "VideoView",
            "NumberFormat", "CallSubDelayed", "CallSubPlus",
            "StartServiceAt", "StartReceiverAt",
            "AsyncStreams", "HttpJob", "JavaObject", "NativeObject",
            "Process_Globals", "Application_Error",
            "NotInitialized",
            "Smart String", "smart string", "Round2",
            "xui.DefaultFolder", "xui.Msgbox",
        };

        /// <summary>
        /// Which intents get the B4X reference preamble prepended. Excludes
        /// pure ops/validation (git_ops, search_code, validate_project,
        /// explore_project, remove_library, compile_debug) and the
        /// <c>b4x_api_lookup</c> intent itself (which already drives the
        /// reference tools explicitly). All code-touching intents and
        /// <c>api_reference</c> are included so the AI has the bundled
        /// reference on hand before it starts.
        /// </summary>
        private static bool ShouldAddB4xReferencePreamble(string intentName) =>
            intentName is
                "edit_code" or
                "create_module" or
                "compile_debug" or
                "debug_runtime" or
                "api_reference";

        /// <summary>
        /// Returns the first B4X API name found as a case-insensitive
        /// substring in <paramref name="task"/>, or <c>null</c> if no
        /// match. The first match (in <see cref="B4xApiNames"/> declaration
        /// order) is returned, so callers that need a stable name can rely
        /// on the array order.
        /// </summary>
        private static string? TryDetectB4xApiName(string task)
        {
            if (string.IsNullOrEmpty(task)) return null;
            foreach (var name in B4xApiNames)
            {
                if (task.Contains(name, StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            return null;
        }

        /// <summary>
        /// Builds 2-3 preamble steps that surface the bundled B4X reference
        /// tools. Step 1 is always <c>ListB4xReferenceSections</c> (cheap
        /// table-of-contents call). Step 2 is always <c>GetLanguageGotchas</c>
        /// so the model has the bundled "what to avoid" list on hand. If the
        /// task description mentions a known B4X API, a third
        /// <c>SearchB4xReference</c> step is added targeting that API so the
        /// model has the relevant section ready before the intent-specific
        /// work begins.
        /// </summary>
        private static List<WorkflowStep> BuildB4xReferencePreamble(int startingStep, string task)
        {
            var steps = new List<WorkflowStep>();
            int n = startingStep;
            Add(steps, ref n, "ListB4xReferenceSections",
                "Survey the bundled B4X reference sections so you can pull just the slice you need during the task.",
                new Dictionary<string, object?>());
            Add(steps, ref n, "GetLanguageGotchas",
                "Read the bundled 'what to avoid' gotcha list before writing any B4X code.",
                new Dictionary<string, object?>());
            var api = TryDetectB4xApiName(task);
            if (api != null)
            {
                Add(steps, ref n, "SearchB4xReference",
                    $"The task mentions '{api}'. Search the bundled B4X reference for it before writing code.",
                    new() { ["query"] = api });
            }
            return steps;
        }

        // ── Git sub-intent helpers ───────────────────────────────────

        /// <summary>
        /// Coarse classification of a git_ops task. <see cref="None"/> means the
        /// task is the default inspection / commit-push flow (status → diff →
        /// add → commit → push). <see cref="InitRepo"/> and <see cref="CloneRepo"/>
        /// are the two repo-creation flows that need their first step to be
        /// <c>GitInit</c> or <c>GitClone</c>. <see cref="ShowDiff"/>,
        /// <see cref="ShowCommit"/>, and <see cref="ListBranches"/> are the three
        /// read-only inspection flows that surface <c>GitDiff</c>/<c>GitDiffFull</c>,
        /// <c>GitLog</c>+<c>GitShow</c>, and <c>GitBranchList</c> respectively,
        /// and skip the add/commit/push follow-up. <see cref="MergeBranch"/> is
        /// the merge flow that surfaces <c>GitMerge</c> + a post-merge
        /// <c>GitStatus</c> (no add/commit/push follow-up — the merge commit IS
        /// the commit).
        /// </summary>
        private enum GitSubIntent
        {
            None,
            InitRepo,
            CloneRepo,
            ShowDiff,
            ShowCommit,
            ListBranches,
            MergeBranch
        }

        /// <summary>
        /// Detects whether a task routed to <c>git_ops</c> is specifically about
        /// creating / initializing a new repo (<see cref="GitSubIntent.InitRepo"/>)
        /// or cloning an existing one (<see cref="GitSubIntent.CloneRepo"/>).
        /// Returns <see cref="GitSubIntent.None"/> for the default
        /// status/diff/commit/push flow.
        /// </summary>
        /// <remarks>
        /// Both <paramref name="words"/> and <paramref name="lowerTask"/> must
        /// already be lower-cased so substring / word comparisons are
        /// case-insensitive by construction. Phrase checks run first (more
        /// specific, less ambiguous); single-word checks are fallbacks for
        /// terse prompts like "clone the b4x repo".
        /// </remarks>
        private static GitSubIntent DetectGitSubIntent(HashSet<string> words, string lowerTask)
        {
            // Multi-word phrases — checked first because they are the most
            // specific (and least likely to misfire on non-git usage).
            if (ContainsPhrase(lowerTask,
                    "initialize a repo", "init a repo", "init the repo", "init this repo",
                    "create a new repo", "create a new repository",
                    "new git repo", "new repository", "git init"))
            {
                return GitSubIntent.InitRepo;
            }
            if (ContainsPhrase(lowerTask,
                    "git clone", "check out a repo", "checkout a repo",
                    "clone the repo", "clone the repository",
                    "clone a repo", "clone a repository",
                    "clone this repo", "clone this repository"))
            {
                return GitSubIntent.CloneRepo;
            }
            // Show-diff / "what changed". Phrases cover the natural English
            // variations; the single-word fallback below picks up terse prompts.
            if (ContainsPhrase(lowerTask,
                    "show the diff", "show me the diff", "show diff", "show changes",
                    "show all changes", "show pending changes", "show uncommitted",
                    "what changed", "what's changed", "whats changed",
                    "what is changed", "what was changed", "what has changed",
                    "uncommitted changes", "working tree changes", "working-tree changes"))
            {
                return GitSubIntent.ShowDiff;
            }
            // Show-commit. Phrases cover "show me the last commit", "view commit X",
            // and the natural language a user would type when they want details
            // for a specific revision.
            if (ContainsPhrase(lowerTask,
                    "show commit", "show me commit", "show the commit",
                    "show a commit", "show this commit", "show last commit",
                    "show latest commit", "show previous commit",
                    "show the last commit", "show the latest commit",
                    "show the previous commit", "view commit", "inspect commit",
                    "show details of commit", "show details for commit"))
            {
                return GitSubIntent.ShowCommit;
            }
            // Merge-branch. Phrased here BEFORE ListBranches so the multi-word
            // "merge branches" / "merge the branch" cases match MergeBranch
            // first; the single-word "merge" fallback below is similarly
            // placed before the "branches" fallback for the same reason.
            if (ContainsPhrase(lowerTask,
                    "merge branches", "merge the branches",
                    "merge branch", "merge the branch", "merge this branch",
                    "merge that branch", "merge a branch",
                    "merge into", "merge from",
                    "merge changes from", "merge in changes from",
                    "git merge"))
            {
                return GitSubIntent.MergeBranch;
            }
            // List-branches. The plural 'branches' is also a top-level trigger
            // word in DetectIntent so the user lands in git_ops in the first place.
            if (ContainsPhrase(lowerTask,
                    "list branches", "show branches", "show all branches",
                    "list all branches", "show remote branches", "list remote branches",
                    "what branches", "which branches", "list local branches",
                    "show local branches"))
            {
                return GitSubIntent.ListBranches;
            }

            // Single-word fallbacks. 'clone' alone is a strong git signal even
            // without "repo" (e.g. "clone the B4X samples") because the
            // routing intent already filtered for git_ops. 'init' alone is too
            // broad (Initialize() methods, init blocks), so we require the
            // companion 'repo' / 'repository' word.
            if (words.Contains("clone"))
            {
                return GitSubIntent.CloneRepo;
            }
            if ((words.Contains("init") || words.Contains("initialize")) &&
                (words.Contains("repo") || words.Contains("repository")))
            {
                return GitSubIntent.InitRepo;
            }

            // Inspection-flow single-word fallbacks. 'diff' alone is a strong
            // inspection signal (the user said "diff" and the intent detector
            // already routed them here). 'changes' alone is too ambiguous with
            // "commit my changes", so it is NOT a fallback — it only fires via
            // the phrase checks above. 'commit' alone is also ambiguous
            // (commit vs show-commit), so it requires an explicit show verb.
            if (words.Contains("diff"))
            {
                return GitSubIntent.ShowDiff;
            }
            if (words.Contains("commit") &&
                (words.Contains("show") || words.Contains("view") || words.Contains("inspect")))
            {
                return GitSubIntent.ShowCommit;
            }
            // 'merge' alone is unambiguous in git_ops context (we've already
            // filtered for git here), so route to MergeBranch. Must run BEFORE
            // the branches fallback, otherwise "merge branches" would hit
            // ListBranches first and surface GitBranchList instead of GitMerge.
            if (words.Contains("merge"))
            {
                return GitSubIntent.MergeBranch;
            }
            if (words.Contains("branches") ||
                (words.Contains("branch") && (words.Contains("list") || words.Contains("show") || words.Contains("all"))))
            {
                return GitSubIntent.ListBranches;
            }

            return GitSubIntent.None;
        }

        private static bool ContainsPhrase(string haystack, params string[] needles)
        {
            foreach (var n in needles)
            {
                if (haystack.Contains(n)) return true;
            }
            return false;
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

            // B4X reference preamble: surface the bundled reference tools before
            // any code-touching workflow so the AI has the relevant guidance on
            // hand. Excluded intents are listed in ShouldAddB4xReferencePreamble.
            if (ShouldAddB4xReferencePreamble(intent.Name))
            {
                foreach (var s in BuildB4xReferencePreamble(stepNumber, task))
                {
                    steps.Add(s);
                    stepNumber++;
                }
            }

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

                case "layout_blocked":
                    Add(steps, ref stepNumber, "GetProjectStructure", "Inspect the project to understand the current layout structure (read-only).",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "GetLayoutStructure", "Read the layout structure as JSON for reference (read-only).",
                        new() { ["layoutPath"] = Placeholder("<layoutPath>") });
                    break;

                case "library_blocked":
                    Add(steps, ref stepNumber, "ListProjectLibraries", "List the currently enabled libraries (read-only).",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "ListAvailableLibraries", "List all available libraries on the system (read-only).",
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
                    Add(steps, ref stepNumber, "GetB4xReference", "Or pull a specific section of the bundled B4X reference for deeper language / cross-platform guidance.",
                        new() { ["sectionName"] = Placeholder("<optionalSectionName>") });
                    break;

                case "b4x_api_lookup":
                    // Intent-specific: build a 2-3 step plan that drives the
                    // three B4X-reference tools directly. The preamble is
                    // skipped for this intent (see ShouldAddB4xReferencePreamble)
                    // because we already cover the same ground here with more
                    // structure: discover sections, search, then optionally
                    // fetch the full section content.
                    Add(steps, ref stepNumber, "ListB4xReferenceSections",
                        "Discover the available sections of the bundled B4X reference.",
                        new Dictionary<string, object?>());
                    if (TryDetectB4xApiName(task) is string apiName)
                    {
                        Add(steps, ref stepNumber, "SearchB4xReference",
                            $"Search the bundled B4X reference for '{apiName}'.",
                            new() { ["query"] = apiName });
                        Add(steps, ref stepNumber, "GetB4xReference",
                            "If the hit looks relevant, fetch the full section content.",
                            new() { ["sectionName"] = apiName });
                    }
                    else
                    {
                        Add(steps, ref stepNumber, "GetB4xReference",
                            "Fetch the full reference for context.",
                            new Dictionary<string, object?>());
                    }
                    break;

                case "create_module":
                    Add(steps, ref stepNumber, "GetProjectStructure", "Confirm project root and choose the new module path.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "CreateBasModule", "Show the manual steps the user must follow in the B4X IDE to create the module safely.",
                        new()
                        {
                            ["filePath"] = Placeholder("<filePath>"),
                            ["moduleType"] = Placeholder("<activity|class>")
                        });
                    Add(steps, ref stepNumber, "CompileProject", "After the user has created and registered the module in the IDE, compile to verify.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    break;

                case "validate_project":
                    Add(steps, ref stepNumber, "GetProjectStructure", "Discover the project.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    Add(steps, ref stepNumber, "ValidateProject", "Run structural validation on all modules.",
                        new() { ["projectPath"] = Placeholder("<projectPath>") });
                    break;

                case "git_ops":
                    {
                        // Detect which git sub-flow the task is asking for.
                        // - InitRepo / CloneRepo: prepend the repo-creation tool
                        //   and let the canonical status → diff → add → commit →
                        //   push follow-up run as the natural next steps (the AI
                        //   skips whichever ones don't apply).
                        // - ShowDiff / ShowCommit / ListBranches: read-only
                        //   inspection flows — surface the right read tool and
                        //   skip the commit-push follow-up entirely.
                        // - None: the default status → diff → add → commit → push
                        //   flow.
                        var lowerTask = task.ToLowerInvariant();
                        var taskWords = lowerTask.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                        var sub = DetectGitSubIntent(taskWords, lowerTask);

                        switch (sub)
                        {
                            case GitSubIntent.CloneRepo:
                                Add(steps, ref stepNumber, "GitClone",
                                    "Clone the remote repository into targetDir. The default branch is checked out automatically — no separate checkout step is required. Uses a 120s timeout to accommodate large repos.",
                                    new()
                                    {
                                        ["url"] = Placeholder("<repositoryUrl>"),
                                        ["targetDir"] = Placeholder("<targetDir>")
                                    });
                                break;

                            case GitSubIntent.InitRepo:
                                Add(steps, ref stepNumber, "GitInit",
                                    "Initialize a new git repository at projectPath. With --bare=true, create a bare repository instead (no working tree).",
                                    new() { ["projectPath"] = Placeholder("<projectPath>") });
                                break;

                            case GitSubIntent.ShowDiff:
                                // Two-step inspection: --stat first (fast, won't
                                // time out even on huge diffs) then full content
                                // for the lines the AI actually wants to inspect.
                                Add(steps, ref stepNumber, "GitDiff",
                                    "Show the --stat summary of working-tree changes first (fast, won't time out) so you can see at a glance which files changed and by how much.",
                                    new()
                                    {
                                        ["projectPath"] = Placeholder("<projectPath>"),
                                        ["mode"] = "unstaged"
                                    });
                                Add(steps, ref stepNumber, "GitDiffFull",
                                    "Fetch the full unified-diff content for the working-tree changes. Output can be very long — read just the relevant hunk(s) if the diff is large.",
                                    new()
                                    {
                                        ["projectPath"] = Placeholder("<projectPath>"),
                                        ["mode"] = "unstaged"
                                    });
                                break;

                            case GitSubIntent.ShowCommit:
                                // GitLog first so the AI can identify the commit
                                // SHA / HEAD position, then GitShow to inspect it.
                                // The revision placeholder is filled by the AI with
                                // HEAD, HEAD~N, a SHA, or a branch name.
                                Add(steps, ref stepNumber, "GitLog",
                                    "List recent commits so you can identify the revision the task is about.",
                                    new()
                                    {
                                        ["projectPath"] = Placeholder("<projectPath>"),
                                        ["count"] = 20
                                    });
                                Add(steps, ref stepNumber, "GitShow",
                                    "Show details (metadata + change stats) for the specific commit. Pass revision= with the SHA / HEAD / HEAD~N to inspect. With filePath, shows the file's content at that revision instead of the commit info.",
                                    new()
                                    {
                                        ["projectPath"] = Placeholder("<projectPath>"),
                                        ["revision"] = Placeholder("<revision>")
                                    });
                                break;

                            case GitSubIntent.ListBranches:
                                // Pass allRemotes=true to also include remote-
                                // tracking branches — useful when the user is
                                // asking "what branches exist on the remote".
                                Add(steps, ref stepNumber, "GitBranchList",
                                    "List local branches. Pass allRemotes=true to also list remote-tracking branches.",
                                    new()
                                    {
                                        ["projectPath"] = Placeholder("<projectPath>"),
                                        ["allRemotes"] = false
                                    });
                                break;

                            case GitSubIntent.MergeBranch:
                                // Merge step first, then status to confirm the
                                // result. With noFf=true for a real merge commit,
                                // or squash=true to stage a single squashed commit
                                // (then commit separately). abort=true ignores
                                // branch= and aborts an in-progress conflicted
                                // merge.
                                Add(steps, ref stepNumber, "GitMerge",
                                    "Merge the source branch into the current branch. Pass branch= with the source branch (or remote-tracking branch like 'origin/dev'). Pass noFf=true to force a merge commit (preserves branch topology). Pass squash=true to stage a single squashed commit (you must commit it separately). Pass abort=true to abort an in-progress merge with conflicts — branch is ignored in that case.",
                                    new()
                                    {
                                        ["projectPath"] = Placeholder("<projectPath>"),
                                        ["branch"] = Placeholder("<sourceBranch>")
                                    });
                                // Status right after the merge to (a) confirm the
                                // merge succeeded, or (b) detect conflict markers
                                // in any conflicted files that need manual
                                // resolution before the user can commit. Does
                                // NOT continue into the add/commit/push flow —
                                // the merge commit IS the commit, and pushing it
                                // is a separate decision the AI should make
                                // deliberately.
                                Add(steps, ref stepNumber, "GitStatus",
                                    "After the merge attempt, check the working-tree state to confirm the merge succeeded or to detect conflict markers in any files that need manual resolution.",
                                    new() { ["projectPath"] = Placeholder("<projectPath>") });
                                // Optional push: most users want to share the
                                // merge commit with teammates once it lands. The
                                // AI can skip this if the user only wants a
                                // local merge (e.g. into a personal branch).
                                Add(steps, ref stepNumber, "GitPush",
                                    "Optional: push the merge commit to the remote so teammates see it (60s network timeout). Skip this step if the user only wanted a local merge.",
                                    new()
                                    {
                                        ["projectPath"] = Placeholder("<projectPath>")
                                    });
                                break;
                        }

                        // Append the canonical status → diff → add → commit → push
                        // follow-up for sub-intents that mutate the repo (or are
                        // explicitly about doing so). Inspection sub-intents
                        // (ShowDiff / ShowCommit / ListBranches) skip it — the
                        // user just wants to look, not change anything.
                        if (sub is GitSubIntent.None or GitSubIntent.InitRepo or GitSubIntent.CloneRepo)
                        {
                            AddGitWorkflowFollowUp(steps, ref stepNumber);
                        }
                    }
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

        /// <summary>
        /// Appends the canonical git_ops follow-up: GitStatus → GitDiff →
        /// GitAdd → GitCommit → GitPush. Used for sub-intents that mutate the
        /// repo (or are explicitly about doing so): <see cref="GitSubIntent.None"/>,
        /// <see cref="GitSubIntent.InitRepo"/>, and <see cref="GitSubIntent.CloneRepo"/>.
        /// Inspection sub-intents (ShowDiff / ShowCommit / ListBranches) skip
        /// this entirely.
        /// </summary>
        private static void AddGitWorkflowFollowUp(List<WorkflowStep> steps, ref int stepNumber)
        {
            Add(steps, ref stepNumber, "GitStatus", "Check current repository state.",
                new() { ["projectPath"] = Placeholder("<projectPath>") });
            Add(steps, ref stepNumber, "GitDiff", "Review working-tree changes.",
                new()
                {
                    ["projectPath"] = Placeholder("<projectPath>"),
                    ["mode"] = "unstaged"
                });
            // Stage and commit are the common follow-up actions; surface them
            // so the AI doesn't have to guess at the tool names. The AI can
            // skip any step that doesn't apply.
            Add(steps, ref stepNumber, "GitAdd",
                "Stage the files you want to commit. Pass a comma-separated filePaths= list to be explicit, or set all=true to stage everything (careful: that picks up .env, secrets, build outputs).",
                new() { ["projectPath"] = Placeholder("<projectPath>") });
            Add(steps, ref stepNumber, "GitCommit", "Create a commit with a descriptive message.",
                new()
                {
                    ["projectPath"] = Placeholder("<projectPath>"),
                    ["message"] = Placeholder("<commitMessage>")
                });
            Add(steps, ref stepNumber, "GitPush",
                "Push the new commit to the remote (60s network timeout). Pass setUpstream=true on the first push of a new branch.",
                new()
                {
                    ["projectPath"] = Placeholder("<projectPath>"),
                    ["setUpstream"] = true
                });
        }

        private static object Placeholder(string text) => $"{{{text}}}";

        private static List<string> BuildRequiredInfo(Intent intent, string task, string? root, string? projectFile)
        {
            var required = new List<string>();

            if (string.IsNullOrWhiteSpace(root))
                required.Add("Provide a valid absolute projectPath so the guide can resolve the project root and file paths.");

            if (intent.Confidence == ConfidenceLevel.Low)
                required.Add("Clarify the task description so the router can pick a more specific workflow.");

            switch (intent.Name)
            {
                case "layout_blocked":
                    required.Add("Layout changes must be made manually by the programmer in the B4X Designer. Use read_layout or list_layout_controls to inspect the layout for reference.");
                    break;
                case "library_blocked":
                    required.Add("Library management must be done manually by the programmer through the B4X IDE. Use list_project_libraries or list_available_libraries for reference.");
                    break;
                case "edit_code":
                    required.Add("Identify the target module file, the Sub to edit, and the new Sub body (newCode).");
                    break;
                case "debug_runtime":
                    required.Add("For B4J, confirm the project has been compiled (Objects/<name>.jar exists). For B4A, attach an emulator/device before installing.");
                    break;

                case "create_module":
                    required.Add("Choose the new module file path and type (activity/class).");
                    break;
                case "git_ops":
                    {
                        // Detect sub-intent again so required-info can ask for
                        // the right missing piece (URL+targetDir for clone,
                        // projectPath for init, revision for show-commit, etc.).
                        // Same routine as in BuildSteps — kept duplicated rather
                        // than threaded through the Intent object to keep the
                        // diff minimal.
                        var lowerTask = task.ToLowerInvariant();
                        var taskWords = lowerTask.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                        var sub = DetectGitSubIntent(taskWords, lowerTask);
                        switch (sub)
                        {
                            case GitSubIntent.CloneRepo:
                                required.Add("Provide the repository URL (HTTPS, SSH, git://, or a local file path) and the absolute target directory where the clone should be created.");
                                break;
                            case GitSubIntent.InitRepo:
                                required.Add("Provide the absolute directory path where the new repository should be initialized (the directory will be created if it does not exist).");
                                break;
                            case GitSubIntent.ShowCommit:
                                required.Add("Provide the revision to inspect: 'HEAD', 'HEAD~N' (N steps back), a short or full SHA, or a branch name.");
                                break;
                            case GitSubIntent.ListBranches:
                                required.Add("If you also want remote-tracking branches, mention 'all' or 'remote' in your task so the workflow guide sets allRemotes=true.");
                                break;
                            case GitSubIntent.MergeBranch:
                                required.Add("Provide the source branch to merge in (e.g. 'feature-x', or a remote-tracking branch like 'origin/dev'). The current branch is the target — switch to the target branch first if needed.");
                                break;
                            // ShowDiff: nothing extra beyond the generic
                            // projectPath requirement already added at the top.
                        }
                    }
                    break;
            }

            return required;
        }
    }
}
