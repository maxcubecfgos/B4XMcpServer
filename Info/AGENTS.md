<!-- BEGIN B4X MCP (auto-generated) -->

# B4X project helper

This directory contains a B4X-aware helper executable at `./B4XMcpServer.exe`.
The exe can compile, lint, encode/decode layouts, search libraries, manage git and more for this project.

Before invoking the exe, verify it exists at that path (PowerShell: `Test-Path ./B4XMcpServer.exe`; Unix shells: `test -x ./B4XMcpServer.exe`). If it has been moved or deleted, ask the user where it lives or look in well-known locations such as `C:\src\b4xtools`.

Run `./B4XMcpServer.exe --help` to see the complete list of tools with usage examples before using any of them.

## Tool usage rules

- ❌ **Don't use your built-in search/Grep** — use `search_code` instead.
- ❌ **Don't use shell commands** (`grep`, `findstr`, `type`, `cat`, `dir`, `cd`, `echo`, `Write-Host`) — use the dedicated MCP tools below.
- ❌ **Don't manually read/parse files** — use `get_file_content`.
- ❌ **Don't manually edit/write code** — use `edit_sub`, `edit_line`, `insert_line`, or `replace_lines`. (Pass `newContent=""` to `edit_line` or `replace_lines` to **delete** lines.)
- ❌ **Don't compile via shell** — use `compile_project`.
- ❌ **Don't manually decode layouts** — use `get_layout_structure`.
- ❌ **Don't manually manage libraries** — use `enable_library` / `disable_library`.
- ✅ **Always prefer MCP tools. They exist because shell commands don't understand B4X project structure.**

## B4X module structure (important)

Every B4X module (`.bas`, `.b4a`, `.b4j`) has the same internal layout:

1. **Metadata header** (IDE settings like `NumberOfModules=`, `Library1=`, `@EndOfDesignText@`) — preserved by editing tools. **Never modify header rows via code tools** — use `enable_library`/`disable_library`/`write_manifest`/`register_layout_in_project`/`register_module_in_project` instead.
2. **Source code section** (after `@EndOfDesignText@`).

**Line numbering is universally FILE-LINE.** All editing tools (`edit_line`, `insert_line`, `replace_lines`, `edit_sub`), `analyze_module`, and `compile_project` error output use **1-based line numbers counted from the absolute first line of the file, INCLUDING the IDE metadata header** — the same convention as B4X compile error output. `get_file_content` returns an explicit `lines` array `[{line: FILE_LINE, text: "..."}, ...]` plus `lineNumbering="file"` and `lineOffset` (header size); the legacy `content` field is source-line only and kept for backward compatibility — **always prefer the `lines` array when you need line numbers**. Editor calls targeting header lines `[1, lineOffset]` are rejected; targeting the source code section lines `[lineOffset + 1, totalLines]` is allowed.

The source code section ALWAYS starts with:

- `#Region Project Attributes` — **UNTOUCHABLE** (#ApplicationLabel, #VersionCode, etc.)
- `#Region Activity Attributes` (B4A only) — **UNTOUCHABLE** (#FullScreen, #IncludeTitle, etc.)
- `Sub Process_Globals` — where **app-wide variables** go (persist across activities)
- `Sub Globals` — where **module-level variables** go
- Then lifecycle Subs: `Activity_Create`, `Activity_Resume`, `Activity_Pause`, etc.

### edit_sub CRITICAL rules

- `newCode` MUST include BOTH the `Sub ...` header line AND the matching `End Sub` line.
- Forgetting `End Sub` corrupts the module. If you only need to change a few lines, use `edit_line` instead.
- To add a NEW Sub, use `insert_line` — NOT `edit_sub`.
- To add a variable, add a `Dim` or `Private` line INSIDE `Process_Globals` or `Globals` (depending on scope).

## How to invoke a tool

```
./B4XMcpServer.exe <tool-name> [--parameter=value ...]
```

Output is JSON on stdout. Errors go to stderr. Exit code 0 = success; non-zero = failure.

## Available tools

Each call starts a fresh process, so prefer batching multiple edits in a single run whenever practical.

- **`get_b4x_reference`**
  Returns a section of the bundled B4X development reference. Pass a section name to get just that section; omit to get the full reference. Use list_b4x_reference_sections first to see the available section names (e.g. 'Database (SQLite)', 'XUI Library', 'Best Practices'). The reference is the same content the AGENTS.md installer drops at .b4x-mcp/skills/b4x/reference.md in every B4X project — these tools expose it as structured slices so the model can load just what it needs without paying the cost of a full ~600-line read every time.

- **`get_core_api`**
  Returns the exact signatures for B4X core API: List, Map, Timer, String, Intent, Activity, DateTime, Bit, Regex, Matcher. Use this to verify method names, parameter types, and return types before writing code. Never guess a method signature — check it here first.

- **`get_language_gotchas`**
  Returns critical B4A/B4J language gotchas and pitfalls that frequently cause hard-to-debug bugs. Call this when starting work on a B4X project or when encountering unexpected behavior. Covers: case-insensitivity, variable shadowing, File.Exists with DirAssets, reserved keywords (Is, Rnd, ATan2), Color component extraction, Application_Error pitfalls, B4XView API, project file structure rules, and more.

- **`list_b4x_reference_sections`**
  Lists the section names of the bundled B4X development reference in document order. Call this before get_b4x_reference to discover which sections are available (e.g. 'Core Philosophy', 'The B4XPages Framework', 'XUI Library', 'Database (SQLite)', 'Best Practices (What to Avoid)').

- **`search_b4x_reference`**
  Searches the bundled B4X development reference for a case-insensitive substring and returns up to 10 matching sections, each clipped to a short snippet around the first hit. Use this when you don't know the section name but remember a keyword (e.g. 'ResumableSub', 'ExoPlayer', 'round 2', 'B4XPages'). For full content, follow up with get_b4x_reference(sectionName=...).

- **`ping`**
  Sanity-check tool. Confirms the B4X MCP server is running and can be called by the AI client.

- **`analyze_module`**
  Analyzes a SINGLE .bas, .b4a, or .b4j module file: lists every Sub (name, parameters, return type, public/private, event handler detection), every Type declaration, and Globals presence. Also reports structural parse issues without compiling. **All returned `subs[*].startLine` / `endLine` and `types[*].startLine` / `endLine` are FILE-LINE** (1-based from the first line of the file); `issues[*].Line` is also FILE-LINE so it can be passed straight into `edit_line` / `insert_line`.

  CRITICAL B4X STRUCTURE: The first two Subs in any module are ALWAYS:
    • Sub Process_Globals — declares app-wide/process-wide variables (persist across activities).
    • Sub Globals — declares activity/module-level variables.
  These are NOT optional — they are the ONLY valid places for global variable declarations.
  In .b4a/.b4j project files, the source code section also starts with:
    • #Region Project Attributes — UNTOUCHABLE (#ApplicationLabel, #VersionCode, etc.)
    • #Region Activity Attributes — UNTOUCHABLE (#FullScreen, #IncludeTitle, etc.)
  These region blocks appear BEFORE Sub Process_Globals in the source code section of .b4a/.b4j files. They are NOT editable via edit_sub — only edit_line can modify individual lines inside them if absolutely necessary.
  Use edit_sub to modify existing Subs; use insert_line to add NEW Subs.

- **`compile_project`**
  Compiles a B4X project (B4A, B4J, or B4i) using the platform-correct builder selected automatically from the project file extension.
  
  *** CRITICAL: This is the ONLY way to compile. NEVER run shell commands (dir, cd, type, cat, B4ABuilder.exe, etc.). If compilation fails, this tool returns the exact errors with file names, line numbers, and source lines. READ THEM and fix the code — do not try to debug by running commands manually. ***

- **`create_bas_module`**
  DEPRECATED — do not use. Creating .bas modules automatically has proven unreliable and can corrupt the project. This tool now returns the exact manual steps the user must follow in the B4X IDE to create and register a new module safely.

- **`edit_line`**
  Replaces a single line in a B4X source file (.bas, .b4a, .b4j, .b4i, or any text file) by its **FILE-LINE** number (1-based from the first line of the file, INCLUDING the IDE metadata header) — the same convention as `compile_project` error output. Pass an empty string as newContent to DELETE the line (subsequent lines shift up). Use this for surgical fixes where you know exactly which line to change (e.g. fixing a typo on line 42). **Header lines [1, lineOffset] are rejected with a hard error** — use `enable_library` etc. for those. Pass expectedText as an atomic safety check to abort if the line has shifted. Creates a .bak backup before writing. For replacing an entire Sub use edit_sub instead; for replacing the whole file use write_file.

- **`edit_sub`**
  Replaces the entire body of a single Sub by name in a B4X module in-place, without touching the rest of the file. Safe for .b4a/.b4j/.b4i project files because it preserves the IDE metadata header and only edits the source code section. CRITICAL: newCode MUST include both the 'Sub ...' header line AND the matching 'End Sub' line — if you forget 'End Sub', the Sub will be corrupted. Use this for modifying existing Subs only; to add a NEW Sub use insert_line instead. If the Sub isn't found, returns the list of Subs that do exist in the file so the caller can retry with the correct name. **Response fields `originalLineRange.start`/`end` and the tool's `lineOffset` are in FILE-LINE** so they can be passed straight into `edit_line` / `insert_line`. Creates a .bak backup first.

- **`get_file_content`**
  Returns the full text content of a file (B4X module .bas, project file .b4a/.b4j/.b4i, or any other text file). For .bas and project files, it returns BOTH a legacy `content` string (source code section only, source-line numbering) AND an explicit `lines` array `[{line: FILE_LINE, text: "..."}, ...]` with the canonical **FILE-LINE** numbers (1-based from the first line of the file, INCLUDING the IDE metadata header). Also returns `lineNumbering="file"`, `lineOffset` (header size), and `totalLines` so you can compute the editor-acceptable range `[lineOffset + 1, totalLines]`. Always prefer `lines` when writing to the editing tools.

- **`get_full_context`**
  Builds a single consolidated Markdown context bundle for a B4X project: an ASCII file tree plus every module/layout, in skeleton form (signatures only, bodies collapsed) by default to keep it compact. Pass FocusFile to keep one specific file fully expanded, or FocusFile+FocusSub to keep just that one Sub expanded while the rest of that same file stays collapsed. Optionally compiles first and attaches real errors. This is the token-efficient alternative to dumping the whole project — use get_file_content afterward for any other single file you need in full.

- **`get_layout_structure`**
  Decodes a B4X visual layout file into readable JSON: control hierarchy, types, positions (resolved from the correct screen variant, not the misleading top-level template defaults), and properties like text/hint/tag/drawable. Works for both .bal (B4A) and .bjl (B4J) — they share the exact same binary format.

- **`get_manifest`**
  Extracts the Manifest Editor block from a B4A project file.

- **`get_project_config`**
  Parses a B4X project file's project metadata into structured JSON: app type, version, referenced libraries, module list, included files, and every other raw key=value setting. Accepts the project FOLDER or the project FILE — both work. This tool is about the PROJECT as a whole (metadata, libs, modules), NOT about a single .bas module — for that use analyze_module.

- **`get_project_structure`**
  Returns the structure of a B4X project (B4A or B4J): the project root, the .b4a/.b4j/.b4i project file, and every module (.bas) and layout (.bal/.bjl/.bil) file found, ignoring build folders (Objects/bin/gen/obj). Accepts either the project folder path or the path to the .b4a/.b4j/.b4i file itself.

- **`insert_line`**
  Inserts new content as one or more lines at a given 1-based **FILE-LINE** position in a B4X source file (.bas, .b4a, .b4j, .b4i, or any text file), shifting all subsequent lines down. Use this for adding new Subs, Dim declarations, or comments above existing lines without disturbing surrounding code. Allowed range is `[lineOffset + 1, totalLines + 1]` — `lineOffset + 1` inserts at the very top of the source code section, `totalLines + 1` appends after the last existing line. **Insertion at header lines `[1, lineOffset]` is rejected**; use `enable_library` etc. for those. newContent may contain embedded newlines (`\n`) — each becomes its own inserted line. Creates a .bak backup before writing. For replacing an existing line use edit_line; for replacing an entire Sub use edit_sub; for rewriting the whole file use write_file.

- **`list_layouts`**
  Lists every layout file (.bal/.bjl/.bil) in a project with basic metadata: screen variants and top-level control count.

- **`replace_lines`**
  Replaces a CONTIGUOUS RANGE of inclusive `[startLine, endLine]` FILE-LINE numbers in a B4X source file (.bas, .b4a, .b4j, .b4i, or any text file) with new content, in the spirit of edit_line but spanning multiple lines. The range is inclusive on both ends. Allowed range is `[lineOffset + 1, totalLines]` — header rows are rejected with a hard error. For .b4a/.b4j/.b4i project files the IDE metadata header is preserved automatically. newContent may contain embedded newlines (`\n`) — each becomes its own inserted line. Pass `newContent=""` to DELETE the range entirely (lines after endLine shift up). When newContent has more lines than the original range, lines after shift DOWN; when fewer, they shift UP; when equal, no shift. For single-line precision or deleting one line use edit_line; for inserting new lines use insert_line.

- **`search_code`**
  Searches for a regex pattern across every .bas module (and optionally the .b4a/.b4j project file) in a B4X project, like grep. Returns each match with its file, line number, and the matching line's text.

- **`validate_event_handlers`**
  Statically validates every event handler Sub in a B4X project against the event signatures declared in the referenced libraries. Reports parameter count, name, and type mismatches (e.g. Int vs Double) that cause runtime crashes like java.lang.IllegalArgumentException. Also infers control types from Dim declarations and layout files.

- **`validate_project`**
  Runs the B4X structural parser against every module (.bas) in a project WITHOUT compiling, and reports any structural problems found (unclosed Sub/Type/Region blocks, mismatched End statements). Near-instant sanity check.

- **`write_file`**
  Writes (overwrites) a file with the given content. This replaces the entire file, so read it first with get_file_content if you need to preserve parts of it. Typically used to save an edited B4X module back to disk. BLOCKED for existing .b4a/.b4j/.b4i project files to prevent IDE metadata corruption — use edit_sub for code, enable_library/disable_library for libraries, write_manifest for manifest, and register_layout_in_project/register_module_in_project for layouts/modules. Creates a .bak backup first if the file already exists.

- **`write_manifest`**
  Replaces the Manifest Editor block in a B4A project file. Creates a .bak backup first.

- **`create_empty_layout`**
  Creates an empty B4X layout file by cloning an existing IDE-created layout in the same project and removing all of its controls. This avoids the incompatible binary format produced by writing a layout from JSON. Requires at least one existing .bal/.bjl/.bil layout in the project to use as a template.

- **`generate_code_from_layout`**
  Generates B4X code for a layout control: inserts a Dim declaration in the appropriate Globals section and/or appends an event Sub skeleton at the end of the file. Reads the control type from the layout to produce correct type annotations (e.g. Private btn As B4XView). Creates .bak backup before modifying.

- **`layout_add_control`**
  Adds a new control to an existing layout file. Valid control types for B4A: Button, Label, EditText, Panel, CheckBox, RadioButton, Spinner, ListView, ImageView, WebView, ScrollView, TabStrip. For B4J: Button, Label, TextField, TextArea, CheckBox, RadioButton, ComboBox, ListView, ImageView, Slider, DatePicker. Creates .bak backup first.

- **`layout_move_control`**
  Moves and/or resizes a control in a layout. Only specify the properties you want to change — omitted values keep their current setting. Creates .bak backup first.

- **`layout_remove_control`**
  Removes one or more controls from a layout by name. Names can be a single string or comma-separated list. Creates .bak backup first.

- **`list_layout_controls`**
  Lists all controls in a layout file with their name, type, position, size, and children hierarchy. Use this to understand the structure before adding, removing, or moving controls.

- **`register_layout_in_project`**
  Registers a layout file in the project metadata so the IDE and builder recognize it. Adds FileN= and FileGroupN= entries to the project header, updates NumberOfFiles, and creates .bak backup. If the layout is already registered, does nothing.

- **`register_module_in_project`**
  DEPRECATED — do not use. Registering .bas modules automatically has proven unreliable and can corrupt the project metadata. This tool now returns the exact manual steps the user must follow in the B4X IDE to register a new module safely.

- **`write_layout`**
  DISABLED. This tool is no longer available because writing a layout from JSON produces binaries incompatible with the B4X IDE. Use create_empty_layout instead.

- **`get_logcat`**
  Returns recent Android logcat output filtered to the 'B4A' tag (where B4A's Log() calls and unhandled exceptions show up). Use this to catch runtime crashes/errors that a compile-time check can't see — the natural complement to compile_project.

- **`list_devices`**
  Lists Android devices/emulators currently connected and visible to ADB.

- **`git_add`**
  Stages files for the next commit. Pass filePaths as a comma-separated list of paths relative to the repo root (or absolute paths) to stage only those. With all=true, stages every change including deletions and new untracked files (git add -A). Without explicit filePaths, requires all=true — there is no implicit 'stage everything' to prevent accidental staging of .env files, secrets, or build outputs.

- **`git_branch_checkout`**
  Switches to an existing branch. With create=true, creates the branch first if it doesn't exist (git checkout -b <name>). Pass a revision (e.g. 'HEAD~3', a SHA) to detach HEAD and inspect the working tree at that point in history.

- **`git_branch_create`**
  Creates a new branch. With checkout=true, also switches to it (git checkout -b). With startPoint, the branch is created at that revision (e.g. 'main', 'HEAD~3', a SHA) instead of the current HEAD. To delete a branch, use git_branch_delete instead.

- **`git_branch_delete`**
  Deletes a branch. With force=true, force-deletes even if the branch has unmerged commits (git branch -D); without it, the delete fails if there are unmerged commits (git branch -d). To delete a remote branch, use git_push with the remote name and ':<branch>' as the refspec instead — that is how Git itself is designed to delete remote branches.

- **`git_branch_list`**
  Lists local branches. Pass allRemotes=true to also list remote-tracking branches. The currently checked-out branch is marked with '*'.

- **`git_clone`**
  Clones a git repository from a URL (HTTPS, SSH, git://, or a local file path) into targetDir. The clone always checks out the default branch automatically — no separate checkout step is needed or supported. targetDir must NOT already exist as a non-empty directory; the parent directory must exist and be writable. Authentication uses your pre-configured credential helper / SSH keys; interactive prompts are disabled, so credentials must already be set up. Large repos can be slow — uses a 120s timeout.

- **`git_commit`**
  Creates a git commit with the provided message. With all=true, automatically stages all tracked-but-modified files (git commit -a) so the commit captures them without an explicit git add. With amend=true, replaces the previous commit's content and message (REWRITES HISTORY — never use on commits that have already been pushed/shared).

- **`git_diff`**
  Shows git diff --stat for the repository containing the given path. mode='unstaged' (default, working tree changes not yet staged), mode='staged' (changes added with git add), or a revision range like 'HEAD~1..HEAD' or 'main..feature'. Returns file names and change counts (fast, won't time out).

- **`git_diff_full`**
  Shows the full unified diff content (not just --stat) for changes. mode='unstaged' (default), 'staged' (--cached), or a revision range like 'HEAD~1..HEAD'. Pass context to control the number of unified-diff context lines (default 3). Returns the full diff text — may be very long for large changes; prefer git_diff --stat for a quick overview.

- **`git_fetch`**
  Fetches from a remote without merging or rebasing — just updates the remote-tracking refs. With allRemotes=true, fetches from every configured remote. With prune=true, removes remote-tracking refs that no longer exist on the remote. Does NOT modify your local branches.

- **`git_init`**
  Initializes a new git repository at projectPath. Creates the directory if it does not exist; if it already contains a repo, git re-initializes it (safe, no data loss). With bare=true, creates a bare repository (no working tree) — useful as a remote or central server. Does NOT add a remote or make any commits; use git_remote_list/git_add/git_commit afterward for that.

- **`git_log`**
  Shows recent git commit history for the repository. Returns last N commits with hash, author, date, and message in a compact format.

- **`git_merge`**
  Merges a branch into the current branch. Pass branch= the source branch (or remote-tracking branch like 'origin/dev') to merge in. With noFf=true, always create a merge commit even if a fast-forward is possible (preserves the branch topology). With squash=true, stage all merged changes as a single squashed commit; you must then commit separately. With abort=true, abort an in-progress merge with conflicts — branch is ignored in this case. Local operation — uses the default 30s timeout.

- **`git_pull`**
  Pulls from a remote and merges (or rebases, with rebase=true) into the current branch. If remote/branch are omitted, uses the upstream configured for the current branch. Network operation — runs with a 60s timeout.

- **`git_push`**
  Pushes the current branch to a remote. With setUpstream=true, also sets the upstream tracking ref so future push/pull commands work without arguments. With force=true, uses --force-with-lease (NOT bare --force): it refuses to overwrite if the remote advanced past your last known position, which prevents accidentally clobbering a teammate's commit. Use only on branches you own. Network operation — runs with a 60s timeout.

- **`git_remote_list`**
  Lists configured git remotes with their fetch/push URLs (git remote -v).

- **`git_reset`**
  Resets the current HEAD to a specified state. mode='soft' (keep changes staged), 'mixed' (default; keep changes unstaged), or 'hard' (DESTRUCTIVE: discards ALL working-tree and index changes — requires confirmHardReset=true as a safety check). Pass a revision to reset to (e.g. 'HEAD~1', a SHA, or a branch); default HEAD, which is a no-op.

- **`git_show`**
  Shows details for a specific commit (hash, author, date, full message, and the change stats). Pass a revision (e.g. 'HEAD', 'HEAD~1', a short or full SHA, or a branch name). With filePath, shows the file's content at that revision instead of the full commit info (git show <rev>:<file>).

- **`git_stash`**
  Manages git stashes — saves uncommitted working-tree changes to a stack you can reapply later. action='list' (default) shows all stashes; 'save' stashes working-tree changes (pass message= for a description, includeUntracked=true to also stash new untracked files); 'pop' applies and removes the top stash (pass index= for a specific one, 0-based, 0=top); 'apply' applies but does not remove; 'drop' removes a stash without applying it; 'show' shows a stash's diff stat.

- **`git_status`**
  Shows current git status: current branch, staged changes, unstaged changes, and untracked files in a compact format.

- **`git_unstage`**
  Unstages files that have been added but not yet committed (git restore --staged). Pass filePaths as a comma-separated list. Use this to undo a mistaken git add without losing the file content.

- **`compare_with_library_signature`**
  Compares a single user-written Sub signature against the expected library event signature for a given library/type/event. Returns whether it matches and a list of differences.

- **`disable_library`**
  Disables (removes) a library from a B4X project. Also renumbers remaining LibraryN entries so there are no gaps. Creates .bak backup first.

- **`enable_library`**
  Enables a library in a B4X project by adding it to the LibraryN keys in the project file header. If already enabled, does nothing. Creates .bak backup first. Refuses to enable a library that isn't found in any configured library directory (B4A, B4J, AdditionalLibraries, project-local Libraries/) — so a typo can't later break compilation.

- **`get_library_docs`**
  Returns the documented methods, properties, and events of a B4X library in compact format: kind (method/property/event), name, return type, parameters, and a one-line description. Use this to discover what a library can do before writing code that uses it.

- **`get_library_events`**
  Returns the event declarations for a given library/type. Use this to discover the exact parameter names and types an event expects before writing its handler Sub.

- **`list_available_libraries`**
  Lists every available library found in all configured library folders (B4A, B4J, AdditionalLibrariesFolder from IDE settings, plus project-local Libraries/). Includes name and version.

- **`list_project_libraries`**
  Lists all libraries referenced in a B4X project file (.b4a/.b4j/.b4i): reads the Library1, Library2... keys from the IDE metadata header. Returns the library names in order.

- **`search_library`**
  Searches all available library documentation for methods, properties, or events matching a query string. Searches in member names, type names, and descriptions. Use this to find which library provides a specific feature, or to discover how to use a method.

- **`get_workflow_guide`**
  Call this FIRST when you are unsure which B4X tools to use or in what order. Describe the task in plain English and (optionally) pass the project path. You will get back a detected intent, a confidence level, an explanation, and a step-by-step plan of tool calls to execute. Follow the steps in order.

- **`get_b4x_stack_trace`**
  Returns the most recent unhandled exception captured by the last run_project call for this project, already mapped to B4X source. Use this when the AI just ran the app and wants to dig into the failure without re-running it.

- **`get_runtime_error_detail`**
  Parses a raw Java stack trace (e.g. pasted from the user's console output) and maps each frame to the corresponding B4X source file, Sub, and line in the project. Use this when you have a stack trace but no live run, or when run_project timing out prevents a fresh capture.

- **`launch_debug`**
  Alias for run_project, signals that the AI expects to capture a crash (e.g. when investigating a user-reported runtime error). Returns early once an exception is captured so the AI can move directly to fixing it.

- **`run_project`**
  Runs a compiled B4X project and captures stdout/stderr along with any unhandled Java exception. The exception's stack trace is automatically mapped back to the B4X source file, Sub, and line, with a heuristic cause suggestion. Currently supports B4J (java -jar the built Objects/<name>.jar); B4A requires DeviceTools.install_apk + get_logcat, and B4i is not supported by this tool.

## Bundled skills

This helper bundles two B4X reference files at `.b4x-mcp/skills/b4x/`:

- **`SKILL.md`** — discovery manifest (YAML frontmatter; describes when to load the reference and which rules always apply).
- **`reference.md`** — full B4X development reference: B4XPages, XUI, SQLite, Resumable Subs, custom views, and the "what to avoid" table.

Load them when starting any B4X task, when reviewing generated code, or when deciding whether an old snippet found online is still current. The installer keeps them in sync with the running executable: a fresh `B4XMcpServer.exe` automatically refreshes the files when the version changes, and never overwrites user-authored content at the same path.

The same content is also exposed as MCP tools so you can pull just the slice you need without paying the cost of re-reading the full 600-line file every time:

- **`list_b4x_reference_sections`** — table of contents. Call first to discover what sections exist (e.g. "Database (SQLite)", "XUI Library", "Best Practices").
- **`search_b4x_reference --query="..."`** — keyword search; returns up to 10 sections with clipped snippets. Use when you remember a feature but not the section name (e.g. `query="ExoPlayer"`, `query="ResumableSub"`).
- **`get_b4x_reference --sectionName="..."`** — fetch one section in full. Follow up on a search hit. Omit `sectionName` to get the whole reference (avoid unless you actually need it).
- **`get_language_gotchas`** — compact list of the "what to avoid" entries from the reference; useful before any B4X code change. `get_workflow_guide` auto-recommends it on B4X tasks.

## Best practice after any code edit

Always finish by compiling to verify nothing broke:

```
./B4XMcpServer.exe compile_project --projectPath=.
```

If the exit code is non-zero, read the JSON `buildErrors` array from stdout for the exact file/line/message of each compile error. Do not invoke the builder manually — `compile_project` is the only supported path.

---

This block is auto-generated by `B4XMcpServer.exe`. Editing it by hand works until the next install, at which point invalid markers trigger a regeneration that overwrites your edits. To regenerate deliberately, delete `AGENTS.md` and re-run the exe from the project directory.
<!-- END B4X MCP (auto-generated) -->

