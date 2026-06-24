using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace VideoWall.Network
{
    /// <summary>
    /// Anuncia periodicamente uma mensagem (JSON) por broadcast UDP. Para ser
    /// confiável em máquinas com várias interfaces (Hyper-V, VPN, Wi-Fi +
    /// cabo...), envia para o broadcast dirigido de CADA interface IPv4 ativa,
    /// além do broadcast limitado 255.255.255.255.
    /// </summary>
    public sealed class UdpBeacon : IDisposable
    {
        private readonly byte[] _payload;
        private readonly int _port;
        private readonly UdpClient _udp;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        public UdpBeacon(object message, int port = DiscoveryConstants.Port)
        {
            _payload = JsonSerializer.SerializeToUtf8Bytes(message);
            _port = port;
            _udp = new UdpClient { EnableBroadcast = true };
        }

        public void Start()
        {
            _loop = Task.Run(() => BroadcastLoopAsync(_cts.Token));
        }

        private async Task BroadcastLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var target in GetBroadcastTargets(_port))
                {
                    try
                    {
                        await _udp.SendAsync(_payload, _payload.Length, target);
                    }
                    catch
                    {
                        // Interface indisponível: tenta as outras.
                    }
                }

                try
                {
                    await Task.Delay(DiscoveryConstants.BeaconInterval, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>Endereços de broadcast: limitado + dirigido por interface.</summary>
        private static List<IPEndPoint> GetBroadcastTargets(int port)
        {
            var targets = new List<IPEndPoint> { new(IPAddress.Broadcast, port) };

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork || addr.IPv4Mask == null)
                            continue;

                        byte[] ip = addr.Address.GetAddressBytes();
                        byte[] mask = addr.IPv4Mask.GetAddressBytes();
                        if (mask.Length != 4)
                            continue;

                        var broadcast = new byte[4];
                        for (int i = 0; i < 4; i++)
                            broadcast[i] = (byte)(ip[i] | (~mask[i] & 0xFF));

                        targets.Add(new IPEndPoint(new IPAddress(broadcast), port));
                    }
                }
            }
            catch
            {
                // Enumeração de interfaces falhou: usa só o broadcast limitado.
            }

            return targets;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _udp.Dispose(); } catch { }
            _cts.Dispose();
        }
    }
}
