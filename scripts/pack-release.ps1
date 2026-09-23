# Pack a self-contained Windows zip (repo root).
# Does not include Maps / sounds / skins / mods (those live in LocalAppData).
# Zip layout: BrawlEngine.lnk + app\
# Do not set IconLocation: a relative .ico is ignored and Explorer shows a blank page.
# The shortcut target is the published exe; Explorer uses that file's icon.

param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $csproj = Get-Content (Join-Path $root "host\BrawlEngine.Host.csproj") -Raw
    if ($csproj -match "<Version>([^<]+)</Version>") {
        $Version = $Matches[1].Trim()
    }
    else {
        $Version = "0.0.0"
    }
}

$ui = Join-Path $root "ui"
$hostDir = Join-Path $root "host"
$dist = Join-Path $root "dist"
$stageName = "BrawlEngine-$Version-win-x64"
$stage = Join-Path $dist $stageName
$app = Join-Path $stage "app"
$zip = Join-Path $dist "$stageName.zip"

Write-Host "UI build…"
Push-Location $ui
try {
    npx ng build
    if ($LASTEXITCODE -ne 0) {
        throw "ng build failed"
    }
}
finally {
    Pop-Location
}

if (Test-Path $stage) {
    Remove-Item $stage -Recurse -Force
}

Write-Host "dotnet publish → $app"
Push-Location $hostDir
try {
    dotnet publish -c Release -r win-x64 --self-contained true -p:DebugType=None -o $app
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed"
    }
}
finally {
    Pop-Location
}

foreach ($name in @("Maps", "sounds", "skins", "mods", "library", "backups")) {
    $junk = Join-Path $app $name
    if (Test-Path $junk) {
        Write-Host "Removing $name from the zip (user downloads do not ship)."
        Remove-Item $junk -Recurse -Force
    }
}

Get-ChildItem $app -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

$lnkPath = Join-Path $stage "BrawlEngine.lnk"
$exePath = Join-Path $app "BrawlEngine.exe"

if (-not ("BrawlEnginePack.PortableShortcut" -as [type])) {
    Add-Type -ReferencedAssemblies @("System.IO.Compression", "System.IO.Compression.FileSystem") -TypeDefinition @"
using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace BrawlEnginePack {

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
public class ShellLink { }

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
public interface IShellLinkW {
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
    void Resolve(IntPtr hwnd, int fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("0000010b-0000-0000-C000-000000000046")]
public interface IPersistFile {
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile(out IntPtr ppszFileName);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("45e2b4ae-b1c3-11d0-b92f-00a0c90312e1")]
public interface IShellLinkDataList {
    void AddDataBlock(IntPtr pDataBlock);
    void CopyDataBlock(uint dwSig, out IntPtr ppDataBlock);
    void RemoveDataBlock(uint dwSig);
    void GetFlags(out uint pdwFlags);
    void SetFlags(uint dwFlags);
}

public static class PortableShortcut {
    const uint SLDF_FORCE_NO_LINKINFO = 0x00000100;
    const uint SLDF_FORCE_NO_LINKTRACK = 0x00040000;

    public static void Write(string lnkPath, string targetExe, string workDir) {
        var com = new ShellLink();
        try {
            var link = (IShellLinkW)com;
            link.SetPath(targetExe);
            link.SetWorkingDirectory(workDir);
            link.SetDescription("BrawlEngine");
            link.SetShowCmd(1);
            link.SetRelativePath(lnkPath, 0);

            var data = (IShellLinkDataList)com;
            uint flags;
            data.GetFlags(out flags);
            flags |= SLDF_FORCE_NO_LINKINFO | SLDF_FORCE_NO_LINKTRACK;
            data.SetFlags(flags);

            ((IPersistFile)com).Save(lnkPath, true);
        }
        finally {
            Marshal.ReleaseComObject(com);
        }
    }
}

public static class SharedZip {
    public static void Write(string folder, string zipPath, string entryRoot) {
        if (File.Exists(zipPath)) {
            File.Delete(zipPath);
        }
        folder = Path.GetFullPath(folder);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create)) {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) {
                var rel = file.Substring(folder.Length).TrimStart('\\', '/').Replace('\\', '/');
                var entry = archive.CreateEntry(entryRoot + "/" + rel);
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var output = entry.Open()) {
                    input.CopyTo(output);
                }
            }
        }
    }
}
}
"@
}

[BrawlEnginePack.PortableShortcut]::Write($lnkPath, $exePath, $app)
Write-Host "Shortcut → $lnkPath"

Write-Host "Zipping $zip"
[BrawlEnginePack.SharedZip]::Write($stage, $zip, $stageName)

Write-Host "Done. $zip"
Write-Host "Extract the folder and double-click BrawlEngine.lnk (next to app\)."
