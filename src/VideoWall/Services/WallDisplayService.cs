using System.Collections.ObjectModel;
using VideoWall.Models;
using VideoWall.Views;

namespace VideoWall.Services
{
    /// <inheritdoc cref="IWallDisplayService"/>
    public class WallDisplayService : IWallDisplayService
    {
        private readonly List<DisplayWindow> _windows = new();
        private readonly Dictionary<DisplayWindow, MonitorViewportElements> _viewports = new();

        public bool IsRunning => _windows.Count > 0;

        public void Start(IEnumerable<MonitorInfo> monitors, ObservableCollection<WallElement> elements, double dpiScale)
        {
            // Garante um estado limpo antes de (re)projetar a parede.
            Stop();

            foreach (var monitor in monitors)
            {
                // Cada janela observa apenas os elementos que tocam o seu monitor.
                var viewport = new MonitorViewportElements(elements, monitor, dpiScale);
                var window = new DisplayWindow(monitor, viewport.Visible, dpiScale);

                // Remove a janela caso seja fechada individualmente (ex.: Esc),
                // mantendo o estado consistente e liberando o viewport.
                window.Closed += OnWindowClosed;

                _windows.Add(window);
                _viewports[window] = viewport;
                window.Show();
            }
        }

        public void Stop()
        {
            // Itera sobre uma cópia, pois Close() dispara Closed -> remove da lista.
            foreach (var window in _windows.ToList())
            {
                window.Closed -= OnWindowClosed;
                DisposeViewport(window);
                window.Close();
            }

            _windows.Clear();
            _viewports.Clear();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (sender is DisplayWindow window)
            {
                window.Closed -= OnWindowClosed;
                DisposeViewport(window);
                _windows.Remove(window);
            }
        }

        private void DisposeViewport(DisplayWindow window)
        {
            if (_viewports.TryGetValue(window, out var viewport))
            {
                viewport.Dispose();
                _viewports.Remove(window);
            }
        }
    }
}
