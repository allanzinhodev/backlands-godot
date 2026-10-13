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
client/                 cenas e scripts C# do jogo
server/                 servidor C++ (CMake + vcpkg + Asio)
tools/oldtimes-docker/  emulador de referência (Docker)
tools/export_grid/      exportador do grid lógico (Godot tool script)
docs/                   GDD, protocolo, notas de estudo
```

## Build do server

Requisitos: Visual Studio 2022 (MSVC), CMake, vcpkg (`VCPKG_ROOT` definido).

```powershell
cd server
cmake --preset debug
cmake --build --preset debug
ctest --preset debug
```

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
