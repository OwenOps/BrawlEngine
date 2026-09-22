using System.Text;

namespace BrawlEngine.Host.Infrastructure.Apply;

/// <summary>
/// Brawlhalla music plays from the DATA chunk inside MUS_*.bnk, not the loose numbered .wem.
/// Replace that blob (full RIFF .wem) and rebuild DIDX offsets.
/// </summary>
public static class WwiseBankPatch
{
    private const int Align = 16;

    public static bool ReplaceMedia(string bnkPath, uint mediaId, byte[] wem, double durationMs = 0)
    {
        if (wem.Length < 12)
        {
            throw new InvalidOperationException("The replacement .wem is empty.");
        }

        var chunks = ReadChunks(bnkPath);
        var didxIndex = chunks.FindIndex(chunk => chunk.Tag == "DIDX");
        var dataIndex = chunks.FindIndex(chunk => chunk.Tag == "DATA");
        if (didxIndex < 0 || dataIndex < 0)
        {
            return false;
        }

        var entries = ParseDidx(chunks[didxIndex].Payload);
        var data = chunks[dataIndex].Payload;
        var rebuilt = new List<(uint Id, byte[] Blob)>(entries.Count);
        var found = false;
        foreach (var entry in entries)
        {
            if (entry.Offset + entry.Size > data.Length)
            {
                throw new InvalidOperationException("The sound bank DIDX is out of range.");
            }

            byte[] blob;
            if (entry.Id == mediaId)
            {
                blob = wem;
                found = true;
            }
            else
            {
                blob = data.AsSpan((int)entry.Offset, (int)entry.Size).ToArray();
            }

            rebuilt.Add((entry.Id, blob));
        }

        if (!found)
        {
            return false;
        }

        var (newDidx, newData) = Pack(rebuilt);
        chunks[didxIndex] = new Chunk("DIDX", newDidx);
        chunks[dataIndex] = new Chunk("DATA", newData);
        var hircIndex = chunks.FindIndex(chunk => chunk.Tag == "HIRC");
        if (hircIndex >= 0)
        {
            var hirc = chunks[hircIndex].Payload;
            PatchHirc(hirc, mediaId, (uint)wem.Length, durationMs);
            chunks[hircIndex] = new Chunk("HIRC", hirc);
        }

        WriteChunks(bnkPath, chunks);
        return true;
    }

    public static byte[]? ReadMedia(string bnkPath, uint mediaId)
    {
        var chunks = ReadChunks(bnkPath);
        var didxIndex = chunks.FindIndex(chunk => chunk.Tag == "DIDX");
        var dataIndex = chunks.FindIndex(chunk => chunk.Tag == "DATA");
        if (didxIndex < 0 || dataIndex < 0)
        {
            return null;
        }

        var data = chunks[dataIndex].Payload;
        foreach (var entry in ParseDidx(chunks[didxIndex].Payload))
        {
            if (entry.Id != mediaId || entry.Offset + entry.Size > data.Length)
            {
                continue;
            }

            return data.AsSpan((int)entry.Offset, (int)entry.Size).ToArray();
        }

        return null;
    }

    /// <summary>
    /// Wwise 2017.2 (bank v128): Vorbis source is plugin 0x00040001, stream type, media id, in-memory size.
    /// Playlist clip duration sits 24 bytes after the same media id (three doubles).
    /// </summary>
    private static void PatchHirc(byte[] hirc, uint mediaId, uint byteSize, double durationMs)
    {
        for (var i = 0; i + 13 <= hirc.Length; i++)
        {
            if (hirc[i] != 0x01 || hirc[i + 1] != 0x00 || hirc[i + 2] != 0x04 || hirc[i + 3] != 0x00)
            {
                continue;
            }

            if (hirc[i + 4] > 2)
            {
                continue;
            }

            if (BitConverter.ToUInt32(hirc, i + 5) != mediaId)
            {
                continue;
            }

            WriteU32(hirc, i + 9, byteSize);
        }

        if (durationMs <= 0)
        {
            return;
        }

        for (var i = 4; i + 36 <= hirc.Length; i++)
        {
            if (BitConverter.ToUInt32(hirc, i) != mediaId)
            {
                continue;
            }

            if (BitConverter.ToUInt32(hirc, i - 4) > 16)
            {
                continue;
            }

            var playAt = BitConverter.ToDouble(hirc, i + 4);
            if (playAt is < 0 or > 1)
            {
                continue;
            }

            var begin = BitConverter.ToDouble(hirc, i + 12);
            var end = BitConverter.ToDouble(hirc, i + 20);
            if (begin < -1 || end < -1 || begin > 1_000_000 || end > 1_000_000)
            {
                continue;
            }

            var duration = BitConverter.GetBytes(durationMs);
            Buffer.BlockCopy(duration, 0, hirc, i + 28, 8);
        }
    }

    private static void WriteU32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static (byte[] Didx, byte[] Data) Pack(List<(uint Id, byte[] Blob)> entries)
    {
        using var data = new MemoryStream();
        using var didx = new MemoryStream();
        using var didxWriter = new BinaryWriter(didx);
        for (var i = 0; i < entries.Count; i++)
        {
            if (data.Length > 0)
            {
                var pad = (int)((Align - (data.Length % Align)) % Align);
                if (pad > 0)
                {
                    data.Write(new byte[pad]);
                }
            }

            var (id, blob) = entries[i];
            didxWriter.Write(id);
            didxWriter.Write((uint)data.Length);
            didxWriter.Write((uint)blob.Length);
            data.Write(blob);
        }

        return (didx.ToArray(), data.ToArray());
    }

    private static List<DidxEntry> ParseDidx(byte[] payload)
    {
        var list = new List<DidxEntry>(payload.Length / 12);
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);
        while (stream.Position + 12 <= stream.Length)
        {
            list.Add(new DidxEntry(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()));
        }

        return list;
    }

    private static List<Chunk> ReadChunks(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var chunks = new List<Chunk>();
        while (stream.Position + 8 <= stream.Length)
        {
            var tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadUInt32();
            if (stream.Position + size > stream.Length)
            {
                break;
            }

            chunks.Add(new Chunk(tag, reader.ReadBytes((int)size)));
        }

        return chunks;
    }

    private static void WriteChunks(string path, List<Chunk> chunks)
    {
        var part = path + ".part";
        using (var stream = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(stream))
        {
            foreach (var chunk in chunks)
            {
                var tag = Encoding.ASCII.GetBytes(chunk.Tag);
                if (tag.Length != 4)
                {
                    throw new InvalidOperationException("Invalid Wwise chunk tag.");
                }

                writer.Write(tag);
                writer.Write((uint)chunk.Payload.Length);
                writer.Write(chunk.Payload);
            }
        }

        File.Copy(part, path, overwrite: true);
        File.Delete(part);
    }

    private readonly record struct Chunk(string Tag, byte[] Payload);

    private readonly record struct DidxEntry(uint Id, uint Offset, uint Size);
}
