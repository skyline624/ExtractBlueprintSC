namespace ExtractBlueprintSC.Infrastructure.DataCore;

/// <summary>
/// GUID CIG : 16 octets en layout mixed-endian propre à Star Citizen.
/// Display : b[7]b[6]b[5]b[4]-b[3]b[2]-b[1]b[0]-b[15]b[14]-b[13..b[8]
/// </summary>
internal readonly struct CigGuid : IEquatable<CigGuid>
{
    private readonly byte[] _bytes;

    private CigGuid(byte[] bytes) => _bytes = bytes;

    public static CigGuid Read(BinaryReader br) => new(br.ReadBytes(16));

    public ReadOnlySpan<byte> AsSpan() => _bytes;

    public bool IsEmpty => _bytes == null || Array.TrueForAll(_bytes, b => b == 0);

    public bool Equals(CigGuid other)
        => _bytes != null && other._bytes != null && _bytes.AsSpan().SequenceEqual(other._bytes.AsSpan());

    public override bool Equals(object? obj) => obj is CigGuid g && Equals(g);

    public override int GetHashCode()
    {
        var hc = new HashCode();
        if (_bytes != null) foreach (var b in _bytes) hc.Add(b);
        return hc.ToHashCode();
    }

    public override string ToString()
    {
        if (_bytes == null) return "00000000-0000-0000-0000-000000000000";
        var b = _bytes;
        return $"{b[7]:x2}{b[6]:x2}{b[5]:x2}{b[4]:x2}" +
               $"-{b[3]:x2}{b[2]:x2}" +
               $"-{b[1]:x2}{b[0]:x2}" +
               $"-{b[15]:x2}{b[14]:x2}" +
               $"-{b[13]:x2}{b[12]:x2}{b[11]:x2}{b[10]:x2}{b[9]:x2}{b[8]:x2}";
    }
}

// ── Définitions de schéma (toutes lues séquentiellement par BinaryReader LE) ──

internal readonly struct StructDef
{
    public readonly int    NameOffset2;
    public readonly int    ParentTypeIndex;
    public readonly ushort AttributeCount;
    public readonly ushort FirstAttributeIndex;
    public readonly uint   StructSize;

    public static StructDef Read(BinaryReader br)
        => new(br.ReadInt32(), br.ReadInt32(), br.ReadUInt16(), br.ReadUInt16(), br.ReadUInt32());

    public StructDef(int n, int p, ushort ac, ushort fa, uint ss)
    { NameOffset2 = n; ParentTypeIndex = p; AttributeCount = ac; FirstAttributeIndex = fa; StructSize = ss; }
}

internal readonly struct PropertyDef
{
    public readonly int            NameOffset2;
    public readonly ushort         StructIndex;
    public readonly DataType       DataType;
    public readonly ConversionType ConversionType;

    public static PropertyDef Read(BinaryReader br)
    {
        int n   = br.ReadInt32();
        ushort si = br.ReadUInt16();
        var dt  = (DataType)br.ReadUInt16();
        var ct  = (ConversionType)br.ReadUInt16();
        br.ReadUInt16(); // padding
        return new PropertyDef(n, si, dt, ct);
    }

    public PropertyDef(int n, ushort si, DataType dt, ConversionType ct)
    { NameOffset2 = n; StructIndex = si; DataType = dt; ConversionType = ct; }
}

internal readonly struct EnumDef
{
    public readonly int    NameOffset2;
    public readonly ushort ValueCount;
    public readonly ushort FirstValueIndex;

    public static EnumDef Read(BinaryReader br)
        => new(br.ReadInt32(), br.ReadUInt16(), br.ReadUInt16());

    public EnumDef(int n, ushort vc, ushort fv)
    { NameOffset2 = n; ValueCount = vc; FirstValueIndex = fv; }
}

internal readonly struct DataMapping
{
    public readonly uint StructCount;
    public readonly int  StructIndex;

    public static DataMapping Read(BinaryReader br) => new(br.ReadUInt32(), br.ReadInt32());
    public DataMapping(uint sc, int si) { StructCount = sc; StructIndex = si; }
}

internal readonly struct DcbRecord
{
    public readonly int     NameOffset2;
    public readonly int     FileNameOffset;
    public readonly int     StructIndex;
    public readonly CigGuid Id;
    public readonly ushort  InstanceIndex;
    public readonly ushort  StructSize;

    public static DcbRecord Read(BinaryReader br)
        => new(br.ReadInt32(), br.ReadInt32(), br.ReadInt32(),
               CigGuid.Read(br), br.ReadUInt16(), br.ReadUInt16());

    public DcbRecord(int n, int fn, int si, CigGuid id, ushort ii, ushort ss)
    { NameOffset2 = n; FileNameOffset = fn; StructIndex = si; Id = id; InstanceIndex = ii; StructSize = ss; }
}

internal readonly struct DcbPointer
{
    public readonly int StructIndex;
    public readonly int InstanceIndex;

    public bool IsNull => StructIndex == -1 && InstanceIndex == -1;

    public static DcbPointer Read(BinaryReader br) => new(br.ReadInt32(), br.ReadInt32());

    public static DcbPointer ReadFrom(ReadOnlySpan<byte> span, int offset)
        => new(BitConverter.ToInt32(span.Slice(offset, 4)),
               BitConverter.ToInt32(span.Slice(offset + 4, 4)));

    public DcbPointer(int si, int ii) { StructIndex = si; InstanceIndex = ii; }
}

internal readonly struct DcbReference
{
    public readonly int     InstanceIndex;
    public readonly CigGuid RecordId;

    public bool IsNull => InstanceIndex == 0 && RecordId.IsEmpty;

    public static DcbReference Read(BinaryReader br)
        => new(br.ReadInt32(), CigGuid.Read(br));

    public static DcbReference ReadFrom(ReadOnlySpan<byte> span, int offset)
    {
        int ii      = BitConverter.ToInt32(span.Slice(offset, 4));
        var guidMs  = new MemoryStream(span.Slice(offset + 4, 16).ToArray());
        var guidBr  = new BinaryReader(guidMs);
        var id      = CigGuid.Read(guidBr);
        return new DcbReference(ii, id);
    }

    public DcbReference(int ii, CigGuid id) { InstanceIndex = ii; RecordId = id; }
}
