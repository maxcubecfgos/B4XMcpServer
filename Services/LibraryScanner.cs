using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace B4XMcpServer.Services
{
    public class LibraryInfo
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "?";

        public string Source { get; set; } = "";

        public bool IsB4XLib { get; set; }

        public string JarPath { get; set; } = "";

        public string XmlPath { get; set; } = "";

        public string B4XLibPath { get; set; } = "";
    }

    public class LibraryMember
    {
        public string Module { get; set; } = "";

        public string Kind { get; set; } = "";

        public string Name { get; set; } = "";

        public string Signature { get; set; } = "";

        public string? ReturnType { get; set; }

        public string? Parameters { get; set; }

        public string? Description { get; set; }
    }

    public class LibraryDocs
    {
        public string Name { get; set; } = "";

        public string Version { get; set; } = "?";

        public string TypeName { get; set; } = "";

        public bool IsB4XLib { get; set; }

        public List<LibraryMember> Members { get; set; } = new();
    }

    public class LibrarySearchResult
    {
        public string Library { get; set; } = "";

        public string Module { get; set; } = "";

        public string Kind { get; set; } = "";

        public string Name { get; set; } = "";

        public string Signature { get; set; } = "";

        public string Description { get; set; } = "";
    }

    public static class LibraryScanner
    {
        // Regex timeout protects against catastrophic backtracking on malformed
        // source files passed to the library scanner.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        private static readonly Regex PublicSubRegex =
    new(@"(?im)^\s*(Public\s+)?Sub\s+([A-Za-z_]\w*)\s*\((.*?)\)",
        RegexOptions.Compiled, RegexTimeout);

        private static readonly Regex TypeRegex =
            new(@"(?im)^\s*Type\s+([A-Za-z_]\w*)",
                RegexOptions.Compiled, RegexTimeout);

        private static readonly Regex EnumRegex =
            new(@"(?im)^\s*Enum\s+([A-Za-z_]\w*)",
                RegexOptions.Compiled, RegexTimeout);

        private static readonly Regex PublicFieldRegex =
            new(@"(?im)^\s*Public\s+([A-Za-z_]\w*)",
                RegexOptions.Compiled, RegexTimeout);
        /// <summary>
        /// Lists all available libraries (.jar + .xml pairs) from the given directories.
        /// </summary>
        public static List<LibraryInfo> ListLibraries(List<string> libraryDirs)
        {
            var libs = new List<LibraryInfo>();

            foreach (var dir in libraryDirs)
            {
                if (!Directory.Exists(dir))
                    continue;

                foreach (var xmlFile in Directory.GetFiles(dir, "*.xml"))
                {
                    var jar = Path.ChangeExtension(xmlFile, ".jar");

                    if (!File.Exists(jar))
                        continue;

                    try
                    {
                        var doc = XDocument.Load(xmlFile);

                        libs.Add(new LibraryInfo
                        {
                            Name = doc.Root?.Element("name")?.Value ??
                                   Path.GetFileNameWithoutExtension(xmlFile),

                            Version = doc.Root?.Element("version")?.Value ?? "?",

                            Source = dir,

                            XmlPath = xmlFile,

                            JarPath = jar,

                            IsB4XLib = false
                        });
                    }
                    catch
                    {
                        libs.Add(new LibraryInfo
                        {
                            Name = Path.GetFileNameWithoutExtension(xmlFile),

                            Version = "?",

                            Source = dir,

                            XmlPath = xmlFile,

                            JarPath = jar,

                            IsB4XLib = false
                        });
                    }
                }

                foreach (var lib in Directory.GetFiles(dir, "*.b4xlib"))
                {
                    libs.Add(new LibraryInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(lib),
                        Version = "?",
                        Source = dir,
                        B4XLibPath = lib,
                        IsB4XLib = true
                    });
                }
            }

            return libs
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static LibraryInfo? FindLibrary(string name, List<string> libraryDirs)
        {
            return ListLibraries(libraryDirs)
                .FirstOrDefault(x =>
                    x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Finds the XML path for a library by name.
        /// </summary>
        public static string? FindLibraryXml(string name, List<string> libraryDirs)
        {
            return FindLibrary(name, libraryDirs)?.XmlPath;
        }

        public static LibraryDocs GetLibraryDocs(LibraryInfo library, string? filterTypeName = null)
        {
            if (library.IsB4XLib)
                return GetB4XLibraryDocs(library.B4XLibPath);

            return GetLibraryDocs(library.XmlPath, filterTypeName);
        }

        private static LibraryDocs GetB4XLibraryDocs(string b4xlibPath)
        {
            var docs = new LibraryDocs
            {
                Name = Path.GetFileNameWithoutExtension(b4xlibPath),
                Version = "?",
                IsB4XLib = true
            };

            using var zip = ZipFile.OpenRead(b4xlibPath);

            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.EndsWith(".bas", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var stream = entry.Open();
                using var reader = new StreamReader(stream);

                string source = reader.ReadToEnd();

                ParseBasModule(
                    Path.GetFileNameWithoutExtension(entry.Name),
                    source,
                    docs);
            }

            return docs;
        }

        private static void ParseBasModule(
            string moduleName,
            string source,
            LibraryDocs docs)
        {
            foreach (Match m in PublicSubRegex.Matches(source))
            {
                docs.Members.Add(new LibraryMember
                {
                    Module = moduleName,
                    Kind = "method",
                    Name = m.Groups[2].Value.Trim(),
                    Parameters = m.Groups[3].Value.Trim(),
                    Signature = $"{m.Groups[2].Value}({m.Groups[3].Value})"
                });
            }

            foreach (Match m in TypeRegex.Matches(source))
            {
                docs.Members.Add(new LibraryMember
                {
                    Module = moduleName,
                    Kind = "type",
                    Name = m.Groups[1].Value,
                    Signature = m.Value.Trim()
                });
            }

            foreach (Match m in EnumRegex.Matches(source))
            {
                docs.Members.Add(new LibraryMember
                {
                    Module = moduleName,
                    Kind = "enum",
                    Name = m.Groups[1].Value,
                    Signature = m.Value.Trim()
                });
            }

            foreach (Match m in PublicFieldRegex.Matches(source))
            {
                docs.Members.Add(new LibraryMember
                {
                    Module = moduleName,
                    Kind = "field",
                    Name = m.Groups[1].Value,
                    Signature = m.Value.Trim()
                });
            }
        }

        /// <summary>
        /// Returns only the event declarations for a specific type in a library XML.
        /// </summary>
        public static List<LibraryMember> GetLibraryEvents(string xmlPath, string typeName)
        {
            var docs = GetLibraryDocs(xmlPath, typeName);
            return docs.Members.Where(m => m.Kind == "event").ToList();
        }

        /// <summary>
        /// Extracts documentation from a library XML: methods, properties, events.
        /// </summary>
        public static LibraryDocs GetLibraryDocs(string xmlPath, string? filterTypeName = null)
        {
            var doc = XDocument.Load(xmlPath);
            var nameEl = doc.Root?.Element("name");
            var versionEl = doc.Root?.Element("version");

            var result = new LibraryDocs
            {
                Name = nameEl?.Value ?? Path.GetFileNameWithoutExtension(xmlPath),
                Version = versionEl?.Value ?? "?"
            };

            foreach (var cls in doc.Descendants("class"))
            {
                var typeName = cls.Attribute("typeName")?.Value ?? "";
                if (!string.IsNullOrEmpty(filterTypeName) &&
                    !typeName.Contains(filterTypeName, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.TypeName = typeName;

                // Methods
                foreach (var m in cls.Elements("method"))
                {
                    // User-feedback (AI external, round 2): the previous fallback "?\" made
                    // it look like a real symbol name to LLMs (e.g. jSystemTray, CSSUtils).
                    // Self-describing label so the AI sees immediately that the source XML
                    // doc is incomplete and shouldn't be trusted for signatures.
                    var mName = m.Attribute("name")?.Value ?? "(missing from XML doc)";
                    var retType = ShortType(m.Attribute("returnType")?.Value ?? "");
                    var parameters = string.Join(", ", m.Elements("parameter").Select(p =>
                    {
                        var pName = p.Attribute("name")?.Value ?? "";
                        var pType = ShortType(p.Attribute("type")?.Value ?? "");
                        return $"{pName}: {pType}";
                    }));
                    var comment = m.Element("comment")?.Value?.Trim() ?? "";

                    result.Members.Add(new LibraryMember
                    {
                        Kind = "method",
                        Name = mName,
                        ReturnType = string.IsNullOrEmpty(retType) || retType == "void" ? null : retType,
                        Parameters = string.IsNullOrEmpty(parameters) ? null : parameters,
                        Description = TruncateComment(comment)
                    });
                }

                // Properties
                foreach (var p in cls.Elements("property"))
                {
                    // See method-loop comment above for why "?\" was misleading.
                    var pName = p.Attribute("name")?.Value ?? "(missing from XML doc)";
                    var pType = ShortType(p.Attribute("type")?.Value ?? "");
                    var comment = p.Element("comment")?.Value?.Trim() ?? "";

                    result.Members.Add(new LibraryMember
                    {
                        Kind = "property",
                        Name = pName,
                        ReturnType = pType,
                        Description = TruncateComment(comment)
                    });
                }

                // Events
                foreach (var ev in cls.Elements("event"))
                {
                    // See method-loop comment above for why "?\" was misleading.
                    var evName = ev.Attribute("name")?.Value ?? "(missing from XML doc)";
                    var parameters = string.Join(", ", ev.Elements("parameter").Select(p =>
                    {
                        var pName = p.Attribute("name")?.Value ?? "";
                        var pType = ShortType(p.Attribute("type")?.Value ?? "");
                        return $"{pName}: {pType}";
                    }));
                    var comment = ev.Element("comment")?.Value?.Trim() ?? "";

                    result.Members.Add(new LibraryMember
                    {
                        Kind = "event",
                        Name = evName,
                        Parameters = string.IsNullOrEmpty(parameters) ? null : parameters,
                        Description = TruncateComment(comment)
                    });
                }

                // If filtering by typeName, only return the first match
                if (!string.IsNullOrEmpty(filterTypeName)) break;
            }

            return result;
        }

        /// <summary>
        /// Searches all libraries for methods/properties/events matching a query.
        /// </summary>
        public static List<object> SearchLibraries(string query, List<string> libraryDirs)
        {
            var matches = new List<object>();

            string q = query.Trim();

            foreach (var library in ListLibraries(libraryDirs))
            {
                if (library.IsB4XLib)
                {
                    var docs = GetB4XLibraryDocs(library.B4XLibPath);

                    foreach (var member in docs.Members)
                    {
                        if (Contains(member.Name, q) ||
                            Contains(member.Signature, q) ||
                            Contains(member.Module, q))
                        {
                            matches.Add(new LibrarySearchResult
                            {
                                Library = library.Name,
                                Module = member.Module,
                                Kind = member.Kind,
                                Name = member.Name,
                                Signature = member.Signature,
                                Description = member.Description ?? ""
                            });
                        }
                    }
                }
                else
                {
                    try
                    {
                        var xml = XDocument.Load(library.XmlPath);

                        foreach (var cls in xml.Descendants("class"))
                        {
                            string typeName = cls.Attribute("typeName")?.Value ?? "";

                            foreach (var elem in cls.Elements())
                            {
                                string memberName = elem.Attribute("name")?.Value ?? "";

                                string comment =
                                    elem.Element("comment")?.Value ?? "";

                                if (Contains(memberName, q) ||
                                    Contains(typeName, q) ||
                                    Contains(comment, q))
                                {
                                    matches.Add(new LibrarySearchResult
                                    {
                                        Library = library.Name,
                                        Module = typeName,
                                        Kind = elem.Name.LocalName,
                                        Name = memberName,
                                        Signature = memberName,
                                        Description = TruncateComment(comment) ?? ""
                                    });
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return matches
                .OrderBy(m => ((LibrarySearchResult)m).Library)
                .ThenBy(m => ((LibrarySearchResult)m).Module)
                .ThenBy(m => ((LibrarySearchResult)m).Name)
                .Take(100)
                .Cast<object>()
                .ToList();
        }

        private static bool Contains(string? text, string query)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return text.IndexOf(
                query,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static string ShortType(string fullType)
        {
            if (string.IsNullOrEmpty(fullType)) return "";
            var dot = fullType.LastIndexOf('.');
            return dot >= 0 ? fullType.Substring(dot + 1) : fullType;
        }

        private static string? TruncateComment(string? comment)
        {
            if (string.IsNullOrWhiteSpace(comment)) return null;
            var firstLine = comment.Split('\n')[0].Trim();
            return firstLine.Length > 120 ? firstLine.Substring(0, 120) + "..." : firstLine;
        }
    }
}