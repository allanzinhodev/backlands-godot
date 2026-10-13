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
/// Esta classe não conhece gameplay: entrega payloads a handlers por opcode.
/// </summary>
public sealed partial class NetworkClient : Node
{
    /// Handler de um pacote do servidor. Devolve false para indicar payload
    /// inválido, o que derruba a conexão.
    public delegate bool PacketHandler(ref PacketReader reader);

    private const int ReadChunk = 4096;

    /// Teto do buffer de entrada. Um servidor que despeje dados sem parar não
    /// pode fazer o client crescer memória indefinidamente.
    private const int MaxInbox = 64 * 1024;

    private readonly Dictionary<ServerOpcode, PacketHandler> _handlers = new();
    private readonly List<byte> _inbox = new(Protocol.MaxPacketSize);
    private readonly List<byte> _outbox = new(Protocol.MaxPacketSize);

    private StreamPeerTcp? _peer;

    // Gate de emissão separado do status do socket. Reaproveitar o último
    // status para isso era o bug: o status já havia sido sobrescrito para
    // None/Error antes de o encerramento testar se devia avisar, então toda
    // desconexão involuntária era silenciosa.
    private bool _reportedConnected;
    private bool _noDelaySet;
    private double _connectingSeconds;

    /// Segundos em Connecting antes de desistir. Obrigatório: no Godot 4.7 uma
    /// conexão recusada permanece em Connecting indefinidamente e nunca vira
    /// Status.Error, então sem isto apontar para um servidor morto produz
    /// silêncio absoluto — nenhum sinal, nenhum erro.
    public double ConnectTimeoutSeconds { get; set; } = 5.0;

    [Signal]
    public delegate void ConnectedEventHandler();

    [Signal]
    public delegate void DisconnectedEventHandler(string reason);

    public bool IsPeerConnected => _peer?.GetStatus() == StreamPeerSocket.Status.Connected;

    public NetworkClient()
    {
        // Sem isto, qualquer pausa (menu, tela de carregamento, perda de foco)
        // interrompe o polling do socket e o servidor derruba o client por
        // inatividade. ProcessMode.Inherit resolve para Pausable na raiz.
        ProcessMode = ProcessModeEnum.Always;
    }

    public void RegisterHandler(ServerOpcode opcode, PacketHandler handler)
    {
        _handlers[opcode] = handler;
    }

    public Error ConnectToServer(string host, int port)
    {
        Shutdown("reconectando", emit: false);

        var peer = new StreamPeerTcp();
        var error = peer.ConnectToHost(host, port);
        if (error != Error.Ok)
        {
            GD.PushError($"[net] não foi possível conectar em {host}:{port}: {error}");
            return error;
        }

        _peer = peer;
        _connectingSeconds = 0.0;
        GD.Print($"[net] conectando em {host}:{port}");
        return Error.Ok;
    }

    public void DisconnectFromServer(string reason)
    {
        Shutdown(reason, emit: true);
    }

    public void Send(PacketWriter writer)
    {
        if (_peer == null)
        {
            GD.PushWarning("[net] envio ignorado: sem conexão");
            return;
        }

        // Vai para a fila em vez de PutData direto: PutData bloqueia a thread
        // principal até o frame inteiro entrar no buffer do kernel (um servidor
        // travado congelaria o jogo), e a primeira chamada depois de o peer
        // fechar devolve Ok perdendo os bytes em silêncio.
        _outbox.AddRange(writer.Finish());
        if (_peer.GetStatus() == StreamPeerSocket.Status.Connected)
        {
            Flush();
        }
    }

    public override void _Process(double delta)
    {
        if (_peer == null)
        {
            return;
        }

        _peer.Poll();
        switch (_peer.GetStatus())
        {
            case StreamPeerSocket.Status.Connecting:
                _connectingSeconds += delta;
                if (_connectingSeconds > ConnectTimeoutSeconds)
                {
                    Shutdown($"timeout de conexão ({ConnectTimeoutSeconds:F0}s)", emit: true);
                }
                return;

            case StreamPeerSocket.Status.Error:
                Shutdown("erro de socket", emit: true);
                return;

            // Toda forma de queda remota (FIN limpo, half-close, até RST)
            // chega aqui como None; Status.Error é praticamente inalcançável.
            case StreamPeerSocket.Status.None:
                Shutdown(_reportedConnected ? "servidor encerrou a conexão" : "conexão falhou",
                         emit: true);
                return;
        }

        if (!_noDelaySet)
        {
            // Só agora é seguro: antes de o socket conectar, SetNoDelay emite
            // erro de engine e o Nagle continua ligado. Pacotes nossos são
            // pequenos e frequentes, então latência importa mais que throughput.
            _noDelaySet = true;
            _peer.SetNoDelay(true);
        }

        if (!_reportedConnected)
        {
            _reportedConnected = true;
            GD.Print("[net] conectado");
            EmitSignal(SignalName.Connected);
        }

        if (!Flush() || !Receive())
        {
            return;
        }

        ProcessFrames();
    }

    private void Shutdown(string reason, bool emit)
    {
        if (_peer == null)
        {
            return;
        }

        _peer.DisconnectFromHost();
        _peer = null;
        _inbox.Clear();
        _outbox.Clear();
        _noDelaySet = false;
        _connectingSeconds = 0.0;

        var wasConnected = _reportedConnected;
        _reportedConnected = false;

        if (emit && wasConnected)
        {
            GD.Print($"[net] desconectado: {reason}");
            EmitSignal(SignalName.Disconnected, reason);
        }
        else if (emit)
        {
            // Nunca chegou a conectar: ainda precisa avisar a UI, senão a tela
            // de login fica esperando para sempre.
            GD.Print($"[net] falhou: {reason}");
            EmitSignal(SignalName.Disconnected, reason);
        }
    }

    private bool Flush()
    {
        while (_outbox.Count > 0 && _peer != null)
        {
            var result = _peer.PutPartialData(_outbox.ToArray());
            if (result[0].As<Error>() != Error.Ok)
            {
                Shutdown($"falha ao enviar ({result[0].As<Error>()})", emit: true);
                return false;
            }

            var sent = result[1].AsInt32();
            if (sent <= 0)
            {
                break; // buffer do kernel cheio: continua no próximo frame
            }
            _outbox.RemoveRange(0, sent);
        }
        return true;
    }

    private bool Receive()
    {
        while (_peer != null)
        {
            // GetPartialData e não GetData(GetAvailableBytes()): nunca bloqueia,
            // já devolve o array no tamanho lido, e GetAvailableBytes devolve -1
            // com erro de engine depois que o socket fecha.
            var result = _peer.GetPartialData(ReadChunk);
            if (result[0].As<Error>() != Error.Ok)
            {
                Shutdown($"falha ao ler do socket ({result[0].As<Error>()})", emit: true);
                return false;
            }

            // Em leitura com erro o array vem preenchido com lixo do tamanho
            // pedido, então o código do erro é checado ANTES de tocar nele.
            var chunk = result[1].AsByteArray();
            if (chunk.Length == 0)
            {
                return true;
            }
            if (_inbox.Count + chunk.Length > MaxInbox)
            {
                Shutdown("buffer de entrada estourou", emit: true);
                return false;
            }

            _inbox.AddRange(chunk);
            if (chunk.Length < ReadChunk)
            {
                return true; // leu menos do que pediu: o buffer drenou
            }
        }
        return false;
    }

    private void ProcessFrames()
    {
        // Um pacote pode chegar partido em vários frames, e vários pacotes
        // podem chegar de uma vez: processa enquanto houver frame completo.
        while (_inbox.Count >= Protocol.HeaderSize)
        {
            var opcodeRaw = (ushort)(_inbox[0] | (_inbox[1] << 8));
            var length = (ushort)(_inbox[2] | (_inbox[3] << 8));

            // Mesma defesa do servidor: header inconsistente é conexão morta,
            // não há como ressincronizar o stream.
            if (length < Protocol.HeaderSize || length > Protocol.MaxPacketSize)
            {
                Shutdown($"length inválido ({length})", emit: true);
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
                Shutdown($"opcode desconhecido (0x{opcodeRaw:X4})", emit: true);
                return;
            }

            var reader = new PacketReader(payload);
            if (!handler(ref reader) || reader.Failed)
            {
                Shutdown($"payload inválido em 0x{opcodeRaw:X4}", emit: true);
                return;
            }
        }
    }

    public override void _ExitTree()
    {
        // _ExitTree é o lugar seguro para isto: ainda estamos na árvore e
        // GetTree() é válido. Em NotificationPredelete, GetTree() é nulo.
        Shutdown("saindo", emit: false);
    }
}
