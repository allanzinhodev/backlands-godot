namespace Backlands.Game;

/// <summary>
/// Os pedidos que o jogador faz ao servidor.
///
/// Quem decide O QUE pedir (o clique, o piloto automático dos testes) depende
/// só disto, e não da sessão inteira: não precisa saber de socket, de login
/// nem de pacote, e pode ser exercitado com um dublê.
/// </summary>
public interface IPlayerCommands
{
    void RequestWalk(int cellX, int cellY);

    void RequestAttack(uint targetId);
}
