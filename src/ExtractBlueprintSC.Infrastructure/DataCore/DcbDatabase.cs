using System.Text;

namespace ExtractBlueprintSC.Infrastructure.DataCore;

/// <summary>
/// Base de données DataCore v6 parsée depuis un fichier .dcb binaire.
/// Reproduit fidèlement le parsing de starbreaker-datacore/src/database.rs.
/// </summary>
internal sealed class DcbDatabase
{
    // ── Schéma ────────────────────────────────────────────────────────────────
    public StructDef[]    StructDefs    { get; }
    public PropertyDef[]  PropertyDefs  { get; }
    public EnumDef[]      EnumDefs      { get; }
    public DataMapping[]  DataMappings  { get; }
    public DcbRecord[]    Records       { get; }

    // ── Value arrays (stockés en raw bytes LE, accès via Get*) ───────────────
    private readonly byte[] _int8Raw, _int16Raw, _int32Raw, _int64Raw;
    private readonly byte[] _uint8Raw, _uint16Raw, _uint32Raw, _uint64Raw;
    private readonly byte[] _boolRaw, _singleRaw, _doubleRaw;

    public CigGuid[]     GuidValues      { get; }
    public int[]         StringIdValues  { get; }   // offsets dans string_table1
    public int[]         LocaleValues    { get; }
    public int[]         EnumValues      { get; }
    public DcbPointer[]  StrongValues    { get; }
    public DcbPointer[]  WeakValues      { get; }
    public DcbReference[] ReferenceValues { get; }
    private readonly int[] _enumOptionsAll;   // offsets dans string_table2

    // ── Tables de chaînes ────────────────────────────────────────────────────
    private readonly byte[] _stringTable1;
    private readonly byte[] _stringTable2;

    // ── Données d'instance ───────────────────────────────────────────────────
    private readonly byte[] _instanceData;
    private readonly int[]  _instanceOffsets;

    // ── Caches ───────────────────────────────────────────────────────────────
    private readonly int[][]  _cachedProperties;  // indices de PropertyDef, ordre parent-first
    private readonly bool[]   _hasWeakPointers;
    private readonly HashSet<CigGuid>              _mainRecordIds;
    private readonly Dictionary<CigGuid, int>      _recordMap;

    // ── Parsing principal ────────────────────────────────────────────────────

    public static DcbDatabase FromBytes(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        // 1. Header (120 octets)
        uint magic    = br.ReadUInt32();
        uint version  = br.ReadUInt32();
        if (version != 6)
            throw new InvalidDataException($"Version DCB non supportée : {version}");

        br.ReadUInt32(); br.ReadUInt32(); // reserved

        int structCount    = br.ReadInt32();
        int propCount      = br.ReadInt32();
        int enumCount      = br.ReadInt32();
        int mappingCount   = br.ReadInt32();
        int recordCount    = br.ReadInt32();
        int boolCount      = br.ReadInt32();
        int int8Count      = br.ReadInt32();
        int int16Count     = br.ReadInt32();
        int int32Count     = br.ReadInt32();
        int int64Count     = br.ReadInt32();
        int uint8Count     = br.ReadInt32();
        int uint16Count    = br.ReadInt32();
        int uint32Count    = br.ReadInt32();
        int uint64Count    = br.ReadInt32();
        int singleCount    = br.ReadInt32();
        int doubleCount    = br.ReadInt32();
        int guidCount      = br.ReadInt32();
        int stringIdCount  = br.ReadInt32();
        int localeCount    = br.ReadInt32();
        int enumValCount   = br.ReadInt32();
        int strongCount    = br.ReadInt32();
        int weakCount      = br.ReadInt32();
        int refCount       = br.ReadInt32();
        int enumOptCount   = br.ReadInt32();
        uint textLen       = br.ReadUInt32();
        uint textLen2      = br.ReadUInt32();

        // 2. Définitions
        var structDefs  = ReadArray(br, structCount,  StructDef.Read);
        var propDefs    = ReadArray(br, propCount,    PropertyDef.Read);
        var enumDefs    = ReadArray(br, enumCount,    EnumDef.Read);
        var mappings    = ReadArray(br, mappingCount, DataMapping.Read);
        var records     = ReadArray(br, recordCount,  DcbRecord.Read);

        // 3. Value arrays raw
        byte[] int8Raw   = br.ReadBytes(int8Count   * 1);
        byte[] int16Raw  = br.ReadBytes(int16Count  * 2);
        byte[] int32Raw  = br.ReadBytes(int32Count  * 4);
        byte[] int64Raw  = br.ReadBytes(int64Count  * 8);
        byte[] uint8Raw  = br.ReadBytes(uint8Count  * 1);
        byte[] uint16Raw = br.ReadBytes(uint16Count * 2);
        byte[] uint32Raw = br.ReadBytes(uint32Count * 4);
        byte[] uint64Raw = br.ReadBytes(uint64Count * 8);
        byte[] boolRaw   = br.ReadBytes(boolCount   * 1);
        byte[] singleRaw = br.ReadBytes(singleCount * 4);
        byte[] doubleRaw = br.ReadBytes(doubleCount * 8);

        // 4. Value arrays structurés
        var guids       = ReadArray(br, guidCount,     _ => CigGuid.Read(_));
        var stringIds   = ReadArray(br, stringIdCount, r => r.ReadInt32());
        var localeVals  = ReadArray(br, localeCount,   r => r.ReadInt32());
        var enumVals    = ReadArray(br, enumValCount,  r => r.ReadInt32());
        var strongVals  = ReadArray(br, strongCount,   DcbPointer.Read);
        var weakVals    = ReadArray(br, weakCount,     DcbPointer.Read);
        var refVals     = ReadArray(br, refCount,      DcbReference.Read);
        var enumOpts    = ReadArray(br, enumOptCount,  r => r.ReadInt32()); // StringId2 = int

        // 5. Tables de chaînes
        byte[] st1 = br.ReadBytes((int)textLen);
        byte[] st2 = br.ReadBytes((int)textLen2);

        // 6. Instance data = tout le reste
        byte[] instanceData = br.ReadBytes((int)(data.Length - ms.Position));

        // 7. Offsets d'instance
        var instanceOffsets = new int[structDefs.Length];
        int running = 0;
        foreach (var mapping in mappings)
        {
            int si = mapping.StructIndex;
            instanceOffsets[si] = running;
            running += (int)mapping.StructCount * (int)structDefs[si].StructSize;
        }

        // 8. Cache de propriétés (parent-first)
        var cachedProperties = BuildPropertyCache(structDefs, propDefs);

        // 9. has_weak_pointers par transitivité
        var hasWeakPointers = BuildWeakPointerFlags(structDefs, propDefs, cachedProperties);

        // 10. Main records : dernier record par file_name_offset unique
        var lastByFile = new Dictionary<int, CigGuid>();
        foreach (var r in records)
            lastByFile[r.FileNameOffset] = r.Id;
        var mainRecordIds = new HashSet<CigGuid>(lastByFile.Values);

        // 11. Record map par GUID
        var recordMap = new Dictionary<CigGuid, int>(records.Length);
        for (int i = 0; i < records.Length; i++)
            recordMap[records[i].Id] = i;

        return new DcbDatabase(
            structDefs, propDefs, enumDefs, mappings, records,
            int8Raw, int16Raw, int32Raw, int64Raw,
            uint8Raw, uint16Raw, uint32Raw, uint64Raw,
            boolRaw, singleRaw, doubleRaw,
            guids, stringIds, localeVals, enumVals, strongVals, weakVals, refVals, enumOpts,
            st1, st2, instanceData, instanceOffsets,
            cachedProperties, hasWeakPointers, mainRecordIds, recordMap);
    }

    // ── Accesseurs value arrays ───────────────────────────────────────────────

    public sbyte  GetInt8  (int i) => (sbyte)_int8Raw[i];
    public short  GetInt16 (int i) => BitConverter.ToInt16  (_int16Raw, i * 2);
    public int    GetInt32 (int i) => BitConverter.ToInt32  (_int32Raw, i * 4);
    public long   GetInt64 (int i) => BitConverter.ToInt64  (_int64Raw, i * 8);
    public byte   GetUInt8 (int i) => _uint8Raw[i];
    public ushort GetUInt16(int i) => BitConverter.ToUInt16 (_uint16Raw, i * 2);
    public uint   GetUInt32(int i) => BitConverter.ToUInt32 (_uint32Raw, i * 4);
    public ulong  GetUInt64(int i) => BitConverter.ToUInt64 (_uint64Raw, i * 8);
    public bool   GetBool  (int i) => _boolRaw[i] != 0;
    public float  GetSingle(int i) => BitConverter.ToSingle (_singleRaw, i * 4);
    public double GetDouble(int i) => BitConverter.ToDouble (_doubleRaw, i * 8);

    // ── Résolution de chaînes ─────────────────────────────────────────────────

    /// <summary>Résout un offset dans string_table1 (noms de fichiers).</summary>
    public string ResolveString(int offset)
    {
        if (offset < 0 || offset >= _stringTable1.Length) return string.Empty;
        int end = Array.IndexOf(_stringTable1, (byte)0, offset);
        if (end < 0) end = _stringTable1.Length;
        return Encoding.UTF8.GetString(_stringTable1, offset, end - offset);
    }

    /// <summary>Résout un offset dans string_table2 (noms de types/propriétés).</summary>
    public string ResolveString2(int offset)
    {
        if (offset < 0 || offset >= _stringTable2.Length) return string.Empty;
        int end = Array.IndexOf(_stringTable2, (byte)0, offset);
        if (end < 0) end = _stringTable2.Length;
        return Encoding.UTF8.GetString(_stringTable2, offset, end - offset);
    }

    // ── Accès instance data ───────────────────────────────────────────────────

    public ReadOnlySpan<byte> GetInstance(int structIndex, int instanceIndex)
    {
        int size  = (int)StructDefs[structIndex].StructSize;
        int start = _instanceOffsets[structIndex] + instanceIndex * size;
        return _instanceData.AsSpan(start, size);
    }

    // ── Cache de propriétés ───────────────────────────────────────────────────

    public int[] AllPropertyIndices(int structIndex) => _cachedProperties[structIndex];

    // ── Records ───────────────────────────────────────────────────────────────

    public bool IsMainRecord(in DcbRecord record) => _mainRecordIds.Contains(record.Id);

    public DcbRecord? RecordById(CigGuid id)
    {
        if (_recordMap.TryGetValue(id, out int idx))
            return Records[idx];
        return null;
    }

    public bool StructHasWeakPointers(int structIndex) => _hasWeakPointers[structIndex];

    // ── Construction ─────────────────────────────────────────────────────────

    private DcbDatabase(
        StructDef[] structDefs, PropertyDef[] propDefs, EnumDef[] enumDefs,
        DataMapping[] mappings, DcbRecord[] records,
        byte[] int8Raw, byte[] int16Raw, byte[] int32Raw, byte[] int64Raw,
        byte[] uint8Raw, byte[] uint16Raw, byte[] uint32Raw, byte[] uint64Raw,
        byte[] boolRaw, byte[] singleRaw, byte[] doubleRaw,
        CigGuid[] guids, int[] stringIds, int[] localeVals, int[] enumVals,
        DcbPointer[] strongVals, DcbPointer[] weakVals, DcbReference[] refVals,
        int[] enumOpts,
        byte[] st1, byte[] st2, byte[] instanceData, int[] instanceOffsets,
        int[][] cachedProperties, bool[] hasWeakPointers,
        HashSet<CigGuid> mainRecordIds, Dictionary<CigGuid, int> recordMap)
    {
        StructDefs    = structDefs;
        PropertyDefs  = propDefs;
        EnumDefs      = enumDefs;
        DataMappings  = mappings;
        Records       = records;
        _int8Raw = int8Raw; _int16Raw = int16Raw; _int32Raw = int32Raw; _int64Raw = int64Raw;
        _uint8Raw = uint8Raw; _uint16Raw = uint16Raw; _uint32Raw = uint32Raw; _uint64Raw = uint64Raw;
        _boolRaw = boolRaw; _singleRaw = singleRaw; _doubleRaw = doubleRaw;
        GuidValues       = guids;
        StringIdValues   = stringIds;
        LocaleValues     = localeVals;
        EnumValues       = enumVals;
        StrongValues     = strongVals;
        WeakValues       = weakVals;
        ReferenceValues  = refVals;
        _enumOptionsAll  = enumOpts;
        _stringTable1    = st1;
        _stringTable2    = st2;
        _instanceData    = instanceData;
        _instanceOffsets = instanceOffsets;
        _cachedProperties = cachedProperties;
        _hasWeakPointers  = hasWeakPointers;
        _mainRecordIds    = mainRecordIds;
        _recordMap        = recordMap;
    }

    // ── Helpers statiques ────────────────────────────────────────────────────

    private static T[] ReadArray<T>(BinaryReader br, int count, Func<BinaryReader, T> read)
    {
        var arr = new T[count];
        for (int i = 0; i < count; i++)
            arr[i] = read(br);
        return arr;
    }

    private static int[][] BuildPropertyCache(StructDef[] structs, PropertyDef[] props)
    {
        var cache = new int[structs.Length][];
        for (int si = 0; si < structs.Length; si++)
        {
            var s = structs[si];
            if (s.AttributeCount == 0 && s.ParentTypeIndex == -1)
            {
                cache[si] = [];
                continue;
            }

            // Compter le total en remontant la hiérarchie
            int total = s.AttributeCount;
            var walk  = s;
            while (walk.ParentTypeIndex != -1)
            {
                walk   = structs[walk.ParentTypeIndex];
                total += walk.AttributeCount;
            }

            // Remplir dans l'ordre parent-first
            var indices = new int[total];
            int pos     = total;
            var current = s;
            while (true)
            {
                int count = current.AttributeCount;
                pos -= count;
                int first = current.FirstAttributeIndex;
                for (int i = 0; i < count; i++)
                    indices[pos + i] = first + i;

                if (current.ParentTypeIndex == -1) break;
                current = structs[current.ParentTypeIndex];
            }
            cache[si] = indices;
        }
        return cache;
    }

    private static bool[] BuildWeakPointerFlags(StructDef[] structs, PropertyDef[] props, int[][] cached)
    {
        int n = structs.Length;
        var direct = new bool[n];
        var edges  = new List<int>[n];
        for (int i = 0; i < n; i++) edges[i] = [];

        for (int si = 0; si < n; si++)
        {
            foreach (int pi in cached[si])
            {
                var p = props[pi];
                switch (p.DataType)
                {
                    case DataType.WeakPointer:
                    case DataType.Reference:
                        direct[si] = true;
                        break;
                    case DataType.Class:
                    case DataType.StrongPointer:
                        edges[si].Add(p.StructIndex);
                        break;
                }
            }
        }

        // Propagation par point-fixe
        var result  = (bool[])direct.Clone();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int si = 0; si < n; si++)
            {
                if (result[si]) continue;
                foreach (int target in edges[si])
                {
                    if (result[target]) { result[si] = true; changed = true; break; }
                }
            }
        }
        return result;
    }
}
