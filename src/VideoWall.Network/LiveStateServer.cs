using System.Net;
using System.Net.Sockets;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no terminal: responde, sob demanda, com o estado atual de uma célula (a
    /// página aberta e a rolagem) para o controlador reabrir o controle ao vivo no mesmo
    /// ponto. Protocolo: o controlador envia o índice da célula (uma linha) e recebe um
    /// JSON de <see cref="CellState"/>.
    /// </summary>
    public sealed class LiveStateServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<int, Task<string?>> _getState;
        private readonly CancellationTokenSource _cts = new();

        public LiveStateServer(Func<int, Task<string?>> getState, int port = CellState.Port)
        {
            _getState = getState;
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
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream);
                    using var writer = new StreamWriter(stream) { AutoFlush = true };

                    string? line = await reader.ReadLineAsync();
                    if (line != null && int.TryParse(line.Trim(), out int index))
                    {
                        string? json = await _getState(index);
                        if (!string.IsNullOrEmpty(json))
                            await writer.WriteLineAsync(json);
                    }
                }
                catch { /* conexão abortada / estado indisponível */ }
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
