using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using B4XContext.Services;
using B4XContext.Utils;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class LibraryTools
    {
        [McpServerTool, Description("Lists all libraries referenced in a B4X project file (.b4a/.b4j/.b4i): reads the Library1, Library2... keys from the IDE metadata header. Returns the library names in order.")]
        public static string ListProjectLibraries(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file.")] string projectPath)
        {
            string? projectFile = File.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectFile(projectPath);
            if (projectFile == null)
                throw new FileNotFoundException($"No .b4a/.b4j/.b4i project file found for '{projectPath}'.");

            string raw = File.ReadAllText(projectFile);
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            string headerSection = markerIdx >= 0 ? raw.Substring(0, markerIdx) : raw;

            var rawSettings = new Dictionary<string, string>();
            foreach (var lineRaw in headerSection.Split('\n'))
            {
                var line = lineRaw.TrimEnd('\r').Trim();
                if (string.IsNullOrEmpty(line)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                rawSettings[key] = value;
            }

            var libraries = rawSettings
                .Where(kv => Regex.IsMatch(kv.Key, @"^Library\d+$"))
                .OrderBy(kv => int.Parse(Regex.Match(kv.Key, @"\d+").Value))
                .Select(kv => new { key = kv.Key, name = kv.Value })
                .ToList();

            return JsonSerializer.Serialize(new
            {
                projectFile,
                libraryCount = libraries.Count,
                libraries
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        [McpServerTool, Description("Lists every available library found in all configured library folders (B4A, B4J, AdditionalLibrariesFolder from IDE settings, plus project-local Libraries/). Includes name and version.")]
        public static string ListAvailableLibraries(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file.")] string? projectPath = null)
        {
            string? root = null;
            if (!string.IsNullOrEmpty(projectPath))
            {
                root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            }

            string cacheKey = $"libs:list:{root ?? "global"}";
            if (CacheManager.TryGetByTtl<string>(cacheKey, out var cached) && cached != null)
                return cached;

            var dirs = B4aConfig.GetLibraryDirectories(root);
            var libs = LibraryScanner.ListLibraries(dirs);

            var result = JsonSerializer.Serialize(new
            {
                libraryDirectories = dirs,
                count = libs.Count,
                libraries = libs.Select(l => new { name = l.Name, version = l.Version, source = l.Source })
            }, new JsonSerializerOptions { WriteIndented = true });

            CacheManager.SetByTtl(cacheKey, result, 120);
            return result;
        }

        [McpServerTool, Description("Returns the documented methods, properties, and events of a B4X library in compact format: kind (method/property/event), name, return type, parameters, and a one-line description. Use this to discover what a library can do before writing code that uses it.")]
        public static string GetLibraryDocs(
            [Description("Library name (e.g. 'Core', 'XUI', 'XUI Views', 'StringUtils')")] string libraryName,
            [Description("Optional: filter to a specific class/type name within the library (e.g. 'B4XView', 'BitmapCreator')")] string? typeName = null,
            [Description("Absolute path to the B4X project folder (optional, helps find project-local libraries)")] string? projectPath = null)
        {
            string? root = null;
            if (!string.IsNullOrEmpty(projectPath))
            {
                root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            }

            var dirs = B4aConfig.GetLibraryDirectories(root);
            var xmlPath = LibraryScanner.FindLibraryXml(libraryName, dirs);

            if (xmlPath == null)
                return JsonSerializer.Serialize(new
                {
                    error = $"Library '{libraryName}' not found in any configured library directory.",
                    searchedDirectories = dirs
                }, new JsonSerializerOptions { WriteIndented = true });

            string cacheKey = $"libs:docs:{xmlPath}:{typeName ?? ""}";
            if (CacheManager.TryGetByMtime<string>(xmlPath, out var cached) && cached != null &&
                string.IsNullOrEmpty(typeName)) // Don't cache filtered queries
                return cached;

            var docs = LibraryScanner.GetLibraryDocs(xmlPath, typeName);

            var result = JsonSerializer.Serialize(new
            {
                library = docs.Name,
                version = docs.Version,
                typeName = string.IsNullOrEmpty(docs.TypeName) ? null : docs.TypeName,
                memberCount = docs.Members.Count,
                members = docs.Members.Select(m => new
                {
                    kind = m.Kind,
                    signature = m.Kind switch
                    {
                        "method" => $"{m.Name}({m.Parameters ?? ""})" + (m.ReturnType != null ? $" → {m.ReturnType}" : ""),
                        "property" => $"{m.Name}: {m.ReturnType ?? "?"}",
                        "event" => $"{m.Name}({m.Parameters ?? ""})",
                        _ => m.Name
                    },
                    description = m.Description
                })
            }, new JsonSerializerOptions { WriteIndented = true });

            if (string.IsNullOrEmpty(typeName))
                CacheManager.SetByMtime(xmlPath, result);

            return result;
        }

        [McpServerTool, Description("Searches all available library documentation for methods, properties, or events matching a query string. Searches in member names, type names, and descriptions. Use this to find which library provides a specific feature, or to discover how to use a method.")]
        public static string SearchLibrary(
            [Description("Search query: method name, property name, event name, or keyword")] string query,
            [Description("Absolute path to the B4X project folder (optional, helps search project-local libraries)")] string? projectPath = null)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Query must not be empty.");

            string? root = null;
            if (!string.IsNullOrEmpty(projectPath))
            {
                root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            }

            string cacheKey = $"libs:search:{query.ToLowerInvariant()}:{root ?? "global"}";
            if (CacheManager.TryGetByTtl<string>(cacheKey, out var cached) && cached != null)
                return cached;

            var dirs = B4aConfig.GetLibraryDirectories(root);
            var matches = LibraryScanner.SearchLibraries(query, dirs);

            var result = JsonSerializer.Serialize(new
            {
                query,
                matchCount = matches.Count,
                results = matches
            }, new JsonSerializerOptions { WriteIndented = true });

            CacheManager.SetByTtl(cacheKey, result, 60);
            return result;
        }

        [McpServerTool, Description("Enables a library in a B4X project by adding it to the LibraryN keys in the project file header. If already enabled, does nothing. Creates .bak backup first.")]
        public static string EnableLibrary(
        [Description("Absolute path to the .b4a/.b4j/.b4i project file, or to the project folder.")] string projectFile,
        [Description("Library name to enable (must match exactly as shown in list_project_libraries or list_available_libraries).")] string libraryName)
        {
            // If a directory was passed, find the project file
            if (Directory.Exists(projectFile))
            {
                var found = ProjectScanner.FindProjectFile(projectFile);
                if (found == null)
                    throw new FileNotFoundException($"No .b4a/.b4j/.b4i project file found in '{projectFile}'.");
                projectFile = found;
            }

            if (!File.Exists(projectFile))
                throw new FileNotFoundException($"Project file not found: {projectFile}");

            string raw = File.ReadAllText(projectFile);
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            string headerSection = markerIdx >= 0 ? raw.Substring(0, markerIdx) : raw;
            string rest = markerIdx >= 0 ? raw.Substring(markerIdx + marker.Length) : "";

            var lines = headerSection.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            // Check if already enabled
            if (lines.Any(l => Regex.IsMatch(l, @"^Library\d+=") &&
                l.Split('=')[1].Trim().Equals(libraryName, StringComparison.OrdinalIgnoreCase)))
            {
                return JsonSerializer.Serialize(new { success = true, projectFile, action = "already_enabled", library = libraryName });
            }

            // Find highest LibraryN number
            int maxNum = 0;
            foreach (var line in lines)
            {
                var m = Regex.Match(line, @"^Library(\d+)=");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int num))
                    maxNum = Math.Max(maxNum, num);
            }

            int newNum = maxNum + 1;
            lines.Add($"Library{newNum}={libraryName}");

            // Update NumberOfLibraries if present
            for (int i = 0; i < lines.Count; i++)
            {
                if (Regex.IsMatch(lines[i], @"^NumberOfLibraries=", RegexOptions.IgnoreCase))
                {
                    lines[i] = $"NumberOfLibraries={newNum}";
                    break;
                }
            }

            // Remove empty lines that break the parser (@EndOfDesignText@ must follow a key=value line directly)
            lines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

            string newHeader = string.Join("\n", lines);
            string newContent = markerIdx >= 0 ? newHeader + "\n" + marker + rest : newHeader;

            File.Copy(projectFile, projectFile + ".bak", overwrite: true);
            File.WriteAllText(projectFile, newContent);
            CacheManager.Invalidate(projectFile);

            return JsonSerializer.Serialize(new
            {
                success = true,
                projectFile,
                action = "enabled",
                library = libraryName,
                key = $"Library{newNum}"
            });
        }

        [McpServerTool, Description("Disables (removes) a library from a B4X project. Also renumbers remaining LibraryN entries so there are no gaps. Creates .bak backup first.")]
        public static string DisableLibrary(
            [Description("Absolute path to the .b4a/.b4j/.b4i project file, or to the project folder.")] string projectFile,
            [Description("Library name to disable (exact name as shown in list_project_libraries).")] string libraryName)
        {
            // If a directory was passed, find the project file
            if (Directory.Exists(projectFile))
            {
                var found = ProjectScanner.FindProjectFile(projectFile);
                if (found == null)
                    throw new FileNotFoundException($"No .b4a/.b4j/.b4i project file found in '{projectFile}'.");
                projectFile = found;
            }

            if (!File.Exists(projectFile))
                throw new FileNotFoundException($"Project file not found: {projectFile}");

            string raw = File.ReadAllText(projectFile);
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            string headerSection = markerIdx >= 0 ? raw.Substring(0, markerIdx) : raw;
            string rest = markerIdx >= 0 ? raw.Substring(markerIdx + marker.Length) : "";

            var lines = headerSection.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            var remaining = new List<string>();
            bool removed = false;
            foreach (var line in lines)
            {
                if (Regex.IsMatch(line, @"^Library\d+=") &&
                    line.Split('=')[1].Trim().Equals(libraryName, StringComparison.OrdinalIgnoreCase))
                {
                    removed = true;
                    continue;
                }
                remaining.Add(line);
            }

            if (!removed)
            {
                return JsonSerializer.Serialize(new { success = true, projectFile, action = "not_found", library = libraryName });
            }

            // Renumber
            var renumbered = new List<string>();
            int idx = 1;
            foreach (var line in remaining)
            {
                if (Regex.IsMatch(line, @"^Library\d+="))
                {
                    var value = line.Split('=')[1].Trim();
                    renumbered.Add($"Library{idx}={value}");
                    idx++;
                }
                else
                {
                    renumbered.Add(line);
                }
            }

            // Update NumberOfLibraries
            for (int i = 0; i < renumbered.Count; i++)
            {
                if (Regex.IsMatch(renumbered[i], @"^NumberOfLibraries=", RegexOptions.IgnoreCase))
                {
                    renumbered[i] = $"NumberOfLibraries={idx - 1}";
                    break;
                }
            }

            // Remove empty lines that break the parser (@EndOfDesignText@ must follow a key=value line directly)
            renumbered = renumbered.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

            string newHeader = string.Join("\n", renumbered);
            string newContent = markerIdx >= 0 ? newHeader + "\n" + marker + rest : newHeader;

            File.Copy(projectFile, projectFile + ".bak", overwrite: true);
            File.WriteAllText(projectFile, newContent);
            CacheManager.Invalidate(projectFile);

            return JsonSerializer.Serialize(new
            {
                success = true,
                projectFile,
                action = "disabled",
                library = libraryName,
                remainingLibraries = idx - 1
            });
        }
    }
}