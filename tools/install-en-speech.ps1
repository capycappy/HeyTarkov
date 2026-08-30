# Installs the en-US speech recognition capability.
# Run from an ELEVATED PowerShell. Results are written to install-en-speech.log.
#
# ASCII only on purpose: Windows PowerShell 5.1 reads .ps1 files as the system
# ANSI codepage, so non-ASCII text here would be mojibake and break parsing.

$ErrorActionPreference = 'Continue'
$log = Join-Path $PSScriptRoot 'install-en-speech.log'
Start-Transcript -Path $log -Force | Out-Null

function Section($name) {
    Write-Output ''
    Write-Output "=== $name ==="
}

Section 'Elevation'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Output "IsAdministrator = $isAdmin"

if (-not $isAdmin) {
    Write-Output 'NOT ELEVATED. Reopen PowerShell via "Terminal (Admin)" and retry.'
    Stop-Transcript | Out-Null
    exit 1
}

Section 'Windows Update policy (main cause of 0x800f0954)'
$wuPolicy = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'
if (Test-Path $wuPolicy) {
    Get-ItemProperty $wuPolicy | Format-List UseWUServer, NoAutoUpdate
} else {
    Write-Output 'No WSUS policy present (good).'
}

Section 'Capability state BEFORE'
Get-WindowsCapability -Online -Name 'Language.Speech*' |
    Select-Object Name, State | Format-Table -AutoSize

Section 'Installing Language.Speech~~~en-US~0.0.1.0'
try {
    Add-WindowsCapability -Online -Name 'Language.Speech~~~en-US~0.0.1.0' -ErrorAction Stop |
        Format-List Online, RestartNeeded
    Write-Output 'Add-WindowsCapability: OK'
} catch {
    Write-Output 'Add-WindowsCapability: FAILED'
    Write-Output ("  Message : " + $_.Exception.Message)
    Write-Output ("  HResult : 0x{0:X8}" -f $_.Exception.HResult)
}

Section 'Capability state AFTER'
Get-WindowsCapability -Online -Name 'Language.Speech*' |
    Select-Object Name, State | Format-Table -AutoSize

Section 'Installed recognizers'
Add-Type -AssemblyName System.Speech
$recognizers = [System.Speech.Recognition.SpeechRecognitionEngine]::InstalledRecognizers()
if ($recognizers.Count -eq 0) {
    Write-Output '(none)'
} else {
    $recognizers |
        Select-Object @{ n = 'Culture'; e = { $_.Culture.Name } }, Name |
        Format-Table -AutoSize
}

$hasEnglish = @($recognizers | Where-Object { $_.Culture.Name -eq 'en-US' }).Count -gt 0

Section 'Result'
if ($hasEnglish) {
    Write-Output 'SUCCESS: en-US recognizer is available. Restart Hey Tarkov.'
} else {
    Write-Output 'NOT DONE: en-US recognizer is still missing. See the log above.'
}

Stop-Transcript | Out-Null
