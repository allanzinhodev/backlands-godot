#include "core/Config.hpp"
#include "core/Log.hpp"
#include "game/GameLoop.hpp"
#include "game/World.hpp"
#include "handlers/Dispatch.hpp"
#include "net/Listener.hpp"

#include <boost/asio/io_context.hpp>
#include <boost/asio/signal_set.hpp>

#include <exception>
#include <string>

#ifdef _WIN32
#include <windows.h>
#endif

int main(int argc, char** argv) {
    using namespace backlands;

#ifdef _WIN32
    // Os logs saem em UTF-8; sem isto o console usa a codepage ANSI e os
    // acentos aparecem corrompidos.
    SetConsoleOutputCP(CP_UTF8);
#endif

    // Único argumento opcional: caminho do config.lua.
    const std::string config_path = (argc > 1) ? argv[1] : "config.lua";

    core::ServerConfig config;
    try {
        config = core::ServerConfig::load_from_file(config_path);
    } catch (const std::exception& error) {
        log::error("{}", error.what());
        return 1;
    }
    log::info("configuração carregada de '{}'", config_path);

    try {
        // Um único thread para accept, leitura, escrita e tick: o estado do
        // mundo tem um só dono, então não há lock nem data race por construção.
        boost::asio::io_context io(1);

        game::World world;
        world.load_map(config.map_path);

        net::PacketDispatcher dispatcher(world);
        net::install_handlers(dispatcher);

        net::Listener listener(io, config.port, dispatcher);
        listener.start();

        game::GameLoop loop(io, world, config.ticks_per_second);
        loop.start();

        boost::asio::signal_set signals(io, SIGINT, SIGTERM);
        signals.async_wait([&](const boost::system::error_code& ec, int signal) {
            if (ec) {
                return;
            }
            log::info("sinal {} recebido, encerrando", signal);
            loop.stop();
            listener.stop();
            io.stop();
        });

        log::info("backlands-server pronto");
        io.run();
        log::info("encerrado");
        return 0;
    } catch (const std::exception& error) {
        log::error("falha fatal: {}", error.what());
        return 1;
    }
}
