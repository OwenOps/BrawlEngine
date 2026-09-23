# Pack a self-contained Windows zip (repo root).
# Does not include Maps / sounds / skins / mods (those live in LocalAppData).
# Zip layout: one BrawlEngine.exe (PublishSingleFile). Natives extract to %TEMP%\.net\…

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

Write-Host "dotnet publish (single-file) → $stage"
Push-Location $hostDir
try {
    dotnet publish -c Release -r win-x64 --self-contained true `
        -p:DebugType=None `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -o $stage
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed"
    }
}
finally {
    Pop-Location
}

foreach ($name in @("Maps", "sounds", "skins", "mods", "library", "backups")) {
    $junk = Join-Path $stage $name
    if (Test-Path $junk) {
        Write-Host "Removing $name from the zip (user downloads do not ship)."
        Remove-Item $junk -Recurse -Force
    }
}

Get-ChildItem $stage -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "Zipping $zip"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $zip) {
    Remove-Item $zip -Force
}
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $stageFull = (Resolve-Path $stage).Path
    Get-ChildItem -LiteralPath $stageFull -Recurse -Force -File | ForEach-Object {
        $rel = $_.FullName.Substring($stageFull.Length).TrimStart('\')
        $entry = $archive.CreateEntry(($stageName + '/' + $rel.Replace('\', '/')))
        $input = [System.IO.File]::Open($_.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $output = $entry.Open()
            try { $input.CopyTo($output) }
            finally { $output.Dispose() }
        }
        finally { $input.Dispose() }
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Done. $zip"
Write-Host "Extract the folder and double-click BrawlEngine.exe."
