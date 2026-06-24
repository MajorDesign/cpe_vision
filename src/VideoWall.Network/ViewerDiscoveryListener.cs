using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace VideoWall.Network
{
    /// <summary>
    /// Escuta os anúncios dos viewers na rede e mantém a lista atualizada das
    /// telas online. Dispara <see cref="ViewersChanged"/> a cada alteração.
    /// </summary>
    public sealed class ViewerDiscoveryListener : IDisposable
    {
        private readonly UdpClient _udp;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<string, ViewerInfo> _viewers = new();
        private readonly System.Timers.Timer _pruneTimer;
        private Task? _loop;

        /// <summary>Disparado (em thread de fundo) quando a lista de viewers muda.</summary>
        public event Action<IReadOnlyList<ViewerInfo>>? ViewersChanged;

        public ViewerDiscoveryListener()
        {
            _udp = new UdpClient { EnableBroadcast = true };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryConstants.Port));

            _pruneTimer = new System.Timers.Timer(1000) { AutoReset = true };
            _pruneTimer.Elapsed += (_, _) => Prune();
        }

        public void Start()
        {
            _loop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            _pruneTimer.Start();
        }

        public IReadOnlyList<ViewerInfo> Current => Snapshot();

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _udp.ReceiveAsync(ct);
                    var info = JsonSerializer.Deserialize<ViewerInfo>(result.Buffer);
                    if (info == null || info.Type != DiscoveryConstants.MessageType || string.IsNullOrEmpty(info.Id))
                        continue;

                    info.LastSeenUtc = DateTime.UtcNow;
                    bool isNew = !_viewers.ContainsKey(info.Id);
                    _viewers[info.Id] = info;

                    if (isNew)
                        RaiseChanged();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Pacote inválido / erro transitório: ignora.
                }
            }
        }

        private void Prune()
        {
            DateTime cutoff = DateTime.UtcNow - DiscoveryConstants.OfflineTimeout;
            bool removed = false;

            foreach (var pair in _viewers)
            {
                if (pair.Value.LastSeenUtc < cutoff && _viewers.TryRemove(pair.Key, out _))
                    removed = true;
            }

            if (removed)
                RaiseChanged();
        }

        private IReadOnlyList<ViewerInfo> Snapshot() =>
            _viewers.Values.OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        private void RaiseChanged() => ViewersChanged?.Invoke(Snapshot());

        public void Dispose()
        {
            _cts.Cancel();
            _pruneTimer.Dispose();
            try { _udp.Dispose(); } catch { }
            _cts.Dispose();
        }
    }
}
