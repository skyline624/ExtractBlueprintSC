namespace ExtractBlueprintSC.Infrastructure.P4k;

public sealed class P4kEntry
{
    public string Name { get; }
    public ulong CompressedSize { get; }
    public ulong UncompressedSize { get; }
    public ushort CompressionMethod { get; }
    public bool IsEncrypted { get; }
    public ulong Offset { get; }
    public uint Crc32 { get; }
    public uint LastModified { get; }

    internal P4kEntry(
        string name,
        ulong compressedSize,
        ulong uncompressedSize,
        ushort compressionMethod,
        bool isEncrypted,
        ulong offset,
        uint crc32,
        uint lastModified)
    {
        Name = name;
        CompressedSize = compressedSize;
        UncompressedSize = uncompressedSize;
        CompressionMethod = compressionMethod;
        IsEncrypted = isEncrypted;
        Offset = offset;
        Crc32 = crc32;
        LastModified = lastModified;
    }
}
