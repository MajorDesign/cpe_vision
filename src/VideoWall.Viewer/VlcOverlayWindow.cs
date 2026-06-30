using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using VideoWall.Network;

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
        private readonly System.Windows.Controls.TextBlock _status;
        private Media? _media;
        private string? _url;
        private bool _pendingPlay;
        private bool _wantPlaying;
        private bool _retryScheduled;

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

            // Diagnóstico visível: mostra o estado do VLC por cima (some quando toca).
            _status = new System.Windows.Controls.TextBlock
            {
                Text = "VLC: iniciando…",
                Foreground = System.Windows.Media.Brushes.White,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(160, 0, 0, 0)),
                FontSize = 13,
                Padding = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };

            var grid = new System.Windows.Controls.Grid();
            grid.Children.Add(_videoView);
            grid.Children.Add(_status);
            Content = grid;

            _player.Opening += (_, _) => SetStatus("VLC: abrindo…");
            _player.Buffering += (_, a) => SetStatus(a.Cache >= 100 ? null : $"VLC: carregando {a.Cache:0}%");
            _player.Playing += (_, _) => SetStatus(null);
            // Live HLS expira de tempos em tempos -> erro/fim: re-extrai e reabre sozinho.
            _player.EncounteredError += (_, _) => { SetStatus("VLC: erro — reabrindo…"); ScheduleRetry(); };
            _player.EndReached += (_, _) => { SetStatus("VLC: reabrindo…"); ScheduleRetry(); };
        }

        private void SetStatus(string? text)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (text == null) { _status.Visibility = Visibility.Collapsed; return; }
                _status.Text = text;
                _status.Visibility = Visibility.Visible;
            }));
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
            _wantPlaying = true;
            _pendingPlay = true;
            if (IsVisible)
                StartPlay(); // já mostrada: toca agora
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

            // Toca DEPOIS de a janela existir/estar dimensionada (o VLC precisa do HWND).
            if (_pendingPlay)
                StartPlay();
        }

        /// <summary>
        /// Resolve e toca: para o YouTube, EXTRAI o .m3u8 real (confiável) em vez de deixar
        /// o VLC adivinhar; para RTSP/HLS, toca direto.
        /// </summary>
        private async void StartPlay()
        {
            _pendingPlay = false;
            var url = _url;
            if (string.IsNullOrWhiteSpace(url))
                return;

            string toPlay = url;
            if (YouTubeLive.IsYouTube(url))
            {
                SetStatus("VLC: obtendo a live do YouTube…");
                var hls = await YouTubeHls.ExtractAsync(url);
                if (!string.IsNullOrWhiteSpace(hls))
                    toPlay = hls!;
                else
                    SetStatus("VLC: live indisponível — tentando…");
            }

            try
            {
                SetStatus("VLC: abrindo…");
                var old = _media;
                _media = new Media(_libVlc, toPlay, FromType.FromLocation);
                _player.Play(_media);
                old?.Dispose();
            }
            catch
            {
                SetStatus("VLC: erro ao abrir — tentando…");
                ScheduleRetry();
            }
        }

        /// <summary>Reabre sozinho após alguns segundos (live HLS expira; offline volta).</summary>
        private void ScheduleRetry()
        {
            if (!_wantPlaying || _retryScheduled)
                return;
            _retryScheduled = true;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await System.Threading.Tasks.Task.Delay(5000);
                _retryScheduled = false;
                if (_wantPlaying && !_player.IsPlaying)
                    StartPlay();
            }));
        }

        public void CloseOverlay()
        {
            _wantPlaying = false;
            try { _player.Stop(); } catch { }
            try { _media?.Dispose(); } catch { }
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
