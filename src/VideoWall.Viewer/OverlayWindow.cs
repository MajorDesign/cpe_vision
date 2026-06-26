using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
