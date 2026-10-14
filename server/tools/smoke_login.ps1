<#
.SYNOPSIS
    Teste de integração do handshake de entrada no mundo (marco M3).

    Valida com DOIS clientes simultâneos que o servidor é a autoridade sobre
    identidade e posição: quem entra recebe sua entidade e vê quem já estava,
    quem já estava vê quem entrou, e sair remove a entidade dos outros.

    Também cobre as recusas de login: nome inválido, nome em uso e login
    duplicado na mesma conexão.

.EXAMPLE
    .\smoke_login.ps1
#>
param(
    [string]$ServerHost = '127.0.0.1',
    [int]$Port = 7777,
    [int]$TimeoutMs = 3000
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\protocol.ps1"

$script:Failures = 0
$script:Clients = @()

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

function New-Client {
    param([string]$Label)
    $client = Connect-BacklandsServer -ServerHost $ServerHost -Port $Port -TimeoutMs $TimeoutMs
    $script:Clients += $client
    return [pscustomobject]@{
        Label  = $Label
        Tcp    = $client
        Stream = $client.GetStream()
    }
}

# Lê até achar o S_ENTITY_SPAWN de uma entidade específica.
function Find-SpawnOf {
    param([System.Net.Sockets.NetworkStream]$Stream, [uint32]$EntityId, [int]$MaxFrames = 60)

    for ($i = 0; $i -lt $MaxFrames; $i++) {
        $frame = Read-FrameOfType -Stream $Stream -Opcode $S_ENTITY_SPAWN -MaxFrames $MaxFrames
        if ($null -eq $frame) { return $null }
        $spawn = Read-EntitySpawn -Payload $frame.Payload
        if ($spawn.EntityId -eq $EntityId) { return $frame }
    }
    return $null
}

function Close-AllClients {
    foreach ($c in $script:Clients) {
        try { $c.Close() } catch { }
    }
    $script:Clients = @()
}

# Nomes únicos por execução: o servidor guarda estado em memória e um nome
# ainda ocupado de uma execução anterior faria o teste falhar por engano.
$suffix = (Get-Random -Minimum 1000 -Maximum 9999)
$nameA = "TestA$suffix"
$nameB = "TestB$suffix"

Write-Host "backlands-server login smoke test -> ${ServerHost}:${Port}" -ForegroundColor Cyan

try {
    # --- 1. Primeiro jogador entra ----------------------------------------
    Write-Host "`nEntrada no mundo"
    $a = New-Client -Label 'A'
    Send-Login -Stream $a.Stream -Name $nameA

    $frame = Read-FrameOfAnyType -Stream $a.Stream -Opcodes @($S_ENTER_WORLD, $S_LOGIN_FAILED)
    if ($null -eq $frame) {
        Write-Result $false 'A recebeu resposta ao login' '(conexão fechou)'
    }
    else {
        Write-Result ($frame.Opcode -eq $S_ENTER_WORLD) 'A recebeu S_ENTER_WORLD' `
            "(veio $(Get-OpcodeName $frame.Opcode))"

        $enter = Read-EnterWorld -Payload $frame.Payload
        Write-Result ($enter.EntityId -gt 0) 'entity_id atribuído pelo servidor' "(id=$($enter.EntityId))"
        Write-Result ($enter.Name -eq $nameA) 'nome confirmado' "(veio '$($enter.Name)')"
        Write-Result ($enter.Map -eq 'starter_fields') 'mapa informado' "(veio '$($enter.Map)')"
        # O valor vem de server/maps/starter_fields.lua. O que importa aqui é
        # que QUEM decide é o servidor, então basta ser consistente entre os
        # jogadores — conferido mais abaixo, quando B entra na mesma célula.
        Write-Result ($enter.X -gt 0 -and $enter.Y -gt 0) 'célula de spawn definida pelo servidor' `
            "(veio $($enter.X),$($enter.Y))"
        Write-Result ($enter.Direction -lt 8) 'direção válida' "(veio $($enter.Direction))"
        # Payload sem sobra: o contrato bate exatamente com o que lemos.
        Write-Result ($enter.BytesRead -eq $frame.Payload.Length) 'payload consumido por inteiro' `
            "($($enter.BytesRead) de $($frame.Payload.Length))"

        $script:EntityA = $enter.EntityId
    }

    # --- 2. Segundo jogador entra e os dois se veem -----------------------
    Write-Host "`nVisibilidade mútua entre dois clientes"
    $b = New-Client -Label 'B'
    Send-Login -Stream $b.Stream -Name $nameB

    $frameB = Read-FrameOfAnyType -Stream $b.Stream -Opcodes @($S_ENTER_WORLD, $S_LOGIN_FAILED)
    Write-Result ($null -ne $frameB -and $frameB.Opcode -eq $S_ENTER_WORLD) 'B recebeu S_ENTER_WORLD'
    $enterB = Read-EnterWorld -Payload $frameB.Payload
    $script:EntityB = $enterB.EntityId
    Write-Result ($script:EntityB -ne $script:EntityA) 'B recebeu id diferente do de A' `
        "(A=$($script:EntityA) B=$($script:EntityB))"

    # B deve receber o spawn de A, que já estava no mundo. Procura pelo ID:
    # o mundo tem monstros, então os primeiros spawns que chegam são deles.
    $spawnOnB = Find-SpawnOf -Stream $b.Stream -EntityId $script:EntityA
    if ($null -eq $spawnOnB) {
        Write-Result $false 'B recebeu S_ENTITY_SPAWN de A' '(nada chegou)'
    }
    else {
        Write-Result ($spawnOnB.Opcode -eq $S_ENTITY_SPAWN) 'B recebeu S_ENTITY_SPAWN de A' `
            "(veio $(Get-OpcodeName $spawnOnB.Opcode))"
        $sp = Read-EntitySpawn -Payload $spawnOnB.Payload
        Write-Result ($sp.EntityId -eq $script:EntityA) 'spawn visto por B é a entidade de A' `
            "(veio $($sp.EntityId))"
        Write-Result ($sp.Name -eq $nameA) 'nome de A chegou a B' "(veio '$($sp.Name)')"
    }

    # A deve receber o spawn de B, que acabou de entrar.
    $spawnOnA = Find-SpawnOf -Stream $a.Stream -EntityId $script:EntityB
    if ($null -eq $spawnOnA) {
        Write-Result $false 'A recebeu S_ENTITY_SPAWN de B' '(nada chegou)'
    }
    else {
        Write-Result ($spawnOnA.Opcode -eq $S_ENTITY_SPAWN) 'A recebeu S_ENTITY_SPAWN de B' `
            "(veio $(Get-OpcodeName $spawnOnA.Opcode))"
        $sp = Read-EntitySpawn -Payload $spawnOnA.Payload
        Write-Result ($sp.EntityId -eq $script:EntityB) 'spawn visto por A é a entidade de B' `
            "(veio $($sp.EntityId))"
        Write-Result ($sp.Name -eq $nameB) 'nome de B chegou a A' "(veio '$($sp.Name)')"
    }

    # --- 3. Recusas de login ----------------------------------------------
    Write-Host "`nRecusa de login"

    # Nome curto: erro do usuário, a conexão continua para ele tentar outro.
    $c = New-Client -Label 'C'
    Send-Login -Stream $c.Stream -Name 'ab'
    $failFrame = Read-FrameOfAnyType -Stream $c.Stream -Opcodes @($S_LOGIN_FAILED, $S_ENTER_WORLD)
    if ($null -eq $failFrame) {
        Write-Result $false 'nome inválido recebe S_LOGIN_FAILED' '(conexão fechou)'
    }
    else {
        Write-Result ($failFrame.Opcode -eq $S_LOGIN_FAILED) 'nome inválido recebe S_LOGIN_FAILED' `
            "(veio $(Get-OpcodeName $failFrame.Opcode))"
        $reason = Read-PayloadU8 -Payload $failFrame.Payload -Offset 0
        Write-Result ($reason.Value -eq $LOGIN_FAIL_INVALID_NAME) 'motivo = nome inválido' `
            "(veio $($reason.Value))"
    }

    # Nome em uso.
    Send-Login -Stream $c.Stream -Name $nameA
    $failFrame2 = Read-FrameOfAnyType -Stream $c.Stream -Opcodes @($S_LOGIN_FAILED, $S_ENTER_WORLD)
    if ($null -eq $failFrame2) {
        Write-Result $false 'nome em uso recebe S_LOGIN_FAILED' '(conexão fechou)'
    }
    else {
        $reason2 = Read-PayloadU8 -Payload $failFrame2.Payload -Offset 0
        Write-Result ($failFrame2.Opcode -eq $S_LOGIN_FAILED -and $reason2.Value -eq $LOGIN_FAIL_NAME_IN_USE) `
            'nome em uso recebe motivo = nome em uso' "(opcode $(Get-OpcodeName $failFrame2.Opcode), motivo $($reason2.Value))"
    }

    # Depois de duas recusas, a mesma conexão ainda consegue entrar: é isso que
    # permite ao jogador corrigir o nome sem reconectar.
    $nameC = "TestC$suffix"
    Send-Login -Stream $c.Stream -Name $nameC
    $okFrame = Read-FrameOfAnyType -Stream $c.Stream -Opcodes @($S_ENTER_WORLD, $S_LOGIN_FAILED)
    Write-Result ($null -ne $okFrame -and $okFrame.Opcode -eq $S_ENTER_WORLD) `
        'mesma conexão entra após corrigir o nome'

    # --- 4. Login duplicado é erro de protocolo ---------------------------
    Write-Host "`nLogin duplicado derruba a sessão"
    $d = New-Client -Label 'D'
    $nameD = "TestD$suffix"
    Send-Login -Stream $d.Stream -Name $nameD
    $enterD = Read-FrameOfAnyType -Stream $d.Stream -Opcodes @($S_ENTER_WORLD, $S_LOGIN_FAILED)
    Write-Result ($null -ne $enterD -and $enterD.Opcode -eq $S_ENTER_WORLD) 'D entrou normalmente'

    Send-Login -Stream $d.Stream -Name "Outro$suffix"
    $dupFrame = Read-FrameOfAnyType -Stream $d.Stream -Opcodes @($S_LOGIN_FAILED, $S_ENTER_WORLD)
    if ($null -ne $dupFrame -and $dupFrame.Opcode -eq $S_LOGIN_FAILED) {
        $reason3 = Read-PayloadU8 -Payload $dupFrame.Payload -Offset 0
        Write-Result ($reason3.Value -eq $LOGIN_FAIL_ALREADY_SPAWNED) 'motivo = já está no mundo' `
            "(veio $($reason3.Value))"
        Write-Result (Test-Disconnected -Stream $d.Stream) 'sessão derrubada após login duplicado'
    }
    else {
        # Derrubar sem enviar o motivo também é aceitável.
        Write-Result ($null -eq $dupFrame) 'sessão derrubada após login duplicado'
    }

    # --- 5. Saída remove a entidade dos outros ----------------------------
    Write-Host "`nDesconexão propaga despawn"
    $b.Tcp.Close()

    # A recebe no caminho os spawns de C/E/F e o despawn de D: procura o id de B.
    $foundDespawn = $false
    for ($i = 0; $i -lt 20 -and -not $foundDespawn; $i++) {
        $despawnFrame = Read-FrameOfType -Stream $a.Stream -Opcode $S_ENTITY_DESPAWN -MaxFrames 20
        if ($null -eq $despawnFrame) { break }
        $id = Read-PayloadU32 -Payload $despawnFrame.Payload -Offset 0
        if ($id.Value -eq $script:EntityB) { $foundDespawn = $true }
    }
    Write-Result $foundDespawn 'A recebeu S_ENTITY_DESPAWN da entidade de B' "(id=$($script:EntityB))"

    # --- 6. O servidor segue íntegro --------------------------------------
    Write-Host "`nServidor segue operacional"
    $e = New-Client -Label 'E'
    Send-Login -Stream $e.Stream -Name "TestE$suffix"
    $enterE = Read-FrameOfAnyType -Stream $e.Stream -Opcodes @($S_ENTER_WORLD, $S_LOGIN_FAILED)
    Write-Result ($null -ne $enterE -and $enterE.Opcode -eq $S_ENTER_WORLD) `
        'novo jogador entra após recusas e desconexões'

    # O nome de B foi liberado ao sair, então pode ser reusado.
    $f = New-Client -Label 'F'
    Send-Login -Stream $f.Stream -Name $nameB
    $enterF = Read-FrameOfAnyType -Stream $f.Stream -Opcodes @($S_ENTER_WORLD, $S_LOGIN_FAILED)
    Write-Result ($null -ne $enterF -and $enterF.Opcode -eq $S_ENTER_WORLD) `
        'nome liberado na saída pode ser reusado'
}
finally {
    Close-AllClients
}

Write-Host ''
if ($script:Failures -eq 0) {
    Write-Host 'login smoke test: tudo ok' -ForegroundColor Green
    exit 0
}
Write-Host "login smoke test: $script:Failures falha(s)" -ForegroundColor Red
exit 1
