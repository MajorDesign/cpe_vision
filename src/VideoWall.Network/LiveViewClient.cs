using System.Net.Sockets;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no controlador: conecta ao <see cref="LiveViewServer"/> do terminal e recebe
    /// o fluxo de frames (JPEG) de uma célula, disparando <see cref="FrameReceived"/> a
    /// cada frame. Usado pelo "controle ao vivo" para exibir o espelho exato da TV.
    /// </summary>
    public sealed class LiveViewClient : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private TcpClient? _client;

        /// <summary>Disparado (em thread de fundo) a cada frame JPEG recebido.</summary>
        public event Action<byte[]>? FrameReceived;

        public void Start(string ip, int targetIndex, int port = LiveViewServer.Port)
        {
            _ = Task.Run(() => RunAsync(ip, targetIndex, port, _cts.Token));
        }

        private async Task RunAsync(string ip, int index, int port, CancellationToken ct)
        {
            try
            {
                _client = new TcpClient { NoDelay = true };
                await _client.ConnectAsync(ip, port, ct);

                var stream = _client.GetStream();
                await stream.WriteAsync(BitConverter.GetBytes(index), ct);
                await stream.FlushAsync(ct);

                var len = new byte[4];
                while (!ct.IsCancellationRequested)
                {
                    if (!await ReadExactAsync(stream, len, 4, ct)) break;
                    int n = BitConverter.ToInt32(len, 0);
                    if (n <= 0 || n > 50_000_000) break;

                    var buf = new byte[n];
                    if (!await ReadExactAsync(stream, buf, n, ct)) break;
                    FrameReceived?.Invoke(buf);
                }
            }
            catch { /* sem terminal / desconectou: encerra silenciosamente */ }
        }

        private static async Task<bool> ReadExactAsync(NetworkStream s, byte[] buf, int count, CancellationToken ct)
        {
            int read = 0;
            while (read < count)
            {
                int n = await s.ReadAsync(buf.AsMemory(read, count - read), ct);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _client?.Close(); } catch { }
            _cts.Dispose();
        }
    }
}
