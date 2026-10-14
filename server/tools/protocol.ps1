<#
.SYNOPSIS
    Helpers do protocolo Backlands para os scripts de teste.

    Dot-source este arquivo: . "$PSScriptRoot\protocol.ps1"

    Mantém o framing em um só lugar: os scripts de smoke test falam o mesmo
    contrato de docs/protocol.md que o servidor C++ e o client C#.
#>

# --- Opcodes (espelham server/src/net/Opcodes.hpp) -------------------------
$script:C_PING = 0x0001
$script:C_LOGIN = 0x0002
$script:C_WALK_TO = 0x0003
$script:C_ATTACK = 0x0004

$script:S_PONG = 0x1001
$script:S_ENTER_WORLD = 0x1002
$script:S_LOGIN_FAILED = 0x1003
$script:S_ENTITY_SPAWN = 0x1004
$script:S_ENTITY_DESPAWN = 0x1005
$script:S_MAP_INFO = 0x1006
$script:S_MAP_ROW = 0x1007
$script:S_ENTITY_WALK = 0x1008
$script:S_ENTITY_STOP = 0x1009
$script:S_ENTITY_ATTACK = 0x100A
$script:S_ENTITY_DIED = 0x100B
$script:S_ENTITY_RESPAWN = 0x100C
$script:S_ATTACK_REJECTED = 0x100D

# Precisa bater com kProtocolVersion em server/src/net/Protocol.hpp.
$script:PROTOCOL_VERSION = 2

# Motivos de S_LOGIN_FAILED (EntityRegistry::SpawnError).
$script:LOGIN_FAIL_INVALID_NAME = 1
$script:LOGIN_FAIL_NAME_IN_USE = 2
$script:LOGIN_FAIL_ALREADY_SPAWNED = 3
$script:LOGIN_FAIL_PROTOCOL = 4

function Get-OpcodeName {
    param([int]$Opcode)
    switch ($Opcode) {
        0x0001 { 'C_PING' }
        0x0002 { 'C_LOGIN' }
        0x0003 { 'C_WALK_TO' }
        0x0004 { 'C_ATTACK' }
        0x1001 { 'S_PONG' }
        0x1002 { 'S_ENTER_WORLD' }
        0x1003 { 'S_LOGIN_FAILED' }
        0x1004 { 'S_ENTITY_SPAWN' }
        0x1005 { 'S_ENTITY_DESPAWN' }
        0x1006 { 'S_MAP_INFO' }
        0x1007 { 'S_MAP_ROW' }
        0x1008 { 'S_ENTITY_WALK' }
        0x1009 { 'S_ENTITY_STOP' }
        0x100A { 'S_ENTITY_ATTACK' }
        0x100B { 'S_ENTITY_DIED' }
        0x100C { 'S_ENTITY_RESPAWN' }
        0x100D { 'S_ATTACK_REJECTED' }
        default { "0x$($Opcode.ToString('X4'))" }
    }
}

function Connect-BacklandsServer {
    param(
        [string]$ServerHost = '127.0.0.1',
        [int]$Port = 7777,
        [int]$TimeoutMs = 3000
    )
    $client = [System.Net.Sockets.TcpClient]::new()
    $client.Connect($ServerHost, $Port)
    $client.ReceiveTimeout = $TimeoutMs
    $client.SendTimeout = $TimeoutMs
    return $client
}

# Monta um frame: uint16 opcode + uint16 length + payload, little-endian.
# -Length permite forjar um header inconsistente de propósito nos testes.
function New-Frame {
    param([uint16]$Opcode, [byte[]]$Payload = @(), [Nullable[int]]$Length = $null)

    $total = if ($null -ne $Length) { $Length } else { 4 + $Payload.Length }
    $frame = [System.Collections.Generic.List[byte]]::new()
    $frame.AddRange([BitConverter]::GetBytes($Opcode))
    $frame.AddRange([BitConverter]::GetBytes([uint16]$total))
    if ($Payload.Length -gt 0) { $frame.AddRange($Payload) }
    return $frame.ToArray()
}

# String no fio: uint16 length + bytes UTF-8, sem terminador nulo.
function New-ProtoString {
    param([string]$Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $out = [System.Collections.Generic.List[byte]]::new()
    $out.AddRange([BitConverter]::GetBytes([uint16]$bytes.Length))
    $out.AddRange($bytes)
    return $out.ToArray()
}

# Lê exatamente $Count bytes, ou devolve $null se a conexão fechar antes.
function Read-Exactly {
    param([System.Net.Sockets.NetworkStream]$Stream, [int]$Count)

    if ($Count -eq 0) { return @() }
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

# Lê um frame completo. Devolve $null se a conexão fechou.
function Read-Frame {
    param([System.Net.Sockets.NetworkStream]$Stream)

    $header = Read-Exactly -Stream $Stream -Count 4
    if ($null -eq $header) { return $null }

    $length = [BitConverter]::ToUInt16($header, 2)
    $payload = Read-Exactly -Stream $Stream -Count ($length - 4)
    if ($null -eq $payload) { return $null }

    return [pscustomobject]@{
        Opcode  = [int][BitConverter]::ToUInt16($header, 0)
        Length  = [int]$length
        Payload = [byte[]]$payload
    }
}

# Lê até achar o opcode esperado, pulando broadcasts que chegaram no meio
# (S_ENTITY_SPAWN de quem já estava no mundo, despawn de outro jogador...).
# Devolve $null se a conexão fechar ou se o limite de frames for atingido.
function Read-FrameOfType {
    param(
        [System.Net.Sockets.NetworkStream]$Stream,
        [int]$Opcode,
        # Alto de proposito: ao entrar no mundo o servidor manda o mapa inteiro
        # (uma linha por pacote) ANTES do S_ENTER_WORLD, entao um limite baixo
        # faria o teste desistir antes do pacote que ele procura.
        [int]$MaxFrames = 100
    )
    for ($i = 0; $i -lt $MaxFrames; $i++) {
        $frame = Read-Frame -Stream $Stream
        if ($null -eq $frame) { return $null }
        if ($frame.Opcode -eq $Opcode) { return $frame }
    }
    return $null
}

# Igual ao anterior, mas aceita qualquer um de vários opcodes — útil quando a
# resposta correta pode ser sucesso OU recusa.
function Read-FrameOfAnyType {
    param(
        [System.Net.Sockets.NetworkStream]$Stream,
        [int[]]$Opcodes,
        # Alto de proposito: ao entrar no mundo o servidor manda o mapa inteiro
        # (uma linha por pacote) ANTES do S_ENTER_WORLD, entao um limite baixo
        # faria o teste desistir antes do pacote que ele procura.
        [int]$MaxFrames = 100
    )
    for ($i = 0; $i -lt $MaxFrames; $i++) {
        $frame = Read-Frame -Stream $Stream
        if ($null -eq $frame) { return $null }
        if ($Opcodes -contains $frame.Opcode) { return $frame }
    }
    return $null
}

# Lê até achar um pacote de um opcode PARA UMA ENTIDADE específica.
#
# Necessário desde que o mundo tem monstros: eles passeiam o tempo todo, então
# o primeiro S_ENTITY_WALK que chega quase nunca é o do jogador que o teste
# acabou de mover. Vale para os pacotes cujo primeiro campo é o id (walk, stop,
# despawn, respawn).
function Find-FrameForEntity {
    param(
        [System.Net.Sockets.NetworkStream]$Stream,
        [int]$Opcode,
        [uint32]$EntityId,
        [int]$MaxFrames = 120
    )

    for ($i = 0; $i -lt $MaxFrames; $i++) {
        $frame = Read-Frame -Stream $Stream
        if ($null -eq $frame) { return $null }
        if ($frame.Opcode -ne $Opcode) { continue }
        if ([BitConverter]::ToUInt32($frame.Payload, 0) -eq $EntityId) { return $frame }
    }
    return $null
}

function Send-Frame {
    param([System.Net.Sockets.NetworkStream]$Stream, [byte[]]$Frame)
    $Stream.Write($Frame, 0, $Frame.Length)
    $Stream.Flush()
}

function Send-Login {
    param(
        [System.Net.Sockets.NetworkStream]$Stream,
        [string]$Name,
        [Nullable[int]]$Version = $null
    )
    $payload = [System.Collections.Generic.List[byte]]::new()
    $v = if ($null -ne $Version) { $Version } else { $script:PROTOCOL_VERSION }
    $payload.AddRange([BitConverter]::GetBytes([uint16]$v))
    $payload.AddRange([byte[]](New-ProtoString $Name))
    Send-Frame -Stream $Stream -Frame (New-Frame -Opcode $script:C_LOGIN -Payload $payload.ToArray())
}

# --- Leitura de payload ----------------------------------------------------
# Cada função devolve o valor e o próximo offset, para o chamador encadear.

function Read-PayloadU8 {
    param([byte[]]$Payload, [int]$Offset)
    return [pscustomobject]@{ Value = [int]$Payload[$Offset]; Next = $Offset + 1 }
}

function Read-PayloadU16 {
    param([byte[]]$Payload, [int]$Offset)
    return [pscustomobject]@{ Value = [int][BitConverter]::ToUInt16($Payload, $Offset); Next = $Offset + 2 }
}

function Read-PayloadU32 {
    param([byte[]]$Payload, [int]$Offset)
    return [pscustomobject]@{ Value = [BitConverter]::ToUInt32($Payload, $Offset); Next = $Offset + 4 }
}

function Read-PayloadI32 {
    param([byte[]]$Payload, [int]$Offset)
    return [pscustomobject]@{ Value = [BitConverter]::ToInt32($Payload, $Offset); Next = $Offset + 4 }
}

function Read-PayloadString {
    param([byte[]]$Payload, [int]$Offset)
    $length = [int][BitConverter]::ToUInt16($Payload, $Offset)
    $start = $Offset + 2
    $value = if ($length -eq 0) { '' } else { [System.Text.Encoding]::UTF8.GetString($Payload, $start, $length) }
    return [pscustomobject]@{ Value = $value; Next = $start + $length }
}

# S_ENTER_WORLD: u32 id, u16 x, u16 y, u8 dir, i32 hp, i32 hp_max,
#                string sprite, string mapa, string nome.
function Read-EnterWorld {
    param([byte[]]$Payload)
    $id = Read-PayloadU32 -Payload $Payload -Offset 0
    $x = Read-PayloadU16 -Payload $Payload -Offset $id.Next
    $y = Read-PayloadU16 -Payload $Payload -Offset $x.Next
    $dir = Read-PayloadU8 -Payload $Payload -Offset $y.Next
    $hp = Read-PayloadI32 -Payload $Payload -Offset $dir.Next
    $maxHp = Read-PayloadI32 -Payload $Payload -Offset $hp.Next
    $sprite = Read-PayloadString -Payload $Payload -Offset $maxHp.Next
    $map = Read-PayloadString -Payload $Payload -Offset $sprite.Next
    $name = Read-PayloadString -Payload $Payload -Offset $map.Next
    return [pscustomobject]@{
        EntityId = $id.Value; X = $x.Value; Y = $y.Value
        Direction = $dir.Value; Hp = $hp.Value; MaxHp = $maxHp.Value
        Sprite = $sprite.Value; Map = $map.Value; Name = $name.Value
        BytesRead = $name.Next
    }
}

# S_ENTITY_SPAWN: u32 id, u16 x, u16 y, u8 dir, u8 tipo, i32 hp, i32 hp_max,
#                 string sprite, string nome.
function Read-EntitySpawn {
    param([byte[]]$Payload)
    $id = Read-PayloadU32 -Payload $Payload -Offset 0
    $x = Read-PayloadU16 -Payload $Payload -Offset $id.Next
    $y = Read-PayloadU16 -Payload $Payload -Offset $x.Next
    $dir = Read-PayloadU8 -Payload $Payload -Offset $y.Next
    $kind = Read-PayloadU8 -Payload $Payload -Offset $dir.Next
    $hp = Read-PayloadI32 -Payload $Payload -Offset $kind.Next
    $maxHp = Read-PayloadI32 -Payload $Payload -Offset $hp.Next
    $sprite = Read-PayloadString -Payload $Payload -Offset $maxHp.Next
    $name = Read-PayloadString -Payload $Payload -Offset $sprite.Next
    return [pscustomobject]@{
        EntityId = $id.Value; X = $x.Value; Y = $y.Value
        Direction = $dir.Value; Kind = $kind.Value
        Hp = $hp.Value; MaxHp = $maxHp.Value
        Sprite = $sprite.Value; Name = $name.Value
        BytesRead = $name.Next
    }
}

# S_ENTITY_ATTACK: u32 atacante, u32 alvo, u16 x, u16 y, u8 dir,
#                  u16 motion_ms, i32 dano, i32 hp_restante.
function Read-EntityAttack {
    param([byte[]]$Payload)
    $src = Read-PayloadU32 -Payload $Payload -Offset 0
    $dst = Read-PayloadU32 -Payload $Payload -Offset $src.Next
    $x = Read-PayloadU16 -Payload $Payload -Offset $dst.Next
    $y = Read-PayloadU16 -Payload $Payload -Offset $x.Next
    $dir = Read-PayloadU8 -Payload $Payload -Offset $y.Next
    $motion = Read-PayloadU16 -Payload $Payload -Offset $dir.Next
    $damage = Read-PayloadI32 -Payload $Payload -Offset $motion.Next
    $hp = Read-PayloadI32 -Payload $Payload -Offset $damage.Next
    return [pscustomobject]@{
        Source = $src.Value; Target = $dst.Value
        X = $x.Value; Y = $y.Value; Direction = $dir.Value
        MotionMs = $motion.Value; Damage = $damage.Value; Hp = $hp.Value
        BytesRead = $hp.Next
    }
}

function Send-Attack {
    param([System.Net.Sockets.NetworkStream]$Stream, [uint32]$TargetId)
    Send-Frame -Stream $Stream -Frame (New-Frame -Opcode $script:C_ATTACK `
        -Payload ([BitConverter]::GetBytes($TargetId)))
}

# Confirma que o servidor fechou a conexão (leitura devolve 0 / erro).
function Test-Disconnected {
    param([System.Net.Sockets.NetworkStream]$Stream)
    return $null -eq (Read-Exactly -Stream $Stream -Count 1)
}
