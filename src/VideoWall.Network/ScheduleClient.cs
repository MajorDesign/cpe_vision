using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace VideoWall.Network
{
    /// <summary>
    /// Usado pelo controlador para ler e gravar a programação de uma tela. Sempre
    /// devolve o que ficou valendo NA TELA — é assim que dois controladores
    /// enxergam a mesma coisa, em vez de cada um confiar na própria memória.
    /// </summary>
    public static class ScheduleClient
    {
        public static Task<ScreenSchedule?> GetAsync(string ip, int port = ScreenSchedule.Port, int timeoutMs = 5000) =>
            SendAsync(ip, "GET", port, timeoutMs);

        public static Task<ScreenSchedule?> SetAsync(ScreenSchedule schedule, string ip,
            int port = ScreenSchedule.Port, int timeoutMs = 8000) =>
            SendAsync(ip, "SET" + JsonSerializer.Serialize(schedule), port, timeoutMs);

        private static async Task<ScreenSchedule?> SendAsync(string ip, string pedido, int port, int timeoutMs)
        {
            try
            {
                using var client = new TcpClient();
                var conectar = client.ConnectAsync(ip, port);
                if (await Task.WhenAny(conectar, Task.Delay(timeoutMs)).ConfigureAwait(false) != conectar)
                    return null;
                await conectar.ConfigureAwait(false);

                using var stream = client.GetStream();
                await stream.WriteAsync(Encoding.UTF8.GetBytes(pedido)).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                client.Client.Shutdown(SocketShutdown.Send); // sinaliza fim do pedido

                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer).ConfigureAwait(false);
                if (buffer.Length == 0)
                    return null;

                return JsonSerializer.Deserialize<ScreenSchedule>(buffer.ToArray());
            }
            catch
            {
                // Tela offline ou versão antiga (sem este canal): o chamador avisa.
                return null;
            }
        }
    }
}
