using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace B4XEngineCore
{
    public class DesignerProperty
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string FieldType { get; set; } = "string";
        public string DefaultValue { get; set; } = "";
        public string? Description { get; set; }
        public double? MinRange { get; set; }
        public double? MaxRange { get; set; }
        public List<string>? List { get; set; }
    }

    public class CustomViewDef
    {
        public string ShortName { get; set; } = "";
        public string JavaType { get; set; } = "";
        public List<string> Events { get; set; } = new();
        public List<DesignerProperty> DesignerProperties { get; set; } = new();
        public string SourceFile { get; set; } = "";
    }

    public static class LibraryLoader
    {
        public static List<CustomViewDef> ParseLibraryXml(string xml, string filePath)
        {
            var results = new List<CustomViewDef>();
            var classRegex = new Regex(@"<class\b[^>]*>([\s\S]*?)<\/class>", RegexOptions.IgnoreCase);
            var classMatches = classRegex.Matches(xml);

            foreach (Match classMatch in classMatches)
            {
                string classBody = classMatch.Groups[1].Value;
                var designerProps = new List<DesignerProperty>();
                var propRegex = new Regex(@"<designerProperty>(.*?)<\/designerProperty>", RegexOptions.IgnoreCase);
                var propMatches = propRegex.Matches(classBody);
                foreach (Match propMatch in propMatches)
                {
                    var parsed = ParseDesignerPropertyString(propMatch.Groups[1].Value.Trim());
                    if (parsed != null) designerProps.Add(parsed);
                }
                if (designerProps.Count == 0) continue;

                var shortNameMatch = Regex.Match(classBody, @"<shortname>(.*?)<\/shortname>", RegexOptions.IgnoreCase);
                if (!shortNameMatch.Success) continue;
                string shortName = shortNameMatch.Groups[1].Value.Trim();

                var nameMatch = Regex.Match(classBody, @"<name>(.*?)<\/name>", RegexOptions.IgnoreCase);
                string javaType = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : "";

                var events = new List<string>();
                var eventRegex = new Regex(@"<event>(.*?)<\/event>", RegexOptions.IgnoreCase);
                var eventMatches = eventRegex.Matches(classBody);
                foreach (Match eventMatch in eventMatches)
                    events.Add(eventMatch.Groups[1].Value.Trim());

                results.Add(new CustomViewDef { ShortName = shortName, JavaType = javaType, Events = events, DesignerProperties = designerProps, SourceFile = filePath });
            }
            return results;
        }

        public static DesignerProperty? ParseDesignerPropertyString(string s)
        {
            var knownKeys = new[] { "Key", "DisplayName", "FieldType", "DefaultValue", "Description", "MinRange", "MaxRange", "List" };
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var splitPattern = new Regex(@",\s*(?=" + string.Join("|", knownKeys) + @")\s*", RegexOptions.IgnoreCase);
            var parts = splitPattern.Split(s);

            foreach (var part in parts)
            {
                int colonIdx = part.IndexOf(':');
                if (colonIdx < 0) continue;
                string key = part[..colonIdx].Trim();
                string value = part[(colonIdx + 1)..].Trim();
                attrs[key.ToLowerInvariant()] = value;
            }

            if (!attrs.ContainsKey("key") || !attrs.ContainsKey("displayname") || !attrs.ContainsKey("fieldtype") || !attrs.ContainsKey("defaultvalue"))
                return null;

            string ft = attrs["fieldtype"].ToLowerInvariant();
            if (ft is not ("string" or "int" or "float" or "boolean" or "color")) return null;

            var dp = new DesignerProperty
            {
                Key = attrs["key"],
                DisplayName = attrs["displayname"],
                FieldType = ft,
                DefaultValue = attrs["defaultvalue"],
            };

            if (attrs.TryGetValue("description", out var desc)) dp.Description = desc.Replace("\\n", "\n");
            if (attrs.TryGetValue("minrange", out var minR) && double.TryParse(minR, out var minV)) dp.MinRange = minV;
            if (attrs.TryGetValue("maxrange", out var maxR) && double.TryParse(maxR, out var maxV)) dp.MaxRange = maxV;
            if (attrs.TryGetValue("list", out var list)) dp.List = list.Split('|').Select(v => v.Trim()).ToList();

            return dp;
        }

        public static List<CustomViewDef> ParseBasSource(string source, string shortName, string sourceFile)
        {
            var lines = source.Split('\n');
            var designerProps = new List<DesignerProperty>();
            var events = new List<string>();
            bool hasDesignerCreateView = false;

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#DesignerProperty:"))
                {
                    var parsed = ParseDesignerPropertyString(trimmed["#DesignerProperty:".Length..].Trim());
                    if (parsed != null) designerProps.Add(parsed);
                }
                else if (trimmed.StartsWith("#Event:"))
                {
                    events.Add(trimmed["#Event:".Length..].Trim());
                }
                else if (Regex.IsMatch(trimmed, @"^\s*Public\s+Sub\s+DesignerCreateView\s*\(", RegexOptions.IgnoreCase))
                {
                    hasDesignerCreateView = true;
                }
            }

            if (!hasDesignerCreateView || designerProps.Count == 0) return new();
            return new() { new CustomViewDef { ShortName = shortName, JavaType = shortName.ToLowerInvariant(), Events = events, DesignerProperties = designerProps, SourceFile = sourceFile } };
        }
    }
}
