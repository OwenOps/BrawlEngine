using System.ComponentModel;
using BrawlEngine.Host.Domain.Models;
using BrawlEngine.Host.Infrastructure.Storage;
using System.Diagnostics;

namespace BrawlEngine.Host.Infrastructure.Ffdec;

public static class FfdecLocator
{
    public const string JarFileName = "ffdec_lib.jar";

    public static string StoredJarPath => Path.Combine(AppPaths.Root, JarFileName);

    public static bool HasJar()
    {
        return ResolveJar(AppSettings.Load().FfdecLibPath) is not null;
    }

    public static FfdecToolsDto Resolve()
    {
        var settings = AppSettings.Load();
        var javaPath = ResolveJava(settings.JavaPath);
        var jarPath = ResolveJar(settings.FfdecLibPath);
        return Describe(javaPath, jarPath);
    }

    public static FfdecToolsDto Pick(string kind)
    {
        if (string.Equals(kind, "jar", StringComparison.OrdinalIgnoreCase))
        {
            return PickJar();
        }

        return PickJava();
    }

    private static FfdecToolsDto PickJava()
    {
        var picked = PickFile("Choose java.exe", "java.exe|java.exe");
        if (picked is null)
        {
            return Resolve();
        }

        if (!LooksLikeJava(picked) || !JavaRuns(picked))
        {
            return Describe(null, ResolveJar(AppSettings.Load().FfdecLibPath)) with
            {
                Error = "Choose java.exe from a JRE or JDK.",
            };
        }

        var settings = AppSettings.Load();
        settings.JavaPath = picked;
        settings.Save();
        return Resolve();
    }

    private static FfdecToolsDto PickJar()
    {
        var picked = PickFile("Choose ffdec_lib.jar", "FFDec library|" + JarFileName);
        if (picked is null)
        {
            return Resolve();
        }

        if (!IsFfdecLibJar(picked))
        {
            return Describe(ResolveJava(AppSettings.Load().JavaPath), null) with
            {
                Error = "Choose ffdec_lib.jar (the library, not the FFDec GUI).",
            };
        }

        var settings = AppSettings.Load();
        settings.FfdecLibPath = picked;
        settings.Save();
        return Resolve();
    }

    private static FfdecToolsDto Describe(string? javaPath, string? jarPath)
    {
        var hasJava = !string.IsNullOrEmpty(javaPath);
        var hasJar = !string.IsNullOrEmpty(jarPath);
        if (hasJava && hasJar)
        {
            return new FfdecToolsDto(true, javaPath, jarPath, null);
        }

        if (!hasJava && !hasJar)
        {
            return new FfdecToolsDto(
                false,
                null,
                null,
                "Java and ffdec_lib.jar were not found. Install a JRE (java on PATH). The library downloads automatically — Retry.");
        }

        if (!hasJava)
        {
            return new FfdecToolsDto(
                false,
                null,
                jarPath,
                "Java was not found. Install a JRE and add it to PATH, or choose java.exe.");
        }

        return new FfdecToolsDto(
            false,
            javaPath,
            null,
            "ffdec_lib.jar was not found. Retry to download it.");
    }

    private static string? ResolveJava(string? saved)
    {
        if (LooksLikeJava(saved) && JavaRuns(saved!))
        {
            return saved;
        }

        var home = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            var fromHome = Path.Combine(home, "bin", "java.exe");
            if (LooksLikeJava(fromHome) && JavaRuns(fromHome))
            {
                return fromHome;
            }
        }

        foreach (var candidate in WhereJava())
        {
            if (LooksLikeJava(candidate) && JavaRuns(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveJar(string? saved)
    {
        if (IsFfdecLibJar(saved))
        {
            return saved;
        }

        var nextToApp = Path.Combine(AppContext.BaseDirectory, JarFileName);
        if (IsFfdecLibJar(nextToApp))
        {
            return nextToApp;
        }

        return IsFfdecLibJar(StoredJarPath) ? StoredJarPath : null;
    }

    private static bool LooksLikeJava(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && File.Exists(path)
            && Path.GetFileName(path).Equals("java.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFfdecLibJar(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && File.Exists(path)
            && Path.GetFileName(path).Equals(JarFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool JavaRuns(string javaPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(8000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return false;
            }

            try
            {
                process.StandardError.ReadToEnd();
            }
            catch (InvalidOperationException)
            {
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    private static IEnumerable<string> WhereJava()
    {
        string output;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "java",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                yield break;
            }

            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            yield break;
        }

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            yield return line.Trim();
        }
    }

    private static string? PickFile(string title, string filter)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return PickFileCore(title, filter);
        }

        string? result = null;
        var thread = new Thread(() => result = PickFileCore(title, filter));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static string? PickFileCore(string title, string filter)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
}
