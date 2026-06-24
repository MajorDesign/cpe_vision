using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no terminal: escuta os anúncios do computador central e guarda o
    /// último <see cref="ControllerInfo"/> conhecido, para localizar onde buscar
    /// atualizações.
    /// </summary>
    public sealed class ControllerLocator : IDisposable
    {
        private readonly UdpClient _udp;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        public ControllerInfo? Current { get; private set; }

        public ControllerLocator()
        {
            _udp = new UdpClient { EnableBroadcast = true };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryConstants.Port));
        }

        public void Start() => _loop = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _udp.ReceiveAsync(ct);
                    var info = JsonSerializer.Deserialize<ControllerInfo>(result.Buffer);
                    if (info != null && info.Type == ControllerInfo.ControllerType && !string.IsNullOrEmpty(info.IpAddress))
                        Current = info;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Pacote alheio / inválido: ignora.
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _udp.Dispose(); } catch { }
            _cts.Dispose();
        }
    }
}
