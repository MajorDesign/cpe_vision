using System.Net;
using System.Net.Sockets;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no terminal: transmite, em tempo real, frames (JPEG) de UMA célula do layout
    /// para o controlador, para o "controle ao vivo" mostrar exatamente o que está na TV.
    /// O cliente conecta, envia 4 bytes (índice da célula) e recebe um fluxo contínuo de
    /// frames com prefixo de tamanho ([4 bytes len][JPEG]). Para ao desconectar.
    /// </summary>
    public sealed class LiveViewServer : IDisposable
    {
        /// <summary>Porta TCP do streaming da célula para o controle ao vivo.</summary>
        public const int Port = 48016;

        private readonly TcpListener _listener;
        private readonly Func<int, byte[]?> _captureCell;
        private readonly CancellationTokenSource _cts = new();

        /// <param name="captureCell">Captura a célula <c>index</c> e devolve um JPEG (ou nulo).</param>
        public LiveViewServer(Func<int, byte[]?> captureCell, int port = Port)
        {
            _captureCell = captureCell;
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
                    _ = Task.Run(() => StreamAsync(client, ct));
                }
                catch (OperationCanceledException) { break; }
                catch { /* erro transitório: continua aceitando */ }
            }
        }

        private async Task StreamAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    var stream = client.GetStream();

                    // Lê o índice da célula (4 bytes).
                    var idxBuf = new byte[4];
                    int read = 0;
                    while (read < 4)
                    {
                        int n = await stream.ReadAsync(idxBuf.AsMemory(read, 4 - read), ct);
                        if (n <= 0) return;
                        read += n;
                    }
                    int index = BitConverter.ToInt32(idxBuf, 0);

                    var len = new byte[4];
                    while (!ct.IsCancellationRequested)
                    {
                        byte[]? jpeg = _captureCell(index);
                        if (jpeg is { Length: > 0 })
                        {
                            BitConverter.TryWriteBytes(len, jpeg.Length);
                            await stream.WriteAsync(len, ct);          // lança ao desconectar
                            await stream.WriteAsync(jpeg, ct);
                            await stream.FlushAsync(ct);
                        }
                        await Task.Delay(100, ct); // ~10 frames/s
                    }
                }
                catch { /* desconectou / erro: encerra a transmissão */ }
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
