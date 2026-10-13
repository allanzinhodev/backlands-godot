# Protocolo Backlands — contrato client/servidor

Transporte: **TCP**. Encoding: **little-endian** em tudo.

## Framing

Todo pacote tem header fixo de 4 bytes:

```text
offset 0  uint16  opcode
offset 2  uint16  length   (tamanho TOTAL do pacote, header incluído; >= 4)
```

Regras:

- `length < 4` ou `length > 2048` (`kMaxPacket`) → desconectar.
- Opcode desconhecido → desconectar.
- Payload menor que o esperado pelo handler → desconectar.
- Strings: `uint16 len` + bytes UTF-8 (sem terminador nulo).
- Células: `uint16 x` + `uint16 y`.

Direção dos opcodes: `C_*` = client→server (0x0001–0x0FFF), `S_*` = server→client (0x1001–0x1FFF).

## Tabela de opcodes (viva — cresce por marco)

| Opcode | Nome | Payload | Marco |
|---|---|---|---|
| 0x0001 | C_PING | uint32 nonce | M2 |
| 0x1001 | S_PONG | uint32 nonce | M2 |

> Os marcos M3+ (login, spawn, walk, combate) adicionam entradas aqui quando implementados.
