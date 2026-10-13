# Efeitos visuais (VFX)

Os efeitos vêm do pacote **WsT Magic VFX — Spells & Impacts v1.5 (64px)**. Cada
efeito é uma **tira horizontal de quadros** num PNG, acompanhada de um JSON com
os metadados que dizem como desenhá-lo.

## Onde eles moram

```
vfx/<id>/<id>.png     tira horizontal, um quadro ao lado do outro
vfx/<id>/<id>.json    metadados já resolvidos
```

O `id` é o nome da pasta, do PNG e do JSON — os três sempre iguais. É a chave
que o código usa: `VfxLibrary.Load("impact_fire_explosion")`.

## O JSON

```json
{
  "id": "impact_fire_explosion",
  "title": "Fireball Explosion",
  "frame_width": 320, "frame_height": 320,
  "frames": 16, "fps": 16, "loop": false,
  "tile_size": 64,
  "pivot": [176.0, 176.0],
  "on_ground": false
}
```

| Campo | Para que serve |
|---|---|
| `tile_size` | Quantos pixels da arte valem **um tile do jogo**. É o que dá a escala: `PixelSize = 1 / tile_size`, então um tile do pacote vira um tile do nosso mundo, sem número mágico |
| `pivot` | O ponto da arte que deve cair **nos pés do alvo**, em pixels a partir do canto superior esquerdo. Não é o centro do quadro |
| `on_ground` | Decide o modo de desenho (abaixo) |
| `fps`, `loop` | Ritmo e se a animação repete |

## Dois modos de desenho

O pacote tem duas famílias de arte, e desenhar as duas do mesmo jeito quebra uma
delas:

- **Impactos** (`on_ground: false`) — explosões, raios, auras. São desenhados de
  lado e **encaram a câmera**, como os personagens (`Billboard = Enabled`).
- **Círculos mágicos** (`on_ground: true`) — vistos de cima. Ficam **deitados no
  plano do chão** (`RotationDegrees(-90, 0, 0)`, billboard desligado), um fio
  acima do solo para não brigar com ele pelo mesmo pixel. Billboardar um círculo
  o deixaria em pé feito parede.

## Decisões que não são óbvias

**O PNG é lido cru, não importado.** `VfxLibrary` usa `FileAccess` +
`ImageTexture.CreateFromImage`, o mesmo caminho das folhas de personagem. O
pipeline de importação do Godot, no padrão, recomprime em VRAM qualquer textura
usada em 3D e destrói a arte — o repositório já se defende disso em
`project.godot`, mas ler cru remove a dependência de vez. O pacote traz `.tres`
prontos; eles passariam por esse pipeline, então não são usados.

**Transparência sem corte duro** (`AlphaCut.Disabled`). O personagem usa
`Discard`, que corta em 0,5 — certo para pixel art de silhueta fechada, errado
para magia, que é feita de brilho e borda macia. Um corte uniforme comeria
exatamente o que faz o efeito parecer luz.

**`Shaded = false`.** O efeito é luz própria; receber a luz do mapa o deixaria
cinza à sombra. `CastShadow = Off` pela mesma razão do personagem: o quadro é
billboard para a **câmera**, não para a luz.

**O efeito nasce na posição interpolada**, não na célula. Durante uma caminhada
a célula já é o destino, e o efeito apareceria à frente do personagem.

**Efeito em loop morre por prazo.** Um círculo mágico nunca emite
`AnimationFinished`; o prazo (`LoopingSeconds`) cobre os dois casos com a mesma
regra. Quando as skills existirem, quem define a duração é o servidor.

## A barra de habilidades

`client/ui/SkillBar.cs` é um **banco de provas de arte**, não o sistema de
skills: 12 slots acionados por F1–F12 ou clique, que tocam o efeito
**localmente**, sem passar pelo servidor. Nenhum outro jogador vê.

Quando as skills existirem de verdade, a tecla vira um pedido ao servidor e o
efeito só toca quando ele confirmar — como já acontece com o ataque hoje.

Os ícones vêm de `icons/spell_icons.png`, uma folha de **14 colunas por 10
linhas**. A célula não é inteira (1536 ÷ 14), então a região de cada ícone sai
do arredondamento das bordas, e não da multiplicação de um tamanho fixo, que
acumularia erro e cortaria a última coluna.

## Conferindo um efeito

```powershell
# aciona o slot N ao entrar no mundo e fotografa
godot --path . -- --autologin=Teste --cast=1 --screenshot=.playtest/cast.png --screenshot-delay=10
```

O atraso é em **frames**, não em segundos — e frame não é relógio. Os primeiros
frames depois de entrar no mundo são lentos (compilação de shader, construção do
mapa) e podem comer meio segundo de animação. Para pegar o auge de uma explosão
de 1 s, 10 frames; com 30 já se fotografa a fumaça.

## Como adicionar outro efeito do pacote

1. Copie `<nome>.png` (a camada `full`, ou `main` quando não houver `full`) para
   `vfx/<id>/<id>.png`.
2. Escreva `vfx/<id>/<id>.json` com os campos da tabela acima, tirados do JSON
   do pacote. `on_ground` é `true` só para os círculos mágicos.
3. Aponte um slot de `SkillBar.Slots` para o novo `id`.

O pacote organiza os efeitos por **variante** (`fire`, `hellfire`, `spirit`…) e
por **camada** (`ground`, `main`, `full`); alguns separam ainda por **estágio**
(`start`, `loop`, `end`). `full` é a composição das camadas e é o que queremos;
quando ele não existe, `main` é o equivalente.
