using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace B4XMcpServer.Engine
{
    public static class BalEncoder
    {
        private const byte CINT = 1;
        private const byte CSTRING = 2;
        private const byte CMAP = 3;
        private const byte ENDOFMAP = 4;
        private const byte BOOL = 5;
        private const byte CCOLOR = 6;
        private const byte CFLOAT = 7;
        private const byte ERREF = 8;
        private const byte CACHED_STRING = 9;
        private const byte CDOUBLE = 10;
        private const byte RECT32 = 11;
        private const byte CNULL = 12;

        public static byte[] Encode(string json)
        {
            var root = JsonNode.Parse(json)?.AsObject();
            if (root == null)
                throw new FormatException("Invalid JSON: root must be an object.");
            return Encode(root);
        }

        public static byte[] Encode(JsonObject root)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, true);

            int version = root["version"]?.GetValue<int>() ?? 5;
            bw.Write(version);

            long lengthPos = bw.BaseStream.Position;
            bw.Write(0); // placeholder

            int gridSize = root["gridSize"]?.GetValue<int>() ?? 10;
            bw.Write(gridSize);

            var outerTable = new Dictionary<string, int>();

            // Manifest
            var manifest = root["manifest"] as JsonArray ?? new JsonArray();
            using var manifestStream = new MemoryStream();
            using var mw = new BinaryWriter(manifestStream, Encoding.UTF8, true);
            mw.Write(manifest.Count);
            foreach (var entry in manifest)
            {
                WriteCachedString(mw, outerTable, entry?["name"]?.GetValue<string>() ?? "");
                WriteCachedString(mw, outerTable, entry?["javaType"]?.GetValue<string>() ?? "");
                WriteCachedString(mw, outerTable, entry?["csType"]?.GetValue<string>() ?? "");
            }

            WriteStringsCache(bw, outerTable);
            manifestStream.Position = 0;
            manifestStream.CopyTo(bw.BaseStream);

            // File references
            var fileRefs = root["fileReferences"] as JsonArray ?? new JsonArray();
            bw.Write(fileRefs.Count);
            foreach (var f in fileRefs)
                WriteString(bw, f?.GetValue<string>() ?? "");

            // Script data
            WriteScriptData(bw, root["scriptData"] as JsonObject, root["variants"] as JsonArray);

            long headerEnd = bw.BaseStream.Position;
            bw.BaseStream.Position = lengthPos;
            bw.Write((int)(headerEnd - lengthPos - 4));
            bw.BaseStream.Position = headerEnd;

            // ── INNER DATA BLOCK (two-pass) ──────────────────────────

            // Pass 1: Collect all strings from the control tree
            var allStrings = new HashSet<string>();
            if (root["rootControl"] is JsonObject rootControl)
                CollectStrings(rootControl, allStrings);

            // Sort alphabetically (Ordinal for byte-identical sort)
            var sortedStrings = new List<string>(allStrings);
            sortedStrings.Sort(StringComparer.Ordinal);

            // Build lookup: string → sorted index
            var stringToIndex = new Dictionary<string, int>();
            for (int i = 0; i < sortedStrings.Count; i++)
                stringToIndex[sortedStrings[i]] = i;

            // Pass 2: Write inner data with correct indices
            using var innerStream = new MemoryStream();
            using var iw = new BinaryWriter(innerStream, Encoding.UTF8, true);

            var variants = root["variants"] as JsonArray ?? new JsonArray();
            iw.Write(variants.Count);
            foreach (var v in variants)
            {
                iw.Write(v?["scale"]?.GetValue<float>() ?? 1f);
                iw.Write(v?["width"]?.GetValue<int>() ?? 320);
                iw.Write(v?["height"]?.GetValue<int>() ?? 480);
            }

            if (root["rootControl"] is JsonObject rc)
                WriteControlTree(iw, stringToIndex, rc);

            WriteEndMarker(iw);
            iw.Write(0); // embedded count

            // Write the sorted string table
            bw.Write(sortedStrings.Count);
            foreach (var s in sortedStrings)
                WriteString(bw, s);

            innerStream.Position = 0;
            innerStream.CopyTo(bw.BaseStream);

            // Trailing flags
            var flags = root["flags"] as JsonObject;
            bw.Write(flags?["c"]?.GetValue<bool>() == true ? (byte)1 : (byte)0);
            bw.Write(flags?["d"]?.GetValue<bool>() == true ? (byte)1 : (byte)0);

            return ms.ToArray();
        }

        // ── Pass 1: Collect all strings ──────────────────────────────

        private static void CollectStrings(JsonObject node, HashSet<string> strings)
        {
            var properties = node["properties"] as JsonObject;
            if (properties != null)
            {
                foreach (var prop in properties)
                {
                    // Key is always a string
                    strings.Add(prop.Key);
                    CollectStringsFromValue(prop.Value, strings);
                }
            }

            var children = node["children"] as JsonArray;
            if (children != null && children.Count > 0)
            {
                strings.Add(":kids");
                for (int i = 0; i < children.Count; i++)
                {
                    strings.Add(i.ToString());
                    if (children[i] is JsonObject child)
                        CollectStrings(child, strings);
                }
            }
        }

        private static void CollectStringsFromValue(JsonNode? value, HashSet<string> strings)
        {
            if (value is JsonObject obj && obj["tag"] != null)
            {
                string tag = obj["tag"]?.GetValue<string>() ?? "";

                if (tag == "StringRef")
                {
                    strings.Add(obj["value"]?.GetValue<string>() ?? "");
                }
                else if (tag == "String")
                {
                    // Inline strings are NOT in the table
                }
                else if (tag == "Object")
                {
                    var nested = obj["value"] as JsonObject;
                    if (nested != null)
                    {
                        foreach (var p in nested)
                        {
                            strings.Add(p.Key);
                            CollectStringsFromValue(p.Value, strings);
                        }
                    }
                }
            }
            else if (value is JsonObject nestedObj && nestedObj["tag"] == null)
            {
                // Untagged object (like drawable) — collect its keys
                foreach (var p in nestedObj)
                {
                    strings.Add(p.Key);
                    CollectStringsFromValue(p.Value, strings);
                }
            }
        }

        // ── Pass 2: Write control tree ───────────────────────────────

        private static void WriteControlTree(BinaryWriter bw, Dictionary<string, int> table, JsonObject node)
        {
            var properties = node["properties"] as JsonObject;
            if (properties != null)
            {
                foreach (var prop in properties)
                    WriteKeyValue(bw, table, prop.Key, prop.Value);
            }

            var children = node["children"] as JsonArray;
            if (children != null && children.Count > 0)
            {
                WriteObjectStart(bw, table, ":kids");
                for (int i = 0; i < children.Count; i++)
                {
                    if (children[i] is JsonObject child)
                    {
                        WriteObjectStart(bw, table, i.ToString());
                        WriteControlTree(bw, table, child);
                        WriteEndMarker(bw);
                    }
                }
                WriteEndMarker(bw);
            }
        }

        private static void WriteKeyValue(BinaryWriter bw, Dictionary<string, int> table, string key, JsonNode? value)
        {
            WriteStringRef(bw, table, key);
            WriteTaggedValue(bw, table, value);
        }

        private static void WriteTaggedValue(BinaryWriter bw, Dictionary<string, int> table, JsonNode? value)
        {
            if (value is JsonObject obj && obj["tag"] != null)
            {
                string tag = obj["tag"]?.GetValue<string>() ?? "";

                switch (tag)
                {
                    case "Int32":
                        bw.Write(CINT);
                        bw.Write(obj["value"]?.GetValue<int>() ?? 0);
                        return;
                    case "String":
                        bw.Write(CSTRING);
                        WriteString(bw, obj["value"]?.GetValue<string>() ?? "");
                        return;
                    case "StringRef":
                        bw.Write(CACHED_STRING);
                        WriteStringRef(bw, table, obj["value"]?.GetValue<string>() ?? "");
                        return;
                    case "Float":
                        bw.Write(CFLOAT);
                        bw.Write(obj["value"]?.GetValue<float>() ?? 0f);
                        return;
                    case "Double":
                        bw.Write(CDOUBLE);
                        bw.Write(obj["value"]?.GetValue<double>() ?? 0.0);
                        return;
                    case "Bool":
                        bw.Write(BOOL);
                        bw.Write(obj["value"]?.GetValue<bool>() == true ? (byte)1 : (byte)0);
                        return;
                    case "Color":
                        bw.Write(CCOLOR);
                        bw.Write((byte)(obj["a"]?.GetValue<int>() ?? 255));
                        bw.Write((byte)(obj["r"]?.GetValue<int>() ?? 0));
                        bw.Write((byte)(obj["g"]?.GetValue<int>() ?? 0));
                        bw.Write((byte)(obj["b"]?.GetValue<int>() ?? 0));
                        return;
                    case "Int32Rect":
                        bw.Write(RECT32);
                        bw.Write((short)(obj["x"]?.GetValue<int>() ?? 0));
                        bw.Write((short)(obj["y"]?.GetValue<int>() ?? 0));
                        bw.Write((short)(obj["width"]?.GetValue<int>() ?? 100));
                        bw.Write((short)(obj["height"]?.GetValue<int>() ?? 50));
                        return;
                    case "Object":
                        bw.Write(CMAP);
                        var nestedProps = obj["value"] as JsonObject;
                        if (nestedProps != null)
                        {
                            foreach (var p in nestedProps)
                                WriteKeyValue(bw, table, p.Key, p.Value);
                        }
                        WriteEndMarker(bw);
                        return;
                    case "ErRef":
                        bw.Write(ERREF);
                        bw.Write(obj["value"]?.GetValue<int>() ?? 0);
                        return;
                    case "Null":
                        bw.Write(CNULL);
                        return;
                }
            }

            if (value is JsonObject nestedObj && nestedObj["tag"] == null)
            {
                bw.Write(CMAP);
                foreach (var p in nestedObj)
                    WriteKeyValue(bw, table, p.Key, p.Value);
                WriteEndMarker(bw);
                return;
            }

            switch (value?.GetValueKind())
            {
                case JsonValueKind.Number:
                    if (value!.AsValue().TryGetValue<int>(out int intVal))
                    {
                        bw.Write(CINT);
                        bw.Write(intVal);
                    }
                    else if (value.AsValue().TryGetValue<float>(out float floatVal))
                    {
                        bw.Write(CFLOAT);
                        bw.Write(floatVal);
                    }
                    else if (value.AsValue().TryGetValue<double>(out double doubleVal))
                    {
                        bw.Write(CDOUBLE);
                        bw.Write(doubleVal);
                    }
                    break;
                case JsonValueKind.String:
                    bw.Write(CACHED_STRING);
                    WriteStringRef(bw, table, value.GetValue<string>() ?? "");
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    bw.Write(BOOL);
                    bw.Write(value.GetValue<bool>() ? (byte)1 : (byte)0);
                    break;
                case JsonValueKind.Null:
                    bw.Write(CNULL);
                    break;
                case JsonValueKind.Object:
                    bw.Write(CMAP);
                    foreach (var p in value.AsObject())
                        WriteKeyValue(bw, table, p.Key, p.Value);
                    WriteEndMarker(bw);
                    break;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static void WriteObjectStart(BinaryWriter bw, Dictionary<string, int> table, string key)
        {
            WriteStringRef(bw, table, key);
            bw.Write(CMAP);
        }

        private static void WriteEndMarker(BinaryWriter bw)
        {
            bw.Write(0);
            bw.Write(ENDOFMAP);
        }

        private static void WriteStringRef(BinaryWriter bw, Dictionary<string, int> table, string value)
        {
            if (value == null) value = "";
            bw.Write(table.TryGetValue(value, out int idx) ? idx : 0);
        }

        private static void WriteScriptData(BinaryWriter bw, JsonObject? scriptData, JsonArray? variants)
        {
            if (scriptData == null)
            {
                bw.Write(0);
                return;
            }

            using var raw = new MemoryStream();
            using var rw = new BinaryWriter(raw, Encoding.UTF8, true);

            string mainScript = scriptData["mainScript"]?.GetValue<string>() ?? "";
            WriteBinaryString(rw, mainScript);

            var variantScripts = scriptData["variantScripts"] as JsonArray ?? new JsonArray();
            rw.Write(variantScripts.Count);

            foreach (var vs in variantScripts)
            {
                var v = vs?["variant"] as JsonObject;
                if (v != null)
                {
                    rw.Write(v["scale"]?.GetValue<float>() ?? 1f);
                    rw.Write(v["width"]?.GetValue<int>() ?? 320);
                    rw.Write(v["height"]?.GetValue<int>() ?? 480);
                }
                string script = vs?["script"]?.GetValue<string>() ?? "";
                WriteBinaryString(rw, script);
            }

            rw.Flush();
            byte[] data = raw.ToArray();

            using var compressed = new MemoryStream();
            using (var gz = new GZipStream(compressed, CompressionLevel.Optimal, true))
            {
                gz.Write(data, 0, data.Length);
            }
            byte[] compressedData = compressed.ToArray();

            bw.Write(compressedData.Length);
            bw.Write(compressedData);
        }

        private static void WriteBinaryString(BinaryWriter bw, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            int length = bytes.Length;
            while (length >= 0x80)
            {
                bw.Write((byte)((length & 0x7F) | 0x80));
                length >>= 7;
            }
            bw.Write((byte)length);
            if (bytes.Length > 0) bw.Write(bytes);
        }

        private static void WriteStringsCache(BinaryWriter bw, Dictionary<string, int> cache)
        {
            bw.Write(cache.Count);
            foreach (var item in cache)
                WriteString(bw, item.Key);
        }

        private static void WriteCachedString(BinaryWriter bw, Dictionary<string, int> cache, string value)
        {
            if (value == null) value = "";
            if (!cache.TryGetValue(value, out int index))
            {
                index = cache.Count;
                cache[value] = index;
            }
            bw.Write(index);
        }

        private static void WriteString(BinaryWriter bw, string? value)
        {
            // value is null-tolerant: callers may pass null when a property lookup
            // yielded no string (e.g. removable tag, optional name). Treating it as "" here
            // avoids noisy casts at every call site.
            if (value == null) value = "";
            byte[] data = Encoding.UTF8.GetBytes(value);
            bw.Write(data.Length);
            if (data.Length > 0) bw.Write(data);
        }
    }
}