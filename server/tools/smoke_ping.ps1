<#
.SYNOPSIS
    Teste de fumaça do protocolo contra um backlands-server em execução.

    Valida o caminho feliz (C_PING -> S_PONG) e, principalmente, que entrada
    hostil derruba a conexão em vez de ser processada: length inválido, opcode
    desconhecido, opcode de servidor vindo do client e payload truncado.

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

function Connect-Server {
    $client = [System.Net.Sockets.TcpClient]::new()
    $client.Connect($ServerHost, $Port)
    $client.ReceiveTimeout = $TimeoutMs
    $client.SendTimeout = $TimeoutMs
    return $client
}

# Monta um frame: uint16 opcode + uint16 length + payload, little-endian.
# $Length permite forjar um header inconsistente de propósito.
function New-Frame {
    param([uint16]$Opcode, [byte[]]$Payload = @(), [Nullable[int]]$Length = $null)

    $total = if ($null -ne $Length) { $Length } else { 4 + $Payload.Length }
    $frame = [System.Collections.Generic.List[byte]]::new()
    $frame.AddRange([BitConverter]::GetBytes($Opcode))
    $frame.AddRange([BitConverter]::GetBytes([uint16]$total))
    if ($Payload.Length -gt 0) { $frame.AddRange($Payload) }
    return $frame.ToArray()
}

# Lê exatamente $Count bytes ou devolve $null se a conexão fechar antes.
function Read-Exactly {
    param([System.Net.Sockets.NetworkStream]$Stream, [int]$Count)

    $buffer = [byte[]]::new($Count)
    $offset = 0
    while ($offset -lt $Count) {
        try { $read = $Stream.Read($buffer, $offset, $Count - $offset) }
        catch { return $null }
        if ($read -le 0) { return $null }
        $offset += $read
    }
    return $buffer
}

# Confirma que o servidor fechou a conexão (leitura devolve 0 / erro).
function Test-Disconnected {
    param([System.Net.Sockets.NetworkStream]$Stream)
    return $null -eq (Read-Exactly -Stream $Stream -Count 1)
}

Write-Host "backlands-server smoke test -> ${ServerHost}:${Port}" -ForegroundColor Cyan

# --- 1. Caminho feliz ------------------------------------------------------
Write-Host "`nPING/PONG"
try {
    $client = Connect-Server
    $stream = $client.GetStream()

    $nonce = [uint32]0x11223344
    $stream.Write((New-Frame -Opcode 0x0001 -Payload ([BitConverter]::GetBytes($nonce))), 0, 8)
    $stream.Flush()

    $header = Read-Exactly -Stream $stream -Count 4
    if ($null -eq $header) {
        Write-Result $false 'servidor respondeu' '(conexão fechou)'
    }
    else {
        $opcode = [BitConverter]::ToUInt16($header, 0)
        $length = [BitConverter]::ToUInt16($header, 2)
        Write-Result ($opcode -eq 0x1001) 'opcode é S_PONG (0x1001)' "(veio 0x$($opcode.ToString('X4')))"
        Write-Result ($length -eq 8) 'length é 8 (header + nonce)' "(veio $length)"

        $body = Read-Exactly -Stream $stream -Count ($length - 4)
        $echo = [BitConverter]::ToUInt32($body, 0)
        Write-Result ($echo -eq $nonce) 'nonce devolvido intacto' "(veio 0x$($echo.ToString('X8')))"
    }

    # Várias requisições na mesma conexão: valida que o loop de leitura
    # continua depois do primeiro pacote.
    $allOk = $true
    for ($i = 1; $i -le 5; $i++) {
        $n = [uint32]$i
        $stream.Write((New-Frame -Opcode 0x0001 -Payload ([BitConverter]::GetBytes($n))), 0, 8)
        $stream.Flush()
        $resp = Read-Exactly -Stream $stream -Count 8
        if ($null -eq $resp -or [BitConverter]::ToUInt32($resp, 4) -ne $n) { $allOk = $false; break }
    }
    Write-Result $allOk '5 pings sequenciais na mesma conexão'

    $client.Close()
}
catch {
    Write-Result $false 'conexão com o servidor' "($($_.Exception.Message))"
}

# --- 2. Entrada hostil -----------------------------------------------------
# Cada caso abre a própria conexão porque o esperado é justamente ser derrubado.
Write-Host "`nRejeição de entrada inválida"

$hostileCases = @(
    @{ Name = 'length menor que o header (2)'; Frame = (New-Frame -Opcode 0x0001 -Length 2) }
    @{ Name = 'length acima de kMaxPacketSize (9999)'; Frame = (New-Frame -Opcode 0x0001 -Length 9999) }
    @{ Name = 'length zero'; Frame = (New-Frame -Opcode 0x0001 -Length 0) }
    @{ Name = 'opcode desconhecido (0xFFFF)'; Frame = (New-Frame -Opcode 0xFFFF) }
    @{ Name = 'opcode de servidor vindo do client (0x1001)'; Frame = (New-Frame -Opcode 0x1001) }
    @{ Name = 'C_PING sem payload'; Frame = (New-Frame -Opcode 0x0001) }
    @{ Name = 'C_PING com payload truncado (2 de 4 bytes)'; Frame = (New-Frame -Opcode 0x0001 -Payload @(1, 2)) }
)

foreach ($case in $hostileCases) {
    try {
        $client = Connect-Server
        $stream = $client.GetStream()
        $stream.Write($case.Frame, 0, $case.Frame.Length)
        $stream.Flush()
        Write-Result (Test-Disconnected -Stream $stream) "derruba: $($case.Name)"
        $client.Close()
    }
    catch {
        # Exceção de socket também significa conexão derrubada: resultado correto.
        Write-Result $true "derruba: $($case.Name)"
    }
}

# --- 3. O servidor continua vivo depois dos abusos -------------------------
Write-Host "`nServidor segue operacional"
try {
    $client = Connect-Server
    $stream = $client.GetStream()
    # Sufixo L: PowerShell leria 0xABCDEF01 como Int32 (negativo) e o cast
    # para uint32 falharia.
    $nonce = [uint32]0xABCDEF01L
    $stream.Write((New-Frame -Opcode 0x0001 -Payload ([BitConverter]::GetBytes($nonce))), 0, 8)
    $stream.Flush()
    $resp = Read-Exactly -Stream $stream -Count 8
    Write-Result ($null -ne $resp -and [BitConverter]::ToUInt32($resp, 4) -eq $nonce) `
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
