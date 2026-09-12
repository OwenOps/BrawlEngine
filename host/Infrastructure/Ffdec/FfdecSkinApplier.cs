using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using BrawlEngine.Host.Infrastructure.Apply;
using BrawlEngine.Host.Infrastructure.Storage;

namespace BrawlEngine.Host.Infrastructure.Ffdec;

public static class FfdecSkinApplier
{
    public const string HelperFileName = "SkinApply.java";

    private static readonly ConcurrentDictionary<string, string?> Java11Error = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CompileGate = new();
    private static string? compiledClassDir;
    private static string? compiledStamp;

    public static string? HelperPath()
    {
        var nextToApp = Path.Combine(AppContext.BaseDirectory, HelperFileName);
        return File.Exists(nextToApp) ? nextToApp : null;
    }

    public static string? ReplaceSprites(
        string javaPath,
        string jarPath,
        IReadOnlyList<string> bmodPaths,
        string gameSwfPath,
        string outSwfPath,
        IReadOnlyList<string> exportNames,
        IReadOnlyList<BmodColorScript> colorScripts,
        out int replaced)
    {
        replaced = 0;
        var helper = HelperPath();
        if (helper is null)
        {
            return "SkinApply.java is missing from the app folder. Rebuild the host.";
        }

        if (bmodPaths.Count == 0)
        {
            return "No .bmod found. Download this skin first, then Apply.";
        }

        var versionError = RequireJava11(javaPath);
        if (versionError is not null)
        {
            return versionError;
        }

        var (colorsFile, colorsError) = WriteColorScripts(colorScripts);
        if (colorsError is not null)
        {
            return colorsError;
        }

        var classDir = TryCompile(javaPath, jarPath, helper);
        var start = new ProcessStartInfo
        {
            FileName = javaPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-cp");
        if (classDir is null)
        {
            start.ArgumentList.Add(jarPath);
            start.ArgumentList.Add(helper);
        }
        else
        {
            start.ArgumentList.Add(jarPath + Path.PathSeparator + classDir);
            start.ArgumentList.Add("SkinApply");
        }

        if (colorsFile is not null)
        {
            start.ArgumentList.Add("--colors");
            start.ArgumentList.Add(colorsFile);
        }

        start.ArgumentList.Add(gameSwfPath);
        start.ArgumentList.Add(outSwfPath);
        foreach (var bmodPath in bmodPaths)
        {
            start.ArgumentList.Add(bmodPath);
        }

        start.ArgumentList.Add("--");
        foreach (var name in exportNames)
        {
            start.ArgumentList.Add(name);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return "Could not start Java to apply this skin.";
            }

            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    output.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    output.AppendLine(e.Data);
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(10 * 60 * 1000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return "Java took too long applying this skin.";
            }

            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                replaced = ReadReplacedCount(output.ToString(), exportNames.Count);
                return null;
            }

            var detail = output.ToString().Trim();
            if (string.IsNullOrEmpty(detail))
            {
                return "Java failed while applying this skin (exit " + process.ExitCode + ").";
            }

            return detail.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return "Could not start Java to apply this skin: " + ex.Message;
        }
        finally
        {
            if (colorsFile is not null)
            {
                try
                {
                    File.Delete(colorsFile);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static int ReadReplacedCount(string output, int listed)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            const string prefix = "Replaced ";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var end = line.IndexOf(' ', prefix.Length);
            if (end > prefix.Length
                && int.TryParse(line.AsSpan(prefix.Length, end - prefix.Length), out var count)
                && count >= 0)
            {
                return count;
            }
        }

        return listed;
    }

    /// <summary>
    /// Writes the recolour palettes as one "ClassName=1,2,3" line each. A file keeps the
    /// command line short when a pack ships a palette for every body part.
    /// </summary>
    private static (string? Path, string? Error) WriteColorScripts(
        IReadOnlyList<BmodColorScript> colorScripts)
    {
        if (colorScripts.Count == 0)
        {
            return (null, null);
        }

        var lines = colorScripts
            .Select(script => script.ClassName + "=" + string.Join(",", script.Colors))
            .ToList();
        var path = Path.Combine(Path.GetTempPath(), "BrawlEngine-colors-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            return (path, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, "Could not write the colour list for this skin: " + ex.Message);
        }
    }

    private static string? TryCompile(string javaPath, string jarPath, string helper)
    {
        var stamp = File.GetLastWriteTimeUtc(helper).Ticks.ToString() + "|" + jarPath;
        lock (CompileGate)
        {
            if (compiledClassDir is not null
                && compiledStamp == stamp
                && File.Exists(Path.Combine(compiledClassDir, "SkinApply.class")))
            {
                return compiledClassDir;
            }

            var javac = Path.Combine(Path.GetDirectoryName(javaPath) ?? "", "javac.exe");
            if (!File.Exists(javac))
            {
                return null;
            }

            var classDir = Path.Combine(AppPaths.Root, "ffdec-helper");
            Directory.CreateDirectory(classDir);
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = javac,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    ArgumentList =
                    {
                        "-encoding",
                        "UTF-8",
                        "-cp",
                        jarPath,
                        "-d",
                        classDir,
                        helper,
                    },
                });
                if (process is null)
                {
                    return null;
                }

                process.StandardError.ReadToEnd();
                process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(60000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    return null;
                }

                if (process.ExitCode != 0 || !File.Exists(Path.Combine(classDir, "SkinApply.class")))
                {
                    return null;
                }

                compiledClassDir = classDir;
                compiledStamp = stamp;
                return classDir;
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
            {
                return null;
            }
        }
    }

    private static string? RequireJava11(string javaPath)
    {
        if (Java11Error.TryGetValue(javaPath, out var cached))
        {
            return cached;
        }

        var error = ProbeJava11(javaPath);
        Java11Error[javaPath] = error;
        return error;
    }

    private static string? ProbeJava11(string javaPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return "Could not start Java.";
            }

            var text = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(8000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return "Could not read the Java version.";
            }

            if (!TryMajorVersion(text, out var major) || major < 11)
            {
                return "Skin Apply needs Java 11 or newer. The current java.exe is too old.";
            }

            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return "Could not start Java: " + ex.Message;
        }
    }

    private static bool TryMajorVersion(string text, out int major)
    {
        major = 0;
        var quote = text.IndexOf('"');
        if (quote < 0)
        {
            return false;
        }

        var end = text.IndexOf('"', quote + 1);
        if (end < 0)
        {
            return false;
        }

        var version = text.Substring(quote + 1, end - quote - 1);
        var first = version.Split('.', 2)[0];
        if (first == "1")
        {
            var parts = version.Split('.');
            return parts.Length > 1 && int.TryParse(parts[1], out major);
        }

        return int.TryParse(first, out major);
    }
}
