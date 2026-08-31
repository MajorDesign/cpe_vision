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

            // Rede de proteção: um erro não tratado fechava o controlador inteiro no meio
            // da operação (aconteceu ao abrir o seletor de janelas). Aqui ele vira um
            // aviso e uma linha no log — o operador não perde a parede que estava montando.
            DispatcherUnhandledException += (_, args) =>
            {
                VideoWall.Network.ErrorLog.Write("Erro não tratado no controlador", args.Exception);
                MessageBox.Show(
                    $"Ocorreu um erro e a ação foi cancelada.\n\n{args.Exception.Message}\n\n" +
                    $"Detalhes em:\n{VideoWall.Network.ErrorLog.FilePath}",
                    "CPE VideoWall", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                VideoWall.Network.ErrorLog.Write("Erro fatal no controlador", args.ExceptionObject as Exception);

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
