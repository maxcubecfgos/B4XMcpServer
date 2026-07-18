using System;
using System.Collections.Generic;

namespace B4XEngineCore
{
    public record Variant(double Scale, int Width, int Height);
    public record ManifestEntry(string Name, string JavaType, string CsType);
    public record VariantScript(Variant Variant, string Script);

    public class ScriptData
    {
        public string MainScript { get; set; } = "";
        public List<VariantScript> VariantScripts { get; set; } = new();
        public byte[]? RawCompressedBytes { get; set; }
    }

    public class ControlNode
    {
        public Dictionary<string, PropertyValue> Properties { get; set; } = new();
        public List<ControlNode> Children { get; set; } = new();
    }

    public class LayoutFile
    {
        public int Version { get; set; }
        public int GridSize { get; set; }
        public List<Variant> Variants { get; set; } = new();
        public ControlNode RootControl { get; set; } = new();
        public List<ManifestEntry> Manifest { get; set; } = new();
        public List<string> FileReferences { get; set; } = new();
        public ScriptData? ScriptData { get; set; }
        public (bool C, bool D) Flags { get; set; }
    }

    public class ParseError : Exception
    {
        public long Offset { get; }
        public ParseError(string message, long offset)
            : base($"Parse error at offset 0x{offset:X}: {message}")
        { Offset = offset; }
    }
}
