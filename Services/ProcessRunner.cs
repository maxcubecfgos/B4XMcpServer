using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace B4XMcpServer.Services
{
    /// <summary>
    /// Async process runner helper. Avoids blocking the calling thread while
    /// waiting for long-running external commands (builders, adb, git, etc.).
    /// </summary>
    public static class ProcessRunner
    {
        public sealed class Result
        {
            public string Output { get; init; } = string.Empty;
            public int ExitCode { get; init; }
            public bool TimedOut { get; init; }
        }

        /// <summary>
        /// Runs a process asynchronously and returns its combined stdout/stderr output.
        /// </summary>
        public static async Task<Result> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string? workingDirectory = null,
            int timeoutMilliseconds = 30000,
            Dictionary<string, string>? environmentVariables = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrEmpty(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            if (environmentVariables != null)
            {
                foreach (var kv in environmentVariables)
                    psi.EnvironmentVariables[kv.Key] = kv.Value;
            }

            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            if (proc == null)
                throw new InvalidOperationException($"Failed to start process: {fileName}");

            var output = new StringBuilder();
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Close stdin immediately so the child never waits on it.
            try { proc.StandardInput.Close(); } catch { }

            using var cts = new System.Threading.CancellationTokenSource(timeoutMilliseconds);
            bool timedOut = false;
            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
            }

            if (timedOut)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return new Result
                {
                    Output = output + "\nError: Process timed out.",
                    ExitCode = -1,
                    TimedOut = true
                };
            }

            int exitCode;
            try { exitCode = proc.ExitCode; }
            catch { exitCode = -1; }

            return new Result
            {
                Output = output.ToString(),
                ExitCode = exitCode,
                TimedOut = false
            };
        }
    }
}
