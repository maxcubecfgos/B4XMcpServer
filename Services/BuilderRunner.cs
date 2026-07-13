using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using B4XContext.Engine;

namespace B4XContext.Services
{
    public static class BuilderRunner
    {
        public static Dictionary<string, object> RunBuild(string builderPath, string projectFile, int timeoutSeconds = 300)
        {
            var result = new Dictionary<string, object> { { "success", false }, { "errors", new List<Dictionary<string, object>>() } };

            if (string.IsNullOrEmpty(builderPath) || !File.Exists(builderPath))
            {
                result["fatal_error"] = $"Builder not found at: {builderPath}";
                return result;
            }

            // CORRECCIÓN: El B4ABuilder.exe NO acepta -task=build. 
            // Pasa simplemente la ruta del proyecto entre comillas para evitar problemas con espacios.
            var startInfo = new ProcessStartInfo
            {
                FileName = builderPath,
                Arguments = $"\"{projectFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(projectFile)
            };

            try
            {
                using var proc = Process.Start(startInfo);
                if (proc == null)
                {
                    result["fatal_error"] = "Failed to start builder process.";
                    return result;
                }

                var output = new StringBuilder();
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (!proc.WaitForExit(timeoutSeconds * 1000))
                {
                    try { proc.Kill(); } catch { }
                    result["fatal_error"] = $"Build timed out after {timeoutSeconds}s.";
                    return result;
                }

                string rawOut = output.ToString();
                var parsed = BuildOutputParser.Parse(rawOut);

                // VETO ABSOLUTO: Si el compilador falla, inyectamos la orden de parada
                if (proc.ExitCode != 0 || rawOut.Contains("Error:"))
                {
                    parsed["success"] = false;
                    parsed["stop_optimization"] = true; // Flag clave para que la IA se detenga
                    parsed["message"] = "Compilation failed. STOP modifying files. Analyze the error reported.";

                    if (!parsed.ContainsKey("fatal_error"))
                    {
                        parsed["fatal_error"] = $"Builder exited with code {proc.ExitCode}.\nOutput:\n{rawOut.Trim()}";
                    }
                }

                return parsed;
            }
            catch (Exception ex)
            {
                result["fatal_error"] = ex.Message;
                return result;
            }
        }
    }
}