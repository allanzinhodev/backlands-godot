cmake_minimum_required(VERSION 3.25)
project(backlands_server CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_package(Boost REQUIRED COMPONENTS asio)
find_package(sol2 CONFIG REQUIRED)
find_package(Lua REQUIRED)

# ---------------------------------------------------------------------------
# backlands_core: toda a lógica testável sem socket.
# Regra de arquitetura: NADA aqui inclui Boost.Asio. Codec, grid e pathfinding
# trabalham com std::span e structs simples, o que os torna testáveis a seco.
# ---------------------------------------------------------------------------
add_library(backlands_core STATIC
    src/core/Config.cpp
    src/net/PacketWriter.cpp
    src/net/Opcodes.cpp
)
target_include_directories(backlands_core PUBLIC src)
target_compile_features(backlands_core PUBLIC cxx_std_20)
target_link_libraries(backlands_core PUBLIC sol2::sol2 ${LUA_LIBRARIES})
target_include_directories(backlands_core PUBLIC ${LUA_INCLUDE_DIR})

# ---------------------------------------------------------------------------
# backlands_net: camada de rede (Asio) + loop de jogo.
# ---------------------------------------------------------------------------
add_library(backlands_net STATIC
    src/net/Listener.cpp
    src/net/Session.cpp
    src/game/GameLoop.cpp
    src/game/World.cpp
    src/handlers/Dispatch.cpp
)
target_link_libraries(backlands_net PUBLIC backlands_core Boost::asio)

add_executable(backlands_server src/main.cpp)
target_link_libraries(backlands_server PRIVATE backlands_net)

if(MSVC)
    # Asio precisa saber a versão mínima do Windows (0x0A00 = Windows 10).
    # ws2_32/mswsock são linkados pelo próprio Asio via #pragma comment.
    target_compile_definitions(backlands_core PUBLIC _WIN32_WINNT=0x0A00 WIN32_LEAN_AND_MEAN)
    target_compile_options(backlands_core PUBLIC /W4 /permissive- /utf-8 /EHsc)
else()
    target_compile_options(backlands_core PUBLIC -Wall -Wextra)
endif()

# O executável espera encontrar config.lua ao lado dele.
add_custom_command(TARGET backlands_server POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "${CMAKE_CURRENT_SOURCE_DIR}/config.lua"
            "$<TARGET_FILE_DIR:backlands_server>/config.lua"
    COMMENT "Copiando config.lua para o diretório do executável"
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
    )
    target_link_libraries(backlands_tests PRIVATE backlands_core Catch2::Catch2WithMain)
    # Permite ao teste validar o config.lua real do repositório, não só os
    # temporários — quebrar o arquivo de produção precisa falhar o build.
    target_compile_definitions(backlands_tests PRIVATE
        BACKLANDS_REPO_CONFIG="${CMAKE_CURRENT_SOURCE_DIR}/config.lua")

    # Um único teste no ctest rodando o binário inteiro, em vez de
    # catch_discover_tests: aquele devolve cada nome de caso como filtro na
    # linha de comando, e nomes com acento não sobrevivem ao codepage do
    # Windows — casos válidos apareciam como falha. O Catch2 já reporta caso a
    # caso na própria saída.
    add_test(NAME backlands_tests COMMAND backlands_tests)
endif()
