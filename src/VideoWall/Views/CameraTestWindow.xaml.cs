using System.Windows;

namespace VideoWall.Views
{
    /// <summary>Janela simples para testar/visualizar uma URL de câmera ou stream.</summary>
    public partial class CameraTestWindow : Window
    {
        public CameraTestWindow(string streamUrl)
        {
            InitializeComponent();
            UrlText.Text = streamUrl;
            Cam.StreamUrl = streamUrl;
        }
    }
}
