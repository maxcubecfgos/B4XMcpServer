using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ModelContextProtocol.Server;

namespace B4XMcpServer.Services
{
    /// <summary>
    /// Self-installing <c>AGENTS.md</c> generator. When the executable is launched
    /// manually (stdio not fully piped — i.e. from a terminal or by double-clicking),
    /// this runs before the MCP host and either writes a fresh AGENTS.md in the
    /// project directory, appends a marked block to one that already exists without
    /// our marker, or does nothing if the file already has the marker or we are not
    /// inside a B4X project. The result tells the host whether to exit cleanly or
    /// to fall through to normal MCP server mode.
    /// <para>
    /// MCP-aware clients (Claude Desktop, Cursor, Cline…) pipe both stdio streams
    /// and therefore skip this logic entirely — they go straight to MCP server
    /// mode unchanged. A known false-positive exists on terminals like Git Bash /
    /// mintty that report stdout as redirected even for interactive sessions; the
    /// workaround documented in README.md is to invoke the exe from Windows
    /// PowerShell or cmd instead.
    /// </para>
    /// </summary>
    public static class B4xProjectInstaller
    {
        /// <summary>Marker searched to detect that the B4X MCP block is already installed.</summary>
        public const string BeginMarker = "<!-- BEGIN B4X MCP (auto-generated) -->";

        /// <summary>Closing marker of the auto-generated block. Visual marker only; detection reads BEGIN.</summary>
        public const string EndMarker = "<!-- END B4X MCP (auto-generated) -->";

        private const string AgentsFileName = "AGENTS.md";

        public enum Outcome
        {
            /// <summary>The installer wrote or appended AGENTS.md. The caller should exit.</summary>
            Installed,
            /// <summary>Nothing to do (already installed, or not in a B4X project). The caller should fall through to MCP mode.</summary>
            Skipped,
        }

        /// <summary>
        /// Inspects <see cref="Environment.CurrentDirectory"/> and either writes,
        /// appends or skips the AGENTS.md bootstrap. Idempotent across re-runs.
        /// </summary>
        /// <returns>
        /// <see cref="Outcome.Installed"/> if AGENTS.md was written or appended (caller exits);
        /// <see cref="Outcome.Skipped"/> if nothing was changed (caller falls through).
        /// </returns>
        public static Outcome TryRun()
        {
            string cwd = Environment.CurrentDirectory;
            string agentsPath = Path.Combine(cwd, AgentsFileName);

            if (!IsInsideB4xProject(cwd))
            {
                // Not in (or directly adjacent to) a B4X project. Behave normally —
                // no output, no log noise. The host will start MCP server mode in
                // case the caller is a remote MCP client that happens to launch
                // the exe from a non-project directory.
                return Outcome.Skipped;
            }

            bool existingHasMarker = File.Exists(agentsPath)
                && File.ReadAllText(agentsPath).Contains(BeginMarker, StringComparison.Ordinal);

            if (existingHasMarker)
            {
                // Idempotent path: the B4X MCP block is already present, no work to do.
                return Outcome.Skipped;
            }

            string template = RenderTemplate();

            if (!File.Exists(agentsPath))
            {
                File.WriteAllText(agentsPath, template);
                Log($"✓ Created {AgentsFileName} in {cwd}");
            }
            else
            {
                // Case C2: append the block to the END of the existing file, preserving
                // any user-authored content above. Two newlines between the user's
                // content and our block, matching common Markdown paragraph spacing.
                // Append is safe to re-run because (a) we re-check the marker first
                // so we never duplicate the block, and (b) any later edits to the
                // appended block invalidate the marker and trigger a re-install.
                string existing = File.ReadAllText(agentsPath);
                string prefix = existing.TrimEnd('\r', '\n');
                string updated = prefix + "\n\n" + template;
                File.WriteAllText(agentsPath, updated);
                Log($"✓ Appended B4X MCP block to existing {AgentsFileName} in {cwd}");
            }

            // After AGENTS.md is settled, also install the bundled B4X skill at
            // <cwd>/.b4x-mcp/skills/b4x/. Strict parallel idempotency with the
            // marker check above: if the marker was already intact (the
            // early-return path) we never reach here, so a clean re-run leaves
            // existing skill files alone — matching how AGENTS.md itself is
            // treated. Errors are logged but never bubble up, mirroring how
            // IsInsideB4xProject swallows IO errors during detection.
            SkillInstaller.TryInstall(cwd);

            return Outcome.Installed;
        }

        /// <summary>
        /// Shallow scan of <paramref name="dir"/> for B4X project files. Honors the
        /// user-facing spec verbatim: "el directorio en que se encuentra" = the
        /// directory the exe is launched from; only files at the top level count.
        /// Subdirectories are intentionally NOT inspected — a project whose
        /// <c>.b4a</c> lives one level deeper than the exe's launch dir will not
        /// match, by design.
        /// </summary>
        private static bool IsInsideB4xProject(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                if (Directory.EnumerateFiles(dir, "*.b4a", SearchOption.TopDirectoryOnly).Any()) return true;
                if (Directory.EnumerateFiles(dir, "*.b4j", SearchOption.TopDirectoryOnly).Any()) return true;
                if (Directory.EnumerateFiles(dir, "*.b4i", SearchOption.TopDirectoryOnly).Any()) return true;
                return false;
            }
            catch
            {
                // Permission errors, IO errors: treat as "no project found" rather
                // than crashing the installer — the host will still spin up if the
                // caller explicitly wants MCP mode.
                return false;
            }
        }

        /// <summary>
        /// Builds the markdown block wrapped between <see cref="BeginMarker"/> and
        /// <see cref="EndMarker"/>. Tool inventory is reflection-generated from
        /// <see cref="SupportedTools.AllTypes"/> — the same list the MCP host wires
        /// up via <c>WithTools&lt;T&gt;()</c> in <see cref="Program"/>.
        /// </summary>
        private static string RenderTemplate()
        {
            var sb = new StringBuilder();
            sb.AppendLine(BeginMarker);
            sb.AppendLine();
            sb.AppendLine("# B4X project helper");
            sb.AppendLine();
            sb.AppendLine("This directory contains a B4X-aware helper executable at `./B4XMcpServer.exe`.");
            sb.AppendLine("The exe can compile, lint, encode/decode layouts, search libraries, manage git and more for this project.");
            sb.AppendLine();
            sb.AppendLine("Before invoking the exe, verify it exists at that path (PowerShell: `Test-Path ./B4XMcpServer.exe`; Unix shells: `test -x ./B4XMcpServer.exe`). If it has been moved or deleted, ask the user where it lives or look in well-known locations such as `C:\\src\\b4xtools`.");
            sb.AppendLine();
            sb.AppendLine("Run `./B4XMcpServer.exe --help` to see the complete list of tools with usage examples before using any of them.");
            sb.AppendLine();
            sb.AppendLine("## Tool usage rules");
            sb.AppendLine();
            sb.AppendLine("- ❌ **Don't use your built-in search/Grep** — use `search_code` instead.");
            sb.AppendLine("- ❌ **Don't use shell commands** (`grep`, `findstr`, `type`, `cat`, `dir`, `cd`, `echo`, `Write-Host`) — use the dedicated MCP tools below.");
            sb.AppendLine("- ❌ **Don't manually read/parse files** — use `get_file_content`.");
            sb.AppendLine("- ❌ **Don't manually edit/write code** — use `edit_sub`, `edit_line`, `insert_line`, or `replace_lines`. (Pass `newContent=\"\"` to `edit_line` or `replace_lines` to **delete** lines.)");
            sb.AppendLine("- ❌ **Don't compile via shell** — use `compile_project`.");
            sb.AppendLine("- ❌ **Don't manually decode layouts** — use `get_layout_structure`.");
            sb.AppendLine("- ❌ **Don't manually manage libraries** — use `enable_library` / `disable_library`.");
            sb.AppendLine("- ✅ **Always prefer MCP tools. They exist because shell commands don't understand B4X project structure.**");
            sb.AppendLine();
            sb.AppendLine("## B4X module structure (important)");
            sb.AppendLine();
            sb.AppendLine("Every B4X module (`.bas`, `.b4a`, `.b4j`) has the same internal layout:");
            sb.AppendLine();
            sb.AppendLine("1. **Metadata header** (IDE settings like `NumberOfModules=`, `Library1=`, `@EndOfDesignText@`) — preserved by editing tools. **Never modify header rows via code tools** — use `enable_library`/`disable_library`/`write_manifest`/`register_layout_in_project`/`register_module_in_project` instead.");
            sb.AppendLine("2. **Source code section** (after `@EndOfDesignText@`).");
            sb.AppendLine();
            sb.AppendLine("**Line numbering is universally FILE-LINE.** All editing tools (`edit_line`, `insert_line`, `replace_lines`, `edit_sub`), `analyze_module`, and `compile_project` error output use **1-based line numbers counted from the absolute first line of the file, INCLUDING the IDE metadata header** — the same convention as B4X compile error output. `get_file_content` returns an explicit `lines` array `[{line: FILE_LINE, text: \"...\"}, ...]` plus `lineNumbering=\"file\"` and `lineOffset` (header size); the legacy `content` field is source-line only and kept for backward compatibility — **always prefer the `lines` array when you need line numbers**. Editor calls targeting header lines `[1, lineOffset]` are rejected; targeting the source code section lines `[lineOffset + 1, totalLines]` is allowed.");
            sb.AppendLine();
            sb.AppendLine("The source code section ALWAYS starts with:");
            sb.AppendLine();
            sb.AppendLine("- `#Region Project Attributes` — **UNTOUCHABLE** (#ApplicationLabel, #VersionCode, etc.)");
            sb.AppendLine("- `#Region Activity Attributes` (B4A only) — **UNTOUCHABLE** (#FullScreen, #IncludeTitle, etc.)");
            sb.AppendLine("- `Sub Process_Globals` — where **app-wide variables** go (persist across activities)");
            sb.AppendLine("- `Sub Globals` — where **module-level variables** go");
            sb.AppendLine("- Then lifecycle Subs: `Activity_Create`, `Activity_Resume`, `Activity_Pause`, etc.");
            sb.AppendLine();
            sb.AppendLine("### edit_sub CRITICAL rules");
            sb.AppendLine();
            sb.AppendLine("- `newCode` MUST include BOTH the `Sub ...` header line AND the matching `End Sub` line.");
            sb.AppendLine("- Forgetting `End Sub` corrupts the module. If you only need to change a few lines, use `edit_line` instead.");
            sb.AppendLine("- To add a NEW Sub, use `insert_line` — NOT `edit_sub`.");
            sb.AppendLine("- To add a variable, add a `Dim` or `Private` line INSIDE `Process_Globals` or `Globals` (depending on scope).");
            sb.AppendLine();
            sb.AppendLine("## How to invoke a tool");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine("./B4XMcpServer.exe <tool-name> [--parameter=value ...]");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("Output is JSON on stdout. Errors go to stderr. Exit code 0 = success; non-zero = failure.");
            sb.AppendLine();
            sb.AppendLine("## Available tools");
            sb.AppendLine();
            sb.AppendLine("Each call starts a fresh process, so prefer batching multiple edits in a single run whenever practical.");
            sb.AppendLine();

            foreach (var block in EnumerateToolBlocks())
            {
                sb.Append("- **`").Append(block.Name).AppendLine("`**");
                if (!string.IsNullOrEmpty(block.Description))
                {
                    // Two-space indent folds correctly inside the bullet item even when
                    // the description spans multiple lines.
                    foreach (var line in block.Description.Split('\n'))
                    {
                        sb.Append("  ").AppendLine(line.TrimEnd('\r'));
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("## Bundled skills");
            sb.AppendLine();
            sb.AppendLine("This helper bundles two B4X reference files at `.b4x-mcp/skills/b4x/`:");
            sb.AppendLine();
            sb.AppendLine("- **`SKILL.md`** — discovery manifest (YAML frontmatter; describes when to load the reference and which rules always apply).");
            sb.AppendLine("- **`reference.md`** — full B4X development reference: B4XPages, XUI, SQLite, Resumable Subs, custom views, and the \"what to avoid\" table.");
            sb.AppendLine();
            sb.AppendLine("Load them when starting any B4X task, when reviewing generated code, or when deciding whether an old snippet found online is still current. The installer keeps them in sync with the running executable: a fresh `B4XMcpServer.exe` automatically refreshes the files when the version changes, and never overwrites user-authored content at the same path.");
            sb.AppendLine();
            sb.AppendLine("The same content is also exposed as MCP tools so you can pull just the slice you need without paying the cost of re-reading the full 600-line file every time:");
            sb.AppendLine();
            sb.AppendLine("- **`list_b4x_reference_sections`** — table of contents. Call first to discover what sections exist (e.g. \"Database (SQLite)\", \"XUI Library\", \"Best Practices\").");
            sb.AppendLine("- **`search_b4x_reference --query=\"...\"`** — keyword search; returns up to 10 sections with clipped snippets. Use when you remember a feature but not the section name (e.g. `query=\"ExoPlayer\"`, `query=\"ResumableSub\"`).");
            sb.AppendLine("- **`get_b4x_reference --sectionName=\"...\"`** — fetch one section in full. Follow up on a search hit. Omit `sectionName` to get the whole reference (avoid unless you actually need it).");
            sb.AppendLine("- **`get_language_gotchas`** — compact list of the \"what to avoid\" entries from the reference; useful before any B4X code change. `get_workflow_guide` auto-recommends it on B4X tasks.");
            sb.AppendLine();
            sb.AppendLine("## Best practice after any code edit");
            sb.AppendLine();
            sb.AppendLine("Always finish by compiling to verify nothing broke:");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine("./B4XMcpServer.exe compile_project --projectPath=.");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("If the exit code is non-zero, read the JSON `buildErrors` array from stdout for the exact file/line/message of each compile error. Do not invoke the builder manually — `compile_project` is the only supported path.");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("This block is auto-generated by `B4XMcpServer.exe`. Editing it by hand works until the next install, at which point invalid markers trigger a regeneration that overwrites your edits. To regenerate deliberately, delete `AGENTS.md` and re-run the exe from the project directory.");
            sb.AppendLine(EndMarker);
            sb.AppendLine();

            return sb.ToString();
        }

        private readonly record struct ToolBlock(string Name, string Description);

        /// <summary>
        /// Walks <see cref="SupportedTools.AllTypes"/> and yields one
        /// <see cref="ToolBlock"/> per <c>[McpServerTool]</c> method, in stable order.
        /// </summary>
        private static IEnumerable<ToolBlock> EnumerateToolBlocks()
        {
            foreach (var type in SupportedTools.AllTypes)
            {
                var methods = type.GetMethods(
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => m.GetCustomAttribute<McpServerToolAttribute>(inherit: false) != null)
                    .OrderBy(m => m.Name, StringComparer.Ordinal);

                foreach (var method in methods)
                {
                    var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>()!;
                    var descAttr = method.GetCustomAttribute<DescriptionAttribute>();

                    // Honor McpServerToolAttribute.Name when explicitly set. Otherwise,
                    // derive a snake_case version of the method name to match what the
                    // MCP SDK emits when registering tools via WithTools<T>() — the
                    // agent reading this AGENTS.md should see the same names the
                    // MCP-aware clients (Claude Code, etc.) see.
                    string name = !string.IsNullOrEmpty(toolAttr.Name)
                        ? toolAttr.Name
                        : ToSnakeCase(method.Name);

                    string description = descAttr?.Description?.Trim() ?? string.Empty;

                    yield return new ToolBlock(name, description);
                }
            }
        }

        /// <summary>
        /// PascalCase to snake_case conversion matching the MCP SDK's default tool
        /// naming scheme. Inserts an underscore before each uppercase letter that
        /// follows a non-uppercase letter, then lowercases the whole string.
        /// Examples: <c>GetProjectStructure → get_project_structure</c>;
        /// <c>CompileProject → compile_project</c>.
        /// </summary>
        private static string ToSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Diagnostic output goes to <see cref="Console.Error"/> only — never stdout,
        /// which is reserved for MCP JSON-RPC frames and for tool JSON output during
        /// CLI invocation.
        /// </summary>
        private static void Log(string message)
        {
            Console.Error.WriteLine(message);
        }
    }
}
