using System;
using System.IO;
using System.Linq;

namespace B4XMcpServer.Utils
{
    /// <summary>
    /// Helper utilities to validate file paths received from MCP tool callers.
    /// Prevents directory traversal (../..) and accidental writes outside the project.
    /// </summary>
    public static class PathSecurity
    {
        /// <summary>
        /// Throws if the path is empty, relative, or contains parent-directory traversal.
        /// </summary>
        public static void ValidateAbsolutePath(string path, string paramName = "path")
        {
            // Round-4 polish: distinguish null (the CLI flag wasn't recognised and the
            // dispatcher defaulted the param to null), empty/whitespace (user passed --key=
            // with nothing after the =), and non-rooted (user passed a relative path).
            // Mixing them into one "Path cannot be empty." swallowed the diagnostic and
            // made AIs and humans chase the wrong fix. Each branch now spells out exactly
            // what went wrong and what to do next.

            if (path is null)
                throw new ArgumentException(
                    $"Parameter '{paramName}' was not supplied (got null). The CLI flag you used was not recognised as a synonym for canonical '--{paramName}'. " +
                    $"Run 'B4XMcpServer.exe --list-tools' and 'B4XMcpServer.exe --describe <tool>' to see the exact canonical flag name and accepted aliases.",
                    paramName);

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException(
                    $"Parameter '{paramName}' was supplied as empty or whitespace. Did you forget the value after '='?  (e.g. use --{paramName}=C:\\Path\\To\\File, not --{paramName}=)",
                    paramName);

            // Reject relative paths — callers should always pass absolute paths.
            if (!Path.IsPathRooted(path))
                throw new ArgumentException(
                    $"Path '{path}' provided for parameter '{paramName}' must be absolute (e.g. 'C:\\Path' on Windows, '/path' on Unix). " +
                    $"Relative paths are rejected by design to prevent tooling mistakes.",
                    paramName);

            // Path.GetFullPath resolves any parent-directory segments, so the only
            // thing left to verify is containment (see ValidateWithinBaseDirectory).
        }

        /// <summary>
        /// Throws if the path is outside the given base directory.
        /// Use this for destructive operations (writes) to keep them inside the project.
        /// </summary>
        public static void ValidateWithinBaseDirectory(string path, string baseDirectory, string paramName = "path")
        {
            ValidateAbsolutePath(path, paramName);
            ValidateAbsolutePath(baseDirectory, nameof(baseDirectory));

            var fullPath = Path.GetFullPath(path);
            var baseFull = Path.GetFullPath(baseDirectory);

            // Ensure base ends with a separator so that C:\Project is not a parent of C:\ProjectEvil.
            if (!baseFull.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                !baseFull.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                baseFull += Path.DirectorySeparatorChar;
            }

            if (!fullPath.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Path '{path}' is outside the allowed directory '{baseDirectory}'.", paramName);
        }

        /// <summary>
        /// Returns the directory to use when looking for a project root. For a path that
        /// already exists as a directory, returns the path itself; otherwise returns the
        /// parent directory. Useful when validating new files that do not exist yet.
        /// </summary>
        public static string GetDirectoryForProjectRoot(string path)
        {
            if (Directory.Exists(path))
                return path;

            var dir = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(dir) ? path : dir;
        }

        /// <summary>
        /// Returns true if the path points to a B4X main project file (.b4a/.b4j/.b4i).
        /// </summary>
        public static bool IsMainProjectFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".b4a" || ext == ".b4j" || ext == ".b4i";
        }

        /// <summary>
        /// Called by <c>write_file</c>, <c>edit_sub</c>, <c>edit_line</c>, <c>insert_line</c>,
        /// and <c>replace_lines</c> against the same corruption vector.
        /// The helper distinguishes CREATION (<c>File.Exists == false</c>) from
        /// EDIT (<c>File.Exists == true</c>) so legitimate edits to a pre-existing
        /// <c>Main.bas</c> the human authored are still allowed.
        /// Returns true if writing <paramref name="filePath"/> would CREATE a
        /// brand-new <c>Main.bas</c> inside a B4X project directory that already
        /// has its own main project file (<c>.b4a</c>/<c>.b4j</c>/<c>.b4i</c>).
        /// In every B4X project the project file's source code section IS the
        /// Main module — creating a separate <c>Main.bas</c> corrupts the project
        /// instantly (duplicate Main, compile errors, IDE confusion).
        /// <para>
        /// Rules of the check:
        /// <list type="bullet">
        ///   <item><description>File name must be <c>Main.bas</c> (case-insensitive).</description></item>
        ///   <item><description>File must NOT already exist — the corruption path is CREATION only; modifying an existing <c>Main.bas</c> that the human authored is allowed.</description></item>
        ///   <item><description>Parent directory must contain a <c>.b4a</c>, <c>.b4j</c> or <c>.b4i</c> at the top level (B4X projects place the project file in the root).</description></item>
        /// </list>
        /// </para>
        /// <paramref name="reason"/> is set to a human-readable explanation
        /// when the call returns true, suitable for inclusion in a
        /// <c>ToolResponse.Error</c> envelope.
        /// </summary>
        public static bool IsForbiddenMainBas(string filePath, out string? reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            // Case-insensitive: Windows filesystems are case-insensitive but AI
            // callers may type any casing ("main.bas", "MAIN.BAS", ...).
            var fileName = Path.GetFileName(filePath);
            if (!string.Equals(fileName, "Main.bas", StringComparison.OrdinalIgnoreCase))
                return false;

            // Don't interfere with edits to an existing Main.bas — only NEW
            // creation is the corruption path the AI keeps hitting.
            if (File.Exists(filePath))
                return false;

            var parent = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                return false;

            string? projectFile = null;
            try
            {
                projectFile = Directory.EnumerateFiles(parent, "*.b4a", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.EnumerateFiles(parent, "*.b4j", SearchOption.TopDirectoryOnly))
                    .Concat(Directory.EnumerateFiles(parent, "*.b4i", SearchOption.TopDirectoryOnly))
                    .FirstOrDefault();
            }
            catch
            {
                // Permission errors, IO errors, etc. — be conservative and don't
                // block. The caller will surface the underlying issue.
                return false;
            }

            if (projectFile == null)
                return false;

            string projectName = Path.GetFileName(projectFile);
            reason =
                $"❌ CRITICAL: Creating 'Main.bas' is blocked because '{projectName}' exists in " +
                $"the same directory and IS the project's Main module. " +
                $"In B4X the .b4a / .b4j / .b4i file at the project root is the Main module — " +
                $"REGARDLESS of what it is named (MiApp.b4a, Project.b4a, MainApp.b4a, anything). " +
                $"Every Sub (Activity_Create, Process_Globals, AppStart, etc.) lives in its source " +
                $"code section — there is no separate Main.bas unless YOU (the human) explicitly " +
                $"authored one already. " +
                $"Use edit_sub on the project file instead: " +
                $"edit_sub --filePath=\"{projectFile}\" --subName=\"Activity_Create\" --newCode=\"...\"";
            return true;
        }
    }
}
