using System;
using System.Windows;
using VideoWall.Views;

namespace VideoWall
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Permite que vídeos (lives do YouTube) toquem sozinhos na pré-visualização,
            // sem clique do usuário. Precisa ser definido ANTES de criar qualquer WebView2.
            Environment.SetEnvironmentVariable(
                "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                "--autoplay-policy=no-user-gesture-required");

            // O pré-load verifica atualizações e então abre a janela principal.
            new SplashWindow().Show();
        }
    }
}
