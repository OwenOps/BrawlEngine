# Refresh a Desktop shortcut to the Release build exe (no folder copy).
# Called from host/BrawlEngine.Host.csproj after Release builds.

param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDir
)

$ErrorActionPreference = "Stop"

$SourceDir = $SourceDir.Trim().TrimEnd('\', '/')

$exe = Join-Path $SourceDir "BrawlEngine.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "BrawlEngine.exe not found in: $SourceDir"
}

$desktop = [Environment]::GetFolderPath("Desktop")

# Older post-build copied the whole output here; remove leftover folder.
$oldCopy = Join-Path $desktop "BrawlEngine"
if (Test-Path -LiteralPath $oldCopy) {
    Remove-Item -LiteralPath $oldCopy -Recurse -Force
}

$lnkPath = Join-Path $desktop "BrawlEngine.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnkPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $SourceDir
$shortcut.IconLocation = $exe + ",0"
$shortcut.Description = "BrawlEngine"
$shortcut.Save()

Write-Host "Shortcut: $lnkPath -> $exe"
