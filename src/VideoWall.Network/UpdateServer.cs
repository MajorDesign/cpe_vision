using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VideoWall.Network
{
    /// <summary>
    /// Servidor HTTP mínimo (sobre TcpListener, sem exigir admin) que o central
    /// roda para entregar a versão e o binário mais recente do terminal. Os
    /// terminais consultam para se auto-atualizar pela rede local.
    ///   GET /version  -> {"version":"x.y.z.w"}
    ///   GET /viewer   -> o executável do terminal
    /// </summary>
    public sealed class UpdateServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly string _viewerExePath;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        public int Port { get; }

        public UpdateServer(string viewerExePath, int port = 48020)
        {
            _viewerExePath = viewerExePath;
            Port = port;
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
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    using var stream = client.GetStream();
                    string path = await ReadRequestPathAsync(stream);

                    if (path.StartsWith("/version", StringComparison.OrdinalIgnoreCase))
                        await WriteJsonAsync(stream, $"{{\"version\":\"{GetVersion()}\"}}");
                    else if (path.StartsWith("/viewer", StringComparison.OrdinalIgnoreCase))
                        await WriteFileAsync(stream);
                    else
                        await WriteStatusAsync(stream, "404 Not Found", "rota desconhecida");
                }
                catch { }
            }
        }

        private static async Task<string> ReadRequestPathAsync(NetworkStream stream)
        {
            var buffer = new byte[2048];
            int read = await stream.ReadAsync(buffer);
            string request = Encoding.ASCII.GetString(buffer, 0, read);
            int a = request.IndexOf(' ');
            int b = a >= 0 ? request.IndexOf(' ', a + 1) : -1;
            return (a >= 0 && b > a) ? request.Substring(a + 1, b - a - 1) : "/";
        }

        private string GetVersion()
        {
            try
            {
                if (File.Exists(_viewerExePath))
                    return FileVersionInfo.GetVersionInfo(_viewerExePath).FileVersion ?? "0.0.0.0";
            }
            catch { }
            return "0.0.0.0";
        }

        private static async Task WriteJsonAsync(NetworkStream stream, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            string header = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n" +
                            $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
            await stream.WriteAsync(body);
        }

        private async Task WriteFileAsync(NetworkStream stream)
        {
            if (!File.Exists(_viewerExePath))
            {
                await WriteStatusAsync(stream, "404 Not Found", "binário indisponível");
                return;
            }

            var info = new FileInfo(_viewerExePath);
            string header = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n" +
                            $"Content-Length: {info.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header));

            using var file = File.OpenRead(_viewerExePath);
            await file.CopyToAsync(stream);
        }

        private static async Task WriteStatusAsync(NetworkStream stream, string status, string message)
        {
            byte[] body = Encoding.UTF8.GetBytes(message);
            string header = $"HTTP/1.1 {status}\r\nContent-Type: text/plain; charset=utf-8\r\n" +
                            $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
            await stream.WriteAsync(body);
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            _cts.Dispose();
        }
    }
}
