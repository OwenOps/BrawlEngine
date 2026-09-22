using System.Text;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class WwiseDidx
{
    public readonly record struct MediaIndex(uint Id, uint Offset, uint Size);

    public static IReadOnlyList<uint> ReadMediaIds(string bnkPath)
    {
        return ReadEntries(bnkPath).Select(entry => entry.Id).ToList();
    }

    public static IReadOnlyList<MediaIndex> ReadEntries(string bnkPath)
    {
        using var stream = File.OpenRead(bnkPath);
        using var reader = new BinaryReader(stream);
        while (stream.Position + 8 <= stream.Length)
        {
            var tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadUInt32();
            var body = stream.Position;
            if (body + size > stream.Length)
            {
                break;
            }

            if (tag == "DIDX")
            {
                var list = new List<MediaIndex>();
                var count = size / 12;
                for (var i = 0; i < count; i++)
                {
                    list.Add(new MediaIndex(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()));
                }

                return list;
            }

            stream.Position = body + size;
        }

        return [];
    }
}
