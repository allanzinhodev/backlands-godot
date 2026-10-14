cmake_minimum_required(VERSION 3.25)
project(backlands_server CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_package(Boost REQUIRED COMPONENTS asio)
find_package(sol2 CONFIG REQUIRED)
find_package(Lua REQUIRED)

# ---------------------------------------------------------------------------
# Camadas de src/ e quem pode incluir quem (a seta aponta a dependência):
#
#   core      Config, Log                        -> nada
#   protocol  framing, opcodes, codec            -> core
#   game      regras puras do jogo               -> core
#   world     orquestra o jogo e replica         -> game, protocol, core
#   net       transporte TCP (Asio)              -> protocol, core, world/Connection
#   handlers  decodifica pacote e delega         -> world, protocol, net, core
#             (e os tipos de game/ que a API do World expõe, sem incluir game/)
#   app       main (composição) e GameLoop       -> tudo
#
# game/ nunca inclui protocol/, world/, net/ ou Asio; world/ nunca inclui Asio.
# Assim as regras e a replicação inteira são testáveis sem socket.
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# backlands_core: toda a lógica testável sem socket.
# Regra de arquitetura: NADA aqui inclui Boost.Asio. Codec, grid e pathfinding
# trabalham com std::span e structs simples, o que os torna testáveis a seco.
# ---------------------------------------------------------------------------
add_library(backlands_core STATIC
    src/core/Config.cpp
    src/protocol/Opcodes.cpp
    src/protocol/PacketWriter.cpp
    src/game/Chase.cpp
    src/game/Combat.cpp
    src/game/Entity.cpp
    src/game/EntityRegistry.cpp
    src/game/Geometry.cpp
    src/game/LineOfSight.cpp
    src/game/MapDefinition.cpp
    src/game/MapGrid.cpp
    src/game/MapLoader.cpp
    src/game/MonsterAi.cpp
    src/game/MonsterSpawner.cpp
    src/game/Movement.cpp
    src/game/Pathfinder.cpp
    src/game/Respawn.cpp
    src/world/ConnectionRegistry.cpp
    src/world/Replication.cpp
    src/world/World.cpp
)
target_include_directories(backlands_core PUBLIC src)
target_compile_features(backlands_core PUBLIC cxx_std_20)
target_link_libraries(backlands_core PUBLIC sol2::sol2 ${LUA_LIBRARIES})
target_include_directories(backlands_core PUBLIC ${LUA_INCLUDE_DIR})

# ---------------------------------------------------------------------------
# backlands_net: camada de rede (Asio) + loop de jogo.
# Os handlers ficam aqui, junto do transporte que os chama pela interface
# net::SessionHandler.
# ---------------------------------------------------------------------------
add_library(backlands_net STATIC
    src/net/Listener.cpp
    src/net/Session.cpp
    src/handlers/PacketDispatcher.cpp
    src/handlers/GameHandlers.cpp
    src/app/GameLoop.cpp
)
target_link_libraries(backlands_net PUBLIC backlands_core Boost::asio)

add_executable(backlands_server src/app/main.cpp)
target_link_libraries(backlands_server PRIVATE backlands_net)

if(MSVC)
    # Asio precisa saber a versão mínima do Windows (0x0A00 = Windows 10).
    # ws2_32/mswsock são linkados pelo próprio Asio via #pragma comment.
    target_compile_definitions(backlands_core PUBLIC _WIN32_WINNT=0x0A00 WIN32_LEAN_AND_MEAN)
    target_compile_options(backlands_core PUBLIC /W4 /permissive- /utf-8 /EHsc)
else()
    target_compile_options(backlands_core PUBLIC -Wall -Wextra)
endif()

# O executável espera encontrar config.lua e maps/ ao lado dele.
add_custom_command(TARGET backlands_server POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "${CMAKE_CURRENT_SOURCE_DIR}/config.lua"
            "$<TARGET_FILE_DIR:backlands_server>/config.lua"
    COMMAND ${CMAKE_COMMAND} -E copy_directory
            "${CMAKE_CURRENT_SOURCE_DIR}/maps"
            "$<TARGET_FILE_DIR:backlands_server>/maps"
    COMMENT "Copiando config.lua e maps/ para o diretório do executável"
)

# ---------------------------------------------------------------------------
# Testes
# ---------------------------------------------------------------------------
include(CTest)
if(BUILD_TESTING)
    find_package(Catch2 3 REQUIRED)
    add_executable(backlands_tests
        tests/test_packet_codec.cpp
        tests/test_config.cpp
        tests/test_entity_registry.cpp
        tests/test_pathfinding.cpp
        tests/test_map_loading.cpp
        tests/test_movement.cpp
        tests/test_combat.cpp
        tests/test_monster_ai.cpp
        tests/test_world.cpp
    )
    target_link_libraries(backlands_tests PRIVATE backlands_core Catch2::Catch2WithMain)
    # Permite ao teste validar o config.lua real do repositório, não só os
    # temporários — quebrar o arquivo de produção precisa falhar o build.
    target_compile_definitions(backlands_tests PRIVATE
        BACKLANDS_REPO_CONFIG="${CMAKE_CURRENT_SOURCE_DIR}/config.lua"
        BACKLANDS_REPO_MAP="${CMAKE_CURRENT_SOURCE_DIR}/maps/starter_fields.lua")

    # Um único teste no ctest rodando o binário inteiro, em vez de
    # catch_discover_tests: aquele devolve cada nome de caso como filtro na
    # linha de comando, e nomes com acento não sobrevivem ao codepage do
    # Windows — casos válidos apareciam como falha. O Catch2 já reporta caso a
    # caso na própria saída.
    add_test(NAME backlands_tests COMMAND backlands_tests)
endif()
