using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no terminal: escuta comandos do Controlador (TCP) e dispara
    /// <see cref="CommandReceived"/> a cada comando recebido. O terminal aplica
    /// o comando (ex.: abrir uma URL em tela cheia).
    /// </summary>
    public sealed class CommandServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        /// <summary>Disparado (em thread de fundo) quando um comando chega.</summary>
        public event Action<ScreenCommand>? CommandReceived;

        public CommandServer(int port = ScreenCommand.DefaultPort)
        {
            _listener = new TcpListener(IPAddress.Any, port);
        }

        public void Start()
        {
            _listener.Start();
            _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => HandleAsync(client));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Erro transitório; continua aceitando.
                }
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer);

                    var command = JsonSerializer.Deserialize<ScreenCommand>(buffer.ToArray());
                    if (command != null && !string.IsNullOrEmpty(command.Type))
                        CommandReceived?.Invoke(command);
                }
                catch
                {
                    // Comando inválido / conexão abortada: ignora.
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            _cts.Dispose();
        }
    }
}
