# backlands-godot

MMORPG 2.5D estilo Ragnarok Online.

- **Client**: Godot 4.7 (.NET / C#) — raiz deste repositório.
- **Server**: C++20 + Boost.Asio, autoritativo — [server/](server/).
- **Protocolo**: TCP binário (opcode + length), contrato em [docs/protocol.md](docs/protocol.md).
- **Emulador de referência**: [Ragnarok-OldTimes](https://github.com/JulioCF/Ragnarok-OldTimes) (Cronus/eAthena)
  rodando via Docker em [tools/oldtimes-docker/](tools/oldtimes-docker/) — apenas para estudo de
  protocolo e validação por células; nenhum código é copiado.

## Estrutura

```text
client/                 scripts C# do jogo, uma pasta por camada (pasta = namespace)
  app/                  raiz de composição (Main) e linha de comando
  model/                estado puro do mundo, sem Godot
  net/                  socket, framing e codec dos pacotes
  game/                 sessão e decisões do jogador (alvo, conjuro, entrada)
  scene/                nós 3D: câmera, mapa, entidades, efeitos
  art/                  folhas de sprite, efeitos e shaders
  ui/                   painéis 2D: login, HUD, barra de skills
  dev/                  pré-visualizações e captura de tela do playtest
server/                 servidor C++ (CMake + vcpkg + Asio)
  src/game/             regras puras (sem rede): grid, A*, movimento, combate, IA
  src/world/            orquestra o tick e replica aos clientes (sem Asio)
  src/protocol/         framing, opcodes e codec
  src/net/, handlers/   transporte TCP e roteamento de pacotes
  src/app/              main e game loop
tools/oldtimes-docker/  emulador de referência (Docker)
docs/                   GDD, protocolo, arquitetura, notas de estudo
```

Quem pode depender de quem, e onde cada coisa nova entra: [docs/arquitetura.md](docs/arquitetura.md).

## Jogar / testar

```powershell
cd server; .\build.ps1; cd ..     # compila o servidor (só na primeira vez)
.\tools\playtest.ps1              # sobe o servidor e abre 2 clientes
```

Cada janela entra com um personagem próprio. **Clique no chão para andar.** O rio só é
atravessável pela ponte: clicar na água não move, porque o servidor recusa o destino.

```powershell
.\tools\playtest.ps1 -Clients 1       # um cliente só
.\tools\playtest.ps1 -Screenshot      # automatizado: salva PNGs em .playtest/ e encerra
```

Para derrubar o servidor: `Get-Process backlands_server | Stop-Process`.

## Build do server

Requisitos: Visual Studio com toolset C++ (MSVC), vcpkg com `VCPKG_ROOT` definido.
O `build.ps1` resolve MSVC, CMake e Ninja do Visual Studio (que não ficam no PATH),
configura, compila e roda os testes:

```powershell
cd server
.\build.ps1              # Debug + testes
.\build.ps1 -Preset release
```

Rodar o servidor e validar o protocolo de ponta a ponta:

```powershell
.\build\debug\backlands_server.exe
.\tools\smoke_ping.ps1   # PING/PONG + rejeição de pacotes adulterados
```

Configuração em [server/config.lua](server/config.lua) (porta, taxa de tick), lida em
tempo de execução — não exige recompilar.

## Emulador de referência

```powershell
cd tools/oldtimes-docker
docker compose up -d --build
docker compose logs -f backlands-server
```

Login 6900 / Char 6121 / Map 5121; MariaDB 10.6. Toda a configuração fica em um único
[config.lua](tools/oldtimes-docker/config.lua) — o entrypoint renderiza de lá os arquivos
`conf/import/*.txt` que o parser em C do emulador exige.

Notas de build e o que estudar no fonte: [docs/oldtimes-notes.md](docs/oldtimes-notes.md).
