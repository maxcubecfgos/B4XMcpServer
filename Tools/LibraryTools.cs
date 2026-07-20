using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using B4XMcpServer.Engine;
using B4XMcpServer.Repositories;
using B4XMcpServer.Services;
using B4XMcpServer.Utils;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class LibraryTools
    {
        private readonly IFileRepository _fileRepository;
        private readonly IProjectRepository _projectRepository;

        // Regex timeout protects against catastrophic backtracking on untrusted input.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        public LibraryTools(IFileRepository fileRepository, IProjectRepository projectRepository)
        {
            _fileRepository = fileRepository;
            _projectRepository = projectRepository;
        }

        [McpServerTool, Description("Lists all libraries referenced in a B4X project file (.b4a/.b4j/.b4i): reads the Library1, Library2... keys from the IDE metadata header. Returns the library names in order.")]
        public string ListProjectLibraries(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file.")] string projectPath)
        {
            string? projectFile = _fileRepository.Exists(projectPath) ? projectPath : _projectRepository.FindProjectFile(projectPath);
            if (projectFile == null)
                throw new FileNotFoundException($"No .b4a/.b4j/.b4i project file found for '{projectPath}'.");

            string raw = _fileRepository.ReadTextWithHeader(projectFile);
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
        public string ListAvailableLibraries(
            [Description("Absolute path to the B4X project folder, or to its .b4a/.b4j/.b4i project file.")] string? projectPath = null)
        {
            string? root = null;
            if (!string.IsNullOrEmpty(projectPath))
            {
                root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
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
        public string GetLibraryDocs(
            [Description("Library name (e.g. 'Core', 'XUI', 'XUI Views', 'StringUtils')")] string libraryName,
            [Description("Optional: filter to a specific class/type name within the library (e.g. 'B4XView', 'BitmapCreator')")] string? typeName = null,
            [Description("Absolute path to the B4X project folder (optional, helps find project-local libraries)")] string? projectPath = null)
        {
            string? root = null;
            if (!string.IsNullOrEmpty(projectPath))
            {
                root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
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
        public string GetLibraryEvents(
            [Description("Library name (e.g. 'jFX')")] string libraryName,
            [Description("Type/class name within the library (e.g. 'Panel', 'TabPane')")] string typeName,
            [Description("Absolute path to the B4X project folder (optional, helps find project-local libraries)")] string? projectPath = null)
        {
            string? root = null;
            if (!string.IsNullOrEmpty(projectPath))
            {
                root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
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
        public string CompareWithLibrarySignature(
            [Description("Library name (e.g. 'jFX')")] string libraryName,
            [Description("Type/class name within the library (e.g. 'Panel')")] string typeName,
            [Description("Event name (e.g. 'Resize')")] string eventName,
            [Description("User-written Sub signature, e.g. 'panRoot_Resize (Width As Int, Height As Int)'")] string subSignature,
            [Description("Absolute path to the B4X project folder (optional, helps find project-local libraries)")] string? projectPath = null)
        {
            string? root = null;
            if (!string.IsNullOrEmpty(projectPath))
            {
                root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
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
        public string SearchLibrary(
            [Description("Search query: method name, property name, event name, or keyword")] string query,
            [Description("Absolute path to the B4X project folder (optional, helps search project-local libraries)")] string? projectPath = null)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Query must not be empty.");

            string? root = null;
            if (!string.IsNullOrEmpty(projectPath))
            {
                root = Directory.Exists(projectPath) ? projectPath : _projectRepository.FindProjectRoot(projectPath);
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


    }
}