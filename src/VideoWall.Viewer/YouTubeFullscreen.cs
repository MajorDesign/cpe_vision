using System;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Mantém uma live/vídeo do YouTube SEMPRE em tela cheia dentro do WebView2 que a
    /// exibe. Sem isso, a página "watch" — usada sempre que a live tem a incorporação
    /// desativada — aparece na TV com o cabeçalho, a logo e o título do site em volta
    /// do vídeo.
    ///
    /// Só o atalho "F" do player entra em tela cheia de forma confiável, e ele exige um
    /// gesto do usuário. Num quiosque não há ninguém para apertá-lo, então a tecla é
    /// enviada pelo CDP — entrada via protocolo conta como gesto.
    ///
    /// O laço é oportunista, não insistente: enquanto NÃO está em tela cheia, tenta a
    /// cada poucos segundos com teto de tentativas; ao conseguir, PARA. Reenviar "F" com
    /// o player já em tela cheia o tiraria de lá e faria a live re-bufferizar (era o
    /// sintoma de "sempre carregando"). Quando a tela cheia se perde — navegação,
    /// recarregamento, anúncio — o evento re-arma o laço: é isso que faz o "sempre"
    /// valer para o dia inteiro, e não só para os primeiros segundos.
    ///
    /// Em página que não é do YouTube o laço não faz nada: enviar "F" ali digitaria a
    /// letra num campo de texto qualquer.
    /// </summary>
    internal sealed class YouTubeFullscreen : IDisposable
    {
        private const int MaxAttempts = 10;
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

        private const string KeyDown = "{\"type\":\"keyDown\",\"windowsVirtualKeyCode\":70,\"key\":\"f\",\"code\":\"KeyF\"}";
        private const string KeyUp = "{\"type\":\"keyUp\",\"windowsVirtualKeyCode\":70,\"key\":\"f\",\"code\":\"KeyF\"}";

        private readonly WebView2 _web;
        private readonly DispatcherTimer _timer;
        private int _attempts;
        private bool _disposed;

        public YouTubeFullscreen(WebView2 web)
        {
            _web = web;
            _timer = new DispatcherTimer { Interval = Interval };
            _timer.Tick += (_, _) => Tick();

            if (web.CoreWebView2 != null)
                Hook(web.CoreWebView2);
            else
                web.CoreWebView2InitializationCompleted += (_, e) =>
                {
                    if (e.IsSuccess && web.CoreWebView2 != null)
                        Hook(web.CoreWebView2);
                };
        }

        private void Hook(CoreWebView2 core)
        {
            core.NavigationCompleted += (_, _) => Rearm();
            core.ContainsFullScreenElementChanged += (_, _) =>
            {
                if (!core.ContainsFullScreenElement)
                    Rearm();
            };
            Rearm();
        }

        private void Rearm()
        {
            if (_disposed)
                return;
            _attempts = 0;
            if (!_timer.IsEnabled)
                _timer.Start();
        }

        private async void Tick()
        {
            try
            {
                var core = _web.CoreWebView2;
                if (core == null)
                    return;

                // Conseguiu: para de tentar (voltará pelo evento se sair da tela cheia).
                if (core.ContainsFullScreenElement)
                {
                    _timer.Stop();
                    return;
                }

                // Página que não é do YouTube: nada a fazer até a próxima navegação.
                var src = core.Source ?? string.Empty;
                if (src.IndexOf("youtube.com", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    _timer.Stop();
                    return;
                }

                // Teto de tentativas: se o player não aceitar, desiste desta página — o
                // CSS injetado já esconde o cabeçalho, então a TV continua apresentável.
                if (_attempts++ >= MaxAttempts)
                {
                    _timer.Stop();
                    return;
                }

                await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", KeyDown);
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", KeyUp);
            }
            catch
            {
                // WebView2 descartado no meio do caminho: encerra o laço.
                try { _timer.Stop(); } catch { }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            try { _timer.Stop(); } catch { }
        }
    }
}
