using System.Net;
using System.Net.Sockets;

namespace VideoWall.Network
{
    public static class NetworkUtil
    {
        /// <summary>
        /// Descobre o endereço IPv4 local usado para sair à rede (sem enviar
        /// dados — apenas resolve a rota). Retorna loopback em caso de falha.
        /// </summary>
        public static string GetLocalIPv4()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint endpoint)
                    return endpoint.Address.ToString();
            }
            catch
            {
                // Sem rota; usa loopback.
            }

            return IPAddress.Loopback.ToString();
        }
    }
}
