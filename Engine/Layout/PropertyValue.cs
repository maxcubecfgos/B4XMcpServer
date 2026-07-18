using System.Collections.Generic;

namespace B4XMcpServer.Engine
{
    public abstract record PropertyValue { public abstract TypeTag Tag { get; } }
    public record IntValue(int Value) : PropertyValue { public override TypeTag Tag => TypeTag.Int32; }
    public record StringValue(string Value) : PropertyValue { public override TypeTag Tag => TypeTag.String; }
    public record StringRefValue(string Value) : PropertyValue { public override TypeTag Tag => TypeTag.StringRef; }
    public record FloatValue(double Value) : PropertyValue { public override TypeTag Tag => TypeTag.Float; }
    public record DoubleValue(double Value) : PropertyValue { public override TypeTag Tag => TypeTag.Double; }
    public record BoolValue(bool Value) : PropertyValue { public override TypeTag Tag => TypeTag.Bool; }
    public record ColorValue(byte A, byte R, byte G, byte B) : PropertyValue { public override TypeTag Tag => TypeTag.Color; }
    public record RectValue(int X, int Y, int Width, int Height) : PropertyValue { public override TypeTag Tag => TypeTag.Int32Rect; }
    public record NullValue : PropertyValue { public override TypeTag Tag => TypeTag.Null; }
    public record ObjectValue(Dictionary<string, PropertyValue> Value) : PropertyValue { public override TypeTag Tag => TypeTag.Object; }
    public record ErRefValue(int Value) : PropertyValue { public override TypeTag Tag => TypeTag.ErRef; }
}
