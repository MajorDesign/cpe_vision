using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace VideoWall.Views
{
    /// <summary>
    /// Controle que renderiza a coleção de elementos da parede, deslocada por
    /// um offset. Os elementos (DataTemplates por tipo) são definidos em App.xaml.
    /// </summary>
    public partial class WallSurfaceView : UserControl
    {
        public WallSurfaceView()
        {
            InitializeComponent();
            UpdateTemplateSelector();
        }

        /// <summary>
        /// Define se a superfície renderiza o conteúdo "ao vivo" (janelas de
        /// saída) ou marcadores (pré-visualização). Afeta apenas fontes como o
        /// navegador, cujo controle HWND não funciona sob transformações.
        /// </summary>
        public static readonly DependencyProperty IsLiveProperty =
            DependencyProperty.Register(
                nameof(IsLive),
                typeof(bool),
                typeof(WallSurfaceView),
                new PropertyMetadata(false, OnIsLiveChanged));

        public bool IsLive
        {
            get => (bool)GetValue(IsLiveProperty);
            set => SetValue(IsLiveProperty, value);
        }

        private static void OnIsLiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((WallSurfaceView)d).UpdateTemplateSelector();
        }

        private void UpdateTemplateSelector()
        {
            Items.ItemTemplateSelector = new WallElementTemplateSelector { IsLive = IsLive };
        }

        public static readonly DependencyProperty ElementsSourceProperty =
            DependencyProperty.Register(
                nameof(ElementsSource),
                typeof(IEnumerable),
                typeof(WallSurfaceView),
                new PropertyMetadata(null));

        /// <summary>Coleção de <see cref="Models.WallElement"/> a renderizar.</summary>
        public IEnumerable? ElementsSource
        {
            get => (IEnumerable?)GetValue(ElementsSourceProperty);
            set => SetValue(ElementsSourceProperty, value);
        }

        public static readonly DependencyProperty OffsetXProperty =
            DependencyProperty.Register(
                nameof(OffsetX),
                typeof(double),
                typeof(WallSurfaceView),
                new PropertyMetadata(0.0));

        /// <summary>Deslocamento horizontal aplicado ao conteúdo (em DIP).</summary>
        public double OffsetX
        {
            get => (double)GetValue(OffsetXProperty);
            set => SetValue(OffsetXProperty, value);
        }

        public static readonly DependencyProperty OffsetYProperty =
            DependencyProperty.Register(
                nameof(OffsetY),
                typeof(double),
                typeof(WallSurfaceView),
                new PropertyMetadata(0.0));

        /// <summary>Deslocamento vertical aplicado ao conteúdo (em DIP).</summary>
        public double OffsetY
        {
            get => (double)GetValue(OffsetYProperty);
            set => SetValue(OffsetYProperty, value);
        }
    }
}
