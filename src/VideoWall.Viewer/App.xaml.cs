using System;
using System.Windows;

namespace VideoWall.Viewer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Permite que vídeos (lives do YouTube) toquem sozinhos — o terminal é um
            // quiosque. E desliga a aceleração de GPU do WebView2: no mini-PC/notebook
            // ligado à TV grande, a composição por GPU faz o conteúdo (principalmente
            // VÍDEO) renderizar PRETO. Por software, renderiza em qualquer saída/TV.
            // Definir ANTES de criar qualquer WebView2.
            Environment.SetEnvironmentVariable(
                "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                "--autoplay-policy=no-user-gesture-required --disable-gpu");

            // O pré-load verifica atualizações no GitHub e então abre o terminal.
            new SplashWindow().Show();
        }
    }
}
