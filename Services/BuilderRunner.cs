using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using B4XMcpServer.Engine;

namespace B4XMcpServer.Services
{
    public static class BuilderRunner
    {
        // Match BuildOutputParser.Parse's nullability contract: every parsed result value
        // is nullable because builder output keys like `java_line` and `symbol` are
        // frequently absent. BuildFormatter.Format was updated to accept this shape.
        public static async Task<Dictionary<string, object?>> RunBuildAsync(string builderPath, string projectFile, int timeoutSeconds = 300)
        {
            var result = new Dictionary<string, object?> { { "success", false }, { "errors", new List<Dictionary<string, object?>>() } };

            if (string.IsNullOrEmpty(builderPath) || !File.Exists(builderPath))
            {
                result["fatal_error"] = $"Builder not found at: {builderPath}";
                return result;
            }

            var workingDirectory = Path.GetDirectoryName(projectFile);
            var arguments = new List<string>
            {
                "-Task=build",
                $"-Project={projectFile}"
            };

            try
            {
                var runResult = await ProcessRunner.RunAsync(builderPath, arguments, workingDirectory, timeoutSeconds * 1000);
                string rawOut = runResult.Output;

                // Si el builder no produjo nada, es un error fatal del sistema
                if (runResult.TimedOut)
                {
                    result["fatal_error"] = $"Build timed out after {timeoutSeconds}s.";
                    return result;
                }

                if (rawOut.Trim().Length == 0 && runResult.ExitCode != 0)
                {
                    result["fatal_error"] = $"Builder exited with code {runResult.ExitCode} and produced no output.";
                    return result;
                }

                // Parsear el output — BuildOutputParser ya detecta errores y pone success=false
                var parsed = BuildOutputParser.Parse(rawOut);
                // Incluir raw_output para post-validación en CompileProject
                parsed["raw_output"] = rawOut;
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