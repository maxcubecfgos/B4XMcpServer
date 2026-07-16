using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using B4XMcpServer.Engine;
using B4XMcpServer.Services;
using B4XMcpServer.Utils;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class LibraryTools
    {
        // Regex timeout protects against catastrophic backtracking on untrusted input.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

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
                .Where(kv => Regex.IsMatch(kv.Key, @"^Library\d+$", RegexOptions.None, RegexTimeout))
                .OrderBy(kv => int.Parse(Regex.Match(kv.Key, @"\d+", RegexOptions.None, RegexTimeout).Value))
                .Select(kv => new { key = kv.Key, name = kv.Value })
                .ToList();

            return JsonSerializer.Serialize(new
            {
                projectFile,
                libraryCount = libraries.Count,
                libraries
            }, JsonOptions.Default);
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
            }, JsonOptions.Default);

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
            var library = LibraryScanner.FindLibrary(libraryName, dirs);

            if (library == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Library '{libraryName}' not found in any configured library directory.",
                    searchedDirectories = dirs
                }, JsonOptions.Default);
            }

            string cacheFile =
                library.IsB4XLib
                    ? library.B4XLibPath
                    : library.XmlPath;

            string cacheKey =
                $"libs:docs:{cacheFile}:{typeName ?? ""}";

            if (string.IsNullOrEmpty(typeName))
            {
                if (CacheManager.TryGetByMtime<string>(cacheFile, out var cached)
                    && cached != null)
                {
                    return cached;
                }
            }

            var docs = LibraryScanner.GetLibraryDocs(library, typeName);

            var result = JsonSerializer.Serialize(new
            {
                library = docs.Name,
                version = docs.Version,
                typeName = string.IsNullOrEmpty(docs.TypeName) ? null : docs.TypeName,
                memberCount = docs.Members.Count,
                members = docs.Members.Select(m => new
                {
                    module = string.IsNullOrWhiteSpace(m.Module)
        ? null
        : m.Module,                kind = m.Kind,

                // Round-3 polish: expose name + returnType + parameters as dedicated
                // JSON fields so consumers can match by name rather than parsing the
                // synthetic signature. The (missing from XML doc) placeholder from
                // round 2 flows through both `name` and `signature` consistently when
                // the source XML doc omitted the `name=` attribute.
                name = m.Name,

                returnType = m.ReturnType,

                parameters = m.Parameters,

                signature =
        !string.IsNullOrEmpty(m.Signature)
            ? m.Signature
            : m.Kind switch
            {
                "method" =>
                    $"{m.Name}({m.Parameters ?? ""})" +
                    (m.ReturnType != null
                        ? $" → {m.ReturnType}"
                        : ""),

                "property" =>
                    $"{m.Name}: {m.ReturnType ?? "?"}",

                "event" =>
                    $"{m.Name}({m.Parameters ?? ""})",

                _ => m.Name
            },

                    description = m.Description
                })
            }, JsonOptions.Default);

            if (string.IsNullOrEmpty(typeName))
            {
                CacheManager.SetByMtime(cacheFile, result);
            }

            return result;
        }

        [McpServerTool, Description("Returns the event declarations for a given library/type. Use this to discover the exact parameter names and types an event expects before writing its handler Sub.")]
        public static string GetLibraryEvents(
            [Description("Library name (e.g. 'jFX')")] string libraryName,
            [Description("Type/class name within the library (e.g. 'Panel', 'TabPane')")] string typeName,
            [Description("Absolute path to the B4X project folder (optional, helps find project-local libraries)")] string? projectPath = null)
        {
            string? root = null;
            if (!string.IsNullOrEmpty(projectPath))
            {
                root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            }

            var dirs = B4aConfig.GetLibraryDirectories(root);
            var library = LibraryScanner.FindLibrary(libraryName, dirs);

            if (library == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Library '{libraryName}' not found in any configured library directory.",
                    searchedDirectories = dirs
                }, JsonOptions.Default);
            }

            if (library.IsB4XLib)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Library '{libraryName}' is a .b4xlib; event extraction from .b4xlib files is not yet supported. Use get_library_docs instead."
                }, JsonOptions.Default);
            }

            var events = LibraryScanner.GetLibraryEvents(library.XmlPath, typeName);

            return JsonSerializer.Serialize(new
            {
                library = libraryName,
                type = typeName,
                eventCount = events.Count,
                events = events.Select(e => new
                {
                    name = e.Name,
                    parameters = e.Parameters,
                    signature = string.IsNullOrEmpty(e.Signature) ? $"{e.Name}({e.Parameters ?? ""})" : e.Signature,
                    description = e.Description
                })
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Compares a single user-written Sub signature against the expected library event signature for a given library/type/event. Returns whether it matches and a list of differences.")]
        public static string CompareWithLibrarySignature(
            [Description("Library name (e.g. 'jFX')")] string libraryName,
            [Description("Type/class name within the library (e.g. 'Panel')")] string typeName,
            [Description("Event name (e.g. 'Resize')")] string eventName,
            [Description("User-written Sub signature, e.g. 'panRoot_Resize (Width As Int, Height As Int)'")] string subSignature,
            [Description("Absolute path to the B4X project folder (optional, helps find project-local libraries)")] string? projectPath = null)
        {
            string? root = null;
            if (!string.IsNullOrEmpty(projectPath))
            {
                root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            }

            var dirs = B4aConfig.GetLibraryDirectories(root);
            var expected = EventHandlerValidator.GetExpectedSignature(libraryName, typeName, eventName, dirs);

            if (expected == null)
            {
                return JsonSerializer.Serialize(new
                {
                    matches = false,
                    error = $"Could not find event '{eventName}' for {libraryName}.{typeName}."
                }, JsonOptions.Default);
            }

            var actualParams = EventHandlerValidator.ParseParameters(subSignature);
            var differences = EventHandlerValidator.CompareSignatures(expected, actualParams, subSignature);

            var expectedSig = EventHandlerValidator.FormatSignatureForDisplay(expected.EventName, expected.Parameters);
            var actualName = subSignature.Contains("_") ? subSignature.Substring(0, subSignature.IndexOf(' ')) : subSignature;
            var actualSig = EventHandlerValidator.FormatSignatureForDisplay(actualName, actualParams);

            return JsonSerializer.Serialize(new
            {
                matches = differences.Count == 0,
                expected = expectedSig,
                actual = actualSig,
                differences
            }, JsonOptions.Default);
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
            }, JsonOptions.Default);

            CacheManager.SetByTtl(cacheKey, result, 60);
            return result;
        }

        [McpServerTool, Description("Enables a library in a B4X project by adding it to the LibraryN keys in the project file header. If already enabled, does nothing. Creates .bak backup first. Refuses to enable a library that isn't found in any configured library directory (B4A, B4J, AdditionalLibraries, project-local Libraries/) — so a typo can't later break compilation.")]
        public static string EnableLibrary(
        [Description("Absolute path to the .b4a/.b4j/.b4i project file, or to the project folder.")] string projectFile,
        [Description("Library name to enable (must match exactly as shown in list_project_libraries or list_available_libraries).")] string libraryName)
        {
            // User-feedback (AI external, round 3): trading the round-1 absolute-path guard for
            // path-policy consistency with get_project_structure (which already accepts ".").
            // The Directory.Exists branch below resolves relative paths to a real project file
            // before any destructive I/O runs, so the round-1 safety net is replaced by an
            // equally strong post-resolution guard.
            // Original round-2 rationale (`PathSecurity.ValidateAbsolutePath`) preserved in
            // git history — see `git log feat/cli-dispatcher~3` for the WAI bug context that
            // justified adding the guard.
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

            // Audit-fixed check ordering: read the project header FIRST and short-circuit
            // with "already_enabled" if the library is already in the file. Then validate
            // the library actually exists on disk. Previously the existence check ran
            // first, which made a no-op re-enable of an already-listed library fail with
            // "library not found" when the library's IDE bundle wasn't installed on the
            // local machine (e.g. B4i icore on a B4A-only dev box, or any library whose
            // src folder isn't registered in B4aConfig). The existence validation still
            // runs when adding a NEW library, so typos that would silently add a fake
            // `LibraryN=jrandom` entry to the project header are still caught.
            string raw = File.ReadAllText(projectFile);
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            string headerSection = markerIdx >= 0 ? raw.Substring(0, markerIdx) : raw;
            string rest = markerIdx >= 0 ? raw.Substring(markerIdx + marker.Length) : "";

            var lines = headerSection.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

            // Check if already enabled
            if (lines.Any(l => Regex.IsMatch(l, @"^Library\d+=", RegexOptions.None, RegexTimeout) &&
                l.Split('=')[1].Trim().Equals(libraryName, StringComparison.OrdinalIgnoreCase)))
            {
                return JsonSerializer.Serialize(new { success = true, projectFile, action = "already_enabled", library = libraryName });
            }

            // Refuse to enable a library that doesn't actually exist in any configured directory —
            // otherwise the project header gets a `LibraryN=jrandom` reference and the IDE/builder
            // fails to compile with an unresolvable library name.
            //
            // MUST come AFTER the already-enabled early-return above: reordering to put this
            // validation first would re-introduce the audit fix bug where a no-op re-enable
            // throws on machines where the library isn't installed locally (e.g. B4i icore
            // on a B4A-only dev box). If you edit the ordering, also update the related
            // comment block above.
            string? projectRoot = null;
            try
            {
                projectRoot = ProjectScanner.FindProjectRoot(projectFile);
            }
            catch { /* fall back to global dirs below */ }
            var dirs = B4aConfig.GetLibraryDirectories(projectRoot);
            var resolved = LibraryScanner.FindLibrary(libraryName, dirs);
            if (resolved == null)
            {
                throw new ArgumentException(
                    $"Library '{libraryName}' not found in any configured library directory. " +
                    $"Searched: {string.Join(", ", dirs)}. " +
                    $"Run list_available_libraries to see the exact names of installed libraries.");
            }

            // Find highest LibraryN number. Regex matches any populated `LibraryN=<value>`
            // line — NOT just empty-value placeholders. Earlier code used `^Library\d+=$`
            // which only matched `LibraryN=` with NOTHING after the `=`, causing real
            // projects' `Library1=core` / `Library2=XUI` entries to be missed, so maxNum
            // stayed 0 and the newly-added `Library1=<newName>` would overwrite the
            // existing `Library1=core`. (Audit fix — broad `=` matches populated entries.)
            int maxNum = 0;
            foreach (var line in lines)
            {
                var m = Regex.Match(line, @"^Library(\d+)=", RegexOptions.None, RegexTimeout);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int num))
                    maxNum = Math.Max(maxNum, num);
            }

            int newNum = maxNum + 1;
            lines.Add($"Library{newNum}={libraryName}");

            // Update NumberOfLibraries if present
            for (int i = 0; i < lines.Count; i++)
            {
                if (Regex.IsMatch(lines[i], @"^NumberOfLibraries=", RegexOptions.IgnoreCase, RegexTimeout))
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
            // User-feedback (AI external, round 3): trading the round-1 absolute-path guard for
            // path-policy consistency with get_project_structure (which already accepts ".").
            // See EnableLibrary for the same rationale; symmetric removal across both
            // library-modifying tools. The Directory.Exists branch resolves relative paths
            // before any destructive I/O runs.
            // Original round-2 rationale (`PathSecurity.ValidateAbsolutePath`) preserved in
            // git history — see `git log feat/cli-dispatcher~3` for the WAI bug context that
            // justified adding the guard.
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
                if (Regex.IsMatch(line, @"^Library\d+=", RegexOptions.None, RegexTimeout) &&
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

            // Renumber remaining LibraryN entries sequentially from 1 so there are no gaps.
            // Regex matches any populated `LibraryN=<value>` line — NOT just empty-value
            // placeholders. Earlier code used `^Library\d+=$` which only matched
            // `LibraryN=` with NOTHING after the `=`, so real entries like
            // `Library1=core` were never renumbered, leaving gaps and producing a
            // broken `NumberOfLibraries=0` (since `idx` never incremented past 1).
            // (Audit fix — broad `=` matches populated entries.)
            var renumbered = new List<string>();
            int idx = 1;
            foreach (var line in remaining)
            {
                if (Regex.IsMatch(line, @"^Library\d+=", RegexOptions.None, RegexTimeout))
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
                if (Regex.IsMatch(renumbered[i], @"^NumberOfLibraries=", RegexOptions.IgnoreCase, RegexTimeout))
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