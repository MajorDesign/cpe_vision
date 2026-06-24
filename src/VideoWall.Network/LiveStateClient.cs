using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace VideoWall.Network
{
    /// <summary>
    /// Roda no controlador: pergunta ao terminal o estado atual de uma célula (página +
    /// rolagem) para reabrir o controle ao vivo continuando exatamente de onde estava.
    /// </summary>
    public static class LiveStateClient
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        public static async Task<CellState?> RequestAsync(string ip, int index, int port = CellState.Port, int timeoutMs = 4000)
        {
            try
            {
                using var client = new TcpClient();
                var connect = client.ConnectAsync(ip, port);
                if (await Task.WhenAny(connect, Task.Delay(timeoutMs)) != connect)
                    return null;
                await connect;

                using var stream = client.GetStream();
                var request = Encoding.UTF8.GetBytes(index + "\n");
                await stream.WriteAsync(request);
                await stream.FlushAsync();

                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                if (buffer.Length == 0)
                    return null;

                return JsonSerializer.Deserialize<CellState>(buffer.ToArray(), JsonOpts);
            }
            catch
            {
                return null;
            }
        }
    }
}
