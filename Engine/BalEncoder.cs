using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace B4XContext.Engine
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
        private const byte CACHED_STRING = 9;
        private const byte RECT32 = 11;
        private const byte CNULL = 12;

        public static byte[] Encode(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return Encode(doc.RootElement);
        }

        public static byte[] Encode(JsonElement root)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, true);

            int version = root.GetProperty("LayoutHeader").GetProperty("Version").GetInt32();
            var header = root.GetProperty("LayoutHeader");
            var variants = root.GetProperty("Variants");
            var data = root.GetProperty("Data");

            WriteLayoutHeader(bw, header, variants);

            WriteAllLayout(bw, variants, data);

            bool fontAwesome = root.TryGetProperty("FontAwesome", out var fa) && fa.GetBoolean();
            bool materialIcons = root.TryGetProperty("MaterialIcons", out var mi) && mi.GetBoolean();

            bw.Write(fontAwesome ? (byte)1 : (byte)0);
            bw.Write(materialIcons ? (byte)1 : (byte)0);

            return ms.ToArray();
        }

        private static void WriteLayoutHeader(BinaryWriter bw, JsonElement header, JsonElement variants)
        {
            int version = header.GetProperty("Version").GetInt32();
            bw.Write(version);

            long stubPos = bw.BaseStream.Position;
            bw.Write(0); // placeholder for header size

            if (version >= 4)
            {
                int grid = header.TryGetProperty("GridSize", out var gs) ? gs.GetInt32() : 10;
                bw.Write(grid);
            }

            var cache = new Dictionary<string, int>();

            var controls = header.GetProperty("ControlsHeaders");
            using var temp = new MemoryStream();
            using var tw = new BinaryWriter(temp, Encoding.UTF8, true);

            tw.Write(controls.GetArrayLength());

            foreach (var control in controls.EnumerateArray())
            {
                WriteCachedString(tw, cache, control.GetProperty("Name").GetString() ?? "");
                WriteCachedString(tw, cache, control.GetProperty("JavaType").GetString() ?? "");
                WriteCachedString(tw, cache, control.GetProperty("DesignerType").GetString() ?? "");
            }

            WriteStringsCache(bw, cache);
            temp.Position = 0;
            temp.CopyTo(bw.BaseStream);

            var files = header.GetProperty("Files");
            bw.Write(files.GetArrayLength());
            foreach (var file in files.EnumerateArray())
            {
                WriteString(bw, file.GetString() ?? "");
            }

            byte[] script = WriteScripts(header.GetProperty("DesignerScript"), variants);
            bw.Write(script.Length);
            bw.Write(script);

            long endPos = bw.BaseStream.Position;
            bw.BaseStream.Position = stubPos;
            bw.Write((int)(endPos - stubPos - 4));
            bw.BaseStream.Position = endPos;
        }

        private static void WriteAllLayout(BinaryWriter bw, JsonElement variants, JsonElement data)
        {
            var cache = new Dictionary<string, int>();
            using var temp = new MemoryStream();
            using var tw = new BinaryWriter(temp, Encoding.UTF8, true);

            tw.Write(variants.GetArrayLength());
            foreach (var variant in variants.EnumerateArray())
            {
                tw.Write(variant.GetProperty("Scale").GetSingle());
                tw.Write(variant.GetProperty("Width").GetInt32());
                tw.Write(variant.GetProperty("Height").GetInt32());
            }

            WriteMap(tw, data, cache);

            WriteStringsCache(bw, cache);
            temp.Position = 0;
            temp.CopyTo(bw.BaseStream);

            bw.Write(0); // trailing 0
        }

        private static void WriteMap(BinaryWriter bw, JsonElement map, Dictionary<string, int> cache)
        {
            foreach (var property in map.EnumerateObject())
            {
                WriteCachedString(bw, cache, property.Name);
                WriteValue(bw, property.Value, cache);
            }
        }

        private static void WriteValue(BinaryWriter bw, JsonElement value, Dictionary<string, int> cache)
        {
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("ValueType", out var typeElement))
            {
                byte type = typeElement.GetByte();
                bw.Write(type);

                switch (type)
                {
                    case CSTRING:
                        WriteString(bw, value.GetProperty("Value").GetString() ?? "");
                        break;
                    case CFLOAT:
                        bw.Write(value.GetProperty("Value").GetSingle());
                        break;
                    case CCOLOR:
                        {
                            string hex = value.GetProperty("Value").GetString() ?? "";
                            if (hex.StartsWith("0x")) hex = hex.Substring(2);
                            byte[] bytes = new ByteConverter().HexToBytes(hex);
                            bw.Write(bytes);
                            break;
                        }
                    case RECT32:
                        {
                            var arr = value.GetProperty("Value");
                            for (int i = 0; i < 4; i++)
                            {
                                bw.Write((short)arr[i].GetInt32());
                            }
                            break;
                        }
                    case CNULL:
                        break;
                }
                return;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    bw.Write(CMAP);
                    WriteMap(bw, value, cache);
                    WriteString(bw, "");
                    bw.Write(ENDOFMAP);
                    break;
                case JsonValueKind.Number:
                    if (value.TryGetInt32(out int iv))
                    {
                        bw.Write(CINT);
                        bw.Write(iv);
                    }
                    else
                    {
                        bw.Write(CFLOAT);
                        bw.Write(value.GetSingle());
                    }
                    break;
                case JsonValueKind.String:
                    bw.Write(CACHED_STRING);
                    WriteCachedString(bw, cache, value.GetString() ?? "");
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    bw.Write(BOOL);
                    bw.Write(value.GetBoolean() ? (byte)1 : (byte)0);
                    break;
                case JsonValueKind.Null:
                    bw.Write(CNULL);
                    break;
            }
        }

        private static byte[] WriteScripts(JsonElement scripts, JsonElement variants)
        {
            using var raw = new MemoryStream();
            using var bw = new BinaryWriter(raw, Encoding.UTF8, true);

            string generalScript = "";
            int scriptCount = 0;

            if (scripts.ValueKind == JsonValueKind.Array && scripts.GetArrayLength() > 0)
            {
                generalScript = scripts[0].GetString() ?? "";
                scriptCount = scripts.GetArrayLength();
            }

            WriteBinaryString(bw, generalScript);
            bw.Write(variants.GetArrayLength());

            for (int i = 0; i < variants.GetArrayLength(); i++)
            {
                var v = variants[i];
                bw.Write(v.GetProperty("Scale").GetSingle());
                bw.Write(v.GetProperty("Width").GetInt32());
                bw.Write(v.GetProperty("Height").GetInt32());

                string variantScript = "";
                if (scripts.ValueKind == JsonValueKind.Array && scripts.GetArrayLength() > i + 1)
                {
                    variantScript = scripts[i + 1].GetString() ?? "";
                }
                WriteBinaryString(bw, variantScript);
            }

            bw.Flush();
            byte[] data = raw.ToArray();

            using var compressed = new MemoryStream();
            using (var gz = new GZipStream(compressed, CompressionLevel.Fastest, true))
            {
                gz.Write(data, 0, data.Length);
            }
            return compressed.ToArray();
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

            if (bytes.Length > 0)
                bw.Write(bytes);
        }

        private static void WriteStringsCache(BinaryWriter bw, Dictionary<string, int> cache)
        {
            bw.Write(cache.Count);
            foreach (var item in cache)
            {
                WriteString(bw, item.Key);
            }
        }

        private static void WriteCachedString(BinaryWriter bw, Dictionary<string, int> cache, string value)
        {
            if (value == null) value = "";
            if (cache.TryGetValue(value, out int index))
            {
                bw.Write(index);
            }
            else
            {
                index = cache.Count;
                cache[value] = index;
                bw.Write(index);
            }
        }

        private static void WriteString(BinaryWriter bw, string value)
        {
            if (value == null) value = "";
            byte[] data = Encoding.UTF8.GetBytes(value);
            bw.Write(data.Length);
            if (data.Length > 0) bw.Write(data);
        }
    }
}