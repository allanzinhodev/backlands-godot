<#
.SYNOPSIS
    Teste de fumaça do framing do protocolo contra um backlands-server em execução.

    Valida o caminho feliz (C_PING -> S_PONG) e, principalmente, que entrada
    hostil derruba a conexão em vez de ser processada: length inválido, opcode
    desconhecido, opcode de servidor vindo do client e payload truncado.

    Cada caso hostil começa provando que a conexão está VIVA (um ping que
    responde) antes de enviar o pacote ruim. Sem isso, um servidor morto
    passaria o teste: toda conexão apareceria como "corretamente derrubada".

.EXAMPLE
    .\smoke_ping.ps1
    .\smoke_ping.ps1 -ServerHost 127.0.0.1 -Port 7777
#>
param(
    [string]$ServerHost = '127.0.0.1',
    [int]$Port = 7777,
    [int]$TimeoutMs = 3000
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\protocol.ps1"

$script:Failures = 0

function Write-Result {
    param([bool]$Pass, [string]$Name, [string]$Detail = '')
    if ($Pass) {
        Write-Host "  [ok]   $Name" -ForegroundColor Green
    }
    else {
        Write-Host "  [FAIL] $Name $Detail" -ForegroundColor Red
        $script:Failures++
    }
}

function New-PingFrame {
    param([uint32]$Nonce)
    return New-Frame -Opcode $C_PING -Payload ([BitConverter]::GetBytes($Nonce))
}

# Faz um ping e confirma o eco. É a prova de vida da conexão.
function Test-PingWorks {
    param([System.Net.Sockets.NetworkStream]$Stream, [uint32]$Nonce)
    try {
        Send-Frame -Stream $Stream -Frame (New-PingFrame -Nonce $Nonce)
        $frame = Read-FrameOfType -Stream $Stream -Opcode $S_PONG
        if ($null -eq $frame) { return $false }
        return [BitConverter]::ToUInt32($frame.Payload, 0) -eq $Nonce
    }
    catch { return $false }
}

Write-Host "backlands-server smoke test -> ${ServerHost}:${Port}" -ForegroundColor Cyan

# --- 1. Caminho feliz ------------------------------------------------------
Write-Host "`nPING/PONG"
try {
    $client = Connect-BacklandsServer -ServerHost $ServerHost -Port $Port -TimeoutMs $TimeoutMs
    $stream = $client.GetStream()

    $nonce = [uint32]0x11223344
    Send-Frame -Stream $stream -Frame (New-PingFrame -Nonce $nonce)

    $frame = Read-Frame -Stream $stream
    if ($null -eq $frame) {
        Write-Result $false 'servidor respondeu' '(conexão fechou)'
    }
    else {
        Write-Result ($frame.Opcode -eq $S_PONG) 'opcode é S_PONG (0x1001)' `
            "(veio $(Get-OpcodeName $frame.Opcode))"
        Write-Result ($frame.Length -eq 8) 'length é 8 (header + nonce)' "(veio $($frame.Length))"
        $echo = [BitConverter]::ToUInt32($frame.Payload, 0)
        Write-Result ($echo -eq $nonce) 'nonce devolvido intacto' "(veio 0x$($echo.ToString('X8')))"
    }

    # Várias requisições na mesma conexão: valida que o loop de leitura
    # continua depois do primeiro pacote.
    $allOk = $true
    for ($i = 1; $i -le 5; $i++) {
        if (-not (Test-PingWorks -Stream $stream -Nonce ([uint32]$i))) { $allOk = $false; break }
    }
    Write-Result $allOk '5 pings sequenciais na mesma conexão'

    $client.Close()
}
catch {
    Write-Result $false 'conexão com o servidor' "($($_.Exception.Message))"
}

# --- 2. Entrada hostil -----------------------------------------------------
# Cada caso abre a própria conexão, prova que ela está viva, e só então envia
# o pacote ruim — o que torna a checagem capaz de distinguir "derrubado pelo
# pacote" de "servidor já estava morto".
Write-Host "`nRejeição de entrada inválida"

$hostileCases = @(
    @{ Name = 'length menor que o header (2)'; Frame = (New-Frame -Opcode $C_PING -Length 2) }
    @{ Name = 'length acima de kMaxPacketSize (9999)'; Frame = (New-Frame -Opcode $C_PING -Length 9999) }
    @{ Name = 'length zero'; Frame = (New-Frame -Opcode $C_PING -Length 0) }
    @{ Name = 'opcode desconhecido (0xFFFF)'; Frame = (New-Frame -Opcode 0xFFFF) }
    @{ Name = 'opcode de servidor vindo do client (0x1001)'; Frame = (New-Frame -Opcode $S_PONG) }
    @{ Name = 'C_PING sem payload'; Frame = (New-Frame -Opcode $C_PING) }
    @{ Name = 'C_PING com payload truncado (2 de 4 bytes)'; Frame = (New-Frame -Opcode $C_PING -Payload @(1, 2)) }
)

foreach ($case in $hostileCases) {
    $alive = $false
    $dropped = $false
    try {
        $client = Connect-BacklandsServer -ServerHost $ServerHost -Port $Port -TimeoutMs $TimeoutMs
        $stream = $client.GetStream()

        $alive = Test-PingWorks -Stream $stream -Nonce ([uint32]0x5A5A5A5A)
        if ($alive) {
            Send-Frame -Stream $stream -Frame $case.Frame
            $dropped = Test-Disconnected -Stream $stream
        }
        $client.Close()
    }
    catch {
        # Exceção de socket após o ping também significa derrubado.
        if ($alive) { $dropped = $true }
    }

    if (-not $alive) {
        Write-Result $false "derruba: $($case.Name)" '(conexão não estava viva antes do teste)'
    }
    else {
        Write-Result $dropped "derruba: $($case.Name)"
    }
}

# --- 3. O servidor continua vivo depois dos abusos -------------------------
Write-Host "`nServidor segue operacional"
try {
    $client = Connect-BacklandsServer -ServerHost $ServerHost -Port $Port -TimeoutMs $TimeoutMs
    $stream = $client.GetStream()
    Write-Result (Test-PingWorks -Stream $stream -Nonce ([uint32]0xABCDEF01L)) `
        'responde PING depois de 7 conexões derrubadas'
    $client.Close()
}
catch {
    Write-Result $false 'servidor ainda aceita conexões' "($($_.Exception.Message))"
}

Write-Host ''
if ($script:Failures -eq 0) {
    Write-Host 'smoke test: tudo ok' -ForegroundColor Green
    exit 0
}
Write-Host "smoke test: $script:Failures falha(s)" -ForegroundColor Red
exit 1
