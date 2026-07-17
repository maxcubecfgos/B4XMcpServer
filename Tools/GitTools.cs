using B4XMcpServer.Repositories;
using B4XMcpServer.Services;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class GitTools
    {
        public GitTools(IFileRepository fileRepository, IProjectRepository projectRepository)
        {
        }

        [McpServerTool, Description("Shows git diff --stat for the repository containing the given path. mode='unstaged' (default, working tree changes not yet staged), mode='staged' (changes added with git add), or a revision range like 'HEAD~1..HEAD' or 'main..feature'. Returns file names and change counts (fast, won't time out).")]
        public async Task<string> GitDiff(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Diff mode: 'unstaged' (default), 'staged' (--cached), or a git revision range like 'HEAD~1..HEAD'.")] string mode = "unstaged",
            [Description("Optional: limit diff to a specific file path relative to repo root.")] string? filePath = null)
        {
            string dir = GetWorkingDir(projectPath);
            var args = new List<string> { "-c", "color.ui=never", "diff", "--stat" };
            if (mode == "staged")
            {
                args = new List<string> { "-c", "color.ui=never", "diff", "--cached", "--stat" };
            }
            else if (mode != "unstaged")
            {
                // mode is a revision range like HEAD~1..HEAD
                args.Add(mode);
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                args.Add("--");
                args.Add(filePath);
            }

            return await RunGitAsync(dir, args);
        }

        [McpServerTool, Description("Shows recent git commit history for the repository. Returns last N commits with hash, author, date, and message in a compact format.")]
        public async Task<string> GitLog(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Number of recent commits to show. Default 20, max 100.")] int count = 20,
            [Description("Optional: show history for a specific file only.")] string? filePath = null)
        {
            count = Math.Clamp(count, 1, 100);
            string dir = GetWorkingDir(projectPath);
            var args = new List<string> { "-c", "color.ui=never", "log", "--oneline", $"-{count}" };
            if (!string.IsNullOrEmpty(filePath))
            {
                args.Add("--");
                args.Add(filePath);
            }

            return await RunGitAsync(dir, args);
        }

        [McpServerTool, Description("Shows current git status: current branch, staged changes, unstaged changes, and untracked files in a compact format.")]
        public async Task<string> GitStatus(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath)
        {
            string dir = GetWorkingDir(projectPath);
            return await RunGitAsync(dir, new List<string> { "-c", "color.ui=never", "status", "--short", "--branch" });
        }

        private static string GetWorkingDir(string path)
        {
            return Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? ".";
        }

        private static async Task<string> RunGitAsync(string workingDir, List<string> arguments, int timeoutMs = 30000)
        {
            var env = new Dictionary<string, string>
            {
                // Prevent git from ever invoking a pager or an interactive credential/SSH prompt,
                // either of which would hang forever with no console attached.
                ["GIT_PAGER"] = "cat",
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["GCM_INTERACTIVE"] = "never"
            };

            try
            {
                var result = await ProcessRunner.RunAsync("git", arguments, workingDir, timeoutMs, env);
                var output = result.Output.Trim();
                return string.IsNullOrEmpty(output) ? "(no output)" : output;
            }
            catch (Exception ex)
            {
                return $"Error running git: {ex.Message}";
            }
        }

        // ── Staging ─────────────────────────────────────────────────

        [McpServerTool, Description("Stages files for the next commit. Pass filePaths as a comma-separated list of paths relative to the repo root (or absolute paths) to stage only those. With all=true, stages every change including deletions and new untracked files (git add -A). Without explicit filePaths, requires all=true — there is no implicit 'stage everything' to prevent accidental staging of .env files, secrets, or build outputs.")]
        public async Task<string> GitAdd(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Optional: comma-separated list of file paths (relative to repo root or absolute) to stage. If empty, requires all=true.")] string? filePaths = null,
            [Description("If true, stage all changes including new untracked files (git add -A). Required when filePaths is null/empty.")] bool all = false)
        {
            if (string.IsNullOrWhiteSpace(filePaths) && !all)
            {
                return "Error: must supply filePaths or set all=true. Refusing to stage nothing.";
            }

            var args = new List<string> { "-c", "color.ui=never", "add" };
            if (all)
            {
                args.Add("-A");
            }
            if (!string.IsNullOrWhiteSpace(filePaths))
            {
                foreach (var p in filePaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    args.Add(p);
                }
            }
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        [McpServerTool, Description("Unstages files that have been added but not yet committed (git restore --staged). Pass filePaths as a comma-separated list. Use this to undo a mistaken git add without losing the file content.")]
        public async Task<string> GitUnstage(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Comma-separated list of file paths (relative to repo root or absolute) to unstage.")] string filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePaths))
            {
                return "Error: filePaths must not be empty.";
            }

            var args = new List<string> { "-c", "color.ui=never", "restore", "--staged" };
            foreach (var p in filePaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                args.Add(p);
            }
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        // ── Commits ────────────────────────────────────────────────

        [McpServerTool, Description("Creates a git commit with the provided message. With all=true, automatically stages all tracked-but-modified files (git commit -a) so the commit captures them without an explicit git add. With amend=true, replaces the previous commit's content and message (REWRITES HISTORY — never use on commits that have already been pushed/shared).")]
        public async Task<string> GitCommit(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Commit message. Multi-line strings are passed through verbatim and become the full commit message (subject + body).")] string message,
            [Description("If true, stage all tracked-but-modified files before committing (git commit -a). Default false.")] bool all = false,
            [Description("If true, amend the previous commit with this message (git commit --amend). Default false. WARNING: rewrites history — never amend a commit that has been pushed/shared.")] bool amend = false)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "Error: message must not be empty.";
            }

            var args = new List<string> { "-c", "color.ui=never", "commit", "-m", message };
            if (all) args.Add("-a");
            if (amend) args.Add("--amend");
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        // ── Diff / show ───────────────────────────────────────────

        [McpServerTool, Description("Shows the full unified diff content (not just --stat) for changes. mode='unstaged' (default), 'staged' (--cached), or a revision range like 'HEAD~1..HEAD'. Pass context to control the number of unified-diff context lines (default 3). Returns the full diff text — may be very long for large changes; prefer git_diff --stat for a quick overview.")]
        public async Task<string> GitDiffFull(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Diff mode: 'unstaged' (default), 'staged' (--cached), or a revision range like 'HEAD~1..HEAD'.")] string mode = "unstaged",
            [Description("Optional: limit diff to a specific file path relative to repo root.")] string? filePath = null,
            [Description("Number of unified-diff context lines. Default 3.")] int context = 3)
        {
            var args = new List<string> { "-c", "color.ui=never", "diff" };
            if (mode == "staged")
            {
                args.Add("--cached");
            }
            else if (mode != "unstaged")
            {
                args.Add(mode);
            }
            args.Add($"--unified={Math.Clamp(context, 0, 100)}");
            if (!string.IsNullOrEmpty(filePath))
            {
                args.Add("--");
                args.Add(filePath);
            }
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        [McpServerTool, Description("Shows details for a specific commit (hash, author, date, full message, and the change stats). Pass a revision (e.g. 'HEAD', 'HEAD~1', a short or full SHA, or a branch name). With filePath, shows the file's content at that revision instead of the full commit info (git show <rev>:<file>).")]
        public async Task<string> GitShow(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Git revision: a SHA, short SHA, 'HEAD', 'HEAD~1', a branch name, etc.")] string revision,
            [Description("Optional: show the content of this file at the given revision instead of the full commit info (git show <rev>:<file>).")] string? filePath = null)
        {
            if (string.IsNullOrWhiteSpace(revision))
            {
                return "Error: revision must not be empty.";
            }

            // git show <rev> shows the commit's metadata + diff. To show a file's
            // CONTENT at a revision we use the colon form `git show <rev>:<path>`
            // (single argument), NOT `git show <rev> -- <path>` which would show
            // the diff the rev made to that file instead.
            var target = string.IsNullOrEmpty(filePath) ? revision : $"{revision}:{filePath}";
            var args = new List<string> { "-c", "color.ui=never", "show", target };
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        // ── Branches ───────────────────────────────────────────────

        [McpServerTool, Description("Lists local branches. Pass allRemotes=true to also list remote-tracking branches. The currently checked-out branch is marked with '*'.")]
        public async Task<string> GitBranchList(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("If true, also list remote-tracking branches (git branch -a). Default false (local only).")] bool allRemotes = false)
        {
            var args = new List<string> { "-c", "color.ui=never", "branch" };
            if (allRemotes) args.Add("-a");
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        [McpServerTool, Description("Switches to an existing branch. With create=true, creates the branch first if it doesn't exist (git checkout -b <name>). Pass a revision (e.g. 'HEAD~3', a SHA) to detach HEAD and inspect the working tree at that point in history.")]
        public async Task<string> GitBranchCheckout(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Branch name (or revision) to switch to.")] string branchName,
            [Description("If true, create the branch if it doesn't exist (git checkout -b). Default false.")] bool create = false)
        {
            if (string.IsNullOrWhiteSpace(branchName))
            {
                return "Error: branchName must not be empty.";
            }

            var args = new List<string> { "-c", "color.ui=never", "checkout" };
            if (create) args.Add("-b");
            args.Add(branchName);
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        [McpServerTool, Description("Creates a new branch. With checkout=true, also switches to it (git checkout -b). With startPoint, the branch is created at that revision (e.g. 'main', 'HEAD~3', a SHA) instead of the current HEAD. To delete a branch, use git_branch_delete instead.")]
        public async Task<string> GitBranchCreate(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Name of the new branch.")] string branchName,
            [Description("If true, switch to the new branch immediately (git checkout -b). Default false.")] bool checkout = false,
            [Description("Optional: starting point for the new branch (branch name, tag, or revision). Default: current HEAD.")] string? startPoint = null)
        {
            if (string.IsNullOrWhiteSpace(branchName))
            {
                return "Error: branchName must not be empty.";
            }

            var args = new List<string> { "-c", "color.ui=never", "checkout" };
            if (checkout) args.Add("-b");
            args.Add(branchName);
            if (!string.IsNullOrEmpty(startPoint)) args.Add(startPoint);
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        [McpServerTool, Description("Deletes a branch. With force=true, force-deletes even if the branch has unmerged commits (git branch -D); without it, the delete fails if there are unmerged commits (git branch -d). To delete a remote branch, use git_push with the remote name and ':<branch>' as the refspec instead — that is how Git itself is designed to delete remote branches.")]
        public async Task<string> GitBranchDelete(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Name of the branch to delete.")] string branchName,
            [Description("If true, force-delete even with unmerged commits (git branch -D). Default false.")] bool force = false)
        {
            if (string.IsNullOrWhiteSpace(branchName))
            {
                return "Error: branchName must not be empty.";
            }

            var args = new List<string> { "-c", "color.ui=never", "branch", force ? "-D" : "-d", branchName };
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        [McpServerTool, Description("Merges a branch into the current branch. Pass branch= the source branch (or remote-tracking branch like 'origin/dev') to merge in. With noFf=true, always create a merge commit even if a fast-forward is possible (preserves the branch topology). With squash=true, stage all merged changes as a single squashed commit; you must then commit separately. With abort=true, abort an in-progress merge with conflicts — branch is ignored in this case. Local operation — uses the default 30s timeout.")]
        public async Task<string> GitMerge(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Source branch (or remote-tracking branch like 'origin/dev') to merge into the current branch. Ignored when abort=true.")] string? branch = null,
            [Description("If true, always create a merge commit even if a fast-forward is possible (git merge --no-ff). Default false.")] bool noFf = false,
            [Description("If true, stage all merged changes as a single squashed commit (git merge --squash). Default false. After this, you must commit the squashed result separately.")] bool squash = false,
            [Description("If true, abort an in-progress merge with conflicts (git merge --abort). Default false. branch is ignored when abort=true.")] bool abort = false)
        {
            // Validate that we have what we need for the chosen mode.
            if (abort)
            {
                if (!string.IsNullOrWhiteSpace(branch))
                {
                    return "Error: branch must be empty when abort=true (the abort discards the in-progress merge regardless of which branch started it).";
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(branch))
                {
                    return "Error: branch must not be empty. Provide the source branch (or remote-tracking branch like 'origin/dev') to merge into the current branch. Pass abort=true to abort an in-progress merge instead.";
                }
                if (noFf && squash)
                {
                    return "Error: noFf and squash are mutually exclusive. --no-ff creates a merge commit; --squash stages changes for a separate commit. Pick one.";
                }
            }

            var args = new List<string> { "-c", "color.ui=never", "merge" };
            if (abort)
            {
                args.Add("--abort");
            }
            else
            {
                if (noFf) args.Add("--no-ff");
                if (squash) args.Add("--squash");
                args.Add(branch!);
            }
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        // ── Stash ──────────────────────────────────────────────────

        [McpServerTool, Description("Manages git stashes — saves uncommitted working-tree changes to a stack you can reapply later. action='list' (default) shows all stashes; 'save' stashes working-tree changes (pass message= for a description, includeUntracked=true to also stash new untracked files); 'pop' applies and removes the top stash (pass index= for a specific one, 0-based, 0=top); 'apply' applies but does not remove; 'drop' removes a stash without applying it; 'show' shows a stash's diff stat.")]
        public async Task<string> GitStash(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Stash action: 'list', 'save', 'pop', 'apply', 'drop', 'show'.")] string action = "list",
            [Description("Used by 'save': description for the new stash.")] string? message = null,
            [Description("Used by 'pop'/'apply'/'drop'/'show': 0-based index into the stash stack. Default 0 (top).")] int index = 0,
            [Description("Used by 'save': also stash untracked files (git stash -u).")] bool includeUntracked = false)
        {
            var validActions = new[] { "list", "save", "pop", "apply", "drop", "show" };
            if (Array.IndexOf(validActions, action) < 0)
            {
                return $"Error: action must be one of: {string.Join(", ", validActions)}";
            }

            var args = new List<string> { "-c", "color.ui=never", "stash" };
            switch (action)
            {
                case "list":
                    break;
                case "save":
                    if (includeUntracked) args.Add("-u");
                    args.Add("push");
                    if (!string.IsNullOrEmpty(message))
                    {
                        args.Add("-m");
                        args.Add(message);
                    }
                    break;
                case "pop":
                case "apply":
                case "drop":
                case "show":
                    args.Add(action);
                    args.Add($"stash@{{{index}}}");
                    break;
            }
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        // ── Remote ─────────────────────────────────────────────────

        [McpServerTool, Description("Lists configured git remotes with their fetch/push URLs (git remote -v).")]
        public async Task<string> GitRemoteList(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath)
        {
            return await RunGitAsync(GetWorkingDir(projectPath), new List<string> { "-c", "color.ui=never", "remote", "-v" });
        }

        [McpServerTool, Description("Fetches from a remote without merging or rebasing — just updates the remote-tracking refs. With allRemotes=true, fetches from every configured remote. With prune=true, removes remote-tracking refs that no longer exist on the remote. Does NOT modify your local branches.")]
        public async Task<string> GitFetch(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Optional: remote name to fetch from (default: upstream of current branch, or 'origin' if none).")] string? remote = null,
            [Description("If true, also fetch from every configured remote. Default false.")] bool allRemotes = false,
            [Description("If true, prune deleted remote-tracking refs. Default false.")] bool prune = false)
        {
            var args = new List<string> { "-c", "color.ui=never", "fetch" };
            if (allRemotes) args.Add("--all");
            if (prune) args.Add("--prune");
            if (!string.IsNullOrEmpty(remote)) args.Add(remote);
            return await RunGitAsync(GetWorkingDir(projectPath), args, timeoutMs: 60000);
        }

        [McpServerTool, Description("Pulls from a remote and merges (or rebases, with rebase=true) into the current branch. If remote/branch are omitted, uses the upstream configured for the current branch. Network operation — runs with a 60s timeout.")]
        public async Task<string> GitPull(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Optional: remote name (default: upstream of current branch).")] string? remote = null,
            [Description("Optional: branch name to pull (default: upstream of current branch).")] string? branch = null,
            [Description("If true, rebase instead of merge. Default false.")] bool rebase = false)
        {
            var args = new List<string> { "-c", "color.ui=never", "pull" };
            if (rebase) args.Add("--rebase");
            if (!string.IsNullOrEmpty(remote)) args.Add(remote);
            if (!string.IsNullOrEmpty(branch)) args.Add(branch);
            return await RunGitAsync(GetWorkingDir(projectPath), args, timeoutMs: 60000);
        }

        [McpServerTool, Description("Pushes the current branch to a remote. With setUpstream=true, also sets the upstream tracking ref so future push/pull commands work without arguments. With force=true, uses --force-with-lease (NOT bare --force): it refuses to overwrite if the remote advanced past your last known position, which prevents accidentally clobbering a teammate's commit. Use only on branches you own. Network operation — runs with a 60s timeout.")]
        public async Task<string> GitPush(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Optional: remote name (default: upstream of current branch, or 'origin').")] string? remote = null,
            [Description("Optional: branch name to push (default: current branch).")] string? branch = null,
            [Description("If true, set the upstream tracking ref so future push/pull work without arguments.")] bool setUpstream = false,
            [Description("If true, force-push with --force-with-lease (refuses if remote advanced). Default false.")] bool force = false)
        {
            var args = new List<string> { "-c", "color.ui=never", "push" };
            if (setUpstream) args.Add("-u");
            if (force) args.Add("--force-with-lease");
            if (!string.IsNullOrEmpty(remote)) args.Add(remote);
            if (!string.IsNullOrEmpty(branch)) args.Add(branch);
            return await RunGitAsync(GetWorkingDir(projectPath), args, timeoutMs: 60000);
        }

        // ── Reset ──────────────────────────────────────────────────

        [McpServerTool, Description("Resets the current HEAD to a specified state. mode='soft' (keep changes staged), 'mixed' (default; keep changes unstaged), or 'hard' (DESTRUCTIVE: discards ALL working-tree and index changes — requires confirmHardReset=true as a safety check). Pass a revision to reset to (e.g. 'HEAD~1', a SHA, or a branch); default HEAD, which is a no-op.")]
        public async Task<string> GitReset(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Reset mode: 'soft' (keep staged), 'mixed' (default; keep unstaged), or 'hard' (DESTRUCTIVE — discards all working-tree changes).")] string mode = "mixed",
            [Description("Optional: revision to reset to (e.g. 'HEAD~1', a SHA, or a branch). Default: HEAD (no-op).")] string? target = null,
            [Description("Safety check: must be true when mode='hard'. Confirms the user understands this discards ALL uncommitted working-tree changes. Default false.")] bool confirmHardReset = false)
        {
            var validModes = new[] { "soft", "mixed", "hard" };
            if (Array.IndexOf(validModes, mode) < 0)
            {
                return $"Error: mode must be one of: {string.Join(", ", validModes)}";
            }
            if (mode == "hard" && !confirmHardReset)
            {
                return "Error: mode='hard' discards ALL uncommitted working-tree changes. Pass confirmHardReset=true to confirm you understand.";
            }

            var args = new List<string> { "-c", "color.ui=never", "reset", $"--{mode}" };
            if (!string.IsNullOrEmpty(target)) args.Add(target);
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        // ── Repo creation ─────────────────────────────────────────

        [McpServerTool, Description("Initializes a new git repository at projectPath. Creates the directory if it does not exist; if it already contains a repo, git re-initializes it (safe, no data loss). With bare=true, creates a bare repository (no working tree) — useful as a remote or central server. Does NOT add a remote or make any commits; use git_remote_list/git_add/git_commit afterward for that.")]
        public async Task<string> GitInit(
            [Description("Absolute path of the directory to initialize as a git repo. The directory is created if it does not exist.")] string projectPath,
            [Description("If true, create a bare repository (no working tree). Default false.")] bool bare = false)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return "Error: projectPath must not be empty.";
            }

            var args = new List<string> { "-c", "color.ui=never", "init" };
            if (bare) args.Add("--bare");
            args.Add(projectPath);
            return await RunGitAsync(GetWorkingDir(projectPath), args);
        }

        [McpServerTool, Description("Clones a git repository from a URL (HTTPS, SSH, git://, or a local file path) into targetDir. The clone always checks out the default branch automatically — no separate checkout step is needed or supported. targetDir must NOT already exist as a non-empty directory; the parent directory must exist and be writable. Authentication uses your pre-configured credential helper / SSH keys; interactive prompts are disabled, so credentials must already be set up. Large repos can be slow — uses a 120s timeout.")]
        public async Task<string> GitClone(
            [Description("Repository URL: HTTPS (https://...), SSH (git@github.com:user/repo.git), git://, or a local file path.")] string url,
            [Description("Absolute path of the directory to create the clone in. Must not already exist (or be empty). The parent must exist and be writable.")] string targetDir)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "Error: url must not be empty.";
            }
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                return "Error: targetDir must not be empty.";
            }

            // Run git clone in the parent of targetDir and pass the basename as
            // the destination, so the clone always creates `<parent>/<basename>`
            // regardless of where the spawned process runs. If targetDir has no
            // parent component (root-level path), fall back to the full targetDir
            // so we still invoke clone correctly.
            string parent = Path.GetDirectoryName(targetDir) ?? "";
            string basename = Path.GetFileName(targetDir);
            string cwd = string.IsNullOrEmpty(parent) ? GetWorkingDir(targetDir) : parent;
            string targetArg = string.IsNullOrEmpty(basename) || basename == "." ? targetDir : basename;

            var args = new List<string> { "-c", "color.ui=never", "clone", url, targetArg };
            return await RunGitAsync(cwd, args, timeoutMs: 120000);
        }
    }
}