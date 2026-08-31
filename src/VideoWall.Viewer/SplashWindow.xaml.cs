using System;
using System.Diagnostics;
using System.Windows;
using VideoWall.Network;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Pré-load do terminal: mostra a versão e verifica se há uma versão mais nova —
    /// primeiro no computador central da rede local, depois no GitHub. Havendo, baixa
    /// o instalador e dispara a instalação silenciosa. Caso contrário, abre o terminal
    /// normalmente. A verificação continua acontecendo com o terminal aberto (ver
    /// <see cref="TerminalUpdater"/> em MainWindow).
    /// </summary>
    public partial class SplashWindow : Window
    {
        // Tempo mínimo que o pré-load fica visível, para dar tempo de ver a animação
        // (sem isso, quando não há atualização, ele fecha rápido demais).
        private const int MinSplashMs = 5000;

        // Espera pelo anúncio do central (chega a cada 2s) antes de decidir a fonte.
        private static readonly TimeSpan ControllerWait = TimeSpan.FromSeconds(4);

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
                using var updater = new TerminalUpdater();
                await updater.WaitForControllerAsync(ControllerWait);

                if (await updater.CheckAndUpdateAsync())
                {
                    // O instalador assume: fecha este processo e o "reabrir.cmd"
                    // abre o terminal já atualizado.
                    StatusText.Text = "Instalando atualização…";
                    return;
                }
            }
            catch
            {
                // Central fora do ar / sem internet: segue abrindo o terminal.
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
