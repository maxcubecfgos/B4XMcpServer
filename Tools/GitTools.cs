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

        private static async Task<string> RunGitAsync(string workingDir, List<string> arguments)
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
                var result = await ProcessRunner.RunAsync("git", arguments, workingDir, 30000, env);
                var output = result.Output.Trim();
                return string.IsNullOrEmpty(output) ? "(no output)" : output;
            }
            catch (Exception ex)
            {
                return $"Error running git: {ex.Message}";
            }
        }
    }
}