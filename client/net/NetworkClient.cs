using System;
using System.Collections.Generic;
using Godot;

namespace Backlands.Net;

/// <summary>
/// Conexão TCP com o backlands-server.
///
/// Godot não tem I/O assíncrono de socket, então a leitura é feita por polling
/// em <see cref="_Process"/>: acumula bytes num buffer e extrai os frames
/// completos. O framing é o mesmo do servidor (uint16 opcode + uint16 length).
///
/// Esta classe não conhece gameplay: ela entrega payloads a handlers
/// registrados por opcode.
/// </summary>
public sealed partial class NetworkClient : Node
{
    /// Handler de um pacote do servidor. Devolve false para indicar payload
    /// inválido, o que derruba a conexão.
    public delegate bool PacketHandler(ref PacketReader reader);

    private readonly Dictionary<ServerOpcode, PacketHandler> _handlers = new();
    private readonly List<byte> _inbox = new(Protocol.MaxPacketSize);

    private StreamPeerTcp? _peer;
    private StreamPeerTcp.Status _lastStatus = StreamPeerTcp.Status.None;

    [Signal]
    public delegate void ConnectedEventHandler();

    [Signal]
    public delegate void DisconnectedEventHandler(string reason);

    public bool IsPeerConnected => _peer?.GetStatus() == StreamPeerTcp.Status.Connected;

    public void RegisterHandler(ServerOpcode opcode, PacketHandler handler)
    {
        _handlers[opcode] = handler;
    }

    public Error ConnectToServer(string host, int port)
    {
        DisconnectFromServer("reconectando");

        _peer = new StreamPeerTcp();
        // Latência importa mais que throughput: pacotes pequenos e frequentes.
        var error = _peer.ConnectToHost(host, port);
        if (error != Error.Ok)
        {
            GD.PushError($"[net] não foi possível conectar em {host}:{port}: {error}");
            _peer = null;
            return error;
        }

        _lastStatus = StreamPeerTcp.Status.None;
        _inbox.Clear();
        GD.Print($"[net] conectando em {host}:{port}");
        return Error.Ok;
    }

    public void DisconnectFromServer(string reason)
    {
        if (_peer == null)
        {
            return;
        }

        _peer.DisconnectFromHost();
        _peer = null;
        _inbox.Clear();

        if (_lastStatus == StreamPeerTcp.Status.Connected)
        {
            GD.Print($"[net] desconectado: {reason}");
            EmitSignal(SignalName.Disconnected, reason);
        }
        _lastStatus = StreamPeerTcp.Status.None;
    }

    public void Send(PacketWriter writer)
    {
        if (_peer == null || _peer.GetStatus() != StreamPeerTcp.Status.Connected)
        {
            GD.PushWarning("[net] envio ignorado: sem conexão");
            return;
        }

        var frame = writer.Finish();
        var error = _peer.PutData(frame);
        if (error != Error.Ok)
        {
            DisconnectFromServer($"falha ao enviar ({error})");
        }
    }

    public override void _Process(double delta)
    {
        if (_peer == null)
        {
            return;
        }

        _peer.Poll();
        var status = _peer.GetStatus();

        if (status != _lastStatus)
        {
            switch (status)
            {
                case StreamPeerTcp.Status.Connected:
                    GD.Print("[net] conectado");
                    _lastStatus = status;
                    EmitSignal(SignalName.Connected);
                    break;
                case StreamPeerTcp.Status.Error:
                    _lastStatus = status;
                    DisconnectFromServer("erro de socket");
                    return;
                case StreamPeerTcp.Status.None when _lastStatus == StreamPeerTcp.Status.Connected:
                    _lastStatus = status;
                    DisconnectFromServer("servidor encerrou a conexão");
                    return;
                default:
                    _lastStatus = status;
                    break;
            }
        }

        if (status != StreamPeerTcp.Status.Connected)
        {
            return;
        }

        ReceiveAvailable();
        ProcessFrames();
    }

    private void ReceiveAvailable()
    {
        if (_peer == null)
        {
            return;
        }

        var available = _peer.GetAvailableBytes();
        if (available <= 0)
        {
            return;
        }

        var result = _peer.GetData(available);
        if ((Error)(int)result[0] != Error.Ok)
        {
            DisconnectFromServer("falha ao ler do socket");
            return;
        }

        _inbox.AddRange((byte[])result[1]);
    }

    private void ProcessFrames()
    {
        // Um pacote pode chegar partido em vários _Process, e vários pacotes
        // podem chegar no mesmo: processa enquanto houver frame completo.
        while (_inbox.Count >= Protocol.HeaderSize)
        {
            var opcodeRaw = (ushort)(_inbox[0] | (_inbox[1] << 8));
            var length = (ushort)(_inbox[2] | (_inbox[3] << 8));

            // Mesma defesa do servidor: header inconsistente é conexão morta,
            // não há como ressincronizar o stream.
            if (length < Protocol.HeaderSize || length > Protocol.MaxPacketSize)
            {
                DisconnectFromServer($"length inválido ({length})");
                return;
            }

            if (_inbox.Count < length)
            {
                return; // frame ainda incompleto
            }

            var payload = new byte[length - Protocol.HeaderSize];
            _inbox.CopyTo(Protocol.HeaderSize, payload, 0, payload.Length);
            _inbox.RemoveRange(0, length);

            if (!_handlers.TryGetValue((ServerOpcode)opcodeRaw, out var handler))
            {
                DisconnectFromServer($"opcode desconhecido (0x{opcodeRaw:X4})");
                return;
            }

            var reader = new PacketReader(payload);
            if (!handler(ref reader) || reader.Failed)
            {
                DisconnectFromServer($"payload inválido em 0x{opcodeRaw:X4}");
                return;
            }
        }
    }

    public override void _ExitTree()
    {
        DisconnectFromServer("saindo");
    }
}
