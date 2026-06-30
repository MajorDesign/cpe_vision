using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no terminal: quando o controlador conecta, devolve (JSON) o LAYOUT ATUAL que
    /// a tela está exibindo — fontes com posição/tipo e a URL AO VIVO de cada navegador.
    /// Permite o controlador, ao reabrir, reconstruir a parede de cada terminal sem ter
    /// guardado nada (o terminal é a fonte da verdade). Uma resposta por conexão.
    /// </summary>
    public sealed class LayoutQueryServer : IDisposable
    {
        /// <summary>Porta TCP da consulta de layout atual.</summary>
        public const int Port = 48017;

        private readonly TcpListener _listener;
        private readonly Func<Task<string?>> _getLayout;
        private readonly CancellationTokenSource _cts = new();

        /// <param name="getLayout">Devolve o layout atual em JSON (lista de ScreenSource) ou nulo.</param>
        public LayoutQueryServer(Func<Task<string?>> getLayout, int port = Port)
        {
            _getLayout = getLayout;
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
                catch { /* erro transitório: continua aceitando */ }
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    string? json = await _getLayout();
                    if (!string.IsNullOrEmpty(json))
                    {
                        var bytes = Encoding.UTF8.GetBytes(json);
                        var stream = client.GetStream();
                        await stream.WriteAsync(bytes);
                        await stream.FlushAsync();
                    }
                    client.Client.Shutdown(SocketShutdown.Send);
                }
                catch { /* conexão abortada / layout indisponível */ }
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
