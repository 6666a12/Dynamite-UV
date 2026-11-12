param([switch]$CheckOnly, [switch]$ConfirmRemoval)
$ErrorActionPreference = 'Stop'
# Portable uninstall: no recursion, registry changes, user-data deletion or elevation.
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$manifestPath = Join-Path $root 'files.sha256.json'
function Assert-RegularPath([string]$path) {
    $item = Get-Item -LiteralPath $path -Force
    while ($null -ne $item) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing linked/reparse path: $($item.FullName)"
        }
        $item = if ($item -is [IO.DirectoryInfo]) { $item.Parent } else { $item.Directory }
    }
}
try {
    Assert-RegularPath $root
    Assert-RegularPath $manifestPath
    $identityPath = Join-Path $root 'distribution.json'
    Assert-RegularPath $identityPath
    $identity = Get-Content -LiteralPath $identityPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($identity.product -cne 'DynaMaker UV') { throw 'This is not a DynaMaker UV package.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $files = New-Object 'System.Collections.Generic.List[string]'
    $directories = New-Object 'System.Collections.Generic.HashSet[string]'
    $preserved = 0
    foreach ($entry in $manifest.PSObject.Properties) {
        $relative = $entry.Name
        if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or
            ($relative -split '[/\\]' | Where-Object { $_ -eq '..' -or $_ -eq '.' -or $_ -eq '' }) -or
            $entry.Value -notmatch '^[0-9a-fA-F]{64}$') { throw "Invalid manifest entry: $relative" }
        $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
        if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Manifest path escapes package.'
        }
        # Validate existing ancestors even when the leaf has already been removed.
        $parent = [IO.Path]::GetDirectoryName($path)
        while ($parent.Length -gt $root.Length) {
            if (Test-Path -LiteralPath $parent) { Assert-RegularPath $parent }
            [void]$directories.Add($parent)
            $parent = [IO.Path]::GetDirectoryName($parent)
        }
        if (-not (Test-Path -LiteralPath $path)) { continue }
        Assert-RegularPath $path
        if (-not [IO.File]::Exists($path)) { throw "Expected a file: $relative" }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($hash -ine $entry.Value) { Write-Output "PRESERVED modified file: $relative"; $preserved++; continue }
        $files.Add($path)
    }
    # Refuse before deleting anything if a packaged file is in use.
    $handles = New-Object 'System.Collections.Generic.List[System.IO.FileStream]'
    try {
        foreach ($path in $files) {
            $handles.Add([IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None))
        }
    } finally { foreach ($handle in $handles) { $handle.Dispose() } }
    if ($CheckOnly) { Write-Output "UNINSTALL CHECK PASS files=$($files.Count) preserved=$preserved"; exit 0 }
    if (-not $ConfirmRemoval) {
        Add-Type -AssemblyName System.Windows.Forms
        $answer = [Windows.Forms.MessageBox]::Show(
            "Remove DynaMaker UV program files from:`n$root`n`nCharts, extra/modified files and user preferences will be kept.`nClose the editor before continuing.",
            'DynaMaker UV - Uninstall', [Windows.Forms.MessageBoxButtons]::YesNo,
            [Windows.Forms.MessageBoxIcon]::Question, [Windows.Forms.MessageBoxDefaultButton]::Button2)
        if ($answer -ne [Windows.Forms.DialogResult]::Yes) { Write-Output 'Uninstall cancelled.'; exit 0 }
    }
    foreach ($path in $files) {
        # Recheck after user confirmation; do not delete a file modified meanwhile.
        if (-not [IO.File]::Exists($path)) { continue }
        Assert-RegularPath $path
        $relative = $path.Substring($root.Length + 1).Replace('\', '/')
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $manifest.$relative) {
            Write-Output "PRESERVED changed file: $relative"; $preserved++; continue
        }
        Remove-Item -LiteralPath $path -Force
    }
    if ($preserved -eq 0) { Remove-Item -LiteralPath $manifestPath -Force }
    # Only remove empty, known directories; leave package root for any user files.
    foreach ($directory in ($directories | Sort-Object Length -Descending)) {
        if ([IO.Directory]::Exists($directory) -and [IO.Directory]::GetFileSystemEntries($directory).Length -eq 0) {
            [IO.Directory]::Delete($directory, $false)
        }
    }
    Write-Output "UNINSTALL COMPLETE. Preserved modified files: $preserved. User preferences and extra files were not removed."
    exit 0
} catch {
    Write-Output "UNINSTALL FAILED: $($_.Exception.Message)"
    Write-Output 'Close the editor and retry. Some files may remain; user data was not intentionally removed.'
    exit 1
}
