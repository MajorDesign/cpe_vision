using System;
using System.Windows;

namespace VideoWall.Viewer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Permite que vídeos (lives do YouTube) toquem sozinhos, sem clique do
            // usuário — o terminal é um quiosque. Precisa ser definido ANTES de criar
            // qualquer WebView2.
            Environment.SetEnvironmentVariable(
                "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                "--autoplay-policy=no-user-gesture-required");

            // O pré-load verifica atualizações no GitHub e então abre o terminal.
            new SplashWindow().Show();
        }
    }
}
