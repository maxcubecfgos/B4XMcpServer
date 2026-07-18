using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace B4XEngineCore
{
    public static class LayoutParser
    {
        public static LayoutFile ParseLayoutFile(byte[] data)
        {
            var reader = new BinaryReader(data);

            int version = reader.ReadInt32();
            if (version != 5)
                throw new ParseError($"Unsupported layout version {version} (expected 5)", 0);

            int headerBlockLength = reader.ReadInt32();
            int headerEndPos = reader.Position + headerBlockLength;
            int gridSize = reader.ReadInt32();

            var outerStringTable = ReadStringTable(reader);

            int manifestCount = reader.ReadInt32();
            var manifest = new List<ManifestEntry>();
            for (int i = 0; i < manifestCount; i++)
            {
                manifest.Add(new ManifestEntry(
                    reader.ReadStringRef(outerStringTable),
                    reader.ReadStringRef(outerStringTable),
                    reader.ReadStringRef(outerStringTable)
                ));
            }

            int fileCount = reader.ReadInt32();
            var fileReferences = new List<string>();
            for (int i = 0; i < fileCount; i++)
                fileReferences.Add(reader.ReadLengthPrefixedString());

            var scriptData = ReadScriptData(reader);
            reader.Position = headerEndPos;

            var innerStringTable = ReadStringTable(reader);

            int variantCount = reader.ReadInt32();
            var variants = new List<Variant>();
            for (int i = 0; i < variantCount; i++)
                variants.Add(new Variant(reader.ReadFloat(), reader.ReadInt32(), reader.ReadInt32()));

            var rootDict = ReadObjectTree(reader, innerStringTable);

            int embeddedCount = reader.ReadInt32();
            for (int i = 0; i < embeddedCount; i++)
                reader.ReadLengthPrefixedString();

            bool flagC = reader.ReadByte() != 0;
            bool flagD = reader.ReadByte() != 0;

            var rootControl = BuildControlNode(rootDict);

            return new LayoutFile
            {
                Version = version,
                GridSize = gridSize,
                Variants = variants,
                RootControl = rootControl,
                Manifest = manifest,
                FileReferences = fileReferences,
                ScriptData = scriptData,
                Flags = (flagC, flagD),
            };
        }

        private static string[] ReadStringTable(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var table = new string[count];
            for (int i = 0; i < count; i++)
                table[i] = reader.ReadLengthPrefixedString();
            return table;
        }

        private static Dictionary<string, PropertyValue> ReadObjectTree(BinaryReader reader, string[] stringTable)
        {
            var dict = new Dictionary<string, PropertyValue>();

            while (true)
            {
                int keyIndex = reader.ReadInt32();
                byte tag = reader.ReadByte();

                if (tag == (byte)TypeTag.End)
                    return dict;

                if (keyIndex < 0 || keyIndex >= stringTable.Length)
                    throw new ParseError($"String table index {keyIndex} out of range", reader.Position - 5);

                string key = stringTable[keyIndex];
                var value = ReadTaggedValue(reader, tag, stringTable);
                dict[key] = value;
            }
        }

        private static PropertyValue ReadTaggedValue(BinaryReader reader, byte tag, string[] stringTable)
        {
            switch ((TypeTag)tag)
            {
                case TypeTag.Int32:
                    return new IntValue(reader.ReadInt32());
                case TypeTag.String:
                    return new StringValue(reader.ReadLengthPrefixedString());
                case TypeTag.Object:
                    return new ObjectValue(ReadObjectTree(reader, stringTable));
                case TypeTag.Bool:
                    return new BoolValue(reader.ReadByte() == 1);
                case TypeTag.Color:
                    byte a = reader.ReadByte(), r = reader.ReadByte(), g = reader.ReadByte(), b = reader.ReadByte();
                    return new ColorValue(a, r, g, b);
                case TypeTag.Float:
                    return new FloatValue(reader.ReadFloat());
                case TypeTag.ErRef:
                    return new ErRefValue(reader.ReadInt32());
                case TypeTag.StringRef:
                    return new StringRefValue(reader.ReadStringRef(stringTable));
                case TypeTag.Double:
                    return new DoubleValue(reader.ReadDouble());
                case TypeTag.Int32Rect:
                    short x = reader.ReadInt16(), y = reader.ReadInt16();
                    short w = reader.ReadInt16(), h = reader.ReadInt16();
                    return new RectValue(x, y, w, h);
                case TypeTag.Null:
                    return new NullValue();
                default:
                    throw new ParseError($"Unknown type tag {tag}", reader.Position - 1);
            }
        }

        private static ControlNode BuildControlNode(Dictionary<string, PropertyValue> dict)
        {
            var children = new List<ControlNode>();
            var properties = new Dictionary<string, PropertyValue>();

            foreach (var kvp in dict)
            {
                if (kvp.Key == ":kids" && kvp.Value is ObjectValue ov)
                {
                    var indices = ov.Value.Keys
                        .Select(k => int.TryParse(k, out int idx) ? idx : -1)
                        .Where(idx => idx >= 0)
                        .OrderBy(idx => idx)
                        .ToList();

                    foreach (int idx in indices)
                    {
                        if (ov.Value[idx.ToString()] is ObjectValue childOv)
                            children.Add(BuildControlNode(childOv.Value));
                    }
                }
                else
                {
                    properties[kvp.Key] = kvp.Value;
                }
            }

            return new ControlNode { Properties = properties, Children = children };
        }

        private static ScriptData? ReadScriptData(BinaryReader reader)
        {
            int compressedLength = reader.ReadInt32();
            if (compressedLength <= 0) return null;

            var compressedBytes = reader.ReadBytes(compressedLength);

            byte[] decompressed;
            try
            {
                using var compressedStream = new MemoryStream(compressedBytes);
                using var gzip = new GZipStream(compressedStream, CompressionMode.Decompress);
                using var resultStream = new MemoryStream();
                gzip.CopyTo(resultStream);
                decompressed = resultStream.ToArray();
            }
            catch
            {
                return null;
            }

            var sr = new BinaryReader(decompressed);
            string mainScript = sr.Read7BitEncodedString();
            int variantCount = sr.ReadInt32();
            var variantScripts = new List<VariantScript>();
            for (int i = 0; i < variantCount; i++)
            {
                var variant = new Variant(sr.ReadFloat(), sr.ReadInt32(), sr.ReadInt32());
                string script = sr.Read7BitEncodedString();
                variantScripts.Add(new VariantScript(variant, script));
            }

            return new ScriptData
            {
                MainScript = mainScript,
                VariantScripts = variantScripts,
                RawCompressedBytes = compressedBytes,
            };
        }
    }
}
