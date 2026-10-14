#include "game/Pathfinder.hpp"

#include <algorithm>
#include <cstdlib>
#include <queue>
#include <unordered_map>

namespace backlands::game {
namespace {

// Heurística de Manhattan escalada pelo custo ortogonal, como no rAthena.
// Não é admissível em grid com diagonais (superestima rotas diagonais), e isso
// é deliberado: expande menos nós e produz caminhos próximos aos que o client
// prevê, ao custo de nem sempre ser o ótimo absoluto.
int heuristic(Cell from, Cell to) noexcept {
    const int dx = std::abs(static_cast<int>(from.x) - static_cast<int>(to.x));
    const int dy = std::abs(static_cast<int>(from.y) - static_cast<int>(to.y));
    return (dx + dy) * kMoveCost;
}

struct Node {
    Cell cell;
    int priority;

    // std::priority_queue é max-heap; invertemos para extrair o menor custo.
    friend bool operator<(const Node& lhs, const Node& rhs) noexcept {
        return lhs.priority > rhs.priority;
    }
};

std::uint32_t pack(Cell cell) noexcept {
    return (static_cast<std::uint32_t>(cell.y) << 16) | cell.x;
}

struct Visited {
    int cost = 0;
    Cell parent{};
    Direction from_parent = Direction::South;
};

} // namespace

bool can_step_diagonally(const MapGrid& grid, Cell from, Direction dir) {
    if (!is_diagonal(dir)) {
        return true;
    }
    const auto& delta = kDirectionDeltas[static_cast<std::uint8_t>(dir)];

    // As duas células ortogonais que formam a quina.
    const Cell horizontal{static_cast<std::uint16_t>(from.x + delta.dx), from.y};
    const Cell vertical{from.x, static_cast<std::uint16_t>(from.y + delta.dy)};

    return grid.is_walkable(horizontal) && grid.is_walkable(vertical);
}

WalkPath find_path(const MapGrid& grid, Cell from, Cell to) {
    if (from == to) {
        return {};
    }
    // Destino inválido é recusado aqui: é esta checagem que impede o client de
    // pedir para andar até dentro do rio ou fora do mapa.
    if (!grid.is_walkable(to)) {
        return {};
    }

    // Um destino longe demais nunca caberia em kMaxWalkPath; descartar antes
    // de rodar o A* evita gastar busca para depois jogar o resultado fora.
    const int dx = std::abs(static_cast<int>(from.x) - static_cast<int>(to.x));
    const int dy = std::abs(static_cast<int>(from.y) - static_cast<int>(to.y));
    if (static_cast<std::size_t>(std::max(dx, dy)) > kMaxWalkPath) {
        return {};
    }

    std::priority_queue<Node> open;
    std::unordered_map<std::uint32_t, Visited> visited;

    open.push(Node{from, heuristic(from, to)});
    visited[pack(from)] = Visited{0, from, Direction::South};

    bool found = false;
    while (!open.empty()) {
        const Node current = open.top();
        open.pop();

        if (current.cell == to) {
            found = true;
            break;
        }

        const auto current_it = visited.find(pack(current.cell));
        if (current_it == visited.end()) {
            continue;
        }
        const int current_cost = current_it->second.cost;

        // Entrada obsoleta: já achamos rota melhor para esta célula depois de
        // enfileirar esta. std::priority_queue não tem decrease-key, então a
        // alternativa é descartar a cópia velha aqui.
        if (current.priority > current_cost + heuristic(current.cell, to)) {
            continue;
        }

        for (std::uint8_t i = 0; i < kDirectionCount; ++i) {
            const auto dir = static_cast<Direction>(i);
            const Cell next = step(current.cell, dir);

            if (!grid.is_walkable(next)) {
                continue;
            }
            if (!can_step_diagonally(grid, current.cell, dir)) {
                continue;
            }

            const int next_cost = current_cost + (is_diagonal(dir) ? kMoveDiagonalCost : kMoveCost);

            const auto key = pack(next);
            const auto it = visited.find(key);
            if (it != visited.end() && it->second.cost <= next_cost) {
                continue;
            }

            visited[key] = Visited{next_cost, current.cell, dir};
            open.push(Node{next, next_cost + heuristic(next, to)});
        }
    }

    if (!found) {
        return {};
    }

    // Reconstrói de trás para frente e inverte.
    WalkPath path;
    Cell cursor = to;
    while (!(cursor == from)) {
        const auto it = visited.find(pack(cursor));
        if (it == visited.end()) {
            return {}; // não deveria acontecer; não devolve caminho parcial
        }
        path.steps.push_back(it->second.from_parent);
        cursor = it->second.parent;

        // Guarda contra caminho absurdo: melhor recusar do que montar um
        // pacote gigante ou girar em um ciclo por causa de um bug.
        if (path.steps.size() > kMaxWalkPath) {
            return {};
        }
    }

    std::reverse(path.steps.begin(), path.steps.end());
    return path;
}

} // namespace backlands::game
