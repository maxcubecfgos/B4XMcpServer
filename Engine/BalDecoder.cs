using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace B4XContext.Engine
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
        public const byte CACHED_STRING = 9;
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

            var header = ReadLayoutHeader(br, version);

            var cache = LoadStringsCache(br);

            int numberOfVariants = br.ReadInt32();
            var variants = new List<Dictionary<string, object>>();
            for (int i = 0; i < numberOfVariants; i++)
            {
                variants.Add(ReadVariant(br));
            }

            var layoutData = ReadMap(br, cache);

            br.ReadInt32(); // trailing 0

            bool fontAwesome = false;
            bool materialIcons = false;
            if (version >= 5)
            {
                fontAwesome = br.ReadByte() == 1;
                materialIcons = br.ReadByte() == 1;
            }

            return new Dictionary<string, object>
            {
                ["LayoutHeader"] = header,
                ["Variants"] = variants,
                ["Data"] = layoutData,
                ["FontAwesome"] = fontAwesome,
                ["MaterialIcons"] = materialIcons
            };
        }

        private static Dictionary<string, object> ReadLayoutHeader(BinaryReader br, int version)
        {
            var header = new Dictionary<string, object> { ["Version"] = version };

            if (version < 3)
                throw new Exception("Unsupported BAL version < 3");

            br.ReadInt32(); // header size stub

            int gridSize = 10;
            if (version >= 4)
                gridSize = br.ReadInt32();
            header["GridSize"] = gridSize;

            var cache = LoadStringsCache(br);

            int numberOfControls = br.ReadInt32();
            var controls = new List<Dictionary<string, string>>();
            for (int i = 0; i < numberOfControls; i++)
            {
                controls.Add(new Dictionary<string, string>
                {
                    ["Name"] = ReadCachedString(br, cache),
                    ["JavaType"] = ReadCachedString(br, cache),
                    ["DesignerType"] = ReadCachedString(br, cache)
                });
            }
            header["ControlsHeaders"] = controls;

            int numberOfFiles = br.ReadInt32();
            var files = new List<string>();
            for (int i = 0; i < numberOfFiles; i++)
            {
                files.Add(ReadString(br));
            }
            header["Files"] = files;

            header["DesignerScript"] = ReadScripts(br);

            return header;
        }

        private static List<Dictionary<string, object>> ReadScripts(BinaryReader br)
        {
            int scriptLength = br.ReadInt32();
            byte[] rawData = br.ReadBytes(scriptLength);

            byte[] decompressed = DecompressGZip(rawData);

            using var scriptMs = new MemoryStream(decompressed);
            using var scriptBr = new BinaryReader(scriptMs, Encoding.UTF8);

            var res = new List<Dictionary<string, object>>();
            res.Add(new Dictionary<string, object> { ["Type"] = "General", ["Script"] = ReadBinaryString(scriptBr) });

            int numberOfVariants = scriptBr.ReadInt32();
            for (int i = 0; i < numberOfVariants; i++)
            {
                ReadVariant(scriptBr);
                res.Add(new Dictionary<string, object> { ["Type"] = "Variant", ["Script"] = ReadBinaryString(scriptBr) });
            }
            return res;
        }

        private static Dictionary<string, object> ReadVariant(BinaryReader br)
        {
            return new Dictionary<string, object>
            {
                ["Scale"] = br.ReadSingle(),
                ["Width"] = br.ReadInt32(),
                ["Height"] = br.ReadInt32()
            };
        }

        private static List<string> LoadStringsCache(BinaryReader br)
        {
            int count = br.ReadInt32();
            var cache = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                cache.Add(ReadString(br));
            }
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
            int length = 0;
            int shift = 0;
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

                object? value = null;

                switch (type)
                {
                    case CINT:
                        value = br.ReadInt32();
                        break;

                    case CACHED_STRING:
                        value = ReadCachedString(br, cache);
                        break;

                    case CSTRING:
                        value = new Dictionary<string, object>
                        {
                            ["ValueType"] = type,
                            ["Value"] = ReadString(br)
                        };
                        break;

                    case CFLOAT:
                        value = new Dictionary<string, object>
                        {
                            ["ValueType"] = type,
                            ["Value"] = br.ReadSingle()
                        };
                        break;

                    case CMAP:
                        value = ReadMap(br, cache);
                        break;

                    case BOOL:
                        value = br.ReadByte() == 1;
                        break;

                    case CCOLOR:
                        {
                            byte a = br.ReadByte();
                            byte r = br.ReadByte();
                            byte g = br.ReadByte();
                            byte b = br.ReadByte();
                            value = new Dictionary<string, object>
                            {
                                ["ValueType"] = type,
                                ["Value"] = $"0x{a:X2}{r:X2}{g:X2}{b:X2}"
                            };
                            break;
                        }

                    case RECT32:
                        {
                            var rect = new List<short>
                            {
                                br.ReadInt16(),
                                br.ReadInt16(),
                                br.ReadInt16(),
                                br.ReadInt16()
                            };
                            value = new Dictionary<string, object>
                            {
                                ["ValueType"] = type,
                                ["Value"] = rect
                            };
                            break;
                        }

                    case CNULL:
                        value = new Dictionary<string, object> { ["ValueType"] = type };
                        break;

                    default:
                        throw new Exception($"Unknown BAL value type {type}");
                }

                props[key] = value;
            }

            return props;
        }
        private static byte[] DecompressGZip(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var gzip = new GZipStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            gzip.CopyTo(outMs);
            return outMs.ToArray();
        }
    }

    public class ByteConverter
    {
        public bool LittleEndian { get; set; }

        public string HexFromBytes(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        public byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 != 0) hex = "0" + hex;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        public short[] ShortsFromBytes(byte[] bytes)
        {
            var shorts = new short[bytes.Length / 2];
            for (int i = 0; i < shorts.Length; i++)
            {
                shorts[i] = LittleEndian
                    ? (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8))
                    : (short)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);
            }
            return shorts;
        }

        public byte[] ShortsToBytes(short[] shorts)
        {
            var bytes = new byte[shorts.Length * 2];
            for (int i = 0; i < shorts.Length; i++)
            {
                if (LittleEndian)
                {
                    bytes[i * 2] = (byte)(shorts[i] & 0xFF);
                    bytes[i * 2 + 1] = (byte)((shorts[i] >> 8) & 0xFF);
                }
                else
                {
                    bytes[i * 2] = (byte)((shorts[i] >> 8) & 0xFF);
                    bytes[i * 2 + 1] = (byte)(shorts[i] & 0xFF);
                }
            }
            return bytes;
        }
    }


}