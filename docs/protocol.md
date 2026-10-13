# Protocolo Backlands — contrato client/servidor

Transporte: **TCP**. Encoding: **little-endian** em tudo.

## Framing

Todo pacote tem header fixo de 4 bytes:

```text
offset 0  uint16  opcode
offset 2  uint16  length   (tamanho TOTAL do pacote, header incluído; >= 4)
```

Regras (todas aplicadas antes de qualquer alocação, no `Session::read_header`):

- `length < 4` ou `length > 2048` (`kMaxPacketSize`) → desconectar.
- Opcode desconhecido, ou opcode de servidor vindo do client → desconectar.
- Payload menor que o esperado pelo handler → desconectar.
- Strings: `uint16 len` + bytes UTF-8 (sem terminador nulo), `len <= 255`.
- Células: `uint16 x` + `uint16 y`.

O lado do servidor usa `PacketReader`, que faz bounds-check em toda leitura e
nunca lança: ao faltar byte, marca falha e devolve zero, e a falha é
**persistente** (nenhuma leitura posterior tem sucesso). O handler checa uma vez
no fim; pacote truncado derruba a sessão em vez de agir sobre dado parcial.

Direção dos opcodes: `C_*` = client→server (0x0001–0x0FFF), `S_*` = server→client (0x1001–0x1FFF).

## Tabela de opcodes (viva — cresce por marco)

| Opcode | Nome | Payload | Marco |
|---|---|---|---|
| 0x0001 | C_PING | `uint32 nonce` | M2 |
| 0x0002 | C_LOGIN | `string nome` | M3 |
| 0x0003 | C_WALK_TO | `uint16 x`, `uint16 y` (célula destino) | M4 |
| 0x0004 | C_ATTACK | `uint32 entity_id` (alvo) | M5 |
| 0x1001 | S_PONG | `uint32 nonce` | M2 |
| 0x1002 | S_ENTER_WORLD | `uint32 entity_id`, `uint16 x`, `uint16 y`, `uint8 dir`, `int32 hp`, `int32 hp_max`, `string mapa`, `string nome` | M3/M5 |
| 0x1003 | S_LOGIN_FAILED | `uint8 motivo` | M3 |
| 0x1004 | S_ENTITY_SPAWN | `uint32 entity_id`, `uint16 x`, `uint16 y`, `uint8 dir`, `uint8 tipo`, `int32 hp`, `int32 hp_max`, `string nome` | M3/M5 |
| 0x1005 | S_ENTITY_DESPAWN | `uint32 entity_id` | M3 |
| 0x1006 | S_MAP_INFO | `uint16 largura`, `uint16 altura`, `string nome` | M4 |
| 0x1007 | S_MAP_ROW | `uint16 y`, `uint16 n`, `uint8 células[n]` | M4 |
| 0x1008 | S_ENTITY_WALK | `uint32 entity_id`, `uint16 x`, `uint16 y`, `uint16 ms_por_célula`, `uint8 n`, `uint8 direções[n]` | M4 |
| 0x1009 | S_ENTITY_STOP | `uint32 entity_id`, `uint16 x`, `uint16 y`, `uint8 dir` | M4 |
| 0x100A | S_ENTITY_ATTACK | `uint32 atacante`, `uint32 alvo`, `int32 dano`, `int32 hp_restante` | M5 |
| 0x100B | S_ENTITY_DIED | `uint32 morto`, `uint32 matador` | M5 |
| 0x100C | S_ENTITY_RESPAWN | `uint32 entity_id`, `uint16 x`, `uint16 y`, `int32 hp` | M5 |
| 0x100D | S_ATTACK_REJECTED | `uint8 motivo` | M5 |

> Os marcos M6+ (skills) adicionam entradas aqui quando implementados.

### Tipo de entidade

`0` jogador, `1` monstro.

### Motivos de S_ATTACK_REJECTED

`1` alvo inexistente, `2` alvo morto, `3` atacante morto, `4` alvo é você mesmo,
`5` fora de alcance, `6` ainda em cooldown.

### Células do mapa (um byte por célula)

| Bits | Significado |
|---|---|
| 0 | caminhável |
| 1 | bloqueia linha de visão |
| 4–7 | terreno: 0 grama, 1 água, 2 pedra, 3 ponte, 4 terra |

O servidor envia o grid inteiro ao client (`S_MAP_INFO` + uma `S_MAP_ROW` por linha). Isso
elimina a classe de bug "visual divergir da lógica": só existe um grid, o do servidor. O client
usa as flags apenas para desenhar e dar feedback de cursor — **nunca** para decidir passagem.

### Direções

`0` sul, `1` sudoeste, `2` oeste, `3` noroeste, `4` norte, `5` nordeste, `6` leste, `7` sudeste
(sentido anti-horário a partir do sul). No mundo 3D, `x` da célula vira o eixo X e `y` vira o
eixo Z, então `y` cresce para o sul.

## Fluxo de movimento (M4)

```text
Client                              Servidor
  |-- C_WALK_TO(célula) ----------->|
  |                                 |  A* no grid lógico:
  |                                 |   - destino caminhável?
  |                                 |   - rota existe?
  |                                 |   - cabe em 32 células?
  |<----------- S_ENTITY_WALK ------|  trajeto inteiro, em direções
  |                                 |-- S_ENTITY_WALK --> demais clients
  |   (interpola localmente)        |  (anda 1 célula por vez no tick,
  |                                 |   revalidando cada passo)
  |<----------- S_ENTITY_STOP ------|  se o caminho foi bloqueado no meio,
  |                                 |  ou se o destino foi recusado
```

## Fluxo de combate (M5)

```text
Client                              Servidor
  |-- C_ATTACK(entity_id) --------->|  seleciona o ALVO
  |                                 |  (o golpe sai no tick, não aqui)
  |                                 |
  |                                 |  a cada tick, se o alvo está no alcance
  |                                 |  e o cooldown venceu:
  |                                 |    acerto = 80 + hit - flee  [5, 100]
  |                                 |    dano  = faixa -> defesa % -> defesa plana
  |<----------- S_ENTITY_ATTACK ----|  dano 0 = errou
  |<----------- S_ENTITY_DIED ------|  se o alvo chegou a 0 de HP
  |<----------- S_ENTITY_RESPAWN ---|  depois do tempo de respawn
```

O ataque é **contínuo**: o client escolhe o alvo uma vez e o servidor repete o golpe
enquanto o alvo for válido. Fora de alcance o atacante **persegue**, em vez de recusar.
O cooldown vive no servidor — é ele que impede um client modificado de bater a cada pacote.

Um golpe que erra manda `S_ENTITY_ATTACK` com dano **0**, sem pacote próprio, que é o que o
Ragnarok faz. O HP restante vai no pacote em vez de o client subtrair: se um pacote se
perdesse, uma barra calculada por subtração ficaria errada para sempre.

O client envia **apenas o destino**, nunca passos nem posição. O trajeto vai em um único
pacote, como no Ragnarok: o client interpola as mesmas células que o servidor vai percorrer,
sem tráfego por passo. A posição autoritativa é sempre uma célula inteira.

Destino recusado responde `S_ENTITY_STOP` com a posição autoritativa, para o client desfazer
qualquer animação otimista. `C_WALK_TO` antes de entrar no mundo é violação de protocolo e
derruba a sessão.

Limites que protegem o servidor: a rota é limitada a **32 células** por pedido (`kMaxWalkPath`,
como o `MAX_WALKPATH` do eAthena), e cada entidade avança no máximo 8 passos por tick, para que
um atraso do loop não vire um teleporte.

### Motivos de S_LOGIN_FAILED

| Valor | Significado | Conexão |
|---|---|---|
| 1 | Nome inválido (fora de 3–16 caracteres, ou fora de `[A-Za-z0-9_]`) | mantida |
| 2 | Nome em uso | mantida |
| 3 | A sessão já está no mundo | **derrubada** |

Nome inválido e nome em uso são erro de usuário: a conexão continua aberta para ele tentar
outro nome sem reconectar. Login duplicado é erro de protocolo — nenhum client correto manda
dois logins — então a sessão cai, mas só **depois** de o motivo ser enviado.

## Fluxo de entrada no mundo (M3)

```text
Client                              Servidor
  |-- C_LOGIN(nome) --------------->|
  |                                 |  valida nome, aloca entity_id,
  |                                 |  escolhe a célula de spawn
  |<------------- S_ENTER_WORLD ----|  identidade e posição que o client assume
  |<------------- S_ENTITY_SPAWN ---|  uma por entidade já presente
  |                                 |-- S_ENTITY_SPAWN --> demais clients
  |                                 |
  |     (desconexão)                |-- S_ENTITY_DESPAWN -> demais clients
```

O client **nunca** escolhe seu `entity_id` nem sua célula: ambos vêm do servidor. Ids de
entidade não são reutilizados durante a execução, para que um pacote atrasado referente a uma
entidade removida não acerte uma entidade nova.
