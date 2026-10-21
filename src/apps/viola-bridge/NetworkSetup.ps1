# SPDX-FileCopyrightText: (C) 2026 Viola File Bridge contributors
# SPDX-License-Identifier: MIT
param([Parameter(Mandatory=$true)][string]$ProgramPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
try {
    $profiles=@(Get-NetConnectionProfile | Where-Object { $_.IPv4Connectivity -ne 'Disconnected' })
    if($profiles.Count -eq 0){throw 'No connected IPv4 network found. Connect to your trusted LAN first.'}
    $form=New-Object Windows.Forms.Form
    $form.Text='Choose your trusted LAN'
    $form.ClientSize=New-Object Drawing.Size(540,205)
    $form.StartPosition='CenterScreen'
    $form.FormBorderStyle='FixedDialog'
    $form.MaximizeBox=$false
    $label=New-Object Windows.Forms.Label
    $label.SetBounds(14,12,510,58)
    $label.Text="Select the home/office network connecting your PCs.`r`nTrusting it marks that connection Private in Windows.`r`nDo not choose public Wi-Fi or an untrusted network."
    $combo=New-Object Windows.Forms.ComboBox
    $combo.SetBounds(14,82,510,28)
    $combo.DropDownStyle='DropDownList'
    foreach($p in $profiles){[void]$combo.Items.Add("$($p.Name) — $($p.InterfaceAlias) [$($p.NetworkCategory)]")}
    $combo.SelectedIndex=0
    $ok=New-Object Windows.Forms.Button
    $ok.Text='Trust and configure'
    $ok.SetBounds(255,142,150,32)
    $ok.DialogResult='OK'
    $cancel=New-Object Windows.Forms.Button
    $cancel.Text='Cancel'
    $cancel.SetBounds(415,142,110,32)
    $cancel.DialogResult='Cancel'
    $form.Controls.AddRange(@($label,$combo,$ok,$cancel))
    $form.CancelButton=$cancel
    if($form.ShowDialog() -ne 'OK'){exit 2}
    $chosen=$profiles[$combo.SelectedIndex]
    $current=Get-NetConnectionProfile -InterfaceIndex $chosen.InterfaceIndex
    if($current.Name -ne $chosen.Name){throw 'Network changed. Reopen setup and select it again.'}
    if($current.NetworkCategory -eq 'Public'){Set-NetConnectionProfile -InterfaceIndex $chosen.InterfaceIndex -NetworkCategory Private}
    & (Join-Path $PSScriptRoot 'Firewall.ps1') -ProgramPath $ProgramPath
    if($LASTEXITCODE -eq 1){throw 'Firewall setup failed. See Viola Logs.'}
    [Windows.Forms.MessageBox]::Show('Trusted LAN configured. Viola will refresh within 30 seconds.','Viola network setup') | Out-Null
} catch {
    [Windows.Forms.MessageBox]::Show($_.Exception.Message,'Viola network setup failed','OK','Error') | Out-Null
    exit 1
}
