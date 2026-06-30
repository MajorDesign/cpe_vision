using System;
using System.IO;
using System.Windows;
using VideoWall.Views;

namespace VideoWall
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Pasta de dados do WebView2 em local gravável (LocalAppData) — quando instalado
            // em Arquivos de Programas, a pasta padrão é somente leitura e o WebView2 falha.
            var udf = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CPE Tecnologia", "VideoWall", "WebView2");
            Directory.CreateDirectory(udf);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", udf);

            // Permite que vídeos (lives do YouTube) toquem sozinhos, sem clique do usuário.
            // E desliga o OVERLAY DE VÍDEO por hardware (DirectComposition): em notebooks/
            // mini-PCs ligados a TVs grandes, o overlay faz o VÍDEO renderizar PRETO (a
            // página aparece, mas o vídeo fica escuro). Definir ANTES de criar qualquer WebView2.
            Environment.SetEnvironmentVariable(
                "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                "--autoplay-policy=no-user-gesture-required --disable-features=DirectCompositionVideoOverlays");

            // O pré-load verifica atualizações e então abre a janela principal.
            new SplashWindow().Show();
        }
    }
}
