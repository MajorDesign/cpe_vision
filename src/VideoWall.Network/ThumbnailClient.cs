using System.Net.Sockets;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no controlador: pede ao terminal uma foto (JPEG) do que ele está exibindo,
    /// para mostrar como miniatura ao vivo da tela.
    /// </summary>
    public static class ThumbnailClient
    {
        public static async Task<byte[]?> RequestAsync(string ip, int port = ThumbnailServer.Port, int timeoutMs = 4000)
        {
            try
            {
                using var client = new TcpClient();
                var connect = client.ConnectAsync(ip, port);
                if (await Task.WhenAny(connect, Task.Delay(timeoutMs)) != connect)
                    return null;
                await connect;

                using var stream = client.GetStream();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);

                return buffer.Length > 0 ? buffer.ToArray() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
