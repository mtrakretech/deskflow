param([string]$ProgramPath = (Join-Path $PSScriptRoot 'ViolaFileBridge.exe'),[switch]$Remove)
$ErrorActionPreference='Stop'
$logRoot=Join-Path $env:LOCALAPPDATA 'ViolaFileBridge\Logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
try {
    $ProgramPath=[IO.Path]::GetFullPath($ProgramPath)
    if (-not $Remove -and -not (Test-Path -LiteralPath $ProgramPath -PathType Leaf)) { throw 'Installed application not found.' }
    foreach($name in @('ViolaFileBridge-v2-TCP','ViolaFileBridge-v2-UDP','DeskflowViola-Core-TCP')) {
        Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    }
    if(-not $Remove){
        New-NetFirewallRule -Name 'ViolaFileBridge-v2-TCP' -DisplayName 'Viola File Bridge v2 transfer' -Group 'ViolaFileBridge-v2' -Program $ProgramPath -Direction Inbound -Action Allow -Protocol TCP -LocalPort 48751 -Profile Private -RemoteAddress LocalSubnet | Out-Null
        New-NetFirewallRule -Name 'ViolaFileBridge-v2-UDP' -DisplayName 'Viola File Bridge v2 discovery' -Group 'ViolaFileBridge-v2' -Program $ProgramPath -Direction Inbound -Action Allow -Protocol UDP -LocalPort 48752 -Profile Private -RemoteAddress LocalSubnet | Out-Null
        $core=Join-Path (Split-Path -Parent $ProgramPath) 'Deskflow\deskflow-core.exe'
        if(Test-Path -LiteralPath $core -PathType Leaf){
            New-NetFirewallRule -Name 'DeskflowViola-Core-TCP' -DisplayName 'Deskflow Viola input sharing' -Group 'ViolaFileBridge-v2' -Program $core -Direction Inbound -Action Allow -Protocol TCP -LocalPort 24800 -Profile Private -RemoteAddress LocalSubnet | Out-Null
        }
        # Retire only known legacy rules after replacement rules have been created.
        foreach($name in @('Viola File Bridge TCP','Viola File Bridge Discovery')){Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule}
    }
    Add-Content -LiteralPath (Join-Path $logRoot 'firewall.log') -Value "$(Get-Date -Format o) Firewall configuration succeeded; remove=$Remove"
} catch {
    Add-Content -LiteralPath (Join-Path $logRoot 'firewall.log') -Value "$(Get-Date -Format o) Firewall error: $($_.Exception.Message)"
    exit 1
}
