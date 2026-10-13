# Backlands — Game Design (resumo inicial)

MMORPG 2.5D inspirado em Ragnarok Online: mapa 3D em tiles, personagens sprites 2D billboard,
servidor autoritativo em C++ (Boost.Asio), client Godot 4.7 (C#).

## Atributos

- **STR** — dano físico corpo a corpo, peso carregável.
- **AGI** — velocidade de ataque, esquiva.
- **VIT** — HP máximo, defesa física, regeneração de HP.
- **INT** — dano mágico, SP máximo, regeneração de SP.
- **DEX** — precisão, velocidade de cast, dano à distância.
- **LUK** — crítico, sorte em drops.

## Recursos

- **HP** — vida; 0 = morte, respawn no ponto inicial do mapa.
- **SP** — usado por skills; regenera lentamente fora de combate.

## Movimento

Click-to-move por célula (modelo RO): o client envia a célula destino, o servidor calcula o
caminho (A*, máx. 32 células) e replica. Posição autoritativa é sempre uma célula inteira.

## Primeiras 5 skills

| Skill | Tipo | Efeito |
|---|---|---|
| Bash | Físico, alvo único | Dano físico aumentado (STR) |
| Fire Bolt | Mágico, alvo único | Dano de fogo com cast time (INT) |
| Heal | Suporte, aliado/self | Recupera HP (INT) |
| Poison Sting | Físico, alvo único | Dano + status veneno (tick) |
| Fire Wave | Mágico, área | Dano de fogo em círculo no chão |

## Primeiro mapa

`starter_fields` — campo aberto 32×32 células: área de spawn, lago (água bloqueia),
muro de pedras ao norte, ponte estreita (gargalo de pathfinding para teste).

## Primeiros 3 monstros

| Monstro | Comportamento |
|---|---|
| Slime | Passivo; patrulha aleatória; ataca só se atacado |
| Lobo | Agressivo; persegue quem entra no raio de visão |
| Cogumelo | Estático; não se move, revida à distância curta |

## Armas iniciais

- **Espada curta** — concede Bash.
- **Cajado de aprendiz** — concede Fire Bolt.

Skills são concedidas por equipamento; mestria (usos/kills) torna a skill permanente.
