using System.Buffers.Binary;
using System.Text;

namespace Strongthium.FolderSize.Tests;

/// <summary>File table records made byte by byte, as NTFS writes them.</summary>
public sealed class FileRecordTests
{
    private const int RecordSize = 1024;
    private const ushort Sequence = 0x0007;

    /// <summary>A record in use with the given attributes, its update sequence applied as NTFS would on disk.</summary>
    private static byte[] Record(bool directory, long baseRecord, params byte[][] attributes)
    {
        var record = new byte[RecordSize];
        Encoding.ASCII.GetBytes("FILE").CopyTo(record, 0);
        U16(record, 0x04, 0x30);                 // update sequence array
        U16(record, 0x06, 3);                    // the number, then one for each 512 bytes
        U16(record, 0x14, 0x38);                 // first attribute
        U16(record, 0x16, (ushort)(directory ? 3 : 1));
        I64(record, 0x20, baseRecord);
        var offset = 0x38;
        foreach (var attribute in attributes)
        {
            attribute.CopyTo(record, offset);
            offset += attribute.Length;
        }
        U32(record, offset, FileRecord.End);
        U32(record, 0x18, (uint)(offset + 8));   // bytes in use
        U32(record, 0x1C, RecordSize);

        // The last two bytes of each 512 go into the array, and the sequence number in their place.
        U16(record, 0x30, Sequence);
        for (var i = 1; i <= 2; i++)
        {
            var end = i * 512 - 2;
            record[0x30 + i * 2] = record[end];
            record[0x30 + i * 2 + 1] = record[end + 1];
            U16(record, end, Sequence);
        }
        return record;
    }

    private static byte[] Resident(uint type, byte[] value, string streamName = "")
    {
        var nameBytes = Encoding.Unicode.GetBytes(streamName);
        var valueOffset = Align(0x18 + nameBytes.Length);
        var attribute = new byte[Align(valueOffset + value.Length)];
        U32(attribute, 0, type);
        U32(attribute, 4, (uint)attribute.Length);
        attribute[9] = (byte)streamName.Length;
        U16(attribute, 0x0A, 0x18);
        nameBytes.CopyTo(attribute, 0x18);
        U32(attribute, 0x10, (uint)value.Length);
        U16(attribute, 0x14, (ushort)valueOffset);
        value.CopyTo(attribute, valueOffset);
        return attribute;
    }

    private static byte[] NonResidentData(long size, long allocated, long lowestVcn = 0, ushort flags = 0, long compressed = 0, byte[]? runs = null)
    {
        runs ??= [0x11, 0x02, 0x20, 0x00];
        var attribute = new byte[Align(0x48 + runs.Length)];
        U32(attribute, 0, FileRecord.Data);
        U32(attribute, 4, (uint)attribute.Length);
        attribute[8] = 1;
        U16(attribute, 0x0C, flags);
        I64(attribute, 0x10, lowestVcn);
        U16(attribute, 0x20, 0x48);
        I64(attribute, 0x28, allocated);
        I64(attribute, 0x30, size);
        I64(attribute, 0x38, size);
        I64(attribute, 0x40, compressed);
        runs.CopyTo(attribute, 0x48);
        return attribute;
    }

    private static byte[] StandardInformation(DateTime written)
    {
        var value = new byte[0x48];
        I64(value, 0x08, written.ToFileTimeUtc());
        return Resident(FileRecord.StandardInformation, value);
    }

    private static byte[] FileName(long parent, string name, byte nameSpace)
    {
        var value = new byte[0x42 + name.Length * 2];
        I64(value, 0, parent | (3L << 48));      // the parent's sequence number is in the top 16 bits
        value[0x40] = (byte)name.Length;
        value[0x41] = nameSpace;
        Encoding.Unicode.GetBytes(name).CopyTo(value, 0x42);
        return Resident(FileRecord.FileName, value);
    }

    private static string Name(byte[] record, RecordInfo info) => Encoding.Unicode.GetString(record, info.NameOffset, info.NameLength * 2);

    [Fact]
    public void ReadsAFile()
    {
        var written = new DateTime(2026, 5, 13, 9, 30, 0, DateTimeKind.Utc);
        var record = Record(false, 0,
            StandardInformation(written),
            FileName(1234, "REPORT~1.PDF", 2),
            FileName(1234, "Quarterly report.pdf", 1),
            Resident(FileRecord.Data, new byte[10], "Zone.Identifier"),
            NonResidentData(size: 5_000, allocated: 8_192));

        Assert.True(FileRecord.ApplyFixups(record));
        Assert.True(FileRecord.Parse(record, out var info));
        Assert.False(info.IsDirectory);
        Assert.Equal(0, info.BaseRecord);
        Assert.Equal(1234, info.Parent);
        Assert.Equal("Quarterly report.pdf", Name(record, info));
        Assert.True(info.HasData);
        Assert.Equal(5_000, info.Size);
        Assert.Equal(8_192, info.Allocated);
        Assert.Equal(written, Native.Time(info.LastWrite));
    }

    [Fact]
    public void KeepsTheFirstLongNameOfAHardLink()
    {
        var record = Record(false, 0, FileName(40, "a.txt", 1), FileName(41, "b.txt", 0), Resident(FileRecord.Data, new byte[300]));
        Assert.True(FileRecord.ApplyFixups(record));
        Assert.True(FileRecord.Parse(record, out var info));
        Assert.Equal(40, info.Parent);
        Assert.Equal("a.txt", Name(record, info));
        // Kept in the record: no clusters, counted in 8s.
        Assert.Equal(300, info.Size);
        Assert.Equal(304, info.Allocated);
    }

    [Fact]
    public void ReadsAFolder()
    {
        var record = Record(true, 0, FileName(5, "Users", 3));
        Assert.True(FileRecord.ApplyFixups(record));
        Assert.True(FileRecord.Parse(record, out var info));
        Assert.True(info.IsDirectory);
        Assert.Equal(5, info.Parent);
        Assert.False(info.HasData);
    }

    [Fact]
    public void TakesACompressedFilesClustersFromWhatsLeft()
    {
        var record = Record(false, 0, FileName(5, "log.txt", 1), NonResidentData(size: 1_000_000, allocated: 1_003_520, flags: 0x0001, compressed: 262_144));
        Assert.True(FileRecord.ApplyFixups(record));
        Assert.True(FileRecord.Parse(record, out var info));
        Assert.Equal(1_000_000, info.Size);
        Assert.Equal(262_144, info.Allocated);
    }

    [Fact]
    public void LeavesSizesToTheFirstPieceOfAStream()
    {
        var record = Record(false, 77, NonResidentData(size: 0, allocated: 0, lowestVcn: 4096));
        Assert.True(FileRecord.ApplyFixups(record));
        Assert.True(FileRecord.Parse(record, out var info));
        Assert.Equal(77, info.BaseRecord);
        Assert.False(info.HasData);
        Assert.Equal(-1, info.NameSpace);
    }

    [Fact]
    public void SkipsRecordsNotInUse()
    {
        var record = Record(false, 0, FileName(5, "gone.txt", 1));
        record[0x16] = 0;
        Assert.True(FileRecord.ApplyFixups(record));
        Assert.False(FileRecord.Parse(record, out _));
        Assert.False(FileRecord.Parse(new byte[RecordSize], out _));
    }

    [Fact]
    public void FindsATornRecord()
    {
        var record = Record(false, 0, FileName(5, "torn.txt", 1));
        U16(record, 1022, 0x1234);   // the second half was written later than the first
        Assert.False(FileRecord.ApplyFixups(record));
    }

    [Fact]
    public void PutsBackTheBytesUnderTheSequenceNumber()
    {
        // A long name runs over the first 512 bytes, so that the fixup lands inside it.
        var name = new string('x', 230) + ".bin";
        var record = Record(false, 0, StandardInformation(DateTime.UtcNow), FileName(5, name, 1));
        Assert.Equal(Sequence, BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(510)));
        Assert.True(FileRecord.ApplyFixups(record));
        Assert.True(FileRecord.Parse(record, out var info));
        Assert.Equal(name, Name(record, info));
    }

    [Fact]
    public void DecodesARunList()
    {
        byte[] runs =
        [
            0x31, 0x10, 0x00, 0x40, 0x00,   // 16 clusters at 16384
            0x21, 0x08, 0xF0, 0xFF,         // 8 clusters, 16 back: at 16368
            0x01, 0x04,                     // 4 clusters never written
            0x11, 0x02, 0x20,               // 2 clusters, 32 on from 16368
            0x00,
        ];
        Assert.Equal([(16384L, 16L), (16368L, 8L), (-1L, 4L), (16400L, 2L)], FileRecord.DecodeRuns(runs));
    }

    [Fact]
    public void FindsTheFileTablesOwnRuns()
    {
        var record = Record(false, 0, FileName(5, "$MFT", 3), NonResidentData(size: 1 << 20, allocated: 1 << 20, runs: [0x21, 0x40, 0x00, 0x30, 0x00]));
        Assert.True(FileRecord.ApplyFixups(record));
        Assert.Equal([(0x3000L, 0x40L)], FileRecord.DecodeRuns(FileRecord.DataRuns(record)));
    }

    private static int Align(int value) => (value + 7) & ~7;
    private static void U16(byte[] b, int at, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), v);
    private static void U32(byte[] b, int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v);
    private static void I64(byte[] b, int at, long v) => BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(at), v);
}
