using System.Net;
using System.Net.Sockets;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no terminal: quando o controlador conecta, captura uma foto (JPEG) do que a
    /// tela está exibindo e a envia. Pedido/resposta simples — uma imagem por conexão.
    /// </summary>
    public sealed class ThumbnailServer : IDisposable
    {
        /// <summary>Porta TCP do serviço de miniatura ao vivo das telas.</summary>
        public const int Port = 48014;

        private readonly TcpListener _listener;
        private readonly Func<byte[]?> _capture;
        private readonly CancellationTokenSource _cts = new();

        /// <param name="capture">Função que devolve a foto atual da tela em JPEG (ou nulo).</param>
        public ThumbnailServer(Func<byte[]?> capture, int port = Port)
        {
            _capture = capture;
            _listener = new TcpListener(IPAddress.Any, port);
        }

        public void Start()
        {
            _listener.Start();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
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
                catch (OperationCanceledException) { break; }
                catch { /* erro transitório; continua aceitando */ }
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    byte[]? jpeg = _capture();
                    if (jpeg is { Length: > 0 })
                    {
                        var stream = client.GetStream();
                        await stream.WriteAsync(jpeg);
                        await stream.FlushAsync();
                    }
                    client.Client.Shutdown(SocketShutdown.Send);
                }
                catch { /* captura indisponível / conexão abortada */ }
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
