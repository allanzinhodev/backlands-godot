# Emulador de referência — notas de estudo

O [Ragnarok-OldTimes](https://github.com/JulioCF/Ragnarok-OldTimes) (Cronus Fusion, fork
brasileiro do eAthena, em C) roda em `tools/oldtimes-docker/` **apenas como referência**.
Nenhum código dele entra no nosso server; o valor está em observar como um emulador maduro
resolve validação por células, framing de pacotes e autoridade do servidor.

## Como subir

```powershell
cd tools/oldtimes-docker
docker compose up -d --build
docker compose logs -f backlands-server
```

Portas: login `6900`, char `6121`, map `5121`. Banco: MariaDB 10.6 (`backlands-db`).
Toda a configuração fica em `config.lua` (fonte de verdade única) — `render_conf.lua`
gera de lá os `conf/import/*.txt` que o parser em C exige.

Para derrubar e recomeçar do zero: `docker compose down -v`.

## Armadilhas resolvidas no build (2013 → toolchain atual)

| Sintoma | Causa | Correção aplicada |
|---|---|---|
| `'my_bool' undeclared` em `src/common/sql.c` | MariaDB Connector/C 3.x removeu o typedef `my_bool` do MySQL 5.x | `-Dmy_bool=bool` no `CFLAGS`. O header atual declara `MYSQL_BIND::is_null/error` como `bool*` e já inclui `<stdbool.h>`, então o mapeamento casa exatamente (mapear para `char` compila, mas gera avisos de ponteiro incompatível) |
| `multiple definition of ...` | gcc ≥ 10 usa `-fno-common`; o código depende de definições tentativas | `-fcommon` no `CFLAGS` |
| `ERROR 1071: Specified key was too long; max key length is 1000 bytes` | `global_reg_value` tem `PRIMARY KEY (char_id, str, account_id)` com `varchar(255)` em MyISAM; sob utf8mb4 (default da imagem MariaDB) 255 caracteres ocupam 1020 bytes | servidor em `latin1` via `command:` no compose |
| `ERROR 1050: Table 'picklog' already exists` | `logs.sql` recria tabelas que `main.sql` já criou, sem `IF NOT EXISTS` | import estrito só no `main.sql`; os complementares usam `--force` e não são fatais |
| `./login-server: not found` | `make sql` gera binários com sufixo `_sql` | entrypoint resolve `*-server_sql` antes de `*-server` |
| `\r: command not found` | scripts do repo vêm com CRLF | `dos2unix` no build |
| LTO | ligada por padrão neste `configure` | `--disable-lto` |

Observação: o repo tem um erro de conteúdo próprio (`Missing 1 right curlys at
npc/re/quests/quests_dicastes.txt line 6520`). É bug do fork, não da nossa infra, e não
impede o boot.

## O que estudar no fonte (não precisa rodar para ler)

Dentro do container: `/opt/cronus/src/`.

- `src/map/path.c` — A\* do servidor: heap binário, custo 10 ortogonal / 14 diagonal,
  heurística Manhattan × 10, `MAX_WALKPATH = 32` células, proibição de corner-cutting
  (diagonal só se as duas ortogonais adjacentes forem livres), e a célula de origem
  deliberadamente **não** validada (senão o personagem fica preso).
- `src/map/map.c` — `map_getcell` e os flags de célula vindos do `.gat` do client
  (`CELL_CHKPASS`, `CELL_CHKNOPASS`, `CELL_CHKREACH`).
- `src/map/unit.c` — `unit_walktoxy_timer`: o servidor anda **uma célula por vez** por
  timer, revalidando a próxima célula a cada passo (obstáculos dinâmicos como ice wall),
  com atraso de `speed` ms por célula e `speed * 14 / 10` na diagonal.
- `src/map/clif.c` — framing e os pacotes. O pedido de andar é `0x0085` e carrega apenas a
  **célula destino**: quem decide o caminho é o servidor.

## Modelo que o nosso server imita (M4)

1. O client envia destino, nunca passos.
2. O servidor faz A\* sobre o grid lógico e responde com o caminho + tick de início.
3. A posição autoritativa é sempre uma célula inteira.
4. O client só interpola visualmente; nunca decide walkability.

Diferenças deliberadas nossas:

- Framing uniforme `uint16 opcode + uint16 length` em vez da tabela de tamanhos fixos do
  RO (só precisaríamos dela para falar com um client RO real).
- O grid vem de um blob exportado do Godot (`tools/export_grid/`), não de um `.gat`.
- Em vez de um timer do SO por entidade, cada entidade guarda `next_step_at` e o tick de
  20 Hz avança quem está vencido.
