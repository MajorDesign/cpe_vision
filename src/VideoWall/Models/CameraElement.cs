namespace VideoWall.Models
{
    /// <summary>
    /// Fonte de câmera IP / CFTV: reproduz um fluxo de vídeo de rede (RTSP, RTMP,
    /// HTTP) ao vivo na parede, usando o motor do VLC.
    /// </summary>
    public class CameraElement : WallElement
    {
        private string _streamUrl = "rtsp://";

        public override string Kind => "Câmera";

        /// <summary>Endereço do fluxo (ex.: rtsp://usuario:senha@host:554/stream).</summary>
        public string StreamUrl
        {
            get => _streamUrl;
            set { if (SetProperty(ref _streamUrl, value)) OnPropertyChanged(nameof(Summary)); }
        }

        public override string Summary => StreamUrl;

        public override WallElement Clone()
        {
            var copy = new CameraElement { StreamUrl = StreamUrl };
            CopyBaseTo(copy);
            return copy;
        }
    }
}
