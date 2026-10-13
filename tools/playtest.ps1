<#
.SYNOPSIS
    Sobe o servidor e abre N clientes para testar o jogo.

    É o atalho para "quero ver funcionando": compila o client, inicia o
    backlands-server, e abre as janelas do Godot já conectadas e logadas, cada
    uma com um personagem diferente.

.EXAMPLE
    .\tools\playtest.ps1                   # servidor + 2 clientes
    .\tools\playtest.ps1 -Clients 1        # só um
    .\tools\playtest.ps1 -Screenshot       # headless, salva PNGs e encerra
    .\tools\playtest.ps1 -KeepServer       # não derruba o servidor no fim

.NOTES
    O servidor precisa estar compilado: server\build.ps1
#>
param(
    [int]$Clients = 2,
    [string]$ServerHost = '127.0.0.1',
    [int]$Port = 7777,
    [switch]$Screenshot,
    [switch]$KeepServer,
    [string]$GodotExe = "$env:USERPROFILE\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$serverExe = Join-Path $repo 'server\build\debug\backlands_server.exe'
$outDir = Join-Path $repo '.playtest'

if (-not (Test-Path $serverExe)) {
    throw "servidor não compilado: rode server\build.ps1 primeiro ($serverExe)"
}
if (-not (Test-Path $GodotExe)) {
    throw "Godot não encontrado em $GodotExe (use -GodotExe para apontar)"
}

if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

# Executáveis nativos escrevem em stderr sem que isso seja erro; com
# ErrorActionPreference='Stop' o PowerShell transformaria isso em exceção.
function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments, [string]$What)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Exe @Arguments; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $previous }
    if ($code -ne 0) { throw "$What falhou (exit $code)" }
}

Write-Host '[playtest] compilando o client' -ForegroundColor Cyan
Push-Location $repo
try {
    Invoke-Native -Exe 'dotnet' -Arguments @('build', '-v', 'quiet', '--nologo') -What 'dotnet build'
}
finally {
    Pop-Location
}

# Um servidor já rodando é reaproveitado, para não perder o estado do mundo
# entre execuções seguidas.
$server = Get-Process backlands_server -ErrorAction SilentlyContinue
if ($server) {
    Write-Host "[playtest] servidor já rodando (pid $($server.Id))" -ForegroundColor DarkGray
}
else {
    Write-Host '[playtest] iniciando o servidor' -ForegroundColor Cyan
    $serverLog = Join-Path $outDir 'server.log'
    $server = Start-Process -FilePath $serverExe `
        -WorkingDirectory (Split-Path -Parent $serverExe) `
        -PassThru -RedirectStandardOutput $serverLog `
        -RedirectStandardError (Join-Path $outDir 'server.err.log')
    Start-Sleep -Milliseconds 1200

    if ($server.HasExited) {
        Get-Content $serverLog -Encoding utf8 | Write-Host
        throw "o servidor encerrou logo no início (exit $($server.ExitCode))"
    }
    Write-Host "[playtest] servidor no ar (pid $($server.Id)), log em $serverLog" -ForegroundColor Green
}

# Nomes únicos por execução: o servidor recusa nome em uso, e um personagem da
# execução anterior ainda pode estar conectado.
$suffix = Get-Random -Minimum 100 -Maximum 999
$launched = @()

for ($i = 1; $i -le $Clients; $i++) {
    $name = "Jogador${i}_$suffix"
    $log = Join-Path $outDir "client$i.log"

    $args = @('--path', $repo, '--log-file', $log)
    if ($Screenshot) {
        # --quit-after conta ITERAÇÕES do loop principal, não segundos; é o
        # cão de guarda que evita um processo pendurado para sempre.
        $args += @('--quit-after', '900')
    }
    $args += @('--', "--autologin=$name", "--server=${ServerHost}:${Port}")

    if ($Screenshot) {
        $args += "--screenshot=$(Join-Path $outDir "client$i.png")"
        # Os clientes entram escalonados, então a captura espera tempo bastante
        # para que todos já estejam no mundo. Ninguém encerra ao fotografar:
        # quem encerra é o --quit-after, senão o primeiro a sair esvaziaria a
        # cena dos que ainda vão fotografar.
        $delay = if ($i -eq 1) { 300 } else { 240 }
        $args += "--screenshot-delay=$delay"
    }
    # O primeiro cliente atravessa a ponte, para o movimento ficar visível.
    if ($i -eq 1) {
        $args += '--walk=18,16'
    }

    Write-Host "[playtest] abrindo cliente $i como $name" -ForegroundColor Cyan
    $launched += Start-Process -FilePath $GodotExe -ArgumentList $args -PassThru
    Start-Sleep -Milliseconds 700
}

if ($Screenshot) {
    Write-Host '[playtest] aguardando os clientes encerrarem' -ForegroundColor Cyan
    $launched | Wait-Process -Timeout 120

    foreach ($i in 1..$Clients) {
        $png = Join-Path $outDir "client$i.png"
        $status = if (Test-Path $png) { "ok ($((Get-Item $png).Length) bytes)" } else { 'NÃO GERADO' }
        Write-Host "  cliente ${i}: $status"
    }

    if (-not $KeepServer) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        Write-Host '[playtest] servidor encerrado' -ForegroundColor DarkGray
    }
}
else {
    Write-Host ''
    Write-Host 'Clientes abertos. Clique no cenário para andar.' -ForegroundColor Green
    Write-Host "Logs e capturas em: $outDir" -ForegroundColor DarkGray
    if (-not $KeepServer) {
        Write-Host 'Para derrubar o servidor: Get-Process backlands_server | Stop-Process' -ForegroundColor DarkGray
    }
}
