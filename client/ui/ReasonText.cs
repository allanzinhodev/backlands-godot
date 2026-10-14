using Backlands.Model;

namespace Backlands.Ui;

public static class AttackRejectionText
{
    public static string Describe(AttackRejection reason) => reason switch
    {
        AttackRejection.NoSuchTarget => "Esse alvo não existe mais.",
        AttackRejection.TargetDead => "O alvo já está morto.",
        AttackRejection.AttackerDead => "Você está morto.",
        AttackRejection.SelfTarget => "Você não pode atacar a si mesmo.",
        AttackRejection.OutOfRange => "Alvo fora de alcance.",
        AttackRejection.OnCooldown => "Ainda se recuperando do golpe anterior.",
        _ => $"Ataque recusado (motivo {(byte)reason}).",
    };
}

public static class LoginFailureText
{
    /// Mensagem para a UI. Um valor desconhecido não pode virar exceção: o
    /// servidor pode ganhar motivos novos antes do client ser atualizado.
    public static string Describe(LoginFailure reason) => reason switch
    {
        LoginFailure.InvalidName =>
            $"Nome inválido. Use {CharacterName.MinLength} a {CharacterName.MaxLength} caracteres, "
            + "apenas letras, números e _",
        LoginFailure.NameInUse => "Esse nome já está em uso.",
        LoginFailure.AlreadySpawned => "Esta conexão já entrou no mundo.",
        LoginFailure.ProtocolMismatch =>
            "Versão do jogo incompatível com a do servidor. Atualize o client.",
        _ => $"Login recusado pelo servidor (motivo {(byte)reason}).",
    };
}
