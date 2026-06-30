using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VideoWall.Network;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Janela sem bordas, sempre-no-topo, que exibe uma miniatura sobreposta (PiP) —
    /// como uma live do YouTube — por cima do videowall.
    ///
    /// Usar uma JANELA separada (composta pelo Windows/DWM) é o que garante que a
    /// live fique acima dos navegadores: dois WebView2 dentro da MESMA janela não
    /// respeitam a ordem de empilhamento (limitação de "airspace" do controle HWND).
    /// A posição/tamanho são definidos em pixels físicos (SetWindowPos) para casar
    /// exatamente com a célula, independente da escala de DPI.
    /// </summary>
    internal sealed class OverlayWindow : Window
    {
        private readonly WebView2 _web;
        private readonly DispatcherTimer _fullscreenTimer;
        private int _fsAttempts;
        private string? _url;

        public OverlayWindow(Window owner)
        {
            Owner = owner;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // Começa fora da tela; SetWindowPos coloca no lugar certo.
            Left = -10000; Top = -10000; Width = 320; Height = 180;

            _web = new WebView2();
            _web.CoreWebView2InitializationCompleted += (_, _) =>
            {
                // Serve o player.html das lives (host virtual) — embed direto dá Erro 153.
                try
                {
                    _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        YouTubeLive.VirtualHost, YouTubeLive.EnsurePlayerFolder(),
                        CoreWebView2HostResourceAccessKind.Allow);
                }
                catch { }
                // Mantém a live tocando e remove popups quando cai na página do YouTube.
                try { _ = _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(YouTubeLive.KeepPlayingScript); }
                catch { }
            };
            Content = _web;

            // Quando a live cai na página do YouTube (embed bloqueado), entra em tela
            // cheia sozinha (tecla F do player) — o vídeo preenche o quadro, sem cabeçalho.
            // Entrada via CDP conta como gesto do usuário, permitindo o fullscreen.
            _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _fullscreenTimer.Tick += (_, _) => EnsureFullscreen();
            _fullscreenTimer.Start();
        }

        /// <summary>
        /// Se a página atual for do YouTube e ainda não estiver em tela cheia, envia a
        /// tecla "F" (atalho de tela cheia do player) via CDP. Repetir é seguro: quando
        /// já está em tela cheia, não faz nada.
        /// </summary>
        private async void EnsureFullscreen()
        {
            var core = _web.CoreWebView2;
            if (core == null) return;

            try
            {
                // Já em tela cheia: PARA o timer. Ficar reenviando F entraria/sairia da
                // tela cheia e re-bufferizaria a live (ficava 'sempre carregando').
                if (core.ContainsFullScreenElement)
                {
                    _fullscreenTimer.Stop();
                    return;
                }

                var src = core.Source ?? string.Empty;
                if (src.IndexOf("youtube.com", StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                // Limite de tentativas: se não entrar em tela cheia, desiste (o CSS já
                // esconde o cabeçalho) — evita ficar tentando para sempre.
                if (_fsAttempts++ >= 6)
                {
                    _fullscreenTimer.Stop();
                    return;
                }

                const string down = "{\"type\":\"keyDown\",\"windowsVirtualKeyCode\":70,\"key\":\"f\",\"code\":\"KeyF\"}";
                const string up = "{\"type\":\"keyUp\",\"windowsVirtualKeyCode\":70,\"key\":\"f\",\"code\":\"KeyF\"}";
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", down);
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", up);
            }
            catch { /* tenta de novo no próximo tick */ }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Não rouba o foco (o terminal é um quiosque) e fica fora do Alt+Tab.
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        /// <summary>Define a URL exibida (recarrega só quando muda).</summary>
        public void SetUrl(string url)
        {
            if (string.Equals(_url, url, StringComparison.Ordinal))
                return;
            _url = url;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                try { _web.Source = uri; } catch { }
            }
        }

        /// <summary>Posiciona a janela na área da célula (pixels físicos) e a mostra no topo.</summary>
        public void PlaceOnScreen(int x, int y, int w, int h)
        {
            if (w < 1) w = 1;
            if (h < 1) h = 1;
            if (!IsVisible)
                Show();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        /// <summary>Libera o WebView2 e fecha a janela.</summary>
        public void CloseOverlay()
        {
            try { _fullscreenTimer.Stop(); } catch { }
            try { _web.Dispose(); } catch { }
            try { Close(); } catch { }
        }

        // ----------------------------------------------------------------------- Win32
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
