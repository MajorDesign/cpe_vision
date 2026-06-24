using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no controlador: mantém um canal TCP persistente com o terminal e envia os
    /// eventos de entrada do "controle ao vivo". O envio é assíncrono (fila) para nunca
    /// travar a interface — eventos são serializados como linhas JSON.
    /// </summary>
    public sealed class LiveInputSender : IDisposable
    {
        private readonly Channel<RemoteInputEvent> _queue =
            Channel.CreateBounded<RemoteInputEvent>(new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.DropOldest, // prioriza eventos recentes
                SingleReader = true,
            });

        private TcpClient? _client;
        private NetworkStream? _stream;
        private Task? _pump;
        private volatile bool _connected;

        /// <summary>Indica se há uma sessão de controle ao vivo conectada.</summary>
        public bool Connected => _connected;

        public async Task ConnectAsync(string ip, int port = RemoteInputEvent.LivePort, int timeoutMs = 4000)
        {
            _client = new TcpClient { NoDelay = true };
            var connect = _client.ConnectAsync(ip, port);
            if (await Task.WhenAny(connect, Task.Delay(timeoutMs)).ConfigureAwait(false) != connect)
                throw new TimeoutException($"Sem resposta de {ip}:{port} (controle ao vivo).");
            await connect.ConfigureAwait(false);

            _stream = _client.GetStream();
            _connected = true;
            _pump = Task.Run(PumpAsync);
        }

        /// <summary>Enfileira um evento para envio (não bloqueia).</summary>
        public void Send(RemoteInputEvent ev)
        {
            if (_connected)
                _queue.Writer.TryWrite(ev);
        }

        private async Task PumpAsync()
        {
            try
            {
                await foreach (var ev in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    if (_stream == null)
                        break;
                    var line = JsonSerializer.Serialize(ev) + "\n";
                    var bytes = Encoding.UTF8.GetBytes(line);
                    await _stream.WriteAsync(bytes).ConfigureAwait(false);
                    await _stream.FlushAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // Conexão caiu: encerra a sessão silenciosamente.
            }
            finally
            {
                _connected = false;
            }
        }

        public void Dispose()
        {
            _connected = false;
            _queue.Writer.TryComplete();
            try { _stream?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
        }
    }
}
