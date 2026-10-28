using System.Buffers.Binary;

namespace Strongthium.FolderSize;

/// <summary>What one record of NTFS's file table says about a file or a folder.</summary>
internal struct RecordInfo
{
    public bool IsDirectory;
    /// <summary>For an extension record (attributes that didn't fit in a file's own record), the file's record;
    /// otherwise 0.</summary>
    public long BaseRecord;
    public long Parent;
    /// <summary>Where its name is in the record, in bytes, and how many characters long; NameSpace is -1 when it has
    /// none.</summary>
    public int NameOffset, NameLength, NameSpace;
    /// <summary>Its main stream: how long it is, and what it takes on the disk.</summary>
    public bool HasData;
    public long Size, Allocated;
    public long LastWrite;
    /// <summary>Its attributes go on in other records.</summary>
    public bool HasAttributeList;
}

/// <summary>
/// Reads the records of NTFS's master file table (the MFT): each file and folder has one, normally 1 KB, made of
/// attributes. Of those, $STANDARD_INFORMATION has its times, $FILE_NAME its name and the folder it's in, and the
/// unnamed $DATA its length and the clusters it takes.
/// </summary>
internal static class FileRecord
{
    public const uint StandardInformation = 0x10, AttributeList = 0x20, FileName = 0x30, Data = 0x80, End = 0xFFFF_FFFF;
    private const int DosNameSpace = 2;
    private const ushort Compressed = 0x0001, Sparse = 0x8000;
    private const long ReferenceMask = 0x0000_FFFF_FFFF_FFFF;   // a file reference is 48 bits of record and 16 of sequence

    /// <summary>
    /// Puts back the last two bytes of every 512, which NTFS replaced with the record's update sequence number when it
    /// wrote it (to tell a record that was only half written). False if one doesn't match: that record is torn.
    /// </summary>
    public static bool ApplyFixups(Span<byte> record)
    {
        int arrayOffset = U16(record, 4), count = U16(record, 6);
        if (count < 2 || arrayOffset + count * 2 > record.Length) return false;
        var sequence = U16(record, arrayOffset);
        for (var i = 1; i < count; i++)
        {
            var end = i * 512 - 2;
            if (end + 2 > record.Length) return false;
            if (U16(record, end) != sequence) return false;
            record[end] = record[arrayOffset + i * 2];
            record[end + 1] = record[arrayOffset + i * 2 + 1];
        }
        return true;
    }

    /// <summary>Reads a record whose fixups are applied. False if it isn't a record in use.</summary>
    public static bool Parse(ReadOnlySpan<byte> record, out RecordInfo info)
    {
        info = default;
        info.NameSpace = -1;
        if (record.Length < 48 || BinaryPrimitives.ReadUInt32LittleEndian(record) != 0x454C_4946) return false;   // "FILE"
        var flags = U16(record, 0x16);
        if ((flags & 1) == 0) return false;
        info.IsDirectory = (flags & 2) != 0;
        info.BaseRecord = I64(record, 0x20) & ReferenceMask;

        var used = Math.Min((int)U32(record, 0x18), record.Length);
        var offset = (int)U16(record, 0x14);
        while (offset + 16 <= used)
        {
            var type = U32(record, offset);
            if (type == End) break;
            var length = (int)U32(record, offset + 4);
            if (length < 16 || offset + length > used) break;
            var attribute = record.Slice(offset, length);
            var resident = attribute[8] == 0;
            var named = attribute[9] != 0;

            switch (type)
            {
                case StandardInformation when resident:
                    if (Value(attribute) is { Length: >= 0x24 } standard) info.LastWrite = I64(standard, 0x08);
                    break;

                case AttributeList:
                    info.HasAttributeList = true;
                    break;

                case FileName when resident:
                    var name = Value(attribute);
                    if (name.Length < 0x42) break;
                    int nameLength = name[0x40], nameSpace = name[0x41];
                    if (0x42 + nameLength * 2 > name.Length) break;
                    // A file has a long name and, often, a short DOS one (PROGRA~1); a hard link has one more for
                    // each folder it's in. The first long name is the one it's counted under.
                    if (info.NameSpace < 0 || (info.NameSpace == DosNameSpace && nameSpace != DosNameSpace))
                    {
                        info.Parent = I64(name, 0) & ReferenceMask;
                        info.NameSpace = nameSpace;
                        info.NameLength = nameLength;
                        info.NameOffset = offset + U16(attribute, 0x14) + 0x42;
                    }
                    break;

                case Data when !named:
                    if (resident)
                    {
                        // Small enough to be kept in the record itself: it takes no clusters, and NTFS counts it in
                        // 8s, as a folder listing says.
                        info.HasData = true;
                        info.Size = U32(attribute, 0x10);
                        info.Allocated = (info.Size + 7) / 8 * 8;
                    }
                    else if (attribute.Length >= 0x40 && I64(attribute, 0x10) == 0)
                    {
                        // The sizes are in the first piece of a stream that's spread over several records.
                        info.HasData = true;
                        info.Size = I64(attribute, 0x30);
                        info.Allocated = (U16(attribute, 0x0C) & (Compressed | Sparse)) != 0 && attribute.Length >= 0x48
                            ? I64(attribute, 0x40)    // what compression or sparseness left of it
                            : I64(attribute, 0x28);
                    }
                    break;
            }
            offset += length;
        }
        return true;
    }

    /// <summary>The run list of the unnamed, non-resident $DATA of a record (the file table's own, in record 0).</summary>
    public static ReadOnlySpan<byte> DataRuns(ReadOnlySpan<byte> record)
    {
        var used = Math.Min((int)U32(record, 0x18), record.Length);
        var offset = (int)U16(record, 0x14);
        while (offset + 16 <= used)
        {
            var type = U32(record, offset);
            if (type == End) break;
            var length = (int)U32(record, offset + 4);
            if (length < 16 || offset + length > used) break;
            var attribute = record.Slice(offset, length);
            if (type == Data && attribute[8] != 0 && attribute[9] == 0 && I64(attribute, 0x10) == 0)
                return attribute[U16(attribute, 0x20)..];
            offset += length;
        }
        return [];
    }

    /// <summary>
    /// Reads a run list: where on the volume a stream's pieces are. Each run is a byte giving the size of its two
    /// numbers (the low four bits for the length, the high four for the start), the length in clusters, and the
    /// start, as a signed distance from the run before's. A run with no start is sparse: clusters never written.
    /// </summary>
    public static List<(long Lcn, long Clusters)> DecodeRuns(ReadOnlySpan<byte> runs)
    {
        var list = new List<(long, long)>();
        long lcn = 0;
        var i = 0;
        while (i < runs.Length && runs[i] != 0)
        {
            int header = runs[i++], lengthSize = header & 0xF, offsetSize = header >> 4;
            if (lengthSize is 0 or > 8 || offsetSize > 8 || i + lengthSize + offsetSize > runs.Length)
                throw new InvalidDataException("A run list that isn't one.");
            var clusters = Number(runs.Slice(i, lengthSize), signed: false);
            i += lengthSize;
            if (offsetSize == 0)
            {
                list.Add((-1, clusters));
                continue;
            }
            lcn += Number(runs.Slice(i, offsetSize), signed: true);
            i += offsetSize;
            list.Add((lcn, clusters));
        }
        return list;
    }

    private static long Number(ReadOnlySpan<byte> bytes, bool signed)
    {
        long value = 0;
        for (var i = 0; i < bytes.Length; i++) value |= (long)bytes[i] << (8 * i);
        if (signed && bytes.Length < 8 && (bytes[^1] & 0x80) != 0) value |= -1L << (8 * bytes.Length);
        return value;
    }

    private static ReadOnlySpan<byte> Value(ReadOnlySpan<byte> attribute)
    {
        var length = (int)U32(attribute, 0x10);
        int offset = U16(attribute, 0x14);
        return offset + length <= attribute.Length ? attribute.Slice(offset, length) : [];
    }

    private static ushort U16(ReadOnlySpan<byte> span, int at) => BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
    private static uint U32(ReadOnlySpan<byte> span, int at) => BinaryPrimitives.ReadUInt32LittleEndian(span[at..]);
    private static long I64(ReadOnlySpan<byte> span, int at) => BinaryPrimitives.ReadInt64LittleEndian(span[at..]);
}
