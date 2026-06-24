using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VideoWall.Network;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Terminal: ocupa a tela inteira, anuncia-se na rede, recebe do controlador
    /// um LAYOUT (várias fontes) e o renderiza, e se mantém atualizado pela internet.
    /// A renderização é INCREMENTAL: ao receber um layout novo, só recria/recarrega as
    /// fontes que realmente mudaram — assim reprojetar não reinicia páginas que continuam
    /// iguais (preserva rolagem e o controle ao vivo de cada navegador).
    /// </summary>
    public partial class MainWindow : Window
    {
        private UdpBeacon? _beacon;
        private CommandServer? _commandServer;
        private LiveInputServer? _liveInputServer;
        private ThumbnailServer? _thumbnailServer;
        private LiveStateServer? _liveStateServer;

        // Uma "vaga" por fonte do layout, na MESMA ordem em que o controlador as envia.
        // _slotUrls guarda a última URL PROJETADA de cada navegador (não a navegação ao
        // vivo), para decidir quando recarregar.
        private readonly List<FrameworkElement?> _slots = new();
        private readonly List<string?> _slotUrls = new();

        // Marcações (caneta/seta/retângulo) por célula + o desenho em andamento.
        private readonly Dictionary<int, List<UIElement>> _annotations = new();
        private Polyline? _annoPen;
        private Polyline? _annoArrow;
        private Rectangle? _annoRect;
        private string _annoType = "pen";
        private Brush _annoBrush = Brushes.Red;
        private Point _annoStart;
        private int _annoCell = -1;

        /// <summary>
        /// Largura lógica (CSS) fixa em que toda página é diagramada. Como independe do
        /// tamanho físico da célula, redimensionar a célula NÃO reflui a página (preserva
        /// rolagem) e mantém o controlador e o terminal sempre alinhados. Para a página
        /// ocupar a célula, ajustamos só o zoom de exibição.
        /// </summary>
        private const double CanonicalWidth = 1920;

        public MainWindow()
        {
            InitializeComponent();
            FooterText.Text = $"Esc para sair · v{GitHubUpdater.CurrentVersion()}";
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            string machine = Environment.MachineName;
            string ip = NetworkUtil.GetLocalIPv4();

            ScreenName.Text = machine;
            ScreenAddr.Text = $"{ip} · porta {DiscoveryConstants.Port}";

            var info = new ViewerInfo
            {
                Id = machine,
                Name = machine,
                IpAddress = ip,
                ControlPort = ScreenCommand.DefaultPort,
            };
            _beacon = new UdpBeacon(info);
            _beacon.Start();

            _commandServer = new CommandServer(ScreenCommand.DefaultPort);
            _commandServer.CommandReceived += cmd => Dispatcher.BeginInvoke(() => ApplyCommand(cmd));
            try { _commandServer.Start(); } catch { /* porta ocupada */ }

            // Canal persistente para o "controle ao vivo" (mouse/rolagem/teclado).
            _liveInputServer = new LiveInputServer();
            _liveInputServer.InputReceived += ev => Dispatcher.BeginInvoke(() => InjectInput(ev));
            try { _liveInputServer.Start(); } catch { /* porta ocupada */ }

            // Serve a miniatura ao vivo (foto da própria tela) quando o controlador pede.
            _thumbnailServer = new ThumbnailServer(CaptureScreenJpeg);
            try { _thumbnailServer.Start(); } catch { /* porta ocupada */ }

            // Informa o estado atual de cada célula (página + rolagem) para o controle ao
            // vivo reabrir continuando de onde estava.
            _liveStateServer = new LiveStateServer(GetCellStateAsync);
            try { _liveStateServer.Start(); } catch { /* porta ocupada */ }

            // A atualização agora é pelo GitHub, verificada no pré-load (SplashWindow).
        }

        private void ApplyCommand(ScreenCommand command)
        {
            switch (command.Type)
            {
                case ScreenCommand.ShowBrowser:
                    if (!string.IsNullOrWhiteSpace(command.Url))
                    {
                        ApplyLayout(new[]
                        {
                            new ScreenSource
                            {
                                Kind = ScreenSource.Browser, Url = command.Url, Zoom = command.Zoom,
                                X = 0, Y = 0, Width = 1, Height = 1,
                            }
                        });
                    }
                    break;

                case ScreenCommand.ShowLayout:
                    if (command.Sources is { Count: > 0 })
                        ApplyLayout(command.Sources);
                    break;

                case ScreenCommand.Clear:
                    ClearSurface();
                    Surface.Visibility = Visibility.Collapsed;
                    IdlePanel.Visibility = Visibility.Visible;
                    break;
            }
        }

        // ----------------------------------------------------------------------------
        // Renderização incremental do layout
        // ----------------------------------------------------------------------------

        private void ApplyLayout(IReadOnlyList<ScreenSource> sources)
        {
            double w = Surface.ActualWidth > 0 ? Surface.ActualWidth : ActualWidth;
            double h = Surface.ActualHeight > 0 ? Surface.ActualHeight : ActualHeight;
            if (w <= 0) w = SystemParameters.PrimaryScreenWidth;
            if (h <= 0) h = SystemParameters.PrimaryScreenHeight;

            for (int i = 0; i < sources.Count; i++)
            {
                var src = sources[i];
                EnsureSlotCapacity(i);

                FrameworkElement element = ReconcileSlot(i, src);
                element.Width = Math.Max(1, src.Width * w);
                element.Height = Math.Max(1, src.Height * h);
                Canvas.SetLeft(element, src.X * w);
                Canvas.SetTop(element, src.Y * h);
                Panel.SetZIndex(element, src.ZIndex);
            }

            // Remove fontes que não existem mais no layout novo.
            for (int i = _slots.Count - 1; i >= sources.Count; i--)
                RemoveSlot(i);

            Surface.Visibility = Visibility.Visible;
            IdlePanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Garante que a vaga <paramref name="i"/> existe; cria/reaproveita o elemento
        /// adequado para a fonte e retorna-o. Um navegador só é recarregado quando a URL
        /// PROJETADA daquela vaga muda — navegação ao vivo e rolagem são preservadas.
        /// </summary>
        private FrameworkElement ReconcileSlot(int i, ScreenSource src)
        {
            var existing = _slots[i];

            if (src.Kind == ScreenSource.Browser && Uri.TryCreate(src.Url, UriKind.Absolute, out var uri))
            {
                if (existing is WebView2 web)
                {
                    if (!string.Equals(_slotUrls[i], src.Url, StringComparison.Ordinal))
                    {
                        _slotUrls[i] = src.Url;
                        try { web.Source = uri; } catch { }
                    }
                    web.Tag = src.Zoom; // zoom relativo desejado pelo usuário
                    ApplyCanonicalZoom(web);
                    return web;
                }

                var nw = new WebView2 { Tag = src.Zoom };
                // Reaplica o zoom canônico quando o navegador fica pronto e a cada
                // redimensionamento (mudança de layout) — mantendo a largura lógica fixa.
                nw.CoreWebView2InitializationCompleted += (_, _) => ApplyCanonicalZoom(nw);
                nw.SizeChanged += (_, _) => ApplyCanonicalZoom(nw);
                nw.Source = uri;
                SetSlot(i, nw);
                _slotUrls[i] = src.Url;
                return nw;
            }

            if (src.Kind == ScreenSource.Color)
            {
                if (existing is Border b)
                {
                    b.Background = ToBrush(src.ColorHex, Brushes.Black);
                    _slotUrls[i] = null;
                    return b;
                }
                var nb = new Border { Background = ToBrush(src.ColorHex, Brushes.Black) };
                SetSlot(i, nb);
                _slotUrls[i] = null;
                return nb;
            }

            if (src.Kind == ScreenSource.Text2)
            {
                var grid = new Grid
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = src.Text ?? string.Empty,
                            FontSize = Math.Max(1, src.FontSize),
                            Foreground = ToBrush(src.ForegroundHex, Brushes.White),
                            TextWrapping = TextWrapping.Wrap,
                            VerticalAlignment = VerticalAlignment.Center,
                        }
                    }
                };
                SetSlot(i, grid);
                _slotUrls[i] = null;
                return grid;
            }

            // Tipo não suportado na rede (não deve ocorrer): vaga vazia.
            var empty = new Border();
            SetSlot(i, empty);
            _slotUrls[i] = null;
            return empty;
        }

        private void EnsureSlotCapacity(int i)
        {
            while (_slots.Count <= i)
            {
                _slots.Add(null);
                _slotUrls.Add(null);
            }
        }

        /// <summary>Coloca um novo elemento na vaga, descartando o anterior.</summary>
        private void SetSlot(int i, FrameworkElement element)
        {
            var old = _slots[i];
            if (old != null)
            {
                if (old is WebView2 w) { try { w.Dispose(); } catch { } }
                Surface.Children.Remove(old);
                ClearAnnotations(i); // o conteúdo daquela célula mudou
            }
            _slots[i] = element;
            Surface.Children.Add(element);
        }

        private void RemoveSlot(int i)
        {
            var old = _slots[i];
            if (old != null)
            {
                if (old is WebView2 w) { try { w.Dispose(); } catch { } }
                Surface.Children.Remove(old);
            }
            _slots.RemoveAt(i);
            _slotUrls.RemoveAt(i);
            ClearAllAnnotations(); // índices mudaram: evita marcações desalinhadas
        }

        /// <summary>
        /// Ajusta o zoom de exibição do navegador para que ele seja diagramado na largura
        /// canônica (fixa) e ocupe a célula atual. Como a largura lógica não muda ao
        /// redimensionar, a página não reflui e a rolagem é preservada.
        /// </summary>
        private static void ApplyCanonicalZoom(WebView2 web)
        {
            try
            {
                if (web.CoreWebView2 == null)
                    return;

                double cw = web.ActualWidth;
                if (cw <= 0)
                    return;

                double userZoom = web.Tag is double z ? z : 1.0;
                web.ZoomFactor = Math.Clamp(cw / CanonicalWidth * userZoom, 0.25, 4.0);
            }
            catch { /* zoom indisponível: mantém o atual */ }
        }

        private static Brush ToBrush(string? hex, Brush fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex))
                    return (Brush)new BrushConverter().ConvertFromString(hex)!;
            }
            catch { }
            return fallback;
        }

        private void ClearSurface()
        {
            foreach (var web in Surface.Children.OfType<WebView2>().ToList())
            {
                try { web.Dispose(); } catch { }
            }
            Surface.Children.Clear();
            _slots.Clear();
            _slotUrls.Clear();
            ClearAllAnnotations();
        }

        // ----------------------------------------------------------------------------
        // Controle ao vivo: injeta a entrada no navegador-alvo (uma célula do layout)
        // ----------------------------------------------------------------------------

        private async void InjectInput(RemoteInputEvent ev)
        {
            if (ev.TargetIndex < 0 || ev.TargetIndex >= _slots.Count)
                return;

            var web = _slots[ev.TargetIndex] as WebView2;
            if (web == null)
                return;

            // Zoom e marcações não dependem do CDP (e não precisam do CoreWebView2 pronto).
            switch (ev.Kind)
            {
                case "zoom":
                    web.Tag = ev.Zoom;
                    ApplyCanonicalZoom(web);
                    return;
                case "annot-start":
                    StartAnnotation(ev);
                    return;
                case "annot-point":
                    UpdateAnnotation(ev);
                    return;
                case "annot-end":
                    EndAnnotation(ev);
                    return;
                case "annot-clear":
                    ClearAnnotations(ev.TargetIndex);
                    return;
            }

            var core = web.CoreWebView2;
            if (core == null)
                return;

            // As coordenadas do CDP são em pixels CSS (largura LÓGICA da página), não em
            // pixels físicos da célula. Como a célula está com zoom (canônico), a largura
            // CSS = largura física / zoom. Sem isso, cliques caem no lugar errado.
            double zoom = web.ZoomFactor;
            if (zoom <= 0) zoom = 1.0;
            double w = (web.ActualWidth > 0 ? web.ActualWidth : ActualWidth) / zoom;
            double h = (web.ActualHeight > 0 ? web.ActualHeight : ActualHeight) / zoom;
            double x = ev.X * w;
            double y = ev.Y * h;

            try
            {
                switch (ev.Kind)
                {
                    case "mousemove":
                        await DispatchMouse(core, "mouseMoved", x, y, ev);
                        break;
                    case "mousedown":
                        await DispatchMouse(core, "mousePressed", x, y, ev);
                        break;
                    case "mouseup":
                        await DispatchMouse(core, "mouseReleased", x, y, ev);
                        break;
                    case "wheel":
                        await DispatchWheel(core, x, y, ev);
                        break;
                    case "keydown":
                        await DispatchKey(core, "keyDown", ev);
                        break;
                    case "keyup":
                        await DispatchKey(core, "keyUp", ev);
                        break;
                    case "nav":
                        // Navega SOMENTE esta célula; não mexe na URL projetada, então
                        // reprojetar depois não desfaz a navegação ao vivo. Evita recarregar
                        // se a célula já está nessa página (ex.: o clique já navegou).
                        if (Uri.TryCreate(ev.Url, UriKind.Absolute, out var navUri) &&
                            !IsSameUrl(core.Source, navUri.ToString()))
                            core.Navigate(navUri.ToString());
                        break;
                }
            }
            catch { /* página trocou ou ainda não pronta: ignora o evento */ }
        }

        /// <summary>
        /// Devolve (como JSON de CellState) a página e a rolagem atuais da célula, para o
        /// controlador reabrir o controle ao vivo no mesmo ponto. Marshala para a thread de
        /// UI (acesso ao WebView2).
        /// </summary>
        private Task<string?> GetCellStateAsync(int index)
        {
            var tcs = new TaskCompletionSource<string?>();
            Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    if (index < 0 || index >= _slots.Count || _slots[index] is not WebView2 web ||
                        web.CoreWebView2 is not { } core)
                    {
                        tcs.SetResult(null);
                        return;
                    }

                    string url = core.Source ?? string.Empty;
                    double sx = 0, sy = 0;
                    try
                    {
                        var r = await core.ExecuteScriptAsync("[Math.round(window.scrollX),Math.round(window.scrollY)]");
                        var arr = JsonSerializer.Deserialize<double[]>(r);
                        if (arr is { Length: 2 }) { sx = arr[0]; sy = arr[1]; }
                    }
                    catch { /* sem rolagem acessível */ }

                    tcs.SetResult(JsonSerializer.Serialize(new CellState { Url = url, ScrollX = sx, ScrollY = sy }));
                }
                catch { tcs.SetResult(null); }
            });
            return tcs.Task;
        }

        private static Task DispatchMouse(CoreWebView2 core, string type, double x, double y, RemoteInputEvent ev)
        {
            var p = new Dictionary<string, object?>
            {
                ["type"] = type,
                ["x"] = x,
                ["y"] = y,
                ["button"] = ButtonName(ev.Button),
                ["buttons"] = ev.Buttons,
                ["clickCount"] = 1,
                ["modifiers"] = ev.Modifiers,
            };
            return core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(p));
        }

        private static Task DispatchWheel(CoreWebView2 core, double x, double y, RemoteInputEvent ev)
        {
            var p = new Dictionary<string, object?>
            {
                ["type"] = "mouseWheel",
                ["x"] = x,
                ["y"] = y,
                ["deltaX"] = ev.DeltaX,
                ["deltaY"] = ev.DeltaY,
                ["modifiers"] = ev.Modifiers,
            };
            return core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(p));
        }

        private static Task DispatchKey(CoreWebView2 core, string type, RemoteInputEvent ev)
        {
            var p = new Dictionary<string, object?>
            {
                ["type"] = type,
                ["windowsVirtualKeyCode"] = ev.KeyCode,
                ["key"] = ev.Key ?? string.Empty,
                ["code"] = ev.Code ?? string.Empty,
                ["modifiers"] = ev.Modifiers,
            };
            // Para teclas imprimíveis, envia também o texto (necessário para digitar).
            if (type == "keyDown" && !string.IsNullOrEmpty(ev.Key) && ev.Key!.Length == 1)
                p["text"] = ev.Key;

            return core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(p));
        }

        private static string ButtonName(int domButton) => domButton switch
        {
            0 => "left",
            1 => "middle",
            2 => "right",
            _ => "none",
        };

        /// <summary>Compara duas URLs ignorando barra final e maiúsculas/minúsculas.</summary>
        private static bool IsSameUrl(string? a, string? b)
        {
            static string N(string? u) => (u ?? string.Empty).TrimEnd('/');
            return string.Equals(N(a), N(b), StringComparison.OrdinalIgnoreCase);
        }

        // ----------------------------------------------------------------------------
        // Marcações desenhadas a partir do controlador (caneta/seta/retângulo)
        // ----------------------------------------------------------------------------

        private (double L, double T, double W, double H) CellRect(int index)
        {
            if (index >= 0 && index < _slots.Count && _slots[index] is FrameworkElement fe)
            {
                double l = Canvas.GetLeft(fe);
                double t = Canvas.GetTop(fe);
                if (double.IsNaN(l)) l = 0;
                if (double.IsNaN(t)) t = 0;
                double w = fe.Width > 0 ? fe.Width : fe.ActualWidth;
                double h = fe.Height > 0 ? fe.Height : fe.ActualHeight;
                if (w > 0 && h > 0)
                    return (l, t, w, h);
            }
            return (0, 0, ActualWidth, ActualHeight);
        }

        private Point ToScreen(int cell, double nx, double ny)
        {
            var r = CellRect(cell);
            return new Point(r.L + nx * r.W, r.T + ny * r.H);
        }

        private void StartAnnotation(RemoteInputEvent ev)
        {
            _annoCell = ev.TargetIndex;
            _annoType = ev.ShapeType ?? "pen";
            _annoBrush = ToBrush(ev.ColorHex, Brushes.Red);
            _annoStart = ToScreen(_annoCell, ev.X, ev.Y);

            if (!_annotations.TryGetValue(_annoCell, out var list))
                _annotations[_annoCell] = list = new List<UIElement>();

            switch (_annoType)
            {
                case "rect":
                    _annoRect = new Rectangle { Stroke = _annoBrush, StrokeThickness = 4 };
                    Canvas.SetLeft(_annoRect, _annoStart.X);
                    Canvas.SetTop(_annoRect, _annoStart.Y);
                    AnnotationLayer.Children.Add(_annoRect);
                    list.Add(_annoRect);
                    break;
                case "arrow":
                    _annoArrow = NewStroke();
                    AnnotationLayer.Children.Add(_annoArrow);
                    list.Add(_annoArrow);
                    break;
                default: // pen
                    _annoPen = NewStroke();
                    _annoPen.Points.Add(_annoStart);
                    AnnotationLayer.Children.Add(_annoPen);
                    list.Add(_annoPen);
                    break;
            }
            AnnotationLayer.Visibility = Visibility.Visible;
        }

        private Polyline NewStroke() => new()
        {
            Stroke = _annoBrush,
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };

        private void UpdateAnnotation(RemoteInputEvent ev)
        {
            var p = ToScreen(_annoCell, ev.X, ev.Y);
            switch (_annoType)
            {
                case "rect":
                    if (_annoRect != null)
                    {
                        Canvas.SetLeft(_annoRect, Math.Min(_annoStart.X, p.X));
                        Canvas.SetTop(_annoRect, Math.Min(_annoStart.Y, p.Y));
                        _annoRect.Width = Math.Abs(p.X - _annoStart.X);
                        _annoRect.Height = Math.Abs(p.Y - _annoStart.Y);
                    }
                    break;
                case "arrow":
                    if (_annoArrow != null)
                        _annoArrow.Points = BuildArrow(_annoStart, p);
                    break;
                default:
                    _annoPen?.Points.Add(p);
                    break;
            }
        }

        private void EndAnnotation(RemoteInputEvent ev)
        {
            UpdateAnnotation(ev);
            _annoPen = null;
            _annoArrow = null;
            _annoRect = null;
        }

        private void ClearAnnotations(int cell)
        {
            if (_annotations.TryGetValue(cell, out var list))
            {
                foreach (var u in list)
                    AnnotationLayer.Children.Remove(u);
                list.Clear();
            }
        }

        private void ClearAllAnnotations()
        {
            AnnotationLayer.Children.Clear();
            _annotations.Clear();
            _annoPen = null;
            _annoArrow = null;
            _annoRect = null;
        }

        private static PointCollection BuildArrow(Point a, Point b)
        {
            var pc = new PointCollection { a, b };
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len >= 1)
            {
                double ux = dx / len, uy = dy / len;
                const double head = 20, wide = 11;
                double bx = b.X - ux * head, by = b.Y - uy * head;
                double px = -uy, py = ux; // perpendicular
                pc.Add(new Point(bx + px * wide, by + py * wide));
                pc.Add(b);
                pc.Add(new Point(bx - px * wide, by - py * wide));
            }
            return pc;
        }

        // ----------------------------------------------------------------------------
        // Miniatura ao vivo: captura uma foto da própria tela (JPEG) sob demanda
        // ----------------------------------------------------------------------------

        /// <summary>
        /// Captura a tela atual e devolve um JPEG reduzido. Chamado em thread de fundo
        /// (servidor de miniatura); marshala para a thread de UI para usar o imaging do WPF.
        /// </summary>
        private byte[]? CaptureScreenJpeg()
        {
            try { return Dispatcher.Invoke(CaptureScreenJpegCore); }
            catch { return null; }
        }

        private static byte[]? CaptureScreenJpegCore()
        {
            int w = GetSystemMetrics(SM_CXSCREEN);
            int h = GetSystemMetrics(SM_CYSCREEN);
            if (w <= 0 || h <= 0)
                return null;

            IntPtr hScreen = GetDC(IntPtr.Zero);
            IntPtr hDc = CreateCompatibleDC(hScreen);
            IntPtr hBmp = CreateCompatibleBitmap(hScreen, w, h);
            IntPtr old = SelectObject(hDc, hBmp);
            try
            {
                BitBlt(hDc, 0, 0, w, h, hScreen, 0, 0, SRCCOPY | CAPTUREBLT);

                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                // Reduz para ~960px de largura: leve na rede e nítido o bastante para
                // recortar cada célula na pré-visualização do controlador.
                double scale = source.PixelWidth > 0 ? 960.0 / source.PixelWidth : 1.0;
                BitmapSource thumb = scale < 1.0
                    ? new TransformedBitmap(source, new ScaleTransform(scale, scale))
                    : source;
                thumb.Freeze();

                var encoder = new JpegBitmapEncoder { QualityLevel = 55 };
                encoder.Frames.Add(BitmapFrame.Create(thumb));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            }
            finally
            {
                SelectObject(hDc, old);
                DeleteObject(hBmp);
                DeleteDC(hDc);
                ReleaseDC(IntPtr.Zero, hScreen);
            }
        }

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SRCCOPY = 0x00CC0020;
        private const int CAPTUREBLT = 0x40000000;

        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(
            IntPtr hDc, int x, int y, int w, int h, IntPtr hSrcDc, int xSrc, int ySrc, int rop);

        private void OnClosed(object? sender, EventArgs e)
        {
            _commandServer?.Dispose();
            _liveInputServer?.Dispose();
            _thumbnailServer?.Dispose();
            _liveStateServer?.Dispose();
            _beacon?.Dispose();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
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
