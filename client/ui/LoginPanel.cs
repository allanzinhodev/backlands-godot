using System;
using Backlands.Model;
using Godot;

namespace Backlands.Ui;

/// <summary>
/// A tela de conexão: servidor, nome do personagem, botão de entrar e o rótulo
/// de status.
///
/// Só valida o que dá para validar sem servidor e pede a conexão por
/// <see cref="ConnectRequested"/>; quem conecta de fato é a sessão. O rótulo
/// de status mora aqui dentro, então tudo que o jogo avisa por ele some junto
/// com o painel.
///
/// Os campos nascem no <see cref="_Ready"/>: antes de o painel entrar na
/// árvore, nenhuma propriedade ou método abaixo tem onde ler ou escrever.
/// </summary>
public sealed partial class LoginPanel : PanelContainer, IStatusLine
{
    private LineEdit _hostField = null!;
    private LineEdit _nameField = null!;
    private Button _connectButton = null!;
    private Label _statusLabel = null!;

    /// Pedido de conexão já validado: host, porta e nome do personagem.
    public event Action<string, int, string>? ConnectRequested;

    public LoginPanel()
    {
        Name = "LoginPanel";
        AnchorsPreset = (int)LayoutPreset.Center;
        // Antes de ter filhos e antes de entrar na árvore, como sempre foi: o
        // tamanho ainda é zero, então é o canto superior esquerdo que vai para
        // o centro, e o painel cresce dali.
        SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
    }

    /// Os filhos nascem aqui, e não no construtor, para entrarem com o painel
    /// já na árvore e já posicionado, na mesma ordem em que sempre entraram.
    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        AddChild(margin);

        var column = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        margin.AddChild(column);

        column.AddChild(new Label { Text = "Backlands", HorizontalAlignment = HorizontalAlignment.Center });

        column.AddChild(new Label { Text = "Servidor" });
        _hostField = new LineEdit { Text = "127.0.0.1:7777" };
        column.AddChild(_hostField);

        column.AddChild(new Label { Text = "Personagem" });
        _nameField = new LineEdit
        {
            Text = "Heroi",
            MaxLength = CharacterName.MaxLength,
            PlaceholderText = "3 a 16 caracteres",
        };
        column.AddChild(_nameField);

        _connectButton = new Button { Text = "Entrar" };
        _connectButton.Pressed += Submit;
        column.AddChild(_connectButton);

        _statusLabel = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_statusLabel);

        // Enter no campo de nome também entra.
        _nameField.TextSubmitted += _ => Submit();
    }

    public string HostText
    {
        get => _hostField.Text;
        set => _hostField.Text = value;
    }

    public string NameText
    {
        get => _nameField.Text;
        set => _nameField.Text = value;
    }

    public string StatusText
    {
        get => _statusLabel.Text;
        set => _statusLabel.Text = value;
    }

    public bool StatusVisible
    {
        get => _statusLabel.Visible;
        set => _statusLabel.Visible = value;
    }

    // --- Conexão -----------------------------------------------------------

    /// O mesmo que apertar "Entrar".
    public void Submit()
    {
        var name = _nameField.Text.Trim();
        if (!CharacterName.IsValid(name))
        {
            // Validação local só para evitar um round-trip óbvio; a validação
            // que vale é a do servidor.
            _statusLabel.Text = $"Nome inválido: use {CharacterName.MinLength} a "
                + $"{CharacterName.MaxLength} caracteres, apenas letras, números e _";
            return;
        }

        var (host, port) = ParseHost(_hostField.Text);
        _statusLabel.Text = $"Conectando em {host}:{port}...";
        _connectButton.Disabled = true;
        ConnectRequested?.Invoke(host, port, name);
    }

    /// A tentativa terminou sem entrar: mostra o motivo e libera o botão.
    public void ShowFailure(string message)
    {
        _statusLabel.Text = message;
        _connectButton.Disabled = false;
    }

    /// A conexão caiu: o painel volta, com o motivo.
    public void ShowDisconnected(string reason)
    {
        _statusLabel.Text = $"Desconectado: {reason}";
        _connectButton.Disabled = false;
        Visible = true;
    }

    private static (string Host, int Port) ParseHost(string text)
    {
        var trimmed = text.Trim();
        var separator = trimmed.LastIndexOf(':');
        if (separator > 0 && int.TryParse(trimmed[(separator + 1)..], out var port))
        {
            return (trimmed[..separator], port);
        }
        return (trimmed.Length == 0 ? "127.0.0.1" : trimmed, 7777);
    }
}
