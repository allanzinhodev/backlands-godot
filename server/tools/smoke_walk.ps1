<#
.SYNOPSIS
    Teste de integração do movimento validado pelo servidor (marco M4).

    Prova que o servidor é a autoridade sobre posição: o client manda apenas um
    DESTINO, e o servidor decide o caminho. Destino dentro do rio, fora do mapa
    ou longe demais é recusado, e a recusa devolve a posição autoritativa.

    Também valida que o mapa chega ao client (uma linha por pacote), que o rio
    está onde o servidor diz, e que um segundo jogador vê o movimento do
    primeiro.

.EXAMPLE
    .\smoke_walk.ps1
#>
param(
    [string]$ServerHost = '127.0.0.1',
    [int]$Port = 7777,
    [int]$TimeoutMs = 4000
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
    $client = Connect-BacklandsServer -ServerHost $ServerHost -Port $Port -TimeoutMs $TimeoutMs
    $script:Clients += $client
    return [pscustomobject]@{ Tcp = $client; Stream = $client.GetStream() }
}

# Entra no mundo e coleta o mapa + a própria entidade.
function Enter-World {
    param([string]$Name)

    $c = New-Client
    Send-Login -Stream $c.Stream -Name $Name

    $info = $null
    $rows = @{}
    $enter = $null

    # O servidor manda S_MAP_INFO, as linhas do mapa, e então S_ENTER_WORLD.
    for ($i = 0; $i -lt 200; $i++) {
        $frame = Read-Frame -Stream $c.Stream
        if ($null -eq $frame) { break }

        switch ($frame.Opcode) {
            $S_MAP_INFO {
                $w = Read-PayloadU16 -Payload $frame.Payload -Offset 0
                $h = Read-PayloadU16 -Payload $frame.Payload -Offset $w.Next
                $n = Read-PayloadString -Payload $frame.Payload -Offset $h.Next
                $info = [pscustomobject]@{ Width = $w.Value; Height = $h.Value; Name = $n.Value }
            }
            $S_MAP_ROW {
                $y = Read-PayloadU16 -Payload $frame.Payload -Offset 0
                $len = Read-PayloadU16 -Payload $frame.Payload -Offset $y.Next
                $cells = [byte[]]::new($len.Value)
                [Array]::Copy($frame.Payload, $len.Next, $cells, 0, $len.Value)
                $rows[$y.Value] = $cells
            }
            $S_ENTER_WORLD {
                $enter = Read-EnterWorld -Payload $frame.Payload
            }
        }
        if ($null -ne $enter) { break }
    }

    return [pscustomobject]@{
        Client = $c; Stream = $c.Stream; MapInfo = $info; Rows = $rows; Enter = $enter
    }
}

function Send-WalkTo {
    param([System.Net.Sockets.NetworkStream]$Stream, [int]$X, [int]$Y)
    $payload = [System.Collections.Generic.List[byte]]::new()
    $payload.AddRange([BitConverter]::GetBytes([uint16]$X))
    $payload.AddRange([BitConverter]::GetBytes([uint16]$Y))
    Send-Frame -Stream $Stream -Frame (New-Frame -Opcode $C_WALK_TO -Payload $payload.ToArray())
}

# S_ENTITY_WALK: u32 id, u16 x, u16 y, u16 speed, u8 len, u8 path[len]
function Read-EntityWalk {
    param([byte[]]$Payload)
    $id = Read-PayloadU32 -Payload $Payload -Offset 0
    $x = Read-PayloadU16 -Payload $Payload -Offset $id.Next
    $y = Read-PayloadU16 -Payload $Payload -Offset $x.Next
    $speed = Read-PayloadU16 -Payload $Payload -Offset $y.Next
    $len = Read-PayloadU8 -Payload $Payload -Offset $speed.Next
    $path = @()
    for ($i = 0; $i -lt $len.Value; $i++) { $path += [int]$Payload[$len.Next + $i] }
    return [pscustomobject]@{
        EntityId = $id.Value; X = $x.Value; Y = $y.Value
        Speed = $speed.Value; Path = $path; BytesRead = $len.Next + $len.Value
    }
}

# Mesmos deslocamentos de server/src/game/Pathfinder.cpp.
$script:Deltas = @(
    @(0, 1), @(-1, 1), @(-1, 0), @(-1, -1), @(0, -1), @(1, -1), @(1, 0), @(1, 1)
)

$CELL_WALKABLE = 0x01

$suffix = (Get-Random -Minimum 1000 -Maximum 9999)

Write-Host "backlands-server walk smoke test -> ${ServerHost}:${Port}" -ForegroundColor Cyan

try {
    # --- 1. O mapa chega ao client ----------------------------------------
    Write-Host "`nMapa enviado pelo servidor"
    $a = Enter-World -Name "WalkA$suffix"

    if ($null -eq $a.Enter) {
        Write-Result $false 'entrou no mundo' '(sem S_ENTER_WORLD)'
        throw 'sem entrada no mundo'
    }

    Write-Result ($null -ne $a.MapInfo) 'recebeu S_MAP_INFO'
    Write-Result ($a.MapInfo.Name -eq 'starter_fields') 'nome do mapa' "(veio '$($a.MapInfo.Name)')"
    Write-Result ($a.MapInfo.Width -eq 32 -and $a.MapInfo.Height -eq 32) 'dimensões 32x32' `
        "(veio $($a.MapInfo.Width)x$($a.MapInfo.Height))"
    Write-Result ($a.Rows.Count -eq $a.MapInfo.Height) 'recebeu todas as linhas do grid' `
        "($($a.Rows.Count) de $($a.MapInfo.Height))"

    $allRowsFull = $true
    foreach ($y in $a.Rows.Keys) {
        if ($a.Rows[$y].Length -ne $a.MapInfo.Width) { $allRowsFull = $false; break }
    }
    Write-Result $allRowsFull 'cada linha tem uma célula por coluna'

    # O rio precisa estar no grid que o client recebeu, senão o visual
    # divergiria da lógica do servidor.
    $riverBlocked = (($a.Rows[10][15] -band $CELL_WALKABLE) -eq 0)
    $bridgeOpen = (($a.Rows[16][15] -band $CELL_WALKABLE) -ne 0)
    Write-Result $riverBlocked 'o rio chega ao client como não caminhável'
    Write-Result $bridgeOpen 'a ponte chega ao client como caminhável'

    $spawnX = $a.Enter.X
    $spawnY = $a.Enter.Y
    Write-Result (($a.Rows[$spawnY][$spawnX] -band $CELL_WALKABLE) -ne 0) `
        'o spawn é caminhável no grid recebido' "($spawnX,$spawnY)"

    # --- 2. Caminhada válida ----------------------------------------------
    Write-Host "`nCaminhada validada pelo servidor"
    $targetX = $spawnX + 4
    Send-WalkTo -Stream $a.Stream -X $targetX -Y $spawnY

    $walkFrame = Find-FrameForEntity -Stream $a.Stream -Opcode $S_ENTITY_WALK `
        -EntityId $a.Enter.EntityId
    if ($null -eq $walkFrame) {
        Write-Result $false 'recebeu S_ENTITY_WALK' "(veio $(Get-OpcodeName $walkFrame.Opcode))"
    }
    else {
        $walk = Read-EntityWalk -Payload $walkFrame.Payload
        Write-Result ($walk.EntityId -eq $a.Enter.EntityId) 'o trajeto é da nossa entidade'
        Write-Result ($walk.X -eq $spawnX -and $walk.Y -eq $spawnY) `
            'o trajeto parte da posição autoritativa' "(veio $($walk.X),$($walk.Y))"
        Write-Result ($walk.Speed -gt 0) 'velocidade informada' "($($walk.Speed) ms/célula)"
        Write-Result ($walk.Path.Count -eq 4) 'caminho de 4 passos' "(veio $($walk.Path.Count))"
        Write-Result ($walk.BytesRead -eq $walkFrame.Payload.Length) 'payload consumido por inteiro'

        # Percorre o caminho recebido e confere que ele chega ao destino
        # usando só células caminháveis do grid que o próprio servidor mandou.
        $cx = $walk.X
        $cy = $walk.Y
        $onlyWalkable = $true
        foreach ($dir in $walk.Path) {
            if ($dir -lt 0 -or $dir -gt 7) { $onlyWalkable = $false; break }
            $cx += $script:Deltas[$dir][0]
            $cy += $script:Deltas[$dir][1]
            if (($a.Rows[$cy][$cx] -band $CELL_WALKABLE) -eq 0) { $onlyWalkable = $false; break }
        }
        Write-Result $onlyWalkable 'todo o caminho passa por células caminháveis'
        Write-Result ($cx -eq $targetX -and $cy -eq $spawnY) 'o caminho chega ao destino pedido' `
            "(terminou em $cx,$cy)"
    }

    # --- 3. Destinos recusados --------------------------------------------
    Write-Host "`nDestinos inválidos são recusados"

    # Dentro do rio.
    Send-WalkTo -Stream $a.Stream -X 15 -Y 20
    $stopFrame = Find-FrameForEntity -Stream $a.Stream -Opcode $S_ENTITY_STOP `
        -EntityId $a.Enter.EntityId
    Write-Result ($null -ne $stopFrame) 'andar para dentro do rio é recusado'

    # Fora do mapa.
    Send-WalkTo -Stream $a.Stream -X 9999 -Y 9999
    $stopFrame2 = Find-FrameForEntity -Stream $a.Stream -Opcode $S_ENTITY_STOP `
        -EntityId $a.Enter.EntityId
    Write-Result ($null -ne $stopFrame2) 'andar para fora do mapa é recusado'

    # Em cima da borda sólida.
    Send-WalkTo -Stream $a.Stream -X 0 -Y 0
    $stopFrame3 = Find-FrameForEntity -Stream $a.Stream -Opcode $S_ENTITY_STOP `
        -EntityId $a.Enter.EntityId
    Write-Result ($null -ne $stopFrame3) 'andar para dentro da pedra é recusado'

    # A recusa devolve a posição autoritativa, para o client corrigir qualquer
    # animação otimista que tenha começado.
    if ($null -ne $stopFrame3) {
        $id = Read-PayloadU32 -Payload $stopFrame3.Payload -Offset 0
        $sx = Read-PayloadU16 -Payload $stopFrame3.Payload -Offset $id.Next
        $sy = Read-PayloadU16 -Payload $stopFrame3.Payload -Offset $sx.Next
        Write-Result (($a.Rows[$sy.Value][$sx.Value] -band $CELL_WALKABLE) -ne 0) `
            'a recusa devolve uma célula válida' "($($sx.Value),$($sy.Value))"
    }

    # --- 4. Travessia do rio pela ponte -----------------------------------
    Write-Host "`nTravessia do rio pela ponte"
    # Da margem oeste para a leste, na altura da ponte.
    Send-WalkTo -Stream $a.Stream -X 25 -Y 16
    $crossFrame = Find-FrameForEntity -Stream $a.Stream -Opcode $S_ENTITY_WALK `
        -EntityId $a.Enter.EntityId
    if ($null -eq $crossFrame) {
        Write-Result $false 'travessia aceita' "(veio $(Get-OpcodeName $crossFrame.Opcode))"
    }
    else {
        $cross = Read-EntityWalk -Payload $crossFrame.Payload
        $cx = $cross.X
        $cy = $cross.Y
        $steppedOnWater = $false
        foreach ($dir in $cross.Path) {
            $cx += $script:Deltas[$dir][0]
            $cy += $script:Deltas[$dir][1]
            if (($a.Rows[$cy][$cx] -band $CELL_WALKABLE) -eq 0) { $steppedOnWater = $true }
        }
        Write-Result (-not $steppedOnWater) 'a travessia não pisa na água'
        Write-Result ($cx -eq 25 -and $cy -eq 16) 'a travessia chega à margem leste' `
            "(terminou em $cx,$cy)"
    }

    # --- 5. O outro jogador vê o movimento --------------------------------
    Write-Host "`nMovimento replicado para outro jogador"
    $b = Enter-World -Name "WalkB$suffix"
    Write-Result ($null -ne $b.Enter) 'segundo jogador entrou'

    # B deve ver o spawn de A.
    $spawnOfA = Find-FrameForEntity -Stream $b.Stream -Opcode $S_ENTITY_SPAWN `
        -EntityId $a.Enter.EntityId
    Write-Result ($null -ne $spawnOfA) 'B vê a entidade de A'

    # A anda; B precisa receber o trajeto.
    Send-WalkTo -Stream $a.Stream -X 20 -Y 16
    $walkOnB = Find-FrameForEntity -Stream $b.Stream -Opcode $S_ENTITY_WALK `
        -EntityId $a.Enter.EntityId
    if ($null -eq $walkOnB) {
        Write-Result $false 'B recebe o trajeto de A' '(nada chegou)'
    }
    else {
        $seen = Read-EntityWalk -Payload $walkOnB.Payload
        Write-Result ($seen.EntityId -eq $a.Enter.EntityId) 'o trajeto visto por B é o de A' `
            "(veio entidade $($seen.EntityId))"
        Write-Result ($seen.Path.Count -gt 0) 'o trajeto visto por B tem passos'
    }

    # --- 6. Pedido de caminhada antes de entrar derruba a sessão ----------
    Write-Host "`nCaminhada antes de entrar no mundo"
    $c = New-Client
    Send-WalkTo -Stream $c.Stream -X 10 -Y 10
    Write-Result (Test-Disconnected -Stream $c.Stream) `
        'C_WALK_TO sem login derruba a sessão'

    # --- 7. O servidor segue íntegro --------------------------------------
    Write-Host "`nServidor segue operacional"
    $d = Enter-World -Name "WalkD$suffix"
    Write-Result ($null -ne $d.Enter) 'novo jogador entra depois de tudo isso'
}
finally {
    foreach ($c in $script:Clients) { try { $c.Close() } catch { } }
}

Write-Host ''
if ($script:Failures -eq 0) {
    Write-Host 'walk smoke test: tudo ok' -ForegroundColor Green
    exit 0
}
Write-Host "walk smoke test: $script:Failures falha(s)" -ForegroundColor Red
exit 1
