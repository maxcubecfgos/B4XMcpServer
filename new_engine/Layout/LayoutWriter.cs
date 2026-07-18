using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace B4XEngineCore
{
    public static class LayoutWriter
    {
        public static byte[] WriteLayoutFile(LayoutFile layout)
        {
            var main = new BinaryWriter();

            main.WriteInt32(layout.Version);
            int lengthPos = main.Position;
            main.WriteInt32(0);
            main.WriteInt32(layout.GridSize);

            var manifestStream = new BinaryWriter();
            var outerStringTable = new Dictionary<string, int>();

            manifestStream.WriteInt32(layout.Manifest.Count);
            foreach (var entry in layout.Manifest)
            {
                manifestStream.WriteStringRef(outerStringTable, entry.Name);
                manifestStream.WriteStringRef(outerStringTable, entry.JavaType);
                manifestStream.WriteStringRef(outerStringTable, entry.CsType);
            }

            WriteStringTable(main, outerStringTable);
            main.WriteFrom(manifestStream);

            main.WriteInt32(layout.FileReferences.Count);
            foreach (var file in layout.FileReferences)
                main.WriteLengthPrefixedString(file);

            WriteScriptData(main, layout.ScriptData, layout.Variants);

            int headerEndPos = main.Position;
            main.Position = lengthPos;
            main.WriteInt32(headerEndPos - lengthPos - 4);
            main.Position = headerEndPos;

            var innerStringCollector = new Dictionary<string, int>();
            var innerStream = new BinaryWriter();

            innerStream.WriteInt32(layout.Variants.Count);
            foreach (var variant in layout.Variants)
            {
                innerStream.WriteFloat((float)variant.Scale);
                innerStream.WriteInt32(variant.Width);
                innerStream.WriteInt32(variant.Height);
            }

            WriteControlProperties(innerStream, innerStringCollector, layout.RootControl);
            WriteChildren(innerStream, innerStringCollector, layout.RootControl.Children);
            WriteEndMarker(innerStream);
            innerStream.WriteInt32(0);

            bool needsRemap = !IsAlreadySorted(innerStringCollector);
            if (needsRemap)
            {
                var (sortedTable, remappedStream) = RemapInnerStream(innerStringCollector, innerStream, layout);
                WriteStringTable(main, sortedTable);
                main.WriteFrom(remappedStream);
            }
            else
            {
                WriteStringTable(main, innerStringCollector);
                main.WriteFrom(innerStream);
            }

            main.WriteByte(layout.Flags.C ? (byte)1 : (byte)0);
            main.WriteByte(layout.Flags.D ? (byte)1 : (byte)0);

            return main.ToBuffer();
        }

        private static void WriteStringTable(BinaryWriter writer, Dictionary<string, int> table)
        {
            var byIndex = new string[table.Count];
            foreach (var kvp in table)
                byIndex[kvp.Value] = kvp.Key;

            writer.WriteInt32(byIndex.Length);
            foreach (var str in byIndex)
                writer.WriteLengthPrefixedString(str);
        }

        private static bool IsAlreadySorted(Dictionary<string, int> table)
        {
            string prev = "";
            foreach (var key in table.Keys)
            {
                if (string.Compare(key, prev, StringComparison.Ordinal) < 0) return false;
                prev = key;
            }
            return true;
        }

        private static (Dictionary<string, int> sortedTable, BinaryWriter remappedStream) RemapInnerStream(
            Dictionary<string, int> unsortedTable, BinaryWriter originalStream, LayoutFile layout)
        {
            var sortedKeys = unsortedTable.Keys.OrderBy(k => k).ToList();
            var sortedTable = new Dictionary<string, int>();
            for (int i = 0; i < sortedKeys.Count; i++)
                sortedTable[sortedKeys[i]] = i;

            var remapped = new BinaryWriter();
            remapped.WriteInt32(layout.Variants.Count);
            foreach (var v in layout.Variants)
            {
                remapped.WriteFloat((float)v.Scale);
                remapped.WriteInt32(v.Width);
                remapped.WriteInt32(v.Height);
            }

            WriteControlProperties(remapped, sortedTable, layout.RootControl);
            WriteChildren(remapped, sortedTable, layout.RootControl.Children);
            WriteEndMarker(remapped);
            remapped.WriteInt32(0);

            return (sortedTable, remapped);
        }

        private static void WriteControlProperties(BinaryWriter writer, Dictionary<string, int> table, ControlNode node)
        {
            foreach (var kvp in node.Properties)
                WriteKeyValuePair(writer, table, kvp.Key, kvp.Value);
        }

        private static void WriteChildren(BinaryWriter writer, Dictionary<string, int> table, List<ControlNode> children)
        {
            if (children.Count == 0) return;

            WriteObjectStart(writer, table, ":kids");
            for (int i = 0; i < children.Count; i++)
            {
                WriteObjectStart(writer, table, i.ToString());
                WriteControlProperties(writer, table, children[i]);
                if (children[i].Children.Count > 0)
                    WriteChildren(writer, table, children[i].Children);
                WriteEndMarker(writer);
            }
            WriteEndMarker(writer);
        }

        private static void WriteKeyValuePair(BinaryWriter writer, Dictionary<string, int> table, string key, PropertyValue value)
        {
            WriteStringRef(writer, table, key);
            WriteTaggedValue(writer, table, value);
        }

        private static void WriteTaggedValue(BinaryWriter writer, Dictionary<string, int> table, PropertyValue value)
        {
            switch (value)
            {
                case IntValue iv:
                    writer.WriteByte(1); writer.WriteInt32(iv.Value);
                    break;
                case StringValue sv:
                    writer.WriteByte(2); writer.WriteLengthPrefixedString(sv.Value);
                    break;
                case StringRefValue srv:
                    writer.WriteByte(9); WriteStringRef(writer, table, srv.Value);
                    break;
                case ObjectValue ov:
                    writer.WriteByte(3);
                    foreach (var kvp in ov.Value)
                        WriteKeyValuePair(writer, table, kvp.Key, kvp.Value);
                    WriteEndMarker(writer);
                    break;
                case BoolValue bv:
                    writer.WriteByte(5); writer.WriteByte(bv.Value ? (byte)1 : (byte)0);
                    break;
                case ColorValue cv:
                    writer.WriteByte(6);
                    writer.WriteByte(cv.A); writer.WriteByte(cv.R);
                    writer.WriteByte(cv.G); writer.WriteByte(cv.B);
                    break;
                case FloatValue fv:
                    writer.WriteByte(7); writer.WriteFloat((float)fv.Value);
                    break;
                case DoubleValue dv:
                    writer.WriteByte(7); writer.WriteFloat((float)dv.Value);
                    break;
                case ErRefValue ev:
                    writer.WriteByte(8); writer.WriteInt32(ev.Value);
                    break;
                case RectValue rv:
                    writer.WriteByte(11);
                    writer.WriteInt16((short)rv.X); writer.WriteInt16((short)rv.Y);
                    writer.WriteInt16((short)rv.Width); writer.WriteInt16((short)rv.Height);
                    break;
                case NullValue:
                    writer.WriteByte(12);
                    break;
            }
        }

        private static void WriteStringRef(BinaryWriter writer, Dictionary<string, int> table, string str)
        {
            writer.WriteStringRef(table, str);
        }

        private static void WriteObjectStart(BinaryWriter writer, Dictionary<string, int> table, string key)
        {
            WriteStringRef(writer, table, key);
            writer.WriteByte(3);
        }

        private static void WriteEndMarker(BinaryWriter writer)
        {
            writer.WriteLengthPrefixedString("");
            writer.WriteByte(4);
        }

        private static void WriteScriptData(BinaryWriter writer, ScriptData? scriptData, List<Variant> variants)
        {
            if (scriptData == null)
            {
                writer.WriteInt32(0);
                return;
            }

            if (scriptData.RawCompressedBytes != null)
            {
                writer.WriteInt32(scriptData.RawCompressedBytes.Length);
                writer.WriteBytes(scriptData.RawCompressedBytes);
                return;
            }

            var sw = new BinaryWriter();
            sw.Write7BitEncodedString(scriptData.MainScript);
            sw.WriteInt32(scriptData.VariantScripts.Count);
            foreach (var vs in scriptData.VariantScripts)
            {
                sw.WriteFloat((float)vs.Variant.Scale);
                sw.WriteInt32(vs.Variant.Width);
                sw.WriteInt32(vs.Variant.Height);
                sw.Write7BitEncodedString(vs.Script);
            }

            var uncompressed = sw.ToBuffer();
            byte[] compressed;
            using (var outStream = new MemoryStream())
            {
                using (var gzip = new GZipStream(outStream, CompressionLevel.Optimal))
                    gzip.Write(uncompressed, 0, uncompressed.Length);
                compressed = outStream.ToArray();
            }

            writer.WriteInt32(compressed.Length);
            writer.WriteBytes(compressed);
        }
    }
}
