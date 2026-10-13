#pragma once

#include <cstdio>
#include <format>
#include <string_view>
#include <utility>

// Log mínimo e síncrono. Suficiente enquanto o server é single-thread;
// logs estruturados ficam para a fase de infraestrutura.
namespace backlands::log {

enum class Level { Info, Warn, Error };

namespace detail {
inline const char* prefix(Level level) noexcept {
    switch (level) {
        case Level::Info:
            return "[info ] ";
        case Level::Warn:
            return "[warn ] ";
        case Level::Error:
            return "[error] ";
    }
    return "[?????] ";
}

inline void write(Level level, std::string_view message) {
    std::FILE* stream = (level == Level::Error) ? stderr : stdout;
    std::fputs(detail::prefix(level), stream);
    std::fwrite(message.data(), 1, message.size(), stream);
    std::fputc('\n', stream);
    std::fflush(stream);
}
} // namespace detail

template <typename... Args>
void info(std::format_string<Args...> fmt, Args&&... args) {
    detail::write(Level::Info, std::format(fmt, std::forward<Args>(args)...));
}

template <typename... Args>
void warn(std::format_string<Args...> fmt, Args&&... args) {
    detail::write(Level::Warn, std::format(fmt, std::forward<Args>(args)...));
}

template <typename... Args>
void error(std::format_string<Args...> fmt, Args&&... args) {
    detail::write(Level::Error, std::format(fmt, std::forward<Args>(args)...));
}

} // namespace backlands::log
