using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VideoWall.Network
{
    /// <summary>Pacote de instalação do terminal servido pelo central.</summary>
    public sealed record TerminalPackage(Version Version, string FilePath);

    /// <summary>
    /// Servidor HTTP mínimo (sobre TcpListener, sem exigir admin) que o central
    /// roda para entregar o INSTALADOR mais recente do terminal. Os terminais
    /// consultam periodicamente para se auto-atualizarem pela rede local — assim
    /// só o central precisa de internet e o arquivo trafega uma vez pela WAN.
    ///   GET /version  -> {"version":"x.y.z"}
    ///   GET /setup    -> setup-terminal.exe
    /// </summary>
    public sealed class UpdateServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<TerminalPackage?> _package;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        public int Port { get; }

        public UpdateServer(Func<TerminalPackage?> package, int port = 48020)
        {
            _package = package;
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
                    var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
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
                    string path = await ReadRequestPathAsync(stream).ConfigureAwait(false);
                    var package = GetPackage();

                    if (path.StartsWith("/version", StringComparison.OrdinalIgnoreCase))
                    {
                        string version = package?.Version.ToString() ?? "0.0.0";
                        await WriteJsonAsync(stream, $"{{\"version\":\"{version}\"}}").ConfigureAwait(false);
                    }
                    else if (path.StartsWith("/setup", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteFileAsync(stream, package).ConfigureAwait(false);
                    }
                    else
                    {
                        await WriteStatusAsync(stream, "404 Not Found", "rota desconhecida").ConfigureAwait(false);
                    }
                }
                catch { }
            }
        }

        private TerminalPackage? GetPackage()
        {
            try { return _package(); }
            catch { return null; }
        }

        private static async Task<string> ReadRequestPathAsync(NetworkStream stream)
        {
            var buffer = new byte[2048];
            int read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            string request = Encoding.ASCII.GetString(buffer, 0, read);
            int a = request.IndexOf(' ');
            int b = a >= 0 ? request.IndexOf(' ', a + 1) : -1;
            return (a >= 0 && b > a) ? request.Substring(a + 1, b - a - 1) : "/";
        }

        private static async Task WriteJsonAsync(NetworkStream stream, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            string header = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n" +
                            $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header)).ConfigureAwait(false);
            await stream.WriteAsync(body).ConfigureAwait(false);
        }

        private static async Task WriteFileAsync(NetworkStream stream, TerminalPackage? package)
        {
            if (package == null || !File.Exists(package.FilePath))
            {
                await WriteStatusAsync(stream, "404 Not Found", "instalador indisponível").ConfigureAwait(false);
                return;
            }

            var info = new FileInfo(package.FilePath);
            // Content-Disposition nomeia o arquivo. Sem isto, baixar pelo navegador
            // (o caminho manual de recuperação de uma tela) salvava como "setup", sem
            // extensão, e o Windows não sabia o que fazer com ele.
            string header = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n" +
                            $"Content-Disposition: attachment; filename=\"{info.Name}\"\r\n" +
                            $"Content-Length: {info.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header)).ConfigureAwait(false);

            using var file = File.OpenRead(package.FilePath);
            await file.CopyToAsync(stream).ConfigureAwait(false);
        }

        private static async Task WriteStatusAsync(NetworkStream stream, string status, string message)
        {
            byte[] body = Encoding.UTF8.GetBytes(message);
            string header = $"HTTP/1.1 {status}\r\nContent-Type: text/plain; charset=utf-8\r\n" +
                            $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header)).ConfigureAwait(false);
            await stream.WriteAsync(body).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            _cts.Dispose();
        }
    }
}
