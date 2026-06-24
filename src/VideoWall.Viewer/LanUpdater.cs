using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using VideoWall.Network;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Mantém o terminal atualizado a partir do computador central (rede local).
    /// Localiza o central pela descoberta, compara a versão servida e, se for
    /// mais nova, baixa o binário e se substitui (reinicia via .cmd).
    /// </summary>
    public sealed class LanUpdater
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
        private readonly ControllerLocator _locator;
        private bool _busy;

        public LanUpdater(ControllerLocator locator) => _locator = locator;

        public async Task CheckAndUpdateAsync()
        {
            if (_busy || _locator.Current is not { } controller)
                return;
            _busy = true;
            try
            {
                string baseUrl = $"http://{controller.IpAddress}:{controller.UpdatePort}";
                string json = await Http.GetStringAsync(baseUrl + "/version");

                using var doc = JsonDocument.Parse(json);
                string? remoteText = doc.RootElement.GetProperty("version").GetString();
                if (!Version.TryParse(remoteText, out var remote))
                    return;

                Version local = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);
                if (remote <= local)
                    return;

                byte[] bytes = await Http.GetByteArrayAsync(baseUrl + "/viewer");

                string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
                string dir = Path.GetDirectoryName(exePath)!;
                string exeName = Path.GetFileName(exePath);
                await File.WriteAllBytesAsync(Path.Combine(dir, exeName + ".new"), bytes);

                LaunchSwapAndRestart(dir, exeName);
            }
            catch
            {
                // Central indisponível / sem rede: tenta no próximo ciclo.
            }
            finally
            {
                _busy = false;
            }
        }

        private static void LaunchSwapAndRestart(string dir, string exeName)
        {
            int pid = Environment.ProcessId;
            string cmdPath = Path.Combine(dir, "atualizar.cmd");

            string script =
                "@echo off\r\n" +
                ":wait\r\n" +
                $"tasklist /fi \"PID eq {pid}\" | findstr /i \"{pid}\" >nul && ( timeout /t 1 /nobreak >nul & goto wait )\r\n" +
                $"move /y \"{exeName}.new\" \"{exeName}\" >nul\r\n" +
                $"start \"\" \"{exeName}\"\r\n" +
                "del \"%~f0\"\r\n";

            File.WriteAllText(cmdPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{cmdPath}\"",
                WorkingDirectory = dir,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            });

            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        }
    }
}
