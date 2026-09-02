using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no terminal: guarda e entrega a PROGRAMAÇÃO da tela (trocas por horário
    /// e rotação). Quem executa é a própria tela — este canal só grava e lê.
    ///
    /// Protocolo, uma operação por conexão:
    ///   "GET"          -> devolve a programação atual (JSON)
    ///   "SET" + JSON   -> grava a programação e devolve a que ficou valendo
    ///
    /// Devolver sempre o estado final é o que permite VÁRIOS CONTROLADORES verem a
    /// mesma coisa: quem grava confirma o que ficou, quem só pergunta recebe igual.
    /// </summary>
    public sealed class ScheduleServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<ScreenSchedule> _get;
        private readonly Action<ScreenSchedule> _set;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        public ScheduleServer(Func<ScreenSchedule> get, Action<ScreenSchedule> set, int port = ScreenSchedule.Port)
        {
            _get = get;
            _set = set;
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
                    var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
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
                    using var stream = client.GetStream();
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer).ConfigureAwait(false);

                    string pedido = Encoding.UTF8.GetString(buffer.ToArray()).TrimStart();

                    if (pedido.StartsWith("SET", StringComparison.OrdinalIgnoreCase))
                    {
                        string json = pedido[3..].TrimStart();
                        var nova = JsonSerializer.Deserialize<ScreenSchedule>(json);
                        if (nova != null)
                            _set(nova);
                    }

                    byte[] resposta = JsonSerializer.SerializeToUtf8Bytes(_get());
                    await stream.WriteAsync(resposta).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                    client.Client.Shutdown(SocketShutdown.Send);
                }
                catch
                {
                    // Pedido inválido / conexão abortada: a tela segue com o que tem.
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
