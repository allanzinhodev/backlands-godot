using Backlands.Net;
using Godot;

namespace Backlands.Game;

/// <summary>
/// Dono da conexão e do espelho do mundo. É o ponto único por onde a cena
/// conversa com o servidor: nenhuma cena fala com o socket diretamente.
///
/// Responsabilidade aqui é só roteamento e ciclo de vida; o que cada pacote
/// significa mora em <see cref="WorldState"/>, e o framing em
/// <see cref="NetworkClient"/>.
/// </summary>
public sealed partial class GameSession : Node
{
    private NetworkClient _client = null!;
    private string _pendingName = string.Empty;

    public WorldState World { get; } = new();

    [Signal]
    public delegate void ConnectionFailedEventHandler(string reason);

    [Signal]
    public delegate void DisconnectedEventHandler(string reason);

    public bool IsOnline => _client.IsPeerConnected;

    public override void _Ready()
    {
        _client = new NetworkClient { Name = "NetworkClient" };
        AddChild(_client);

        // Os handlers devolvem false quando o payload não bate com o contrato,
        // e o NetworkClient derruba a conexão — simétrico ao servidor.
        _client.RegisterHandler(ServerOpcode.EnterWorld, World.HandleEnterWorld);
        _client.RegisterHandler(ServerOpcode.EntitySpawn, World.HandleEntitySpawn);
        _client.RegisterHandler(ServerOpcode.EntityDespawn, World.HandleEntityDespawn);
        _client.RegisterHandler(ServerOpcode.LoginFailed, World.HandleLoginFailed);
        _client.RegisterHandler(ServerOpcode.MapInfo, World.HandleMapInfo);
        _client.RegisterHandler(ServerOpcode.MapRow, World.HandleMapRow);
        _client.RegisterHandler(ServerOpcode.EntityWalk, World.HandleEntityWalk);
        _client.RegisterHandler(ServerOpcode.EntityStop, World.HandleEntityStop);
        _client.RegisterHandler(ServerOpcode.Pong, HandlePong);

        _client.Connected += OnConnected;
        _client.Disconnected += OnDisconnected;
    }

    /// Conecta e envia o login assim que a conexão estabelecer. O login não
    /// pode ser enviado antes: o socket ainda está em Connecting.
    public void ConnectAndLogin(string host, int port, string characterName)
    {
        _pendingName = characterName;

        var error = _client.ConnectToServer(host, port);
        if (error != Error.Ok)
        {
            EmitSignal(SignalName.ConnectionFailed, $"não foi possível conectar: {error}");
        }
    }

    public void Disconnect(string reason = "pedido do jogador")
    {
        _client.DisconnectFromServer(reason);
    }

    /// Pede para andar até uma célula. O client manda apenas o destino: o
    /// caminho, e se ele existe, é decisão do servidor.
    public void RequestWalk(int cellX, int cellY)
    {
        if (cellX < 0 || cellY < 0 || cellX > ushort.MaxValue || cellY > ushort.MaxValue)
        {
            return;
        }

        var writer = new PacketWriter(ClientOpcode.WalkTo);
        writer.WriteU16((ushort)cellX);
        writer.WriteU16((ushort)cellY);
        _client.Send(writer);
    }

    /// Mede o RTT. O servidor devolve o nonce intacto em S_PONG.
    public void SendPing(uint nonce)
    {
        var writer = new PacketWriter(ClientOpcode.Ping);
        writer.WriteU32(nonce);
        _client.Send(writer);
    }

    private void OnConnected()
    {
        var writer = new PacketWriter(ClientOpcode.Login);
        writer.WriteString(_pendingName);
        _client.Send(writer);
    }

    private void OnDisconnected(string reason)
    {
        // Sem isto, uma reconexão herdaria entidades fantasmas da sessão
        // anterior: o servidor reenvia tudo, mas não reenvia remoções.
        World.Reset();
        EmitSignal(SignalName.Disconnected, reason);
    }

    private bool HandlePong(ref PacketReader reader)
    {
        var nonce = reader.ReadU32();
        if (reader.Failed)
        {
            return false;
        }
        GD.Print($"[net] pong {nonce}");
        return true;
    }
}
