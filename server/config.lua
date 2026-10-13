-- Configuração do servidor Backlands.
-- Carregado em tempo de execução; não é preciso recompilar para mudar valores.
-- Caminho pode ser sobrescrito pelo primeiro argumento da linha de comando.

return {
  network = {
    -- Porta TCP onde o client conecta.
    port = 7777,
  },

  -- O loop de simulação roda a taxa fixa. 20 Hz (50 ms) é o suficiente para
  -- movimento por célula no estilo Ragnarok e mantém o custo de CPU baixo.
  simulation = {
    ticks_per_second = 20,
  },
}
