using System;
using System.IO;
using System.Windows;

namespace VideoWall.Viewer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Rede de proteção: no terminal, um erro não tratado apaga a TV e ninguém está
            // lá para reabrir. Registra e SEGUE. Sem caixa de diálogo, de propósito — um
            // aviso modal ficaria eternamente parado na parede.
            DispatcherUnhandledException += (_, args) =>
            {
                VideoWall.Network.ErrorLog.Write("Erro não tratado no terminal", args.Exception);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                VideoWall.Network.ErrorLog.Write("Erro fatal no terminal", args.ExceptionObject as Exception);

            // PASTA DE DADOS DO WEBVIEW2 EM LOCAL GRAVÁVEL. O terminal instala em Arquivos
            // de Programas (somente leitura para o quiosque); a pasta padrão do WebView2
            // fica lá e ele FALHA AO INICIAR -> tela preta ("navegador não abre nada").
            // Em LocalAppData ele sempre consegue gravar. Definir ANTES de qualquer WebView2.
            var udf = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CPE Tecnologia", "VideoWall", "WebView2");
            Directory.CreateDirectory(udf);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", udf);

            // Lives tocam sozinhas (quiosque). O OVERLAY DE VÍDEO por hardware é alternável
            // pelo controlador (botão): DESLIGADO (padrão) compõe o vídeo pela GPU — sempre
            // visível; LIGADO usa o plano de overlay — mais leve na GPU (alivia a disputa
            // com dashboards pesados), mas pode ficar preto em algumas placas/TVs.
            var args = "--autoplay-policy=no-user-gesture-required";
            if (!TerminalSettings.HardwareVideoOverlay)
                args += " --disable-features=DirectCompositionVideoOverlays";
            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", args);

            // O pré-load verifica atualizações (central na LAN, GitHub como reserva)
            // e então abre o terminal.
            new SplashWindow().Show();
        }
    }
}
