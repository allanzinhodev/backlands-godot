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
| 0x1001 | S_PONG | `uint32 nonce` | M2 |
| 0x1002 | S_ENTER_WORLD | `uint32 entity_id`, `uint16 x`, `uint16 y`, `uint8 dir`, `string mapa`, `string nome` | M3 |
| 0x1003 | S_LOGIN_FAILED | `uint8 motivo` | M3 |
| 0x1004 | S_ENTITY_SPAWN | `uint32 entity_id`, `uint16 x`, `uint16 y`, `uint8 dir`, `string nome` | M3 |
| 0x1005 | S_ENTITY_DESPAWN | `uint32 entity_id` | M3 |

> Os marcos M4+ (walk, combate, skills) adicionam entradas aqui quando implementados.

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
