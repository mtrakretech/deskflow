# SPDX-FileCopyrightText: (C) 2026 Viola File Bridge contributors
# SPDX-License-Identifier: MIT
param(
    [Parameter(Mandatory=$true)][string]$InputZip,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$SourceCommit
)
$ErrorActionPreference = 'Stop'
$InputZip = [IO.Path]::GetFullPath($InputZip)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $SourceCommit) {
    $SourceCommit = git -C (Join-Path $PSScriptRoot '../../..') rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Specify the corresponding source commit.' }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($InputZip)
try {
    foreach ($entry in $archive.Entries) {
        $relative = $entry.FullName.Replace('\','/')
        if ($relative.StartsWith('/') -or $relative.Contains(':') -or ($relative.Split('/') -contains '..')) {
            throw 'Unsafe archive path.'
        }
        if ($relative -match '(?i)(^|/)(History|Cache|Logs)/|\.(dpapi|vfbpair)$|(^|/)(Viola\.Tests|viola-ui-smoke)\.exe$') {
            throw 'Private data or developer executable in portable input.'
        }
    }
} finally { $archive.Dispose() }
$stage = Join-Path ([IO.Path]::GetTempPath()) ('Viola-portable-' + [Guid]::NewGuid().ToString('N'))
$unpacked = Join-Path $stage 'input'
Expand-Archive -LiteralPath $InputZip -DestinationPath $unpacked
$source = $unpacked
if (-not (Test-Path -LiteralPath (Join-Path $source 'deskflow.exe'))) {
    $roots = @(Get-ChildItem -LiteralPath $unpacked -Directory)
    if ($roots.Count -ne 1) { throw 'Expected a single portable application folder.' }
    $source = $roots[0].FullName
}
$required = @('deskflow.exe','deskflow-core.exe','ViolaFileBridge.exe','ViolaFileBridge.exe.config',
    'NetworkSetup.ps1','Firewall.ps1','Qt6Core.dll','Qt6Gui.dll','Qt6Widgets.dll','Qt6Network.dll','Qt6Svg.dll',
    'plugins/platforms/qwindows.dll','msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll','libssl-3-x64.dll','libcrypto-3-x64.dll')
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) { throw "Required portable dependency missing: $name" }
}
$packageName = 'Deskflow-Viola-0.3.0-Windows-x64-Portable'
$package = Join-Path $stage $packageName
New-Item -ItemType Directory -Force $package | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $package -Recurse -Force
$settingsDirectory = Join-Path $package 'settings'
New-Item -ItemType Directory -Force $settingsDirectory | Out-Null
[IO.File]::WriteAllText((Join-Path $settingsDirectory 'Deskflow.conf'), "[core]`r`nprocessMode=Desktop`r`n[server]`r`nenableClipboard=true`r`nclipboardSize=32`r`n", [Text.UTF8Encoding]::new($false))
$instructions = @'
DESKFLOW + VIOLA 0.3.0 - WINDOWS x64 PORTABLE

1. Extract the complete ZIP to a writable folder on each PC. Do not run inside the ZIP.
2. Before switching versions, exit existing Deskflow and Viola from their tray menus.
3. Double-click deskflow.exe. All DLLs and the clipboard companion are included.
4. Configure Deskflow's usual server/client role and screen positions.
5. Open Clipboard > File sharing and pairing. Use the same pairing file on both PCs,
   compare fingerprints, and configure the trusted Private LAN when requested.
6. Copy files in Explorer, wait for Ready to paste on the receiving PC, then paste.

No installation or Windows service is required. Optional network/firewall configuration
requests administrator approval. Deskflow settings are in this folder's settings directory.
Viola pairing, encrypted history and received cache stay under the current user's
%LOCALAPPDATA%\ViolaFileBridge. Existing pairing is reused; copying this folder to a
different PC does not copy that user's protected pairing/history.

Open Clipboard > Clipboard history for saved text, screenshots and file references.
Keep all DLLs and folders beside deskflow.exe. Start only one input-sharing app at a time.
For an installed copy instead, download Deskflow-Viola-0.3.0-Windows-x64-Setup.exe.

Source and licenses are available with the release. This is an independent Deskflow fork.
https://github.com/mtrakretech/deskflow/releases
'@
[IO.File]::WriteAllText((Join-Path $package 'README-FIRST.txt'), $instructions, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $package 'BUILD-INFO.txt'), "Source commit: $SourceCommit`r`nSource: https://github.com/mtrakretech/deskflow/tree/$SourceCommit`r`nDeskflow: GPL-2.0 with OpenSSL exception; independent Viola companion: MIT.`r`n", [Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$destination = Join-Path $OutputDirectory ($packageName + '.zip')
Compress-Archive -LiteralPath $package -DestinationPath $destination -Force
Write-Output "Portable package: $destination"
Write-Output "Includes deskflow.exe, desktop-mode settings, clipboard companion, Qt/OpenSSL/VC++ dependencies and instructions."
