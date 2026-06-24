using System.Windows;
using System.Windows.Controls;
using VideoWall.Models;

namespace VideoWall.Views
{
    /// <summary>
    /// Escolhe o template de cada elemento conforme o modo da superfície.
    ///
    /// Para fontes "ao vivo" (navegador), na pré-visualização usamos um marcador
    /// estático — pois controles HWND como o WebView2 não respeitam as
    /// transformações de escala do Viewbox — e nas janelas de saída usamos o
    /// controle real. Para os demais tipos, retorna null e o WPF aplica o
    /// DataTemplate implícito por tipo (definido em App.xaml).
    /// </summary>
    public class WallElementTemplateSelector : DataTemplateSelector
    {
        /// <summary>True nas janelas de saída (conteúdo real); false na pré-visualização.</summary>
        public bool IsLive { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is BrowserElement)
            {
                var key = IsLive ? "BrowserLiveTemplate" : "BrowserPreviewTemplate";
                return Application.Current.TryFindResource(key) as DataTemplate;
            }

            if (item is CameraElement)
            {
                var key = IsLive ? "CameraLiveTemplate" : "CameraPreviewTemplate";
                return Application.Current.TryFindResource(key) as DataTemplate;
            }

            // Demais tipos: usa o DataTemplate implícito por tipo.
            return null;
        }
    }
}
