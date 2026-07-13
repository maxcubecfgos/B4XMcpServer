using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace B4XContext.Services
{
    public class LibraryInfo
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "?";
        public string Source { get; set; } = "";
        public string JarPath { get; set; } = "";
        public string XmlPath { get; set; } = "";
    }

    public class LibraryMember
    {
        public string Kind { get; set; } = ""; // method, property, event
        public string Name { get; set; } = "";
        public string? ReturnType { get; set; }
        public string? Parameters { get; set; }
        public string? Description { get; set; }
    }

    public class LibraryDocs
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "?";
        public string TypeName { get; set; } = "";
        public List<LibraryMember> Members { get; set; } = new();
    }

    public static class LibraryScanner
    {
        /// <summary>
        /// Lists all available libraries (.jar + .xml pairs) from the given directories.
        /// </summary>
        public static List<LibraryInfo> ListLibraries(List<string> libraryDirs)
        {
            var libs = new List<LibraryInfo>();

            foreach (var dir in libraryDirs)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var xmlFile in Directory.GetFiles(dir, "*.xml"))
                {
                    var jarFile = Path.ChangeExtension(xmlFile, ".jar");
                    if (!File.Exists(jarFile)) continue;

                    try
                    {
                        var doc = XDocument.Load(xmlFile);
                        var nameEl = doc.Root?.Element("name");
                        var versionEl = doc.Root?.Element("version");
                        libs.Add(new LibraryInfo
                        {
                            Name = nameEl?.Value ?? Path.GetFileNameWithoutExtension(xmlFile),
                            Version = versionEl?.Value ?? "?",
                            Source = dir,
                            JarPath = jarFile,
                            XmlPath = xmlFile
                        });
                    }
                    catch
                    {
                        libs.Add(new LibraryInfo
                        {
                            Name = Path.GetFileNameWithoutExtension(xmlFile),
                            Version = "?",
                            Source = dir,
                            JarPath = jarFile,
                            XmlPath = xmlFile
                        });
                    }
                }
            }

            return libs.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Finds the XML path for a library by name.
        /// </summary>
        public static string? FindLibraryXml(string name, List<string> libraryDirs)
        {
            foreach (var dir in libraryDirs)
            {
                if (!Directory.Exists(dir)) continue;
                var candidate = Path.Combine(dir, name + ".xml");
                if (File.Exists(candidate)) return candidate;
                var found = Directory.GetFiles(dir, "*.xml")
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
            }
            return null;
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
                    var mName = m.Attribute("name")?.Value ?? "?";
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
                    var pName = p.Attribute("name")?.Value ?? "?";
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
                    var evName = ev.Attribute("name")?.Value ?? "?";
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
            var queryLower = query.ToLowerInvariant();

            foreach (var dir in libraryDirs)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var xmlFile in Directory.GetFiles(dir, "*.xml"))
                {
                    if (!File.Exists(Path.ChangeExtension(xmlFile, ".jar"))) continue;

                    try
                    {
                        var doc = XDocument.Load(xmlFile);
                        var libName = doc.Root?.Element("name")?.Value ?? Path.GetFileNameWithoutExtension(xmlFile);

                        foreach (var cls in doc.Descendants("class"))
                        {
                            var typeName = cls.Attribute("typeName")?.Value ?? "";

                            foreach (var elem in cls.Elements())
                            {
                                var mName = elem.Attribute("name")?.Value ?? "";
                                var comment = elem.Element("comment")?.Value ?? "";

                                if (mName.ToLowerInvariant().Contains(queryLower) ||
                                    comment.ToLowerInvariant().Contains(queryLower) ||
                                    typeName.ToLowerInvariant().Contains(queryLower))
                                {
                                    matches.Add(new
                                    {
                                        library = libName,
                                        typeName,
                                        kind = elem.Name.LocalName,
                                        name = mName,
                                        description = TruncateComment(comment.Trim())
                                    });
                                }
                            }
                        }
                    }
                    catch { /* skip malformed XMLs */ }
                }
            }

            return matches.Take(100).ToList();
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