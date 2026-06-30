using System;
using System.Diagnostics;
using System.Windows;
using VideoWall.Network;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Pré-load do terminal: mostra a versão, verifica no GitHub se há uma versão mais nova
    /// e, se houver, baixa o instalador e o executa (silencioso). Caso contrário (ou
    /// sem internet), abre o terminal normalmente.
    /// </summary>
    public partial class SplashWindow : Window
    {
        // Atualiza via INSTALADOR (como o controlador): a autossubstituição do .exe
        // falhava em Arquivos de Programas (sem permissão de escrita do quiosque),
        // deixando o terminal preso numa versão. O instalador eleva e troca o app.
        private const string AssetName = "setup-terminal.exe";

        // Tempo mínimo que o pré-load fica visível, para dar tempo de ver a animação
        // (sem isso, quando não há atualização, ele fecha rápido demais).
        private const int MinSplashMs = 5000;
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
                    string installer = await GitHubUpdater.DownloadToTempAsync(url, AssetName);

                    StatusText.Text = "Instalando atualização…";
                    // Instalação SILENCIOSA: fecha o terminal em uso, instala e reabre
                    // sozinho (ver [Run] WizardSilent no setup-terminal.iss).
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = installer,
                        Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /FORCECLOSEAPPLICATIONS",
                        UseShellExecute = true,
                    });

                    Application.Current.Shutdown(); // o instalador assume e reabre o terminal
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

        private void OpenMainAndClose()
        {
            var main = new MainWindow();
            main.Show();
            Close();
        }
    }
}
