using System.Diagnostics;
using System.Globalization;
using BrawlEngine.Host.Infrastructure.Storage;
using BrawlEngine.Host.Infrastructure.Wwise;

namespace BrawlEngine.Host.Infrastructure.Apply;

public static class WemEncoder
{
    public static bool IsConvertible(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".flac", StringComparison.OrdinalIgnoreCase);
    }

    public static (int SampleRate, int Channels) ReadLayout(string wemPath)
    {
        var fmt = ReadFmt(wemPath);
        return (fmt.SampleRate, fmt.Channels);
    }

    public static TimeSpan ReadDuration(string wemPath)
    {
        return DurationOf(ParseFmt(File.ReadAllBytes(wemPath)));
    }

    public static TimeSpan ReadDuration(byte[] wem)
    {
        return DurationOf(ParseFmt(wem));
    }

    public static void StampFromVanilla(string wemPath, string vanillaWem)
    {
        if (!File.Exists(wemPath) || !File.Exists(vanillaWem))
        {
            return;
        }

        StampPlayback(wemPath, ReadFmt(vanillaWem));
    }

    public static void ConvertToWem(
        string sourcePath,
        string destWem,
        string vanillaWem,
        TimeSpan? skip = null,
        TimeSpan? take = null)
    {
        ApplyProgress.Note("Checking convert tools…");
        WwiseToolFetch.Ensure();
        var ffmpeg = WwiseToolLocator.ResolveFfmpeg()
            ?? throw new InvalidOperationException("ffmpeg.exe was not found.");
        var wav2wem = WwiseToolLocator.ResolveWav2wem()
            ?? throw new InvalidOperationException("wav2wem.exe was not found.");

        var vanilla = File.Exists(vanillaWem) ? ReadFmt(vanillaWem) : WemFmt.Fallback;
        var work = Path.Combine(AppPaths.Root, "tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var wav = Path.Combine(work, "pcm.wav");
        var encoded = Path.Combine(work, "out.wem");
        try
        {
            ApplyProgress.Note("Decoding to WAV…");
            var decode = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", sourcePath };
            if (skip is { } skipFor && skipFor > TimeSpan.Zero)
            {
                decode.Add("-ss");
                decode.Add(Seconds(skipFor));
            }

            if (take is { } takeFor && takeFor > TimeSpan.Zero)
            {
                decode.Add("-t");
                decode.Add(Seconds(takeFor));
            }

            decode.AddRange(["-ac", vanilla.Channels.ToString(), "-ar", vanilla.SampleRate.ToString(), "-c:a", "pcm_s16le", wav]);
            Run(ffmpeg, decode, TimeSpan.FromMinutes(10));
            ApplyProgress.Note("Encoding Wwise .wem…");
            Run(wav2wem, [wav, "-o", encoded, "-no-hash"], TimeSpan.FromMinutes(10));
            if (!File.Exists(encoded) || new FileInfo(encoded).Length < 64)
            {
                throw new InvalidOperationException("wav2wem did not write a .wem file.");
            }

            // wav2wem writes subtype 0 for stereo; Brawlhalla needs 0x3102 or the loop is silent.
            StampPlayback(encoded, vanilla);

            var destDir = Path.GetDirectoryName(destWem);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(encoded, destWem, overwrite: true);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private readonly record struct WemFmt(
        int SampleRate,
        int Channels,
        uint Subtype,
        uint Samples,
        uint CodebookHash,
        uint DecodeAlloc,
        uint DecodeX64Alloc)
    {
        public static WemFmt Fallback { get; } = new(44100, 2, 0x3102, 0, 0, 19488, 19984);
    }

    private static WemFmt ReadFmt(string wemPath)
    {
        return ParseFmt(File.ReadAllBytes(wemPath));
    }

    private static TimeSpan DurationOf(WemFmt fmt)
    {
        if (fmt.SampleRate <= 0 || fmt.Samples <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(fmt.Samples / (double)fmt.SampleRate);
    }

    private static WemFmt ParseFmt(byte[] bytes)
    {
        var payload = FmtPayloadOffset(bytes);
        if (payload < 0 || payload + 18 > bytes.Length)
        {
            return WemFmt.Fallback;
        }

        var channels = BitConverter.ToUInt16(bytes, payload + 2);
        var rate = BitConverter.ToInt32(bytes, payload + 4);
        if (channels is 0 or > 8)
        {
            channels = 2;
        }

        if (rate is < 8000 or > 192000)
        {
            rate = 44100;
        }

        if (payload + 66 > bytes.Length)
        {
            return new WemFmt(rate, channels, SubtypeFor(channels, 0), 0, 0, 0, 0);
        }

        var subtype = BitConverter.ToUInt32(bytes, payload + 0x14);
        var samples = BitConverter.ToUInt32(bytes, payload + 0x18);
        var codebook = BitConverter.ToUInt32(bytes, payload + 0x3C);
        var decodeAlloc = BitConverter.ToUInt32(bytes, payload + 0x34);
        var decodeX64Alloc = BitConverter.ToUInt32(bytes, payload + 0x38);
        return new WemFmt(
            rate,
            channels,
            SubtypeFor(channels, subtype),
            samples,
            codebook,
            decodeAlloc,
            decodeX64Alloc);
    }

    private static void StampPlayback(string wemPath, WemFmt vanilla)
    {
        var bytes = File.ReadAllBytes(wemPath);
        var payload = FmtPayloadOffset(bytes);
        if (payload < 0 || payload + 66 > bytes.Length)
        {
            return;
        }

        var channels = BitConverter.ToUInt16(bytes, payload + 2);
        WriteU32(bytes, payload + 0x14, SubtypeFor(channels, vanilla.Subtype));

        // Wwise sizes the Vorbis decoder from these two fields. wav2wem writes about four times
        // what Brawlhalla ever ships and the source then never starts (silent theme). The game's
        // own values never vary with packet size, so the vanilla ones are always big enough.
        WriteU32(bytes, payload + 0x34, AllocFor(channels, vanilla.DecodeAlloc, x64: false));
        WriteU32(bytes, payload + 0x38, AllocFor(channels, vanilla.DecodeX64Alloc, x64: true));

        // Do not copy vanilla uHashCodebook. wav2wem packets belong to aoTuV hash 0x20CEF588;
        // stamping D54BA8E8 (or EC69CB18) makes Wwise decode them with the wrong packed tables.

        File.WriteAllBytes(wemPath, bytes);
    }

    private static string Seconds(TimeSpan value)
    {
        return value.TotalSeconds.ToString("G", CultureInfo.InvariantCulture);
    }

    private static uint SubtypeFor(int channels, uint subtype)
    {
        if (subtype != 0)
        {
            return subtype;
        }

        return channels == 1 ? 0x4101u : 0x3102u;
    }

    /// <summary>
    /// Brawlhalla only ever ships two decoder sizes per channel count, and both cover every
    /// packet size in the game. Use the larger one when there is no vanilla .wem to copy from.
    /// </summary>
    private static uint AllocFor(int channels, uint vanilla, bool x64)
    {
        if (vanilla != 0)
        {
            return vanilla;
        }

        if (channels == 1)
        {
            return x64 ? 21400u : 20904u;
        }

        return x64 ? 19984u : 19488u;
    }

    private static int FmtPayloadOffset(byte[] bytes)
    {
        if (bytes.Length < 16
            || bytes[0] != (byte)'R'
            || bytes[1] != (byte)'I'
            || bytes[2] != (byte)'F'
            || bytes[3] != (byte)'F')
        {
            return -1;
        }

        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var size = BitConverter.ToUInt32(bytes, pos + 4);
            if (bytes[pos] == (byte)'f' && bytes[pos + 1] == (byte)'m' && bytes[pos + 2] == (byte)'t')
            {
                return pos + 8;
            }

            var next = pos + 8L + size + (size & 1);
            if (next <= pos || next > bytes.Length)
            {
                break;
            }

            pos = (int)next;
        }

        return -1;
    }

    private static void WriteU32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void Run(string fileName, IReadOnlyList<string> args, TimeSpan timeout)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new InvalidOperationException("Audio convert timed out.");
        }

        Task.WaitAll(stderrTask, stdoutTask);
        if (process.ExitCode != 0)
        {
            var stderr = stderrTask.Result;
            var detail = string.IsNullOrWhiteSpace(stderr) ? "exit " + process.ExitCode : stderr.Trim();
            throw new InvalidOperationException("Audio convert failed: " + detail);
        }
    }
}
