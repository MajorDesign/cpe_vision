using System.Net.Sockets;
using System.Text;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no controlador: pergunta ao terminal o LAYOUT ATUAL (JSON) que ele está
    /// exibindo, para reconstruir a parede daquele terminal ao reabrir o controlador.
    /// </summary>
    public static class LayoutQueryClient
    {
        public static async Task<string?> RequestAsync(string ip, int port = LayoutQueryServer.Port, int timeoutMs = 4000)
        {
            try
            {
                using var client = new TcpClient();
                var connect = client.ConnectAsync(ip, port);
                if (await Task.WhenAny(connect, Task.Delay(timeoutMs)) != connect)
                    return null;
                await connect;

                using var stream = client.GetStream();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                return ms.Length > 0 ? Encoding.UTF8.GetString(ms.ToArray()) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
