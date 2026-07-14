using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using B4XContext.Models;

namespace B4XContext.Services
{
    public static class ProjectScanner
    {
        private static readonly string[] IgnoredFolders = new[]
        {
            "Objects", "bin", "gen", "obj", ".git", "temp", "build"
        };

        private static readonly string[] IgnoredFiles = new[]
        {
            "Starter.bas", "Starter.b4a", "Starter.b4j"  // System service modules — never touch
        };

        public static string FindProjectRoot(string startPath)
        {
            if (string.IsNullOrEmpty(startPath))
                return null;

            var dir = new DirectoryInfo(Path.GetDirectoryName(startPath) ?? startPath);
            while (dir != null)
            {
                var files = dir.GetFiles("*.b4a").Concat(dir.GetFiles("*.b4j")).Concat(dir.GetFiles("*.b4i"));
                if (files.Any())
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        public static List<ProjectFile> ScanProject(string projectRoot)
        {
            var files = new List<ProjectFile>();
            if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot))
                return files;

            foreach (var f in Directory.EnumerateFiles(projectRoot, "*.*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(projectRoot, f);

                // Skip ignored folders (Objects, bin, gen, obj, .git, etc.)
                if (IgnoredFolders.Any(x => rel.Split(Path.DirectorySeparatorChar).Contains(x, StringComparer.OrdinalIgnoreCase)))
                    continue;

                // Skip IDE metadata files (.meta)
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".meta")
                    continue;

                // Skip system service modules that should never be touched
                var fileName = Path.GetFileName(f);
                if (IgnoredFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    continue;

                // Only include B4X source and layout files
                if (ext == ".bas" || ext == ".bal" || ext == ".bjl" || ext == ".bil" ||
                    ext == ".b4a" || ext == ".b4j" || ext == ".b4i")
                {
                    var pf = new ProjectFile(f)
                    {
                        Kind = ext.TrimStart('.')
                    };
                    files.Add(pf);
                }
            }

            return files.OrderBy(p => p.Name).ToList();
        }

        public static string FindProjectFile(string projectRoot)
        {
            if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot))
                return null;

            foreach (var fn in Directory.GetFiles(projectRoot))
            {
                var low = Path.GetExtension(fn).ToLowerInvariant();
                if (low == ".b4a" || low == ".b4j" || low == ".b4i")
                    return fn;
            }
            return null;
        }
    }
}