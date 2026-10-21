# SPDX-FileCopyrightText: (C) 2026 Viola File Bridge contributors
# SPDX-License-Identifier: MIT
param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'out'),[string]$DeskflowPayload,[string]$VCRedist,[switch]$Tests,[switch]$Large,[switch]$NoClipboard)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
if(-not(Test-Path -LiteralPath $compiler)){throw '.NET Framework compiler missing.'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Push-Location $PSScriptRoot
try {
    $refs=@('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Security.dll','/r:System.Management.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll','/r:System.Web.Extensions.dll')
    $sources=@('src\Core.cs','src\Network.cs','src\History.cs','src\Commands.cs','src\UiApi.cs','src\App.cs')
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /win32manifest:src\app.manifest "/out:$OutputDirectory\ViolaFileBridge.exe" @refs @sources
    if($LASTEXITCODE -ne 0){throw 'App compilation failed'}
    Copy-Item src\ViolaFileBridge.exe.config,Firewall.ps1,NetworkSetup.ps1,README.md,SECURITY.md,CHANGELOG.md,LICENSE -Destination $OutputDirectory -Force
    $sourceCommit='source archive'
    if(Get-Command git -ErrorAction SilentlyContinue){$candidate=git rev-parse HEAD 2>$null;if($LASTEXITCODE -eq 0){$sourceCommit=$candidate}}
    @("Corresponding source: https://github.com/mtrakretech/deskflow","Build commit: $sourceCommit","Deskflow: GPL-2.0-only WITH LicenseRef-OpenSSL-Exception; Viola companion: MIT.","The source repository includes LICENSES, build scripts, and the Windows CI packaging workflow.") | Set-Content (Join-Path $OutputDirectory 'SOURCE.txt') -Encoding UTF8
    $resources=@();foreach($name in @('ViolaFileBridge.exe','ViolaFileBridge.exe.config','Firewall.ps1','NetworkSetup.ps1','README.md','SECURITY.md','CHANGELOG.md','LICENSE','SOURCE.txt')){$resources+="/resource:$OutputDirectory\$name,$name"}
    if($DeskflowPayload){$DeskflowPayload=[IO.Path]::GetFullPath($DeskflowPayload);if(-not(Test-Path -LiteralPath $DeskflowPayload)){throw 'Deskflow ZIP missing'};$resources+="/resource:$DeskflowPayload,DeskflowPayload.zip"}
    if($VCRedist){$VCRedist=[IO.Path]::GetFullPath($VCRedist);$signature=Get-AuthenticodeSignature -LiteralPath $VCRedist;if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'VC++ runtime must have a valid Microsoft signature'};$resources+="/resource:$VCRedist,VCRedist.exe"}
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /win32manifest:src\app.manifest "/out:$OutputDirectory\ViolaFileBridge-Setup.exe" @refs @resources src\Setup.cs src\Core.cs src\Payload.cs src\Commands.cs
    if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
    if($Tests){
        & $compiler /nologo /target:exe /main:Viola.Tests /platform:anycpu /optimize+ /win32manifest:src\app.manifest "/out:$OutputDirectory\Viola.Tests.exe" @refs @sources tests\Tests.cs tests\HistoryTests.cs src\Payload.cs
        if($LASTEXITCODE -ne 0){throw 'Test compilation failed'}
        Copy-Item src\ViolaFileBridge.exe.config "$OutputDirectory\Viola.Tests.exe.config" -Force
        $testArgs=@();if($Large){$testArgs+='--large'};if($NoClipboard){$testArgs+='--no-clipboard'}
        & "$OutputDirectory\Viola.Tests.exe" @testArgs | Tee-Object -FilePath (Join-Path $OutputDirectory 'TEST-RESULTS.txt')
        if($LASTEXITCODE -ne 0){throw 'Tests failed'}
    }
    Write-Output "Built $OutputDirectory"
} finally {Pop-Location}
