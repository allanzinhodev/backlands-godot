# Arquitetura — camadas e regras de dependência

Os dois projetos seguem a mesma ideia: **regras puras no centro, testáveis sem rede e sem
engine**; em volta, uma camada que orquestra e replica; na borda, transporte e composição.
Cada pasta tem uma responsabilidade só, e a seta de dependência aponta sempre para dentro.

## Servidor (`server/src/`)

```text
app       main.cpp (composição) e GameLoop         -> tudo
handlers  decodifica o pacote e delega             -> world, protocol, net, core
net       transporte TCP (Asio): Listener, Session -> protocol, core, world/Connection
world     orquestra o tick e replica               -> game, protocol, core
game      regras puras do jogo                     -> core
protocol  framing, opcodes, codec                  -> core
core      Config, Log                              -> nada
```

- `game/` **nunca** inclui `protocol/`, `world/`, `net/` nem Asio. Cada regra é uma função que
  recebe o `EntityRegistry` e devolve eventos (`MovementEvent`, `CombatEvent`, `ChaseEvent`,
  ids que renasceram…). Quem transforma evento em pacote é o `World`.
- `world/` **nunca** inclui Asio. O `World` fala com os clientes pela interface abstrata
  `world::Connection`; `net::Session` a implementa. É isso que permite o `test_world.cpp`
  exercitar login, caminhada e combate com uma `FakeConnection` que só guarda frames.
- `world/Replication.*` tem **um** `make_*_frame` por pacote `S_*`. Mudou o contrato em
  [protocol.md](protocol.md), muda ali — e no `ServerPacketReader.cs` do cliente.
- `protocol/Opcodes.*` tem uma tabela única: acrescentar um opcode é uma linha.
- As bibliotecas do CMake refletem isso: `backlands_core` (core + protocol + game + world, sem
  Asio) e `backlands_net` (net + handlers + GameLoop). Os testes linkam só a `core`.

Onde entra o quê:

| Quero… | Onde |
|---|---|
| uma regra nova de jogo (skill, status, drop) | `game/<Regra>.{hpp,cpp}` devolvendo eventos; testes em `tests/` |
| um pacote novo `S_*` | `protocol/Opcodes.hpp`, `world/Replication.*`, `docs/protocol.md`, e o `World` que o emite |
| um pacote novo `C_*` | `protocol/Opcodes.*`, `handlers/GameHandlers.*` (só decodifica) e um `request_*` no `World` |
| configuração nova | `core/Config.*` e `config.lua` |

## Cliente (`client/`)

Pasta = namespace (`client/scene/` → `Backlands.Scene`). Uma classe Godot por arquivo, com o
nome do arquivo.

```text
app     Main (composição) e LaunchOptions           -> tudo
dev     pré-visualizações, piloto automático, captura -> tudo menos app
game    sessão e decisões do jogador                  -> model, net, scene, ui
scene   nós 3D (câmera, mapa, entidades, efeitos)     -> model, art
ui      Controls 2D (login, HUD, barra de skills)     -> model, art
art     folhas de sprite, efeitos, shaders            -> model
net     socket, framing, codec                        -> model
model   estado puro do mundo                          -> nada (sem `using Godot`)
```

- `model/WorldState` não lê bytes: recebe mensagens já decodificadas (`ServerEvents.*`) e aplica,
  devolvendo `false` quando o conteúdo viola o contrato — o que derruba a conexão, como no
  servidor. A leitura dos bytes fica em `net/ServerPacketReader`, a montagem dos enviados em
  `net/ClientPackets`, e o `GameSession` liga as duas pontas por opcode.
- `app/Main` só **compõe**: cria os nós na ordem fixa, liga eventos e dita a ordem do quadro.
  Cada reação a um evento do mundo está escrita num handler só, na sequência que importa
  (view antes do alvo, status antes do log…). Não ponha lógica nova no `Main`; ponha na peça
  certa e chame-a de lá.
- A câmera é o `scene/CameraRig` (não é nó: cria "Sun" e "Camera" no pai). A entrada do jogador
  é o `game/InputController`; o conjuro da barra de teste é o `game/CastController`; o alvo é o
  `game/TargetSelection`.
- `scene/EntityView` cuida do movimento e da máquina de estados; o corpo é `EntitySprite`, o que
  flutua (nome, barras, anel, dano) é `EntityOverlay`.
- Nada em `art/` ou `scene/` lê a linha de comando. O interruptor de congelar animação é
  `scene/AnimationFreeze.Enabled`, ligado pelo `Main` antes de qualquer nó existir.

Onde entra o quê:

| Quero… | Onde |
|---|---|
| reagir a um pacote novo do servidor | `model/ServerEvents` (mensagem), `net/ServerPacketReader` (bytes), `model/WorldState` (estado + evento C#), `GameSession.Register`, e o handler no `Main` |
| um pedido novo ao servidor | `net/ClientPackets` e um método no `GameSession` (`IPlayerCommands`) |
| um painel novo de UI | `ui/<Painel>.cs` derivando do Control certo, criado em `Main.BuildUi` na ordem dos filhos de `Ui` |
| um efeito ou pose nova | `art/` (carregamento) e `scene/` (nó) |
| um flag de teste novo | `app/LaunchOptions` (parse) e `dev/` (o que ele faz) |

## Como conferir que nada mudou

- Servidor: `server\build.ps1` compila e roda os testes (`backlands_tests`); os `smoke_*.ps1`
  em `server/tools/` batem no servidor real.
- Cliente: `dotnet build` com zero avisos; o jogo abre pelos modos de teste do `Main`
  (`--map-preview`, `--sprite-preview=<folha>`, `--autologin` + `--screenshot`), sempre com
  `--freeze-animations` quando a captura vai ser comparada.
