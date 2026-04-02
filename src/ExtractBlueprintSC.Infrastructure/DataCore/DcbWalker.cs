using System.Text.Json;

namespace ExtractBlueprintSC.Infrastructure.DataCore;

/// <summary>
/// Walker récursif qui reproduit starbreaker-datacore/src/walker.rs.
/// Émet la structure complète d'un record DCB sous forme de JSON.
/// </summary>
internal static class DcbWalker
{
    // ── Point d'entrée public ─────────────────────────────────────────────────

    public static void WalkRecord(DcbDatabase db, in DcbRecord record, Utf8JsonWriter w)
    {
        // Pré-scan des weak pointers si nécessaire
        Dictionary<(int, int), int> weakPointers;
        if (db.StructHasWeakPointers(record.StructIndex))
            weakPointers = PrescanWeakPointers(db, record);
        else
            weakPointers = [];

        var pointedTo = new HashSet<(int, int)>(weakPointers.Keys);

        var ctx = new WalkContext
        {
            WeakPointers  = weakPointers,
            PointedTo     = pointedTo,
            FileNameOffset = record.FileNameOffset,
        };

        string recordName = db.ResolveString2(record.NameOffset2);

        w.WriteStartObject();
        w.WriteString("_RecordName_", recordName);
        w.WriteString("_RecordId_",   record.Id.ToString());

        w.WritePropertyName("_RecordValue_");
        w.WriteStartObject();
        WalkInstance(db, record.StructIndex, record.InstanceIndex, w, ctx);
        w.WriteEndObject();

        // _Pointers_ pour les weak pointer targets non encore visités
        if (ctx.PointedTo.Count > 0)
        {
            var remaining = ctx.PointedTo
                .Select(k => (k, ctx.WeakPointers[k]))
                .OrderBy(t => t.Item2)
                .ToList();

            w.WritePropertyName("_Pointers_");
            w.WriteStartObject();
            foreach (var ((si, ii), id) in remaining)
            {
                w.WritePropertyName($"ptr:{id}");
                w.WriteStartObject();
                string typeName = db.ResolveString2(db.StructDefs[si].NameOffset2);
                w.WriteString("_Type_", typeName);
                var instBytes = db.GetInstance(si, ii);
                int pos = 0;
                WalkStructFields(db, si, instBytes, ref pos, w, ctx);
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }

        w.WriteEndObject();
    }

    // ── Walker récursif ───────────────────────────────────────────────────────

    private static void WalkInstance(
        DcbDatabase db, int structIndex, int instanceIndex,
        Utf8JsonWriter w, WalkContext ctx)
    {
        var instBytes = db.GetInstance(structIndex, instanceIndex);
        int pos       = 0;

        var key = (structIndex, instanceIndex);
        if (ctx.WeakPointers.TryGetValue(key, out int ptrId))
        {
            w.WriteString("_Pointer_", $"ptr:{ptrId}");
            ctx.PointedTo.Remove(key);
        }

        string typeName = db.ResolveString2(db.StructDefs[structIndex].NameOffset2);
        w.WriteString("_Type_", typeName);
        WalkStructFields(db, structIndex, instBytes, ref pos, w, ctx);
    }

    private static void WalkStructFields(
        DcbDatabase db, int structIndex,
        ReadOnlySpan<byte> data, ref int pos,
        Utf8JsonWriter w, WalkContext ctx)
    {
        var propIndices = db.AllPropertyIndices(structIndex);
        var propDefs    = db.PropertyDefs;

        foreach (int pi in propIndices)
        {
            var prop = propDefs[pi];
            string name = db.ResolveString2(prop.NameOffset2);

            if (prop.ConversionType == ConversionType.Attribute)
                WalkAttribute(db, prop.DataType, prop.StructIndex, name, data, ref pos, w, ctx);
            else
                WalkArray(db, prop.DataType, prop.StructIndex, name, data, ref pos, w, ctx);
        }
    }

    private static void WalkAttribute(
        DcbDatabase db, DataType dt, int propStructIndex,
        string name, ReadOnlySpan<byte> data, ref int pos,
        Utf8JsonWriter w, WalkContext ctx)
    {
        switch (dt)
        {
            case DataType.Boolean:
                w.WriteBoolean(name, data[pos] != 0);
                pos += 1;
                break;

            case DataType.SByte:
                w.WriteNumber(name, (sbyte)data[pos]);
                pos += 1;
                break;

            case DataType.Int16:
                w.WriteNumber(name, BitConverter.ToInt16(data.Slice(pos, 2)));
                pos += 2;
                break;

            case DataType.Int32:
                w.WriteNumber(name, BitConverter.ToInt32(data.Slice(pos, 4)));
                pos += 4;
                break;

            case DataType.Int64:
                w.WriteNumber(name, BitConverter.ToInt64(data.Slice(pos, 8)));
                pos += 8;
                break;

            case DataType.Byte:
                w.WriteNumber(name, data[pos]);
                pos += 1;
                break;

            case DataType.UInt16:
                w.WriteNumber(name, BitConverter.ToUInt16(data.Slice(pos, 2)));
                pos += 2;
                break;

            case DataType.UInt32:
                w.WriteNumber(name, BitConverter.ToUInt32(data.Slice(pos, 4)));
                pos += 4;
                break;

            case DataType.UInt64:
                w.WriteNumber(name, BitConverter.ToUInt64(data.Slice(pos, 8)));
                pos += 8;
                break;

            case DataType.Single:
            {
                float v = BitConverter.ToSingle(data.Slice(pos, 4));
                pos += 4;
                if (float.IsFinite(v)) w.WriteNumber(name, v);
                else w.WriteNull(name);
                break;
            }

            case DataType.Double:
            {
                double v = BitConverter.ToDouble(data.Slice(pos, 8));
                pos += 8;
                if (double.IsFinite(v)) w.WriteNumber(name, v);
                else w.WriteNull(name);
                break;
            }

            case DataType.String:
            case DataType.Locale:
            case DataType.EnumChoice:
            {
                int offset = BitConverter.ToInt32(data.Slice(pos, 4));
                pos += 4;
                w.WriteString(name, db.ResolveString(offset));
                break;
            }

            case DataType.Guid:
            {
                var guidMs  = new MemoryStream(data.Slice(pos, 16).ToArray());
                var guidBr  = new BinaryReader(guidMs);
                var guid    = CigGuid.Read(guidBr);
                pos += 16;
                w.WriteString(name, guid.ToString());
                break;
            }

            case DataType.Class:
            {
                w.WritePropertyName(name);
                w.WriteStartObject();
                string typeName = db.ResolveString2(db.StructDefs[propStructIndex].NameOffset2);
                w.WriteString("_Type_", typeName);
                WalkStructFields(db, propStructIndex, data, ref pos, w, ctx);
                w.WriteEndObject();
                break;
            }

            case DataType.StrongPointer:
            {
                var ptr = DcbPointer.ReadFrom(data, pos);
                pos += 8;
                if (ptr.IsNull)
                {
                    w.WriteNull(name);
                }
                else
                {
                    w.WritePropertyName(name);
                    w.WriteStartObject();
                    WalkInstance(db, ptr.StructIndex, ptr.InstanceIndex, w, ctx);
                    w.WriteEndObject();
                }
                break;
            }

            case DataType.WeakPointer:
            {
                var ptr = DcbPointer.ReadFrom(data, pos);
                pos += 8;
                if (ptr.IsNull)
                {
                    w.WriteNull(name);
                }
                else
                {
                    var key = (ptr.StructIndex, ptr.InstanceIndex);
                    if (ctx.WeakPointers.TryGetValue(key, out int id))
                        w.WriteString(name, $"_PointsTo_:ptr:{id}");
                    else
                        w.WriteNull(name);
                }
                break;
            }

            case DataType.Reference:
            {
                var refVal = DcbReference.ReadFrom(data, pos);
                pos += 20;
                ResolveReference(db, refVal, name, w, ctx);
                break;
            }
        }
    }

    private static void WalkArray(
        DcbDatabase db, DataType dt, int propStructIndex,
        string name, ReadOnlySpan<byte> data, ref int pos,
        Utf8JsonWriter w, WalkContext ctx)
    {
        int count      = BitConverter.ToInt32(data.Slice(pos, 4));
        int firstIndex = BitConverter.ToInt32(data.Slice(pos + 4, 4));
        pos += 8;

        w.WritePropertyName(name);
        w.WriteStartArray();

        for (int i = firstIndex; i < firstIndex + count; i++)
        {
            switch (dt)
            {
                case DataType.Boolean:      w.WriteBooleanValue(db.GetBool(i));    break;
                case DataType.SByte:        w.WriteNumberValue (db.GetInt8(i));    break;
                case DataType.Int16:        w.WriteNumberValue (db.GetInt16(i));   break;
                case DataType.Int32:        w.WriteNumberValue (db.GetInt32(i));   break;
                case DataType.Int64:        w.WriteNumberValue (db.GetInt64(i));   break;
                case DataType.Byte:         w.WriteNumberValue (db.GetUInt8(i));   break;
                case DataType.UInt16:       w.WriteNumberValue (db.GetUInt16(i));  break;
                case DataType.UInt32:       w.WriteNumberValue (db.GetUInt32(i));  break;
                case DataType.UInt64:       w.WriteNumberValue (db.GetUInt64(i));  break;
                case DataType.Single:
                {
                    float v = db.GetSingle(i);
                    if (float.IsFinite(v)) w.WriteNumberValue(v);
                    else w.WriteNullValue();
                    break;
                }
                case DataType.Double:
                {
                    double v = db.GetDouble(i);
                    if (double.IsFinite(v)) w.WriteNumberValue(v);
                    else w.WriteNullValue();
                    break;
                }
                case DataType.String:
                    w.WriteStringValue(db.ResolveString(db.StringIdValues[i]));
                    break;
                case DataType.Locale:
                    w.WriteStringValue(db.ResolveString(db.LocaleValues[i]));
                    break;
                case DataType.EnumChoice:
                    w.WriteStringValue(db.ResolveString(db.EnumValues[i]));
                    break;
                case DataType.Guid:
                    w.WriteStringValue(db.GuidValues[i].ToString());
                    break;

                case DataType.Class:
                {
                    w.WriteStartObject();
                    WalkInstance(db, propStructIndex, i, w, ctx);
                    w.WriteEndObject();
                    break;
                }

                case DataType.StrongPointer:
                {
                    var ptr = db.StrongValues[i];
                    if (ptr.IsNull)
                    {
                        w.WriteNullValue();
                    }
                    else
                    {
                        w.WriteStartObject();
                        WalkInstance(db, ptr.StructIndex, ptr.InstanceIndex, w, ctx);
                        w.WriteEndObject();
                    }
                    break;
                }

                case DataType.WeakPointer:
                {
                    var ptr = db.WeakValues[i];
                    if (ptr.IsNull)
                    {
                        w.WriteNullValue();
                    }
                    else
                    {
                        var key = (ptr.StructIndex, ptr.InstanceIndex);
                        if (ctx.WeakPointers.TryGetValue(key, out int id))
                            w.WriteStringValue($"_PointsTo_:ptr:{id}");
                        else
                            w.WriteNullValue();
                    }
                    break;
                }

                case DataType.Reference:
                {
                    var refVal = db.ReferenceValues[i];
                    ResolveReference(db, refVal, null, w, ctx);
                    break;
                }
            }
        }

        w.WriteEndArray();
    }

    // ── Résolution de références ──────────────────────────────────────────────

    private static void ResolveReference(
        DcbDatabase db, DcbReference reference,
        string? name, Utf8JsonWriter w, WalkContext ctx)
    {
        if (reference.IsNull)
        {
            WriteNullOrProperty(w, name);
            return;
        }

        var target = db.RecordById(reference.RecordId);
        if (target is null)
        {
            WriteNullOrProperty(w, name);
            return;
        }

        string targetFileName  = db.ResolveString(target.Value.FileNameOffset);
        string contextFileName = db.ResolveString(ctx.FileNameOffset);
        string targetRecordName = db.ResolveString2(target.Value.NameOffset2);

        if (db.IsMainRecord(target.Value))
        {
            // Référence vers un record principal : émettre le chemin relatif
            string path = ComputeRelativePath(targetFileName, contextFileName);
            path = ChangeExtension(path, "json");
            if (name != null) w.WriteString(name, path);
            else w.WriteStringValue(path);
            return;
        }

        if (target.Value.FileNameOffset == ctx.FileNameOffset)
        {
            // Même fichier : émettre inline
            if (name != null) w.WritePropertyName(name);
            w.WriteStartObject();
            w.WriteString("_RecordId_",   reference.RecordId.ToString());
            w.WriteString("_RecordName_", targetRecordName);
            WalkInstance(db, target.Value.StructIndex, target.Value.InstanceIndex, w, ctx);
            w.WriteEndObject();
            return;
        }

        // Fichier différent : émettre chemin + métadonnées
        if (name != null) w.WritePropertyName(name);
        w.WriteStartObject();
        string crossPath = ChangeExtension(ComputeRelativePath(targetFileName, contextFileName), "json");
        w.WriteString("_RecordPath_",  crossPath);
        w.WriteString("_RecordName_",  targetRecordName);
        w.WriteString("_RecordId_",    reference.RecordId.ToString());
        w.WriteEndObject();
    }

    private static void WriteNullOrProperty(Utf8JsonWriter w, string? name)
    {
        if (name != null) w.WriteNull(name);
        else w.WriteNullValue();
    }

    // ── Pré-scan weak pointers ────────────────────────────────────────────────

    private static Dictionary<(int, int), int> PrescanWeakPointers(DcbDatabase db, in DcbRecord record)
    {
        var map   = new Dictionary<(int, int), int>();
        var bytes = db.GetInstance(record.StructIndex, record.InstanceIndex);
        int pos   = 0;
        PrescanStruct(db, record.StructIndex, bytes, ref pos, map, record.FileNameOffset);
        return map;
    }

    private static void PrescanStruct(
        DcbDatabase db, int structIndex,
        ReadOnlySpan<byte> data, ref int pos,
        Dictionary<(int, int), int> map, int fileNameOffset)
    {
        var propIndices = db.AllPropertyIndices(structIndex);
        var propDefs    = db.PropertyDefs;

        foreach (int pi in propIndices)
        {
            var prop = propDefs[pi];
            if (prop.ConversionType == ConversionType.Attribute)
                PrescanAttribute(db, prop.DataType, prop.StructIndex, data, ref pos, map, fileNameOffset);
            else
                PrescanArray(db, prop.DataType, prop.StructIndex, data, ref pos, map, fileNameOffset);
        }
    }

    private static void PrescanAttribute(
        DcbDatabase db, DataType dt, int propStructIndex,
        ReadOnlySpan<byte> data, ref int pos,
        Dictionary<(int, int), int> map, int fileNameOffset)
    {
        switch (dt)
        {
            case DataType.WeakPointer:
            {
                var ptr = DcbPointer.ReadFrom(data, pos);
                pos += 8;
                if (!ptr.IsNull)
                {
                    var key = (ptr.StructIndex, ptr.InstanceIndex);
                    if (!map.ContainsKey(key))
                        map[key] = map.Count + 1;
                }
                break;
            }
            case DataType.StrongPointer:
            {
                var ptr = DcbPointer.ReadFrom(data, pos);
                pos += 8;
                if (!ptr.IsNull)
                {
                    var subData = db.GetInstance(ptr.StructIndex, ptr.InstanceIndex);
                    int subPos  = 0;
                    PrescanStruct(db, ptr.StructIndex, subData, ref subPos, map, fileNameOffset);
                }
                break;
            }
            case DataType.Reference:
            {
                var refVal = DcbReference.ReadFrom(data, pos);
                pos += 20;
                if (!refVal.IsNull)
                {
                    var target = db.RecordById(refVal.RecordId);
                    if (target is not null && !db.IsMainRecord(target.Value)
                        && target.Value.FileNameOffset == fileNameOffset)
                    {
                        var subData = db.GetInstance(target.Value.StructIndex, target.Value.InstanceIndex);
                        int subPos  = 0;
                        PrescanStruct(db, target.Value.StructIndex, subData, ref subPos, map, fileNameOffset);
                    }
                }
                break;
            }
            case DataType.Class:
                PrescanStruct(db, propStructIndex, data, ref pos, map, fileNameOffset);
                break;
            default:
                pos += dt.InlineSize();
                break;
        }
    }

    private static void PrescanArray(
        DcbDatabase db, DataType dt, int propStructIndex,
        ReadOnlySpan<byte> data, ref int pos,
        Dictionary<(int, int), int> map, int fileNameOffset)
    {
        int count      = BitConverter.ToInt32(data.Slice(pos, 4));
        int firstIndex = BitConverter.ToInt32(data.Slice(pos + 4, 4));
        pos += 8;

        for (int i = firstIndex; i < firstIndex + count; i++)
        {
            switch (dt)
            {
                case DataType.WeakPointer:
                {
                    var ptr = db.WeakValues[i];
                    if (!ptr.IsNull)
                    {
                        var key = (ptr.StructIndex, ptr.InstanceIndex);
                        if (!map.ContainsKey(key))
                            map[key] = map.Count + 1;
                    }
                    break;
                }
                case DataType.StrongPointer:
                {
                    var ptr = db.StrongValues[i];
                    if (!ptr.IsNull)
                    {
                        var subData = db.GetInstance(ptr.StructIndex, ptr.InstanceIndex);
                        int subPos  = 0;
                        PrescanStruct(db, ptr.StructIndex, subData, ref subPos, map, fileNameOffset);
                    }
                    break;
                }
                case DataType.Reference:
                {
                    var refVal = db.ReferenceValues[i];
                    if (!refVal.IsNull)
                    {
                        var target = db.RecordById(refVal.RecordId);
                        if (target is not null && !db.IsMainRecord(target.Value)
                            && target.Value.FileNameOffset == fileNameOffset)
                        {
                            var subData = db.GetInstance(target.Value.StructIndex, target.Value.InstanceIndex);
                            int subPos  = 0;
                            PrescanStruct(db, target.Value.StructIndex, subData, ref subPos, map, fileNameOffset);
                        }
                    }
                    break;
                }
                case DataType.Class:
                {
                    var subData = db.GetInstance(propStructIndex, i);
                    int subPos  = 0;
                    PrescanStruct(db, propStructIndex, subData, ref subPos, map, fileNameOffset);
                    break;
                }
            }
        }
    }

    // ── Helpers de chemin ─────────────────────────────────────────────────────

    private static string ComputeRelativePath(string targetFile, string contextFile)
    {
        int slashes = contextFile.Count(c => c == '/');
        var sb = new System.Text.StringBuilder("file://./");
        for (int i = 0; i < slashes; i++) sb.Append("../");
        sb.Append(targetFile);
        return sb.ToString();
    }

    private static string ChangeExtension(string path, string ext)
    {
        int lastSlash = path.LastIndexOf('/') + 1;
        int dot = path.IndexOf('.', lastSlash);
        if (dot >= 0)
            return path[..dot] + '.' + ext;
        return path + '.' + ext;
    }

    // ── Contexte de traversée ─────────────────────────────────────────────────

    private sealed class WalkContext
    {
        public required Dictionary<(int, int), int> WeakPointers { get; init; }
        public required HashSet<(int, int)>          PointedTo   { get; init; }
        public required int                          FileNameOffset { get; init; }
    }
}
