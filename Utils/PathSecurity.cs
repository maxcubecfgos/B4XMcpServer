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
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be empty.", paramName);

            // Reject relative paths — callers should always pass absolute paths.
            if (!Path.IsPathRooted(path))
                throw new ArgumentException($"Path must be absolute: '{path}'", paramName);

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
    }
}
