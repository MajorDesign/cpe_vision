using System;
using System.Diagnostics;
using System.Windows;
using VideoWall.Network;

namespace VideoWall.Views
{
    /// <summary>
    /// Pré-load do controlador: mostra a versão, verifica no GitHub se há uma versão mais
    /// nova e, se houver, baixa e executa o instalador. Caso contrário (ou sem internet),
    /// abre o aplicativo normalmente.
    /// </summary>
    public partial class SplashWindow : Window
    {
        private const string AssetName = "setup-controlador.exe";

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
                    // Instalação SILENCIOSA (sem assistente). Fecha o app em uso, instala
                    // e reabre o controlador sozinho (ver [Run] WizardSilent no instalador).
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = installer,
                        Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /FORCECLOSEAPPLICATIONS",
                        UseShellExecute = true,
                    });

                    Application.Current.Shutdown(); // o instalador assume e reabre o app
                    return;
                }
            }
            catch
            {
                // Sem internet / falha na verificação: segue abrindo o app.
            }

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
