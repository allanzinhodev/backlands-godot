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
    ["T"] = { terrain = "grass", walkable = false, blocks_los = true },
    [":"] = { terrain = "dirt", walkable = true },
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
    "#....:::......~~~......:::.....#", -- y=1
    "#....:::......~~~......:::.....#", -- y=2
    "#.T..:::..T...~~~..T...:::...T.#", -- y=3
    "#....:::......~~~......:::.....#", -- y=4
    "#....:::......~~~...###:::.....#", -- y=5   pedras na margem leste
    "#....:::......~~~...###:::.....#", -- y=6
    "#....:::......~~~...###:::.....#", -- y=7
    "#....:::......~~~......:::.....#", -- y=8
    "#.T..:::..T...~~~..T...:::...T.#", -- y=9
    "#....:::......~~~......:::.....#", -- y=10
    "#....:::......~~~......:::.....#", -- y=11
    "#....:::......~~~......:::.....#", -- y=12
    "#....:::...###~~~###...:::.....#", -- y=13
    "#:::::::::::::~~~::::::::::::::#", -- y=14
    "#:::::::::::::===::::::::::::::#", -- y=15  ponte
    "#:::::::::::::===::::::::::::::#", -- y=16  ponte (altura do spawn)
    "#:::::::::::::~~~::::::::::::::#", -- y=17
    "#....:::...###~~~###...:::.....#", -- y=18
    "#....:::......~~~......:::.....#", -- y=19
    "#....:::......~~~......:::.....#", -- y=20
    "#....:::......~~~......:::.....#", -- y=21
    "#.T..:::..T...~~~..T...:::...T.#", -- y=22
    "#....:::......~~~......:::.....#", -- y=23
    "#....:::......~~~......:::.....#", -- y=24
    "#....:::......~~~......:::.....#", -- y=25
    "#....:::......~~~......:::.....#", -- y=26
    "#....:::......~~~......:::.....#", -- y=27
    "#.T..:::..T...~~~..T...:::...T.#", -- y=28
    "#....:::......~~~......:::.....#", -- y=29
    "#....:::......~~~......:::.....#", -- y=30
    "################################", -- y=31  borda
  },

  -- Modelos de monstro. view_range = 0 significa PASSIVO: não inicia combate,
  -- mas revida em quem o atacar.
  --
  -- Os números do Slime são os do Poring do Ragnarok (mob id 1002), para servir
  -- de âncora de balanceamento: HP 50, dano 7..10, sem defesa, 400 ms por
  -- célula, 1872 ms entre golpes com 672 ms de animação.
  monsters = {
    slime = {
      name = "Slime",
      sprite = "Mog_Animist",   -- folha em spritesheets/
      max_hp = 50,
      attack_min = 7,
      attack_max = 10,
      defense_percent = 0,
      defense_flat = 0,
      hit = 55,              -- erra bastante num jogador comum
      flee = 50,
      attack_range = 1,
      attack_delay_ms = 1872,
      attack_motion_ms = 672,
      walk_speed_ms = 400,
      view_range = 0,        -- passivo, como o Poring de verdade
      chase_range = 12,
      respawn_delay_ms = 8000,
    },

    lobo = {
      name = "Lobo",
      sprite = "Seeq_Viking",
      max_hp = 70,
      attack_min = 12,
      attack_max = 18,
      defense_percent = 5,
      defense_flat = 1,
      hit = 75,
      flee = 65,
      attack_range = 1,
      attack_delay_ms = 1100,
      attack_motion_ms = 500,
      walk_speed_ms = 280,   -- mais rápido que o jogador
      view_range = 7,        -- agressivo: persegue quem chega perto
      chase_range = 12,
      respawn_delay_ms = 12000,
    },
  },

  -- Pontos de nascimento. `spread` espalha os monstros em torno da célula para
  -- não nascerem empilhados.
  spawns = {
    -- Slimes na margem oeste, perto do spawn do jogador: alvo seguro para
    -- testar o combate sem morrer.
    { monster = "slime", x = 9, y = 20, count = 3, spread = 3 },
    { monster = "slime", x = 8, y = 10, count = 2, spread = 2 },

    -- Lobos na margem leste, depois da ponte: atravessar o rio custa caro.
    { monster = "lobo", x = 24, y = 12, count = 2, spread = 3 },
  },
}
