# Spritesheets

A arte dos personagens e monstros fica em **`spritesheets/`**, na raiz do repositório.
O nome do arquivo sem `.png` é a **chave** da folha: `slime.png` → `slime`.

Não existe descritor por arquivo: a regra abaixo vale para toda folha, e colocar o PNG na
pasta basta.

## Formato

| Item | Valor |
|---|---|
| Quadro | **32 × 48 px** |
| Separador da grade | linhas da cor **`#e0e0e0`** |
| Fundo | **`#9c8b93`** — cor-chave, convertida em transparência no carregamento |

A geometria da grade é **detectada**, não presumida: o carregador procura linhas e colunas
inteiramente da cor de grade. Funciona com separador de 1 px ou 2 px, com ou sem borda externa.
Se as células encontradas não tiverem exatamente 32×48, ele **recusa a folha** e diz o que
achou — uma folha deslocada uma linha desenharia uma pose plausível-porém-errada em todas as
direções, e isso é quase invisível no jogo.

## Mapa de células (linha, coluna), 1-indexado

| Estado | Front | Back |
|---|---|---|
| Idle | 1,1 | 1,2 |
| Walk | 1,3 + 1,4 | 1,5 + 1,6 |
| Levelup | 1,7 | 1,8 |
| Evade | 2,1 | 2,2 |
| Dead | 2,3 | 2,4 |
| Cast | 2,5 | 2,6 |
| Attack | 3,1 + 3,2 | 3,3 + 3,4 |

Uma folha com menos linhas é aceita: os estados ausentes caem para `Idle`, com aviso. Dá para
começar só com a linha 1.

## Direções: 2 poses desenhadas, 8 usadas

**Desenhe `front` e `back` viradas para a esquerda** — é como as folhas atuais estão, medido
na própria arte (nas células de frente os olhos ficam à esquerda do centro da cabeça). O
espelhamento horizontal cobre o lado direito:

| Direção | Na tela | Pose | Espelhar |
|---|---|---|---|
| South | baixo-esquerda | `front` | não |
| SouthWest | esquerda (perfil) | `front` | não |
| SouthEast | para baixo, de frente | `front` | não (simétrica) |
| East | baixo-direita | `front` | **sim** |
| NorthEast | direita (perfil) | `front` | **sim** |
| West | cima-esquerda | `back` | não |
| NorthWest | para cima, de costas | `back` | não (simétrica) |
| North | cima-direita | `back` | **sim** |

Se um dia a arte vier desenhada para o outro lado, basta trocar
`SpriteLayout.ArtFacesScreenLeft` — todos os espelhamentos se invertem de uma vez.

A câmera tem guinada de 45°, então a bússola lógica aparece girada na tela: a **pose** vem do
componente vertical (descendo mostra a frente, subindo mostra as costas) e o **espelho** vem do
horizontal — espelha quem aponta para o lado oposto àquele em que a arte foi desenhada. As duas
direções de perfil puro (NorthEast e SouthWest) não têm componente vertical e usam `front`, que
é como o personagem é mais visto.

Mexer no ângulo da câmera muda quais direções são espelho de quais; por isso a tabela é
conferida contra a câmera real e um desencontro vira erro visível, não arte sutilmente trocada.

## Ritmo da animação

A folha nunca define o ritmo: ela sobe com velocidade base 1, e quem decide é o
`SpeedScale` **de cada entidade** — nunca `SpriteFrames.SetAnimationSpeed`, que é
compartilhado entre todas as entidades do mesmo tipo e mudaria todo mundo na tela de uma vez.

- **Walk** roda a **2 quadros por segundo, fixo** (`WalkFps`). Já foi casado com a velocidade
  de andar, um ciclo por célula, o que mantinha o pé plantado no chão mas dava 13,3 fps no
  jogador, 7,1 no lobo e 5 no slime — cada um com uma cadência, e todas corridas demais para
  duas poses. O preço do ritmo fixo é o inverso: **o pé patina**, porque em 1 s de ciclo o
  jogador atravessa quase 7 células. Com 2 poses não há como ter as duas coisas; o jeito de
  recuperar o pé plantado é desenhar mais quadros de caminhada.
- **Attack** distribui seus quadros no `attack_motion_ms` da entidade (672 ms no Slime, 500 no
  Lobo). Como o golpe só repete a cada `attack_delay_ms` (1872 ms no Slime), **o último quadro
  fica segurado por mais de um segundo**. É o comportamento do Ragnarok — mas significa que o
  **segundo quadro do ataque deve ser uma pose de recuperação**, não o auge do golpe, senão o
  personagem congela no meio do lunge.

## Quais folhas o jogo procura

A chave vem do servidor: monstros usam a `species` declarada no mapa Lua, jogadores usam
`player`.

```text
spritesheets/player.png
spritesheets/slime.png
spritesheets/lobo.png
```

Sem o arquivo, a entidade cai no bonequinho gerado em código, com uma linha no log. O jogo
nunca deixa de rodar por falta de arte.

## Conferir a arte

```powershell
# Desenha as 8 direções e todos os estados, sem precisar de servidor.
& $godot --path . --quit-after 240 -- --sprite-preview=slime --screenshot=.playtest/slime.png
```

A pré-visualização mostra, para cada direção, qual pose foi escolhida e se ela está espelhada —
é como se vê de imediato se o recorte ou o espelhamento saíram errados.

Para gerar uma folha de teste no formato (serve de gabarito de dimensões):

```powershell
python tools/make_test_sheet.py spritesheets/teste.png
```

## Importação no Godot

`project.godot` traz `[importer_defaults]` com `detect_3d/compress_to=0`, `compress/mode=0` e
`mipmaps/generate=false`. **Não remova.** O padrão do Godot reimporta a textura com compressão
VRAM assim que um nó 3D a usa, o que destrói as bordas duras do pixel art — e `TextureFilter =
Nearest` não salva, porque corrige magnificação, não texels já perdidos.

Os arquivos `.png.import` devem ser versionados junto com a arte.
