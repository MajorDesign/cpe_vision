using System.Net.Sockets;
using System.Text.Json;

namespace VideoWall.Network
{
    /// <summary>Envia comandos do Controlador para um terminal (TCP).</summary>
    public static class CommandSender
    {
        public static async Task SendAsync(string ip, int port, ScreenCommand command, int timeoutMs = 4000)
        {
            using var client = new TcpClient();

            var connect = client.ConnectAsync(ip, port);
            if (await Task.WhenAny(connect, Task.Delay(timeoutMs)) != connect)
                throw new TimeoutException($"Sem resposta de {ip}:{port}.");
            await connect; // propaga eventual erro de conexão

            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(command);
            var stream = client.GetStream();
            await stream.WriteAsync(payload);
            await stream.FlushAsync();

            // Sinaliza fim do envio para o terminal ler o comando completo.
            client.Client.Shutdown(SocketShutdown.Send);
        }
    }
}
