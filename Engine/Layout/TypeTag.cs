namespace B4XMcpServer.Engine
{
    public enum TypeTag : byte
    {
        Int32 = 1,
        String = 2,
        Object = 3,
        End = 4,
        Bool = 5,
        Color = 6,
        Float = 7,
        ErRef = 8,
        StringRef = 9,
        Double = 10,
        Int32Rect = 11,
        Null = 12,
    }

    public enum Platform { B4A, B4J }
}
