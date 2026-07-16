using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using B4XMcpServer.Services;
using B4XMcpServer.Utils;

namespace B4XMcpServer.Engine
{
    /// <summary>
    /// Statically validates B4X event handler Subs against the event signatures
    /// declared in referenced libraries, reporting parameter count/name/type mismatches.
    /// </summary>
    public static class EventHandlerValidator
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        // Matches: Dim/Public/Private/Globals <names> As <Type>
        private static readonly Regex VariableDeclarationRegex = new Regex(
            @"(?i)^\s*(?:Dim|Public|Private|Globals)\s+([a-zA-Z_][a-zA-Z0-9_]*(?:\s*,\s*[a-zA-Z_][a-zA-Z0-9_]*)*)\s+As\s+([a-zA-Z_][a-zA-Z0-9_]*)",
            RegexOptions.Compiled, RegexTimeout);

        // Matches event handler Sub names: ControlName_EventName
        private static readonly Regex EventHandlerNameRegex = new Regex(
            @"^([a-zA-Z_][a-zA-Z0-9_]*)_([a-zA-Z_][a-zA-Z0-9_]*)$",
            RegexOptions.Compiled, RegexTimeout);

        // Matches parameter fragments like "Width As Double" or "Width As Double = 0"
        private static readonly Regex ParamRegex = new Regex(
            @"(?i)\b([a-zA-Z_][a-zA-Z0-9_]*)\s+As\s+([a-zA-Z_][a-zA-Z0-9_]*(?:\([^)]*\))?)\b",
            RegexOptions.Compiled, RegexTimeout);

        public sealed class ParameterInfo
        {
            public string Name { get; init; } = "";
            public string Type { get; init; } = "";
        }

        public sealed class EventSignature
        {
            public string EventName { get; init; } = "";
            public string TypeName { get; init; } = "";
            public string LibraryName { get; init; } = "";
            public List<ParameterInfo> Parameters { get; init; } = new();
            public string RawSignature { get; init; } = "";
        }

        public sealed class HandlerMismatch
        {
            public string File { get; init; } = "";
            public string Sub { get; init; } = "";
            public string ControlName { get; init; } = "";
            public string? InferredType { get; init; }
            public string EventName { get; init; } = "";
            public string ExpectedSignature { get; init; } = "";
            public string ActualSignature { get; init; } = "";
            public string Library { get; init; } = "";
            public string Severity { get; init; } = "CRITICAL";
            public List<string> Differences { get; init; } = new();
            public string? FixHint { get; init; }
        }

        public sealed class ValidationResult
        {
            public int HandlersChecked { get; init; }
            public int MismatchCount { get; init; }
            public List<HandlerMismatch> Mismatches { get; init; } = new();
            public List<string> Warnings { get; init; } = new();
        }

        /// <summary>
        /// Validates all event handler Subs in a project against referenced library signatures.
        /// </summary>
        public static ValidationResult Validate(string projectPath)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            string? root = Directory.Exists(projectPath) ? projectPath : ProjectScanner.FindProjectRoot(projectPath);
            if (root == null)
                throw new DirectoryNotFoundException($"Could not determine a B4X project root from '{projectPath}'.");

            string? projectFile = ProjectScanner.FindProjectFile(root);
            if (projectFile == null)
                throw new FileNotFoundException($"No .b4a/.b4j/.b4i project file found in '{root}'.");

            var libraryDirs = B4aConfig.GetLibraryDirectories(root);
            var referencedLibraries = ReadReferencedLibraries(projectFile);

            // Build event signature map: typeName -> eventName -> EventSignature
            var eventMap = BuildEventMap(referencedLibraries, libraryDirs);

            // Build control type map: controlName -> typeName from code and layouts
            var controlTypes = BuildControlTypeMap(root);

            var mismatches = new List<HandlerMismatch>();
            var warnings = new List<string>();
            int handlersChecked = 0;

            var sourceFiles = ProjectScanner.ScanProject(root)
                .Where(f => f.Kind == "bas" || f.Kind == "b4a" || f.Kind == "b4j" || f.Kind == "b4i")
                .ToList();

            foreach (var file in sourceFiles)
            {
                string source;
                try
                {
                    source = CodeUtils.ReadTextSafely(file.Path);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not read {file.Path}: {ex.Message}");
                    continue;
                }

                var (rootNode, _) = B4xParser.Parse(source);
                var nodes = B4xParser.FlattenSubsAndTypes(rootNode);

                foreach (var sub in nodes.Where(n => n.Kind == "Sub"))
                {
                    var match = EventHandlerNameRegex.Match(sub.Name);
                    if (!match.Success) continue;

                    handlersChecked++;
                    string controlName = match.Groups[1].Value;
                    string eventName = match.Groups[2].Value;

                    if (!controlTypes.TryGetValue(controlName, out var inferredType) || string.IsNullOrEmpty(inferredType))
                    {
                        warnings.Add($"Could not infer type for control '{controlName}' in {Path.GetFileName(file.Path)}.{sub.Name}");
                        continue;
                    }

                    if (!eventMap.TryGetValue(inferredType, out var events) ||
                        !events.TryGetValue(eventName, out var expected))
                    {
                        // No event signature found for this type/event combination.
                        // This is not necessarily an error (custom events, etc.) but worth noting.
                        continue;
                    }

                    var actualParams = ParseParameters(sub.Params);
                    var diff = CompareSignatures(expected, actualParams, sub.Name);

                    if (diff.Count > 0)
                    {
                        mismatches.Add(new HandlerMismatch
                        {
                            File = file.Path,
                            Sub = sub.Name,
                            ControlName = controlName,
                            InferredType = inferredType,
                            EventName = eventName,
                            ExpectedSignature = FormatSignature(eventName, expected.Parameters),
                            ActualSignature = FormatSignature(sub.Name, actualParams),
                            Library = expected.LibraryName,
                            Severity = "CRITICAL",
                            Differences = diff,
                            FixHint = $"Change the Sub signature to: Sub {sub.Name} ({string.Join(", ", expected.Parameters.Select(p => $"{p.Name} As {p.Type}"))})"
                        });
                    }
                }
            }

            return new ValidationResult
            {
                HandlersChecked = handlersChecked,
                MismatchCount = mismatches.Count,
                Mismatches = mismatches,
                Warnings = warnings
            };
        }

        /// <summary>
        /// Compares a single user-written Sub signature against an expected library event signature.
        /// Returns a list of human-readable differences.
        /// </summary>
        public static List<string> CompareSignatures(EventSignature expected, List<ParameterInfo> actual, string subName)
        {
            var differences = new List<string>();
            var expectedParams = expected.Parameters;

            if (actual.Count != expectedParams.Count)
            {
                differences.Add($"Parameter count mismatch: expected {expectedParams.Count}, got {actual.Count}.");
                return differences;
            }

            for (int i = 0; i < expectedParams.Count; i++)
            {
                var exp = expectedParams[i];
                var act = actual[i];
                if (!string.Equals(exp.Name, act.Name, StringComparison.OrdinalIgnoreCase))
                {
                    differences.Add($"Parameter {i + 1}: expected name '{exp.Name}', got '{act.Name}'.");
                }
                if (!TypesCompatible(exp.Type, act.Type))
                {
                    differences.Add($"Parameter {i + 1} ({exp.Name}): expected type '{exp.Type}', got '{act.Type}'.");
                }
            }

            return differences;
        }

        /// <summary>
        /// Parses a parameter string like "Width As Double, Height As Double" into a list.
        /// </summary>
        public static List<ParameterInfo> ParseParameters(string? paramsText)
        {
            var result = new List<ParameterInfo>();
            if (string.IsNullOrWhiteSpace(paramsText)) return result;

            // Remove surrounding parentheses if present
            var trimmed = paramsText.Trim();
            if (trimmed.StartsWith("(") && trimmed.EndsWith(")"))
                trimmed = trimmed.Substring(1, trimmed.Length - 2);

            var parts = SplitParameters(trimmed);
            foreach (var part in parts)
            {
                var m = ParamRegex.Match(part);
                if (m.Success)
                {
                    result.Add(new ParameterInfo
                    {
                        Name = m.Groups[1].Value.Trim(),
                        Type = m.Groups[2].Value.Trim()
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Looks up the expected event signature for a given library/type/event.
        /// </summary>
        public static EventSignature? GetExpectedSignature(string libraryName, string typeName, string eventName, List<string> libraryDirs)
        {
            var lib = LibraryScanner.FindLibrary(libraryName, libraryDirs);
            if (lib == null) return null;

            var docs = LibraryScanner.GetLibraryDocs(lib, typeName);
            var ev = docs.Members.FirstOrDefault(m => m.Kind == "event" &&
                string.Equals(m.Name, eventName, StringComparison.OrdinalIgnoreCase));

            if (ev == null) return null;

            return new EventSignature
            {
                EventName = ev.Name,
                TypeName = typeName,
                LibraryName = libraryName,
                Parameters = ParseParameters(ev.Parameters),
                RawSignature = ev.Signature
            };
        }

        private static Dictionary<string, Dictionary<string, EventSignature>> BuildEventMap(
            List<string> libraryNames,
            List<string> libraryDirs)
        {
            var map = new Dictionary<string, Dictionary<string, EventSignature>>(StringComparer.OrdinalIgnoreCase);

            foreach (var libName in libraryNames)
            {
                var lib = LibraryScanner.FindLibrary(libName, libraryDirs);
                if (lib == null) continue;

                try
                {
                    var docs = LibraryScanner.GetLibraryDocs(lib.XmlPath, null);
                    foreach (var member in docs.Members.Where(m => m.Kind == "event"))
                    {
                        if (!map.ContainsKey(docs.TypeName))
                            map[docs.TypeName] = new Dictionary<string, EventSignature>(StringComparer.OrdinalIgnoreCase);

                        map[docs.TypeName][member.Name] = new EventSignature
                        {
                            EventName = member.Name,
                            TypeName = docs.TypeName,
                            LibraryName = libName,
                            Parameters = ParseParameters(member.Parameters),
                            RawSignature = member.Signature
                        };
                    }
                }
                catch
                {
                    // Skip libraries that fail to parse
                }
            }

            return map;
        }

        private static Dictionary<string, string> BuildControlTypeMap(string root)
        {
            var controlTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. Variable declarations in source files
            var sourceFiles = ProjectScanner.ScanProject(root)
                .Where(f => f.Kind == "bas" || f.Kind == "b4a" || f.Kind == "b4j" || f.Kind == "b4i")
                .ToList();

            foreach (var file in sourceFiles)
            {
                string source;
                try
                {
                    source = CodeUtils.ReadTextSafely(file.Path);
                }
                catch
                {
                    continue;
                }

                var lines = source.Split('\n');
                foreach (var line in lines)
                {
                    var m = VariableDeclarationRegex.Match(line);
                    if (!m.Success) continue;

                    var names = m.Groups[1].Value.Split(',').Select(n => n.Trim()).Where(n => !string.IsNullOrEmpty(n));
                    var type = m.Groups[2].Value.Trim();

                    foreach (var name in names)
                    {
                        controlTypes[name] = type;
                    }
                }
            }

            // 2. Controls from layout files
            var layoutFiles = ProjectScanner.ScanProject(root)
                .Where(f => f.Kind == "bal" || f.Kind == "bjl" || f.Kind == "bil")
                .ToList();

            foreach (var layout in layoutFiles)
            {
                try
                {
                    var data = File.ReadAllBytes(layout.Path);
                    var decoded = BalDecoder.DecodeToObject(data);
                    if (decoded.TryGetValue("rootControl", out var rootObj) && rootObj is Dictionary<string, object> rootControl)
                    {
                        CollectControlsFromLayout(rootControl, controlTypes);
                    }
                }
                catch
                {
                    // Ignore layout decode failures
                }
            }

            return controlTypes;
        }

        private static void CollectControlsFromLayout(Dictionary<string, object> node, Dictionary<string, string> controlTypes)
        {
            if (node.TryGetValue("properties", out var propsObj) && propsObj is Dictionary<string, object> props)
            {
                if (props.TryGetValue("name", out var nameObj) && nameObj is string name &&
                    props.TryGetValue("type", out var typeObj) && typeObj is string type)
                {
                    controlTypes[name] = type;
                }
            }

            if (node.TryGetValue("children", out var kidsObj) && kidsObj is List<object> kids)
            {
                foreach (var kid in kids)
                {
                    if (kid is Dictionary<string, object> kidDict)
                        CollectControlsFromLayout(kidDict, controlTypes);
                }
            }
        }

        private static List<string> ReadReferencedLibraries(string projectFile)
        {
            var libraries = new List<string>();
            string raw = File.ReadAllText(projectFile);
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            string headerSection = markerIdx >= 0 ? raw.Substring(0, markerIdx) : raw;

            foreach (var lineRaw in headerSection.Split('\n'))
            {
                var line = lineRaw.TrimEnd('\r').Trim();
                if (string.IsNullOrEmpty(line)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                if (Regex.IsMatch(key, @"^Library\d+$", RegexOptions.None, RegexTimeout))
                    libraries.Add(value);
            }

            return libraries;
        }

        private static bool TypesCompatible(string expected, string actual)
        {
            // Event handlers require exact type match. B4X reflection binding is strict.
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatSignature(string name, List<ParameterInfo> parameters)
        {
            var paramString = string.Join(", ", parameters.Select(p => $"{p.Name} As {p.Type}"));
            return $"{name}({paramString})";
        }

        /// <summary>
        /// Formats a signature for human-readable display.
        /// </summary>
        public static string FormatSignatureForDisplay(string name, List<ParameterInfo> parameters)
        {
            var paramString = string.Join(", ", parameters.Select(p => $"{p.Name} As {p.Type}"));
            return $"{name}({paramString})";
        }

        private static List<string> SplitParameters(string parameters)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(parameters)) return result;

            var current = new System.Text.StringBuilder();
            int depth = 0;
            foreach (var c in parameters)
            {
                if (c == '(') depth++;
                if (c == ')') depth--;
                if (c == ',' && depth == 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            if (current.Length > 0)
                result.Add(current.ToString().Trim());

            return result;
        }
    }
}
