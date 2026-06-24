using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using VideoWall.Models;

namespace VideoWall.Services
{
    /// <summary>
    /// Mantém, para um monitor específico, a sub-coleção de elementos cuja área
    /// intersecta a região daquele monitor na parede virtual. Assim, cada janela
    /// de saída só instancia os elementos que realmente aparecem nela — evitando,
    /// por exemplo, criar um WebView2 oculto em todas as telas.
    ///
    /// A pertinência é reavaliada quando elementos são adicionados/removidos e
    /// quando um elemento é movido ou redimensionado.
    /// </summary>
    public sealed class MonitorViewportElements : IDisposable
    {
        private readonly ObservableCollection<WallElement> _source;
        private readonly double _left;
        private readonly double _top;
        private readonly double _right;
        private readonly double _bottom;

        /// <summary>Elementos atualmente visíveis neste monitor.</summary>
        public ObservableCollection<WallElement> Visible { get; } = new();

        /// <param name="source">Coleção mestre de elementos da parede.</param>
        /// <param name="monitor">Monitor de destino (pixels físicos).</param>
        /// <param name="dpiScale">Fator de escala DPI (para converter px → DIP).</param>
        public MonitorViewportElements(ObservableCollection<WallElement> source, MonitorInfo monitor, double dpiScale)
        {
            _source = source;

            double scale = dpiScale <= 0 ? 1.0 : dpiScale;
            _left = monitor.X / scale;
            _top = monitor.Y / scale;
            _right = (monitor.X + monitor.Width) / scale;
            _bottom = (monitor.Y + monitor.Height) / scale;

            _source.CollectionChanged += OnSourceChanged;

            foreach (var element in _source)
            {
                element.PropertyChanged += OnElementPropertyChanged;
                Evaluate(element);
            }
        }

        private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (WallElement element in e.OldItems)
                {
                    element.PropertyChanged -= OnElementPropertyChanged;
                    Visible.Remove(element);
                }
            }

            if (e.NewItems != null)
            {
                foreach (WallElement element in e.NewItems)
                {
                    element.PropertyChanged += OnElementPropertyChanged;
                    Evaluate(element);
                }
            }

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var element in Visible.ToList())
                {
                    element.PropertyChanged -= OnElementPropertyChanged;
                }
                Visible.Clear();
            }
        }

        private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not WallElement element)
                return;

            // Só a geometria altera a pertinência ao monitor.
            if (e.PropertyName is nameof(WallElement.X) or nameof(WallElement.Y)
                or nameof(WallElement.Width) or nameof(WallElement.Height))
            {
                Evaluate(element);
            }
        }

        /// <summary>Inclui ou remove o elemento conforme intersecte o monitor.</summary>
        private void Evaluate(WallElement element)
        {
            bool intersects = Intersects(element);
            bool present = Visible.Contains(element);

            if (intersects && !present)
                Visible.Add(element);
            else if (!intersects && present)
                Visible.Remove(element);
        }

        private bool Intersects(WallElement e)
        {
            // Retângulos disjuntos não se intersectam.
            return !(e.X + e.Width <= _left
                     || e.X >= _right
                     || e.Y + e.Height <= _top
                     || e.Y >= _bottom);
        }

        public void Dispose()
        {
            _source.CollectionChanged -= OnSourceChanged;

            foreach (var element in _source)
            {
                element.PropertyChanged -= OnElementPropertyChanged;
            }

            Visible.Clear();
        }
    }
}
