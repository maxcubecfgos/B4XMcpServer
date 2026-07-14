using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class GitTools
    {
        [McpServerTool, Description("Shows git diff --stat for the repository containing the given path. mode='unstaged' (default, working tree changes not yet staged), mode='staged' (changes added with git add), or a revision range like 'HEAD~1..HEAD' or 'main..feature'. Returns file names and change counts (fast, won't time out).")]
        public static string GitDiff(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Diff mode: 'unstaged' (default), 'staged' (--cached), or a git revision range like 'HEAD~1..HEAD'.")] string mode = "unstaged",
            [Description("Optional: limit diff to a specific file path relative to repo root.")] string? filePath = null)
        {
            string dir = GetWorkingDir(projectPath);
            string args = mode switch
            {
                "unstaged" => "-c color.ui=never diff --stat",
                "staged" => "-c color.ui=never diff --cached --stat",
                _ => $"-c color.ui=never diff --stat {mode}"
            };
            if (!string.IsNullOrEmpty(filePath))
                args += $" -- \"{filePath}\"";

            return RunGit(dir, args);
        }

        [McpServerTool, Description("Shows recent git commit history for the repository. Returns last N commits with hash, author, date, and message in a compact format.")]
        public static string GitLog(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath,
            [Description("Number of recent commits to show. Default 20, max 100.")] int count = 20,
            [Description("Optional: show history for a specific file only.")] string? filePath = null)
        {
            count = Math.Clamp(count, 1, 100);
            string dir = GetWorkingDir(projectPath);
            string args = $"-c color.ui=never log --oneline -{count}";
            if (!string.IsNullOrEmpty(filePath)) args += $" -- \"{filePath}\"";

            return RunGit(dir, args);
        }

        [McpServerTool, Description("Shows current git status: current branch, staged changes, unstaged changes, and untracked files in a compact format.")]
        public static string GitStatus(
            [Description("Absolute path to any file or folder inside the git repo.")] string projectPath)
        {
            string dir = GetWorkingDir(projectPath);
            return RunGit(dir, "-c color.ui=never status --short --branch");
        }

        private static string GetWorkingDir(string path)
        {
            return Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? ".";
        }

        private static string RunGit(string workingDir, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,   // avoid child blocking on an unredirected console stdin handle
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // Prevent git from ever invoking a pager or an interactive credential/SSH prompt,
            // either of which would hang forever with no console attached.
            psi.EnvironmentVariables["GIT_PAGER"] = "cat";
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            psi.EnvironmentVariables["GCM_INTERACTIVE"] = "never";

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) return "Error: Could not start git process. Is git installed and in PATH?";

                // Close stdin immediately so git never waits on it.
                try { proc.StandardInput.Close(); } catch { }

                var sb = new StringBuilder();
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (!proc.WaitForExit(30000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return "Error: Git command timed out after 30 seconds.";
                }

                var result = sb.ToString().Trim();
                return string.IsNullOrEmpty(result) ? "(no output)" : result;
            }
            catch (Exception ex)
            {
                return $"Error running git: {ex.Message}";
            }
        }
    }
}