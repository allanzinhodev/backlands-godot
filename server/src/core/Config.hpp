#pragma once

#include <cstdint>
#include <string>

namespace backlands::core {

// Configuração do servidor, carregada de um arquivo Lua em tempo de execução.
// Lua (e não um formato fixo) porque os dados de gameplay — skills, monstros,
// balanceamento — vão pelo mesmo caminho nos marcos seguintes.
struct ServerConfig {
    std::uint16_t port = 7777;
    std::uint32_t ticks_per_second = 20;

    // Caminho do mapa a carregar, relativo ao diretório de execução.
    std::string map_path = "maps/starter_fields.lua";

    // Carrega e valida o arquivo. Lança std::runtime_error com mensagem
    // legível se o arquivo não existir, não compilar ou tiver valor inválido:
    // subir com configuração silenciosamente errada é pior que não subir.
    static ServerConfig load_from_file(const std::string& path);
};

} // namespace backlands::core
