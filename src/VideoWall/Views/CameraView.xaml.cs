using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using VideoWall.Capture;

namespace VideoWall.Views
{
    /// <summary>
    /// Reproduz um fluxo de vídeo (RTSP/RTMP/HTTP) via VLC. Gerencia o ciclo de
    /// vida do MediaPlayer junto com a presença do controle na árvore visual.
    /// </summary>
    public partial class CameraView : UserControl
    {
        private MediaPlayer? _mediaPlayer;
        private bool _loaded;

        public CameraView()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty StreamUrlProperty =
            DependencyProperty.Register(
                nameof(StreamUrl),
                typeof(string),
                typeof(CameraView),
                new PropertyMetadata(string.Empty, OnStreamUrlChanged));

        /// <summary>URL do fluxo de vídeo a reproduzir.</summary>
        public string StreamUrl
        {
            get => (string)GetValue(StreamUrlProperty);
            set => SetValue(StreamUrlProperty, value);
        }

        private static void OnStreamUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (CameraView)d;
            if (view._loaded)
                view.StartPlayback();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;
            StartPlayback();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _loaded = false;
            StopPlayback();
        }

        private void StartPlayback()
        {
            if (!Uri.TryCreate(StreamUrl, UriKind.Absolute, out var uri))
                return;

            try
            {
                var libvlc = VlcRuntime.Instance;
                _mediaPlayer ??= new MediaPlayer(libvlc);
                Video.MediaPlayer = _mediaPlayer;

                using var media = new Media(libvlc, uri);
                // Baixa latência e RTSP sobre TCP (mais estável em câmeras IP).
                media.AddOption(":network-caching=300");
                media.AddOption(":rtsp-tcp");
                _mediaPlayer.Play(media);
            }
            catch
            {
                // Falha ao iniciar o fluxo (URL inválida/indisponível): segue em preto.
            }
        }

        private void StopPlayback()
        {
            if (_mediaPlayer == null)
                return;

            var mediaPlayer = _mediaPlayer;
            _mediaPlayer = null;
            Video.MediaPlayer = null;

            try { mediaPlayer.Stop(); } catch { }
            try { mediaPlayer.Dispose(); } catch { }
        }
    }
}
