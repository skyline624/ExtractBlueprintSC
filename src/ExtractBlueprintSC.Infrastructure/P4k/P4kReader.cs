using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ZstdSharp;

namespace ExtractBlueprintSC.Infrastructure.P4k;

/// <summary>
/// Lecteur natif pour les archives P4K de Star Citizen.
/// Format : ZIP64 modifié avec extensions CIG (chiffrement AES-128-CBC, compression zstd).
/// </summary>
public sealed class P4kReader : IDisposable
{
    // ── Signatures ZIP ────────────────────────────────────────────────────────
    private const uint EocdSignature          = 0x06054B50;
    private const uint Zip64LocatorSignature  = 0x07064B50;
    private const uint Eocd64Signature        = 0x06064B50;
    private const uint CentralDirSignature    = 0x02014B50;
    private const uint LocalFileSignature     = 0x04034B50;
    private const uint LocalFileCigSignature  = 0x14034B50;

    // ── Clé AES-128-CBC de CIG ────────────────────────────────────────────────
    private static readonly byte[] AesKey =
    [
        0x5E, 0x7A, 0x20, 0x02, 0x30, 0x2E, 0xEB, 0x1A,
        0x3B, 0xB6, 0x17, 0xC3, 0x0F, 0xDE, 0x1E, 0x47
    ];
    private static readonly byte[] AesIv = new byte[16]; // 16 zéros

    private readonly FileStream _stream;
    private readonly List<P4kEntry> _entries;
    private readonly Dictionary<string, int> _pathIndex;
    private readonly Dictionary<string, int> _lowercaseIndex;

    private P4kReader(FileStream stream, List<P4kEntry> entries)
    {
        _stream = stream;
        _entries = entries;
        _pathIndex = new Dictionary<string, int>(entries.Count, StringComparer.Ordinal);
        _lowercaseIndex = new Dictionary<string, int>(entries.Count, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < entries.Count; i++)
        {
            _pathIndex[entries[i].Name] = i;
            _lowercaseIndex[entries[i].Name] = i;
        }
    }

    public IReadOnlyList<P4kEntry> Entries => _entries;

    /// <summary>Ouvre un fichier P4K et parse son Central Directory.</summary>
    public static P4kReader Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
        try
        {
            var entries = ParseCentralDirectory(stream);
            return new P4kReader(stream, entries);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Cherche une entrée par son chemin (insensible à la casse).</summary>
    public P4kEntry? FindEntryIgnoreCase(string path)
    {
        if (_lowercaseIndex.TryGetValue(path, out int idx))
            return _entries[idx];
        return null;
    }

    /// <summary>Lit, déchiffre et décompresse les données d'une entrée.</summary>
    public byte[] ReadEntry(P4kEntry entry)
    {
        lock (_stream)
        {
            _stream.Seek((long)entry.Offset, SeekOrigin.Begin);
            using var br = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);

            // Lire Local File Header (30 octets)
            uint sig = br.ReadUInt32();
            if (sig != LocalFileSignature && sig != LocalFileCigSignature)
                throw new InvalidDataException($"Signature de fichier local invalide : 0x{sig:X8}");

            br.ReadUInt16(); // version needed
            br.ReadUInt16(); // flags
            br.ReadUInt16(); // compression method (on utilise celui du central dir)
            br.ReadUInt16(); // last mod time
            br.ReadUInt16(); // last mod date
            br.ReadUInt32(); // crc32
            br.ReadUInt32(); // compressed size (32-bit, on utilise celui du central dir)
            br.ReadUInt32(); // uncompressed size (32-bit)
            ushort nameLen  = br.ReadUInt16();
            ushort extraLen = br.ReadUInt16();

            // Sauter le nom + extra field pour atteindre les données
            _stream.Seek(nameLen + extraLen, SeekOrigin.Current);

            // Lire les données compressées/chiffrées
            byte[] raw = br.ReadBytes((int)entry.CompressedSize);

            return Decompress(raw, entry);
        }
    }

    // ── Décompression ────────────────────────────────────────────────────────

    private static byte[] Decompress(byte[] raw, P4kEntry entry)
    {
        byte[] data = entry.IsEncrypted ? Decrypt(raw) : raw;

        return entry.CompressionMethod switch
        {
            0   => data,                                              // stocké
            8   => DeflateDecompress(data, (int)entry.UncompressedSize),
            100 => ZstdDecompress(data, (int)entry.UncompressedSize),
            _   => throw new NotSupportedException($"Méthode de compression non supportée : {entry.CompressionMethod}")
        };
    }

    private static byte[] Decrypt(byte[] data)
    {
        if (data.Length == 0) return [];

        using var aes = Aes.Create();
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key     = AesKey;
        aes.IV      = AesIv;

        using var decryptor = aes.CreateDecryptor();
        byte[] buf = decryptor.TransformFinalBlock(data, 0, data.Length);

        // CIG utilise un padding de zéros → supprimer les zéros en fin
        int lastNonZero = buf.Length - 1;
        while (lastNonZero >= 0 && buf[lastNonZero] == 0)
            lastNonZero--;

        return buf[..(lastNonZero + 1)];
    }

    private static byte[] DeflateDecompress(byte[] data, int sizeHint)
    {
        using var ms = new MemoryStream(data);
        using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
        var output = new MemoryStream(sizeHint);
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] ZstdDecompress(byte[] data, int sizeHint)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(data, sizeHint > 0 ? sizeHint : data.Length * 4).ToArray();
    }

    // ── Parsing du Central Directory ─────────────────────────────────────────

    private static List<P4kEntry> ParseCentralDirectory(FileStream file)
    {
        long fileLen = file.Length;

        // Lire la queue du fichier pour trouver EOCD / EOCD64
        int tailSize = (int)Math.Min(fileLen, 22 + 65535 + 56 + 20);
        long tailOffset = fileLen - tailSize;
        file.Seek(tailOffset, SeekOrigin.Begin);
        byte[] tail = new byte[tailSize];
        file.ReadExactly(tail);

        var loc = LocateCentralDirectory(tail, tailOffset);

        // Lire le Central Directory
        file.Seek((long)loc.CdOffset, SeekOrigin.Begin);
        byte[] cdData = new byte[loc.CdSize];
        file.ReadExactly(cdData);

        return ParseEntries(cdData, loc.TotalEntries, loc.IsZip64);
    }

    private record CdLocation(ulong TotalEntries, ulong CdOffset, ulong CdSize, bool IsZip64);

    private static CdLocation LocateCentralDirectory(byte[] tail, long tailFileOffset)
    {
        int eocdOff = FindEocd(tail);

        using var ms = new MemoryStream(tail);
        ms.Seek(eocdOff, SeekOrigin.Begin);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        uint sig = br.ReadUInt32();
        if (sig != EocdSignature)
            throw new InvalidDataException($"Signature EOCD invalide : 0x{sig:X8}");

        br.ReadUInt16(); // disk number
        br.ReadUInt16(); // start disk number
        ushort entriesOnDisk = br.ReadUInt16();
        ushort totalEntries  = br.ReadUInt16();
        uint   cdSize32      = br.ReadUInt32();
        uint   cdOffset32    = br.ReadUInt32();
        br.ReadUInt16(); // comment length

        bool isZip64 = entriesOnDisk == 0xFFFF || totalEntries == 0xFFFF
                    || cdSize32 == 0xFFFFFFFF || cdOffset32 == 0xFFFFFFFF;

        if (isZip64)
        {
            int locOff = FindZip64Locator(tail, eocdOff);
            ms.Seek(locOff, SeekOrigin.Begin);

            uint locSig = br.ReadUInt32();
            if (locSig != Zip64LocatorSignature)
                throw new InvalidDataException($"Signature ZIP64 Locator invalide : 0x{locSig:X8}");

            br.ReadUInt32(); // disk with eocd64
            ulong eocd64AbsOffset = br.ReadUInt64();
            br.ReadUInt32(); // total disks

            long eocd64Rel = (long)eocd64AbsOffset - tailFileOffset;
            if (eocd64Rel < 0 || eocd64Rel >= tail.Length)
                throw new InvalidDataException("EOCD64 hors de la fenêtre de recherche");

            ms.Seek(eocd64Rel, SeekOrigin.Begin);

            uint eocd64Sig = br.ReadUInt32();
            if (eocd64Sig != Eocd64Signature)
                throw new InvalidDataException($"Signature EOCD64 invalide : 0x{eocd64Sig:X8}");

            br.ReadUInt64(); // size of record
            br.ReadUInt16(); // version made by
            br.ReadUInt16(); // version needed
            br.ReadUInt32(); // disk number
            br.ReadUInt32(); // start disk
            br.ReadUInt64(); // entries on disk
            ulong totalEntries64 = br.ReadUInt64();
            ulong cdSize64       = br.ReadUInt64();
            ulong cdOffset64     = br.ReadUInt64();

            return new CdLocation(totalEntries64, cdOffset64, cdSize64, true);
        }

        return new CdLocation(totalEntries, cdOffset32, cdSize32, false);
    }

    private static int FindEocd(byte[] data)
    {
        // Chercher la signature EOCD en remontant depuis la fin (max 65535 octets de commentaire)
        int searchStart = Math.Max(0, data.Length - 22 - 65535);
        int searchEnd   = data.Length - 22;

        for (int i = searchEnd; i >= searchStart; i--)
        {
            if (data[i] == 0x50 && data[i + 1] == 0x4B && data[i + 2] == 0x05 && data[i + 3] == 0x06)
                return i;
        }

        throw new InvalidDataException("EOCD introuvable dans le fichier P4K");
    }

    private static int FindZip64Locator(byte[] data, int eocdOffset)
    {
        int searchStart = Math.Max(0, eocdOffset - 22 - 65535);

        for (int i = eocdOffset - 1; i >= searchStart; i--)
        {
            if (i + 4 <= data.Length
                && data[i]     == 0x50 && data[i + 1] == 0x4B
                && data[i + 2] == 0x06 && data[i + 3] == 0x07)
                return i;
        }

        throw new InvalidDataException("ZIP64 Locator introuvable");
    }

    private static List<P4kEntry> ParseEntries(byte[] cdData, ulong totalEntries, bool isZip64)
    {
        var entries = new List<P4kEntry>((int)totalEntries);

        using var ms = new MemoryStream(cdData);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        for (ulong i = 0; i < totalEntries; i++)
            entries.Add(ReadEntry(br, isZip64));

        return entries;
    }

    private static P4kEntry ReadEntry(BinaryReader br, bool isZip64)
    {
        uint sig = br.ReadUInt32();
        if (sig != CentralDirSignature)
            throw new InvalidDataException($"Signature Central Directory invalide : 0x{sig:X8}");

        br.ReadUInt16(); // version made by
        br.ReadUInt16(); // version needed
        br.ReadUInt16(); // flags
        ushort compressionMethod = br.ReadUInt16();
        uint   lastModified      = br.ReadUInt32();
        uint   crc32             = br.ReadUInt32();
        uint   compressedSize32  = br.ReadUInt32();
        uint   uncompressedSize32 = br.ReadUInt32();
        ushort nameLen           = br.ReadUInt16();
        ushort extraLen          = br.ReadUInt16();
        ushort commentLen        = br.ReadUInt16();
        ushort diskNumberStart   = br.ReadUInt16();
        br.ReadUInt16(); // internal attributes
        br.ReadUInt32(); // external attributes
        uint   localHeaderOffset32 = br.ReadUInt32();

        // Lire le nom du fichier (normaliser '/' → '\')
        byte[] nameBytes = br.ReadBytes(nameLen);
        var sb = new StringBuilder(nameLen);
        foreach (byte b in nameBytes)
            sb.Append(b == (byte)'/' ? '\\' : (char)b);
        string name = sb.ToString();

        ulong compressedSize   = compressedSize32;
        ulong uncompressedSize = uncompressedSize32;
        ulong localHeaderOffset = localHeaderOffset32;
        bool isEncrypted = false;

        if (isZip64)
        {
            // Parser les champs extra CIG dans l'ordre exact
            byte[] extraData = br.ReadBytes(extraLen);
            using var ems = new MemoryStream(extraData);
            using var ebr = new BinaryReader(ems, Encoding.UTF8, leaveOpen: true);

            // Tag 0x0001 : ZIP64 extended info
            ushort tag1 = ebr.ReadUInt16();
            if (tag1 != 0x0001)
                throw new InvalidDataException($"Tag extra attendu 0x0001, obtenu 0x{tag1:X4}");
            ebr.ReadUInt16(); // taille du bloc ZIP64

            // Ordre ZIP64 : uncompressed d'abord, puis compressed (archive.rs)
            if (uncompressedSize32 == 0xFFFFFFFF)
                uncompressedSize = ebr.ReadUInt64();
            if (compressedSize32 == 0xFFFFFFFF)
                compressedSize = ebr.ReadUInt64();
            if (localHeaderOffset32 == 0xFFFFFFFF)
                localHeaderOffset = ebr.ReadUInt64();
            if (diskNumberStart == 0xFFFF)
                ebr.ReadUInt32();

            // Tag 0x5000 : CIG custom
            ushort tag2   = ebr.ReadUInt16();
            if (tag2 != 0x5000)
                throw new InvalidDataException($"Tag extra attendu 0x5000, obtenu 0x{tag2:X4}");
            ushort size5000 = ebr.ReadUInt16();
            // avancer de (size - 4) octets (le "size" inclut 4 octets déjà lus)
            int skip5000 = Math.Max(0, size5000 - 4);
            if (skip5000 > 0) ems.Seek(skip5000, SeekOrigin.Current);

            // Tag 0x5002 : flag de chiffrement
            // size inclut les 4 octets tag+size → size=6 → 2 octets de données = enc_flag
            ushort tag3   = ebr.ReadUInt16();
            if (tag3 != 0x5002)
                throw new InvalidDataException($"Tag extra attendu 0x5002, obtenu 0x{tag3:X4}");
            ushort size5002 = ebr.ReadUInt16();
            if (size5002 != 6)
                throw new InvalidDataException($"Taille tag 0x5002 attendue 6, obtenue {size5002}");
            ushort encFlag = ebr.ReadUInt16(); // les 2 seuls octets de données (size - 4)
            isEncrypted = encFlag == 1;

            // Tag 0x5003 : CIG custom
            ushort tag4   = ebr.ReadUInt16();
            if (tag4 != 0x5003)
                throw new InvalidDataException($"Tag extra attendu 0x5003, obtenu 0x{tag4:X4}");
            ushort size5003 = ebr.ReadUInt16();
            int skip5003 = Math.Max(0, size5003 - 4);
            if (skip5003 > 0) ems.Seek(skip5003, SeekOrigin.Current);
        }
        else
        {
            // Non-ZIP64 : sauter les extra fields
            br.ReadBytes(extraLen);
        }

        // Sauter le commentaire de fichier
        if (commentLen > 0)
            br.ReadBytes(commentLen);

        return new P4kEntry(
            name,
            compressedSize,
            uncompressedSize,
            compressionMethod,
            isEncrypted,
            localHeaderOffset,
            crc32,
            lastModified);
    }

    public void Dispose() => _stream.Dispose();
}
