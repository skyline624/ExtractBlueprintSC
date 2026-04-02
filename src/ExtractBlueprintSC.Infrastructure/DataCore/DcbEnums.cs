namespace ExtractBlueprintSC.Infrastructure.DataCore;

internal enum DataType : ushort
{
    Boolean       = 0x0001,
    SByte         = 0x0002,
    Int16         = 0x0003,
    Int32         = 0x0004,
    Int64         = 0x0005,
    Byte          = 0x0006,
    UInt16        = 0x0007,
    UInt32        = 0x0008,
    UInt64        = 0x0009,
    String        = 0x000A,
    Single        = 0x000B,
    Double        = 0x000C,
    Locale        = 0x000D,
    Guid          = 0x000E,
    EnumChoice    = 0x000F,
    Class         = 0x0010,
    StrongPointer = 0x0110,
    WeakPointer   = 0x0210,
    Reference     = 0x0310,
}

internal enum ConversionType : ushort
{
    Attribute    = 0x00,
    ComplexArray = 0x01,
    SimpleArray  = 0x02,
    ClassArray   = 0x03,
}

internal static class DataTypeExtensions
{
    /// <summary>Taille en octets d'une valeur scalaire (Attribute). 0 pour Class (variable).</summary>
    public static int InlineSize(this DataType dt) => dt switch
    {
        DataType.Boolean       => 1,
        DataType.SByte         => 1,
        DataType.Byte          => 1,
        DataType.Int16         => 2,
        DataType.UInt16        => 2,
        DataType.Int32         => 4,
        DataType.UInt32        => 4,
        DataType.EnumChoice    => 4,
        DataType.Int64         => 8,
        DataType.UInt64        => 8,
        DataType.Single        => 4,
        DataType.Double        => 8,
        DataType.String        => 4,
        DataType.Locale        => 4,
        DataType.Guid          => 16,
        DataType.StrongPointer => 8,
        DataType.WeakPointer   => 8,
        DataType.Reference     => 20,
        DataType.Class         => 0,
        _                      => 0,
    };
}
