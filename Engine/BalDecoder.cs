using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace B4XMcpServer.Engine
{
    public static class BalDecoder
    {
        public const byte CINT = 1;
        public const byte CSTRING = 2;
        public const byte CMAP = 3;
        public const byte ENDOFMAP = 4;
        public const byte BOOL = 5;
        public const byte CCOLOR = 6;
        public const byte CFLOAT = 7;
        public const byte ERREF = 8;
        public const byte CACHED_STRING = 9;
        public const byte CDOUBLE = 10;
        public const byte RECT32 = 11;
        public const byte CNULL = 12;

        public static string Decode(byte[] data)
        {
            var result = DecodeToObject(data);
            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        public static Dictionary<string, object> DecodeToObject(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms, Encoding.UTF8);

            int version = br.ReadInt32();
            if (version != 5)
                throw new Exception($"Unsupported BAL version {version} (expected 5)");

            int headerBlockLength = br.ReadInt32();
            long headerEndPos = br.BaseStream.Position + headerBlockLength;

            int gridSize = br.ReadInt32();

            var outerStringTable = LoadStringsCache(br);

            int manifestCount = br.ReadInt32();
            var manifest = new List<Dictionary<string, object>>();
            for (int i = 0; i < manifestCount; i++)
            {
                manifest.Add(new Dictionary<string, object>
                {
                    ["name"] = ReadCachedString(br, outerStringTable),
                    ["javaType"] = ReadCachedString(br, outerStringTable),
                    ["csType"] = ReadCachedString(br, outerStringTable)
                });
            }

            int fileCount = br.ReadInt32();
            var fileReferences = new List<string>();
            for (int i = 0; i < fileCount; i++)
                fileReferences.Add(ReadString(br));

            var scriptData = ReadScriptData(br);

            br.BaseStream.Position = headerEndPos;

            var innerStringTable = LoadStringsCache(br);

            int variantCount = br.ReadInt32();
            var variants = new List<Dictionary<string, object>>();
            for (int i = 0; i < variantCount; i++)
                variants.Add(ReadVariant(br));

            var layoutData = ReadMap(br, innerStringTable);

            int embeddedCount = br.ReadInt32();
            for (int i = 0; i < embeddedCount; i++)
                ReadString(br);

            bool flagC = false, flagD = false;
            if (br.BaseStream.Position < br.BaseStream.Length)
            {
                flagC = br.ReadByte() == 1;
                if (br.BaseStream.Position < br.BaseStream.Length)
                    flagD = br.ReadByte() == 1;
            }

            var rootControl = BuildControlNode(layoutData);

            return new Dictionary<string, object>
            {
                ["version"] = version,
                ["gridSize"] = gridSize,
                ["variants"] = variants,
                ["manifest"] = manifest,
                ["fileReferences"] = fileReferences,
                ["scriptData"] = scriptData ?? new Dictionary<string, object> { ["mainScript"] = "", ["variantScripts"] = new List<object>() },
                ["flags"] = new Dictionary<string, object> { ["c"] = flagC, ["d"] = flagD },
                ["rootControl"] = rootControl
            };
        }

        private static Dictionary<string, object> BuildControlNode(Dictionary<string, object> dict)
        {
            var properties = new Dictionary<string, object>();
            var children = new List<object>();

            foreach (var kv in dict)
            {
                if (kv.Key == ":kids" && kv.Value is Dictionary<string, object> kidsDict)
                {
                    var indices = new List<int>();
                    foreach (var ck in kidsDict.Keys)
                        if (int.TryParse(ck, out int idx))
                            indices.Add(idx);
                    indices.Sort();

                    foreach (var idx in indices)
                    {
                        if (kidsDict[idx.ToString()] is Dictionary<string, object> childDict)
                            children.Add(BuildControlNode(childDict));
                    }
                }
                else
                {
                    properties[kv.Key] = kv.Value;
                }
            }

            var result = new Dictionary<string, object>
            {
                ["properties"] = properties,
                ["children"] = children
            };
            return result;
        }

        private static object? ReadScriptData(BinaryReader br)
        {
            int compressedLength = br.ReadInt32();
            if (compressedLength <= 0) return null;

            byte[] compressed = br.ReadBytes(compressedLength);
            byte[] decompressed;
            try
            {
                using var ms = new MemoryStream(compressed);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                gz.CopyTo(outMs);
                decompressed = outMs.ToArray();
            }
            catch { return null; }

            using var scriptMs = new MemoryStream(decompressed);
            using var scriptBr = new BinaryReader(scriptMs, Encoding.UTF8);

            string mainScript = ReadBinaryString(scriptBr);
            int variantCount = scriptBr.ReadInt32();
            var variantScripts = new List<object>();

            for (int i = 0; i < variantCount; i++)
            {
                var v = ReadVariant(scriptBr);
                string script = ReadBinaryString(scriptBr);
                variantScripts.Add(new Dictionary<string, object> { ["variant"] = v, ["script"] = script });
            }

            return new Dictionary<string, object>
            {
                ["mainScript"] = mainScript,
                ["variantScripts"] = variantScripts
            };
        }

        private static Dictionary<string, object> ReadVariant(BinaryReader br)
        {
            return new Dictionary<string, object>
            {
                ["scale"] = br.ReadSingle(),
                ["width"] = br.ReadInt32(),
                ["height"] = br.ReadInt32()
            };
        }

        private static List<string> LoadStringsCache(BinaryReader br)
        {
            int count = br.ReadInt32();
            var cache = new List<string>(count);
            for (int i = 0; i < count; i++)
                cache.Add(ReadString(br));
            return cache;
        }

        private static string ReadCachedString(BinaryReader br, List<string> cache)
        {
            if (cache.Count == 0)
                return ReadString(br);
            int index = br.ReadInt32();
            if (index < 0 || index >= cache.Count)
                return $"[Cache {index}]";
            return cache[index];
        }

        private static string ReadString(BinaryReader br)
        {
            int length = br.ReadInt32();
            if (length <= 0) return string.Empty;
            byte[] data = br.ReadBytes(length);
            return Encoding.UTF8.GetString(data);
        }

        private static string ReadBinaryString(BinaryReader br)
        {
            int length = 0, shift = 0;
            byte b;
            do
            {
                b = br.ReadByte();
                length |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            if (length <= 0) return string.Empty;
            byte[] data = br.ReadBytes(length);
            return Encoding.UTF8.GetString(data);
        }

        private static Dictionary<string, object> ReadMap(BinaryReader br, List<string> cache)
        {
            var props = new Dictionary<string, object>();

            while (true)
            {
                string key = ReadCachedString(br, cache);
                byte type = br.ReadByte();

                if (type == ENDOFMAP)
                    break;

                object? value = type switch
                {
                    CINT => br.ReadInt32(),
                    CACHED_STRING => ReadCachedString(br, cache),
                    CSTRING => new Dictionary<string, object> { ["tag"] = "String", ["value"] = ReadString(br) },
                    CFLOAT => new Dictionary<string, object> { ["tag"] = "Float", ["value"] = br.ReadSingle() },
                    CDOUBLE => new Dictionary<string, object> { ["tag"] = "Double", ["value"] = br.ReadDouble() },
                    CMAP => ReadMap(br, cache),
                    BOOL => br.ReadByte() == 1,
                    CCOLOR => new Dictionary<string, object>
                    {
                        ["tag"] = "Color",
                        ["value"] = $"#{br.ReadByte():X2}{br.ReadByte():X2}{br.ReadByte():X2}{br.ReadByte():X2}"
                    },
                    ERREF => br.ReadInt32(),
                    RECT32 => new Dictionary<string, object>
                    {
                        ["tag"] = "Int32Rect",
                        ["x"] = (int)br.ReadInt16(),
                        ["y"] = (int)br.ReadInt16(),
                        ["width"] = (int)br.ReadInt16(),
                        ["height"] = (int)br.ReadInt16()
                    },
                    CNULL => null,
                    _ => throw new Exception($"Unknown BAL value type {type}")
                };

                props[key] = value!;
            }

            return props;
        }
    }
}