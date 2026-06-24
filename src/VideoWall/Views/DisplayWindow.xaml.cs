using System.Collections;
using System.Windows;
using System.Windows.Input;
using VideoWall.Models;

namespace VideoWall.Views
{
    /// <summary>
    /// Janela de saída sem bordas que cobre integralmente um monitor físico e
    /// renderiza o recorte correspondente da parede virtual.
    /// </summary>
    public partial class DisplayWindow : Window
    {
        private readonly MonitorInfo _monitor;
        private readonly double _scale;

        /// <param name="monitor">Monitor de destino (coordenadas em pixels físicos).</param>
        /// <param name="elements">Coleção compartilhada de elementos da parede.</param>
        /// <param name="scale">Fator de escala DPI (ex.: 1.0 para 100%, 1.25 para 125%).</param>
        public DisplayWindow(MonitorInfo monitor, IEnumerable elements, double scale)
        {
            InitializeComponent();

            _monitor = monitor;
            _scale = scale <= 0 ? 1.0 : scale;

            // Posiciona e dimensiona a janela sobre o monitor.
            // As propriedades Left/Top/Width/Height do WPF são em DIP, por isso
            // convertemos a partir dos pixels físicos do monitor.
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _monitor.X / _scale;
            Top = _monitor.Y / _scale;
            Width = _monitor.Width / _scale;
            Height = _monitor.Height / _scale;

            // Janela de saída: renderiza o conteúdo real (ex.: WebView2).
            Surface.IsLive = true;

            // Desloca a superfície para que as coordenadas absolutas da parede
            // virtual apareçam alinhadas dentro desta janela.
            Surface.ElementsSource = elements;
            Surface.OffsetX = -(_monitor.X / _scale);
            Surface.OffsetY = -(_monitor.Y / _scale);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Maximiza o uso da área do monitor garantindo tela cheia sem bordas.
            WindowState = WindowState.Maximized;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Atalho de segurança: Esc fecha esta saída.
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }
    }
}
