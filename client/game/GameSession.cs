using System;
using Backlands.Model;
using Backlands.Net;
using Godot;

namespace Backlands.Game;

/// <summary>
/// Dono da conexão e do espelho do mundo. É o ponto único por onde a cena
/// conversa com o servidor: nenhuma cena fala com o socket diretamente.
///
/// Responsabilidade aqui é só roteamento e ciclo de vida; o que cada pacote
/// significa mora em <see cref="WorldState"/>, e o framing em
/// <see cref="NetworkClient"/>. A leitura dos bytes de cada pacote mora em
/// <see cref="ServerPacketReader"/>, e a montagem dos enviados em
/// <see cref="ClientPackets"/>.
/// </summary>
public sealed partial class GameSession : Node, IPlayerCommands
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
        Register<ServerEvents.EnterWorld>(ServerOpcode.EnterWorld,
            ServerPacketReader.TryReadEnterWorld, World.ApplyEnterWorld);
        Register<ServerEvents.EntitySpawn>(ServerOpcode.EntitySpawn,
            ServerPacketReader.TryReadEntitySpawn, World.ApplyEntitySpawn);
        Register<ServerEvents.EntityDespawn>(ServerOpcode.EntityDespawn,
            ServerPacketReader.TryReadEntityDespawn, World.ApplyEntityDespawn);
        Register<ServerEvents.LoginFailed>(ServerOpcode.LoginFailed,
            ServerPacketReader.TryReadLoginFailed, World.ApplyLoginFailed);
        Register<ServerEvents.MapInfo>(ServerOpcode.MapInfo,
            ServerPacketReader.TryReadMapInfo, World.ApplyMapInfo);
        Register<ServerEvents.MapRow>(ServerOpcode.MapRow,
            ServerPacketReader.TryReadMapRow, World.ApplyMapRow);
        Register<ServerEvents.EntityWalk>(ServerOpcode.EntityWalk,
            ServerPacketReader.TryReadEntityWalk, World.ApplyEntityWalk);
        Register<ServerEvents.EntityStop>(ServerOpcode.EntityStop,
            ServerPacketReader.TryReadEntityStop, World.ApplyEntityStop);
        Register<ServerEvents.EntityAttack>(ServerOpcode.EntityAttack,
            ServerPacketReader.TryReadEntityAttack, World.ApplyEntityAttack);
        Register<ServerEvents.EntityDied>(ServerOpcode.EntityDied,
            ServerPacketReader.TryReadEntityDied, World.ApplyEntityDied);
        Register<ServerEvents.EntityRespawn>(ServerOpcode.EntityRespawn,
            ServerPacketReader.TryReadEntityRespawn, World.ApplyEntityRespawn);
        Register<ServerEvents.AttackRejected>(ServerOpcode.AttackRejected,
            ServerPacketReader.TryReadAttackRejected, World.ApplyAttackRejected);
        Register<uint>(ServerOpcode.Pong, ServerPacketReader.TryReadPong, OnPong);

        _client.Connected += OnConnected;
        _client.Disconnected += OnDisconnected;
    }

    /// Liga um opcode a "decodifica, depois aplica". Payload fora do contrato
    /// nem chega ao estado: o decodificador recusa antes.
    private void Register<T>(ServerOpcode opcode, ServerPacketReader.Decoder<T> decode,
                             Func<T, bool> apply)
    {
        _client.RegisterHandler(opcode,
            (ref PacketReader reader) => decode(ref reader, out var message) && apply(message));
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

        _client.Send(ClientPackets.WalkTo((ushort)cellX, (ushort)cellY));
    }

    /// Escolhe um alvo. O golpe em si sai no servidor, quando o alcance e o
    /// cooldown permitirem — o client não decide se bate nem quanto tira.
    public void RequestAttack(uint targetId)
    {
        _client.Send(ClientPackets.Attack(targetId));
    }

    /// Mede o RTT. O servidor devolve o nonce intacto em S_PONG.
    public void SendPing(uint nonce)
    {
        _client.Send(ClientPackets.Ping(nonce));
    }

    private void OnConnected()
    {
        _client.Send(ClientPackets.Login(_pendingName));
    }

    private void OnDisconnected(string reason)
    {
        // Sem isto, uma reconexão herdaria entidades fantasmas da sessão
        // anterior: o servidor reenvia tudo, mas não reenvia remoções.
        World.Reset();
        EmitSignal(SignalName.Disconnected, reason);
    }

    private static bool OnPong(uint nonce)
    {
        GD.Print($"[net] pong {nonce}");
        return true;
    }
}
