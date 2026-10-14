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
| `lift_tiles` | *(opcional)* Sobrescreve o levantamento deste efeito, em tiles |

## O levantamento

O pacote documenta que o pivô de um impacto é **os pés do alvo**, e para o jogo
dele isso está certo: 2D oblíquo, personagem da altura de um tile, e a
sobreposição resolvida por **ordem de desenho** — a camada `back`/`ground` passa
atrás do personagem e a `front`/`air` passa na frente, envolvendo-o.

Aqui nada disso vale. O personagem tem **3,2 tiles** de altura e nós achatamos as
camadas numa folha só (`full`), então perdemos a ordem de desenho e sobrou a
altura como única ferramenta. Ancorado no pé, o efeito estoura nos tornozelos.

Por isso os impactos sobem **meio tile** (`DefaultLiftTiles = 0.5`, ou 32 px na
grade de 64 px/tile do pacote). O número foi medido, não escolhido: a 0,5 a
explosão cobre o tronco e as fagulhas ainda tocam o chão; a 1,6 (meio corpo) ela
passa da cabeça e descola do alvo.

**Círculos de chão não sobem.** Além de o chão ser o lugar deles, o quadro deles
é *deitado*: o `Offset` é aplicado nos vértices em espaço local, e com o sprite
girado −90° em X o +Y local aponta para o **norte do mapa**. Levantar um círculo
pelo offset não o levantaria — deslizaria ele meio tile para o norte, para fora
dos pés.

**O que isto custa:** 196 dos 211 impactos do pacote trazem uma camada `ground`
(marca de queimado, rachadura no piso) e nós a usamos embutida na folha `full`,
então ela sobe junto. A meio tile o descolamento é quase imperceptível; se um dia
incomodar, o caminho fiel ao pacote é usar as duas folhas separadas — `ground`
sem levantar, `main` levantando.

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

**O efeito é empurrado 5 cm na direção da câmera** (`DepthBias`). Efeito e
personagem nascem na mesma origem e os dois são billboard, logo os dois quadros
ficam coplanares; o personagem escreve profundidade no pré-passe (corte em alpha)
e o efeito testa contra ela. Empate de profundidade tem desempate indefinido — e
agora que o efeito sobe até o tronco, a silhueta disputada é máxima. Em projeção
**ortogonal** andar na direção da câmera não mexe um pixel na tela, então o
desempate sai de graça.

**A fumaça é pontilhada de propósito.** O pacote desenha translucidez com
dithering ordenado, no estilo da arte. O xadrez que aparece nos quadros finais de
uma explosão está **no PNG**, não no nosso renderizador — já perdi tempo caçando
isso como se fosse bug de mesclagem.

**Efeito em loop morre por prazo.** Um círculo mágico nunca emite
`AnimationFinished`; o prazo (`LoopingSeconds`) cobre os dois casos com a mesma
regra. Quando as skills existirem, quem define a duração é o servidor.

## A barra de habilidades

`client/ui/SkillBar.cs` é um **banco de provas**, não o sistema de skills: 12
slots acionados por F1–F12 ou clique. Tudo acontece **no cliente**, sem passar
pelo servidor, e nenhum outro jogador vê.

### Alvo e conjuro

O fluxo imita o do Ragnarok:

1. **Selecionar** — clicar num inimigo o seleciona *e* ataca. Selecionar e
   atacar juntos, porque trocar o clique por "só seleciona" tiraria a única
   forma de atacar que existe. O alvo ganha um **anel amarelo no chão**, deitado
   no piso e não billboard: um anel billboard viraria argola em pé.
2. **Conjurar** — F1–F12 inicia **meio segundo** de conjuro. O personagem entra
   na pose `Cast` (que estava carregada das folhas e nunca era usada) virado
   para o alvo, com o **nome da skill** e uma **barra preta enchendo de verde**
   acima da cabeça — é assim que o Ragnarok desenha, e é metade do
   reconhecimento visual.
3. **Soltar** — ao fim, o efeito sai na posição **interpolada do alvo**, não na
   célula: se ele estiver andando, a célula já é o destino e o efeito sairia à
   frente dele.

**Andar e atacar não cancelam: eles são ignorados.** Esse é o ponto em que é
fácil errar copiando de memória. No Ragnarok, conjurar não é interrompido por
movimento — movimento é *proibido* enquanto o conjuro dura, e o pedido
simplesmente não acontece (no rAthena, `unit_can_move` devolve falso durante o
cast; o pedido não vira cancelamento). Com meio segundo a diferença é enorme:
ignorar faz o personagem plantar os pés e soltar a skill, enquanto cancelar
perderia o conjuro a cada clique apressado. Outra skill durante o conjuro também
é ignorada.

O que de fato encerra o conjuro: o **alvo morrer ou sumir** e o **conjurador
morrer**. O alvo é conferido no início *e* no fim, porque meio segundo é tempo
bastante para ele deixar de existir. No Ragnarok isso aborta **em silêncio**, sem
mensagem de erro, e aqui também.

Uma divergência consciente: o Ragnarok clássico **não mostra alvo nenhum** — não
há tab-target nem retículo, e é reconhecidamente confuso. Com a câmera girando no
botão direito o jogador perde referência espacial, então o anel vale o custo.

### A mentira que isto conta

O conjuro é só aparência. Não há dano, não há alcance, não há interrupção por
tomar dano, e o servidor não sabe de nada. Quando as skills existirem de
verdade, a tecla vira um **pedido** e quem decide duração, alcance, dano e o
momento do efeito é o servidor — como já acontece com o ataque hoje. O que esta
versão entrega é o **ritmo**: dá para sentir se meio segundo é pouco ou demais.

Os ícones vêm de `icons/spell_icons.png`, uma folha de **14 colunas por 10
linhas**. A célula não é inteira (1536 ÷ 14), então a região de cada ícone sai
do arredondamento das bordas, e não da multiplicação de um tamanho fixo, que
acumularia erro e cortaria a última coluna.

## Conferindo um efeito

```powershell
# seleciona o inimigo mais próximo, conjura o slot N e fotografa no mesmo quadro
godot --path . -- --autologin=Teste --attack-nearest --cast=1 --freeze-animations `
      --screenshot=.playtest/cast.png --screenshot-delay=90
```

`--attack-nearest` é obrigatório: a skill exige alvo, e sem ele o conjuro nem
começa. O `--cast` espera o alvo aparecer em vez de disparar ao entrar no mundo,
porque os monstros chegam depois do próprio jogador.

Use **sempre `--freeze-animations`**. Sem ele o quadro fotografado depende de
quanto a máquina demorou para compilar shader na partida: o atraso é contado em
frames, e os primeiros frames depois de entrar no mundo são lentos o bastante
para comer meio segundo de animação. Duas execuções nunca comparam a mesma coisa,
e dá para passar um bom tempo achando que a explosão está cinza quando o que se
fotografou foi a fumaça.

Congelado, o efeito para no **quadro do meio** — o primeiro de uma explosão é
quase vazio e não diz nada sobre tamanho nem posição.

Reinicie o servidor antes de cada captura (`Get-Process backlands_server |
Stop-Process`). Os monstros vagam e se acumulam em cima do ponto de entrada, e
depois de algumas execuções a captura sai com um lobo na frente do personagem.

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
