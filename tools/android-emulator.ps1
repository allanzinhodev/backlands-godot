param(
    [string]$GodotExe = "$env:USERPROFILE\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe",
    [string]$AndroidSdk = "$env:LOCALAPPDATA\Android\Sdk",
    [string]$Device = 'emulator-5554'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$adb = Join-Path $AndroidSdk 'platform-tools\adb.exe'
$apk = Join-Path $repo '.playtest\android\backlands-debug.apk'
function Invoke-Checked {
    param([string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe falhou: $LASTEXITCODE" }
}

Push-Location $repo
try {
    New-Item -ItemType Directory -Force (Split-Path -Parent $apk) | Out-Null
    Invoke-Checked $adb @('-s', $Device, 'get-state')
    Invoke-Checked $GodotExe @('--headless', '--path', $repo, '--import')
    Invoke-Checked $GodotExe @('--headless', '--path', $repo, '--export-debug', 'Android Emulator', $apk)
    Invoke-Checked $adb @('-s', $Device, 'install', '-r', $apk)
    Invoke-Checked $adb @('-s', $Device, 'reverse', 'tcp:7777', 'tcp:7777')
    Invoke-Checked $adb @('-s', $Device, 'shell', 'monkey', '-p', 'com.backlands.game', '-c', 'android.intent.category.LAUNCHER', '1')
    Write-Host 'No jogo, conecte em 127.0.0.1:7777. O servidor deve estar rodando no PC.'
}
finally { Pop-Location }
