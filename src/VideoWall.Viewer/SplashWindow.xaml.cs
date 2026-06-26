using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using VideoWall.Network;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Pré-load do terminal: mostra a versão, verifica no GitHub se há uma versão mais nova
    /// e, se houver, baixa o executável e se auto-substitui (reinicia). Caso contrário (ou
    /// sem internet), abre o terminal normalmente.
    /// </summary>
    public partial class SplashWindow : Window
    {
        private const string AssetName = "VideoWall.Viewer.exe";

        // Tempo mínimo que o pré-load fica visível, para dar tempo de ver a animação
        // (sem isso, quando não há atualização, ele fecha rápido demais).
        private const int MinSplashMs = 3500;
        private readonly Stopwatch _shownSince = Stopwatch.StartNew();

        public SplashWindow()
        {
            InitializeComponent();
            VersionText.Text = "v" + GitHubUpdater.CurrentVersion();
            Loaded += async (_, _) => await RunAsync();
        }

        private async System.Threading.Tasks.Task RunAsync()
        {
            StatusText.Text = "Verificando atualizações…";

            try
            {
                var latest = await GitHubUpdater.GetLatestAsync();
                if (latest != null &&
                    latest.Version > GitHubUpdater.CurrentVersion() &&
                    latest.Assets.TryGetValue(AssetName, out var url))
                {
                    StatusText.Text = $"Baixando versão {latest.Version}…";
                    string temp = await GitHubUpdater.DownloadToTempAsync(url, AssetName);

                    StatusText.Text = "Aplicando atualização…";
                    SelfReplaceAndRestart(temp);
                    return;
                }
            }
            catch
            {
                // Sem internet / falha na verificação: segue abrindo o terminal.
            }

            // Garante o tempo mínimo de exibição do pré-load.
            int elapsed = (int)_shownSince.ElapsedMilliseconds;
            if (elapsed < MinSplashMs)
                await System.Threading.Tasks.Task.Delay(MinSplashMs - elapsed);

            OpenMainAndClose();
        }

        private void SelfReplaceAndRestart(string newExeTempPath)
        {
            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
            string dir = Path.GetDirectoryName(exePath)!;
            string exeName = Path.GetFileName(exePath);

            // Coloca o novo binário ao lado do atual e troca via .cmd depois que o processo sai.
            File.Copy(newExeTempPath, Path.Combine(dir, exeName + ".new"), overwrite: true);

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

            Application.Current.Shutdown();
        }

        private void OpenMainAndClose()
        {
            var main = new MainWindow();
            main.Show();
            Close();
        }
    }
}
