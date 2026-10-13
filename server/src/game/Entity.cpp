#include "game/Entity.hpp"

namespace backlands::game {
namespace {

constexpr bool is_allowed_name_char(char c) noexcept {
    return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
}

} // namespace

bool is_valid_character_name(std::string_view name) noexcept {
    if (name.size() < kMinNameLength || name.size() > kMaxNameLength) {
        return false;
    }
    for (const char c : name) {
        if (!is_allowed_name_char(c)) {
            return false;
        }
    }
    return true;
}

} // namespace backlands::game
