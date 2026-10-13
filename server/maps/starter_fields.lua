-- Mapa inicial: campo aberto cortado por um rio, com uma ponte.
--
-- O grid aqui é a FONTE DE VERDADE sobre passagem. O servidor valida todo
-- movimento contra ele e envia o mesmo grid ao client, que apenas desenha —
-- assim o visual não pode divergir da lógica.
--
-- O rio existe para o teste ter uma obstrução real: ir de uma margem à outra
-- obriga o A* a desviar pela ponte, em vez de andar em linha reta.

return {
  name = "starter_fields",

  -- Margem oeste, na mesma altura da ponte.
  spawn = { x = 6, y = 16 },

  -- `walkable` é obrigatório em cada símbolo: um default silencioso deixaria
  -- o jogador atravessando terreno que o autor do mapa considerou sólido.
  legend = {
    ["."] = { terrain = "grass",  walkable = true },
    ["="] = { terrain = "bridge", walkable = true },
    -- Água não bloqueia linha de visão: dá para atacar de uma margem à outra.
    ["~"] = { terrain = "water",  walkable = false, blocks_los = false },
    -- Pedra bloqueia visão, o que vai importar para skills de alcance.
    ["#"] = { terrain = "rock",   walkable = false, blocks_los = true },
  },

  --        1         2         3
  -- 0123456789012345678901234567890 1   <- x
  rows = {
    "################################", -- y=0   borda
    "#.............~~~..............#", -- y=1
    "#.............~~~..............#", -- y=2
    "#.............~~~..............#", -- y=3
    "#.............~~~..............#", -- y=4
    "#.............~~~...###........#", -- y=5   pedras na margem leste
    "#.............~~~...###........#", -- y=6
    "#.............~~~...###........#", -- y=7
    "#.............~~~..............#", -- y=8
    "#.............~~~..............#", -- y=9
    "#.............~~~..............#", -- y=10
    "#.............~~~..............#", -- y=11
    "#.............~~~..............#", -- y=12
    "#.............~~~..............#", -- y=13
    "#.............~~~..............#", -- y=14
    "#.............===..............#", -- y=15  ponte
    "#.............===..............#", -- y=16  ponte (altura do spawn)
    "#.............~~~..............#", -- y=17
    "#.............~~~..............#", -- y=18
    "#.............~~~..............#", -- y=19
    "#.............~~~..............#", -- y=20
    "#.............~~~..............#", -- y=21
    "#.............~~~..............#", -- y=22
    "#.............~~~..............#", -- y=23
    "#.............~~~..............#", -- y=24
    "#.............~~~..............#", -- y=25
    "#.............~~~..............#", -- y=26
    "#.............~~~..............#", -- y=27
    "#.............~~~..............#", -- y=28
    "#.............~~~..............#", -- y=29
    "#.............~~~..............#", -- y=30
    "################################", -- y=31  borda
  },
}
