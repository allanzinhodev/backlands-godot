#pragma once

#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

// Tipos básicos do mundo. Sem nenhum include de Asio por design, para que as
// regras de entidade sejam testáveis sem socket.
namespace backlands::game {

using EntityId = std::uint32_t;

// 0 nunca é uma entidade válida: serve como "nenhuma".
inline constexpr EntityId kNoEntity = 0;

// Posição lógica em células. O servidor nunca guarda posição fracionária: a
// verdade é sempre uma célula inteira, como no Ragnarok.
struct Cell {
    std::uint16_t x = 0;
    std::uint16_t y = 0;

    friend bool operator==(const Cell& lhs, const Cell& rhs) noexcept {
        return lhs.x == rhs.x && lhs.y == rhs.y;
    }
};

// 8 direções, na ordem usada no fio (0 = sul, sentido anti-horário).
enum class Direction : std::uint8_t {
    South = 0,
    SouthWest = 1,
    West = 2,
    NorthWest = 3,
    North = 4,
    NorthEast = 5,
    East = 6,
    SouthEast = 7,
};

inline constexpr std::uint8_t kDirectionCount = 8;

[[nodiscard]] constexpr bool is_valid_direction(std::uint8_t raw) noexcept {
    return raw < kDirectionCount;
}

// Velocidade padrão de caminhada, em milissegundos por célula. 150 ms é o
// valor clássico do Ragnarok para um personagem sem bônus de AGI.
inline constexpr std::uint16_t kDefaultWalkSpeedMs = 150;

// Caminhada em andamento. O caminho é a sequência de direções que o servidor
// vai percorrer, uma célula por vez — o mesmo formato enviado ao client, para
// que os dois percorram exatamente as mesmas células.
struct WalkState {
    std::vector<Direction> path;
    std::size_t next_step = 0;

    // Instante (no relógio lógico do mundo) em que o próximo passo vence.
    std::uint32_t next_step_at_ms = 0;

    std::uint16_t speed_ms_per_cell = kDefaultWalkSpeedMs;

    // Destino pedido, guardado para log e para eventual re-rota.
    Cell destination;

    [[nodiscard]] bool walking() const noexcept { return next_step < path.size(); }

    void clear() noexcept {
        path.clear();
        next_step = 0;
    }
};

enum class EntityKind : std::uint8_t {
    Player = 0,
    Monster = 1,
};

// Atributos de combate, no formato pré-renewal do Ragnarok. Todos vivem no
// servidor; o client recebe só o que precisa mostrar (HP atual e máximo).
struct Stats {
    std::int32_t max_hp = 100;
    std::int32_t hp = 100;

    // Faixa do golpe. O dano é sorteado nela, como os campos Attack/Attack2 do
    // mob_db: um valor fixo deixaria o combate monótono e previsível.
    std::int32_t attack_min = 8;
    std::int32_t attack_max = 12;

    // Defesa em duas partes, aplicadas NESTA ordem (percentual e depois
    // subtrativa), que é o que o pré-renewal faz. A ordem importa: subtrair
    // antes tornaria a defesa percentual quase irrelevante.
    std::int32_t defense_percent = 0; // 0..99; 100 seria imunidade literal
    std::int32_t defense_flat = 0;

    // Acerto e esquiva. A taxa final é 80 + hit - flee, limitada a [5, 100]:
    // nem o melhor atacante acerta sempre, nem o pior erra sempre.
    std::int32_t hit = 80;
    std::int32_t flee = 60;

    // Alcance do ataque em células (1 = corpo a corpo, adjacente).
    std::uint16_t attack_range = 1;

    // Intervalo mínimo entre golpes. É o que impede um client modificado de
    // atacar a cada pacote: o servidor ignora o pedido até o tempo vencer.
    std::uint16_t attack_delay_ms = 1000;

    // Duração da animação do golpe. Um monstro não repensa durante ela, senão
    // fica tremendo e recalculando caminho no meio do ataque.
    std::uint16_t attack_motion_ms = 500;

    [[nodiscard]] bool alive() const noexcept { return hp > 0; }
};

// Estado de combate de uma entidade.
struct CombatState {
    // Alvo atual. O ataque é CONTÍNUO, como no Ragnarok: o client pede uma vez
    // e o servidor repete enquanto o alvo for válido e estiver no alcance.
    EntityId target = kNoEntity;

    // Instante (relógio lógico do mundo) em que o próximo golpe é permitido.
    std::uint32_t next_attack_at_ms = 0;

    // Para mortos: quando a entidade volta. 0 = não está esperando respawn.
    std::uint32_t respawn_at_ms = 0;

    void clear_target() noexcept { target = kNoEntity; }
};

// Comportamento de um monstro.
struct MonsterBrain {
    // Template de origem, para respawn e log.
    std::string species;

    // Onde nasceu: o monstro volta para cá quando desiste da perseguição e é
    // onde renasce.
    Cell home;

    // Raio de visão em células. 0 = passivo, nunca inicia combate.
    std::uint16_t view_range = 0;

    // Até onde persegue antes de desistir, medido a partir de casa.
    std::uint16_t chase_range = 12;

    std::uint32_t respawn_delay_ms = 5000;

    // Próximo instante em que a IA pensa. Pensar a cada tick seria desperdício.
    std::uint32_t next_think_at_ms = 0;

    // Próximo passeio ocioso. Agendar em intervalo (em vez de sortear a cada
    // pensamento) mantém o ritmo independente da frequência da IA.
    std::uint32_t next_wander_at_ms = 0;

    // Quem bateu por último: um monstro passivo revida em quem o atacou.
    EntityId retaliate_against = kNoEntity;
};

// Uma entidade do mundo: player, monstro ou NPC.
struct Entity {
    EntityId id = kNoEntity;
    std::string name;
    Cell cell;
    Direction facing = Direction::South;
    WalkState walk;
    EntityKind kind = EntityKind::Player;
    Stats stats;
    CombatState combat;

    // Só preenchido quando kind == Monster.
    MonsterBrain brain;

    // Sessão que controla esta entidade; 0 para entidades sem jogador.
    std::uint32_t session_id = 0;

    [[nodiscard]] bool is_player() const noexcept { return kind == EntityKind::Player; }
    [[nodiscard]] bool is_monster() const noexcept { return kind == EntityKind::Monster; }
    [[nodiscard]] bool alive() const noexcept { return stats.alive(); }
};

// Regras de nome de personagem. Validar no servidor (e não só na UI) impede
// que um client modificado registre nome vazio, gigante ou com controle.
inline constexpr std::size_t kMinNameLength = 3;
inline constexpr std::size_t kMaxNameLength = 16;

// Aceita apenas [A-Za-z0-9_]: evita homóglifos, espaços em branco à toa e
// problemas de normalização no chat e nos logs.
[[nodiscard]] bool is_valid_character_name(std::string_view name) noexcept;

} // namespace backlands::game
