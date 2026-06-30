using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Janela sobreposta (PiP) sempre-no-topo que toca uma câmera/live pelo VLC (nativo).
    /// Muito mais leve que a página do navegador (sem site, decode por hardware) — coexiste
    /// melhor com dashboards pesados na parede. Aceita link do YouTube (VLC extrai o stream),
    /// RTSP, HLS… Janela própria (composta pelo DWM) para ficar acima dos navegadores.
    /// </summary>
    internal sealed class VlcOverlayWindow : Window, IOverlay
    {
        private readonly LibVLC _libVlc;
        private readonly MediaPlayer _player;
        private readonly VideoView _videoView;
        private string? _url;

        public VlcOverlayWindow(Window owner, LibVLC libVlc)
        {
            _libVlc = libVlc;
            Owner = owner;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Background = System.Windows.Media.Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -10000; Top = -10000; Width = 320; Height = 180;

            _player = new MediaPlayer(_libVlc);
            _videoView = new VideoView { MediaPlayer = _player };
            Content = _videoView;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        public void SetUrl(string url)
        {
            if (string.Equals(_url, url, StringComparison.Ordinal))
                return;
            _url = url;
            try
            {
                // FromLocation deixa o VLC resolver (inclui o extrator do YouTube via lua).
                var media = new Media(_libVlc, url, FromType.FromLocation);
                _player.Play(media);
                media.Dispose();
            }
            catch { /* URL inválida / VLC indisponível */ }
        }

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

        public void CloseOverlay()
        {
            try { _player.Stop(); } catch { }
            try { _player.Dispose(); } catch { }
            try { _videoView.Dispose(); } catch { }
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
