using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Cortina exibida durante a troca de layout, enquanto as páginas novas carregam e
    /// os vídeos entram em tela cheia. Sem ela, a parede mostra o bastidor: páginas
    /// aparecendo uma a uma, vídeo quebrado, cada quadro ajustando o enquadramento na
    /// frente de quem está assistindo.
    ///
    /// Precisa ser uma JANELA própria, sempre-no-topo: qualquer elemento WPF desenhado
    /// sobre um WebView2 fica invisível (airspace), então um retângulo por cima da
    /// superfície simplesmente não cobriria nada.
    /// </summary>
    internal sealed class CurtainWindow : Window
    {
        public CurtainWindow(Window owner)
        {
            Owner = owner;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            AllowsTransparency = false;
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0F, 0x12));
            WindowStartupLocation = WindowStartupLocation.Manual;

            Content = new TextBlock
            {
                Text = "Preparando a parede…",
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Não rouba foco (quiosque) e fica fora do Alt+Tab.
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        /// <summary>Cobre a tela inteira e vai para o topo (acima das lives sobrepostas).</summary>
        public void Cover(Window screen)
        {
            var origem = screen.PointToScreen(new Point(0, 0));
            var canto = screen.PointToScreen(new Point(screen.ActualWidth, screen.ActualHeight));

            if (!IsVisible)
                Show();

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            int w = Math.Max(1, (int)(canto.X - origem.X));
            int h = Math.Max(1, (int)(canto.Y - origem.Y));
            SetWindowPos(hwnd, HWND_TOPMOST, (int)origem.X, (int)origem.Y, w, h,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        /// <summary>Reafirma o topo — as janelas das lives também são topmost.</summary>
        public void BringToTop()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
