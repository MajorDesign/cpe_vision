using System.Windows.Media;

namespace VideoWall.Models
{
    /// <summary>
    /// Fonte do tipo navegador: exibe uma página web ao vivo (motor Chromium/Edge
    /// via WebView2) sobre a parede. Útil para dashboards, painéis web e sistemas
    /// acessados pelo navegador numa sala de monitoramento.
    /// </summary>
    public class BrowserElement : WallElement
    {
        private string _url = "https://";
        private double _zoomFactor = 1.0;
        private ImageSource? _previewImage;

        public override string Kind => "Navegador";

        /// <summary>Miniatura do conteúdo da página (capturada ao editar a URL).</summary>
        public ImageSource? PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
        }

        /// <summary>Endereço (URL) da página a ser exibida.</summary>
        public string Url
        {
            get => _url;
            set { if (SetProperty(ref _url, value)) OnPropertyChanged(nameof(Summary)); }
        }

        public override string Summary => Url;

        /// <summary>Fator de zoom da página (1.0 = 100%).</summary>
        public double ZoomFactor
        {
            get => _zoomFactor;
            set => SetProperty(ref _zoomFactor, Math.Clamp(value, 0.25, 5.0));
        }

        public override WallElement Clone()
        {
            var copy = new BrowserElement { Url = Url, ZoomFactor = ZoomFactor, PreviewImage = PreviewImage };
            CopyBaseTo(copy);
            return copy;
        }
    }
}
