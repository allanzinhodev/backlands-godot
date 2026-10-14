namespace Backlands.Ui;

/// <summary>
/// A linha onde o jogo dá um aviso curto ao jogador.
///
/// Hoje ela é o rótulo de status do painel de login, que some junto com o
/// painel ao entrar no mundo. A interface existe para quem avisa (o conjuro,
/// por exemplo) não depender do painel inteiro — e para a linha poder mudar de
/// lugar sem mexer em quem escreve nela.
/// </summary>
public interface IStatusLine
{
    string StatusText { get; set; }

    bool StatusVisible { get; set; }
}
