using System.Text;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class WwiseDidx
{
    public static IReadOnlyList<uint> ReadMediaIds(string bnkPath)
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
                var ids = new List<uint>();
                var count = size / 12;
                for (var i = 0; i < count; i++)
                {
                    ids.Add(reader.ReadUInt32());
                    reader.ReadUInt32();
                    reader.ReadUInt32();
                }

                return ids;
            }

            stream.Position = body + size;
        }

        return [];
    }
}
