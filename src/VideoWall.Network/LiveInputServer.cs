using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no terminal: mantém um canal TCP persistente para receber, em tempo real,
    /// os eventos de entrada (mouse/rolagem/teclado) enviados pelo controlador durante
    /// o "controle ao vivo". Cada evento chega como uma linha JSON.
    /// </summary>
    public sealed class LiveInputServer : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();

        /// <summary>Disparado (em thread de fundo) a cada evento de entrada recebido.</summary>
        public event Action<RemoteInputEvent>? InputReceived;

        /// <summary>Disparado quando um controlador conecta para controlar ao vivo.</summary>
        public event Action? SessionStarted;

        /// <summary>Disparado quando a sessão de controle ao vivo termina.</summary>
        public event Action? SessionEnded;

        public LiveInputServer(int port = RemoteInputEvent.LivePort)
        {
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
                    _ = Task.Run(() => HandleAsync(client, ct));
                }
                catch (OperationCanceledException) { break; }
                catch { /* erro transitório; continua aceitando */ }
            }
        }

        private async Task HandleAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                SessionStarted?.Invoke();
                try
                {
                    client.NoDelay = true; // baixa latência (sem buffer de Nagle)
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream);

                    string? line;
                    while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync(ct)) != null)
                    {
                        if (line.Length == 0)
                            continue;
                        try
                        {
                            var ev = JsonSerializer.Deserialize<RemoteInputEvent>(line, JsonOpts);
                            if (ev != null && !string.IsNullOrEmpty(ev.Kind))
                                InputReceived?.Invoke(ev);
                        }
                        catch { /* linha inválida: ignora */ }
                    }
                }
                catch { /* conexão encerrada/abortada */ }
                finally
                {
                    SessionEnded?.Invoke();
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
