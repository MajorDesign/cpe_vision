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
        private LiveViewServer? _liveViewServer;
        private LayoutQueryServer? _layoutQueryServer;
        private System.Windows.Threading.DispatcherTimer? _layoutSaveTimer;

        // Último layout aplicado (para o controlador reconstruir a parede ao reabrir).
        private IReadOnlyList<ScreenSource>? _currentSources;

        // Uma "vaga" por fonte do layout, na MESMA ordem em que o controlador as envia.
        // _slotUrls guarda a última URL PROJETADA de cada navegador (não a navegação ao
        // vivo), para decidir quando recarregar.
        private readonly List<FrameworkElement?> _slots = new();
        private readonly List<string?> _slotUrls = new();

        // Janelas sobrepostas (PiP, ex.: lives) por índice de fonte. Ficam sempre no topo,
        // fora do Surface, porque dois WebView2 na mesma janela não empilham (airspace).
        private readonly Dictionary<int, OverlayWindow> _overlays = new();
        private static readonly object OverlayPlaceholderTag = new();

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
            string version = "v" + GitHubUpdater.CurrentVersion();
            FooterVersionRun.Text = version;
            VersionLine.Text = "VERSÃO  " + version;
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

            // Transmite os frames de uma célula para o controle ao vivo (espelho exato da TV).
            _liveViewServer = new LiveViewServer(CaptureCellJpeg);
            try { _liveViewServer.Start(); } catch { /* porta ocupada */ }

            // Responde o layout atual (fontes + URLs ao vivo) para o controlador reconstruir
            // a parede ao reabrir — o terminal é a fonte da verdade.
            _layoutQueryServer = new LayoutQueryServer(GetCurrentLayoutAsync);
            try { _layoutQueryServer.Start(); } catch { /* porta ocupada */ }

            // RESTAURA o layout salvo (atualização/overlay/queda de energia): volta exibindo
            // o que estava, com as URLs ao vivo. A sessão/login está na pasta do WebView2,
            // então a página volta logada — sem reprojetar do controlador.
            var saved = TerminalLayoutStore.Load();
            if (saved is { Count: > 0 })
                Dispatcher.BeginInvoke(new Action(() => ApplyLayout(saved)),
                    System.Windows.Threading.DispatcherPriority.Loaded);

            // Atualiza o layout salvo periodicamente para capturar navegação/login ao vivo.
            _layoutSaveTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(15) };
            _layoutSaveTimer.Tick += (_, _) => SaveCurrentLayout();
            _layoutSaveTimer.Start();

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

                case ScreenCommand.Restart:
                    RestartSelf();
                    break;

                case ScreenCommand.ToggleOverlay:
                    // Inverte a preferência do overlay de vídeo e reinicia para aplicar.
                    TerminalSettings.SetHardwareVideoOverlay(!TerminalSettings.HardwareVideoOverlay);
                    RestartSelf();
                    break;
            }
        }

        /// <summary>Reabre o terminal: a nova instância passa pelo preload e busca a versão
        /// nova no GitHub (permite atualizar terminais 24/7 pelo controlador).</summary>
        private void RestartSelf()
        {
            SaveCurrentLayout(); // garante o estado mais recente (com login/URLs ao vivo)
            try
            {
                var exe = Environment.ProcessPath ??
                          System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                });
            }
            catch { /* não conseguiu relançar: ainda assim encerra */ }
            Application.Current.Shutdown();
        }

        // ----------------------------------------------------------------------------
        // Renderização incremental do layout
        // ----------------------------------------------------------------------------

        private void ApplyLayout(IReadOnlyList<ScreenSource> sources)
        {
            _currentSources = sources; // guarda para a consulta de layout (reabrir controlador)
            double w = Surface.ActualWidth > 0 ? Surface.ActualWidth : ActualWidth;
            double h = Surface.ActualHeight > 0 ? Surface.ActualHeight : ActualHeight;
            if (w <= 0) w = SystemParameters.PrimaryScreenWidth;
            if (h <= 0) h = SystemParameters.PrimaryScreenHeight;

            for (int i = 0; i < sources.Count; i++)
            {
                var src = sources[i];
                EnsureSlotCapacity(i);

                // Miniatura sobreposta (PiP): vive numa janela própria sempre-no-topo.
                // A vaga no Surface fica como um marcador transparente (só para manter os
                // índices alinhados com o controlador, usados pelo controle ao vivo).
                bool isOverlay = src.Kind == ScreenSource.Browser && src.Overlay;

                FrameworkElement element = isOverlay ? ReconcilePlaceholder(i) : ReconcileSlot(i, src);
                element.Width = Math.Max(1, src.Width * w);
                element.Height = Math.Max(1, src.Height * h);
                Canvas.SetLeft(element, src.X * w);
                Canvas.SetTop(element, src.Y * h);
                Panel.SetZIndex(element, src.ZIndex);

                if (isOverlay)
                    UpdateOverlay(i, src, w, h);
                else
                    CloseOverlay(i); // caso a vaga tenha deixado de ser overlay
            }

            // Remove fontes que não existem mais no layout novo.
            for (int i = _slots.Count - 1; i >= sources.Count; i--)
                RemoveSlot(i);

            Surface.Visibility = Visibility.Visible;
            IdlePanel.Visibility = Visibility.Collapsed;

            // Persiste para restaurar ao reabrir (atualização/overlay/queda de energia).
            SaveCurrentLayout();
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
                nw.CoreWebView2InitializationCompleted += (_, _) =>
                {
                    ApplyCanonicalZoom(nw);
                    // Injeta a biblioteca de marcações (camada SVG) em toda página.
                    try { _ = nw.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(AnnoLibScript); } catch { }
                    // Serve o player.html das lives do YouTube (host virtual) — sem isso,
                    // embutir a live direto dá Erro 153.
                    try
                    {
                        nw.CoreWebView2.SetVirtualHostNameToFolderMapping(
                            YouTubeLive.VirtualHost, YouTubeLive.EnsurePlayerFolder(),
                            CoreWebView2HostResourceAccessKind.Allow);
                    }
                    catch { }
                };
                nw.SizeChanged += (_, _) => ApplyCanonicalZoom(nw);
                nw.Source = uri;
                SetSlot(i, nw);
                _slotUrls[i] = src.Url;
                return nw;
            }

            if (src.Kind == ScreenSource.Color)
            {
                // Cor = moldura colorida (borda) para identificar a célula por cor.
                if (existing is Border b)
                {
                    b.BorderBrush = ToBrush(src.ColorHex, Brushes.Gold);
                    b.BorderThickness = new Thickness(12);
                    b.Background = Brushes.Transparent;
                    _slotUrls[i] = null;
                    return b;
                }
                var nb = new Border
                {
                    BorderBrush = ToBrush(src.ColorHex, Brushes.Gold),
                    BorderThickness = new Thickness(12),
                    Background = Brushes.Transparent,
                };
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
            CloseOverlay(i);
            _slots.RemoveAt(i);
            _slotUrls.RemoveAt(i);
        }

        /// <summary>
        /// Marcador transparente para uma vaga de fonte sobreposta (a live aparece numa
        /// janela própria, no topo). Mantém o índice da vaga alinhado com o controlador.
        /// </summary>
        private FrameworkElement ReconcilePlaceholder(int i)
        {
            if (_slots[i] is Border ph && ReferenceEquals(ph.Tag, OverlayPlaceholderTag))
                return ph;

            var nb = new Border { Background = null, IsHitTestVisible = false, Tag = OverlayPlaceholderTag };
            SetSlot(i, nb);
            _slotUrls[i] = null;
            return nb;
        }

        /// <summary>Cria/reposiciona a janela sobreposta da vaga <paramref name="i"/>.</summary>
        private void UpdateOverlay(int i, ScreenSource src, double w, double h)
        {
            if (!_overlays.TryGetValue(i, out var ov))
            {
                ov = new OverlayWindow(this);
                _overlays[i] = ov;
            }

            ov.SetUrl(src.Url ?? string.Empty);

            // Retângulo da célula convertido para pixels físicos da tela. O Surface fica
            // na origem (0,0) da janela, então convertemos pela própria janela (sempre
            // arranjada/visível, mesmo quando o Surface ainda está oculto).
            try
            {
                var p0 = PointToScreen(new Point(src.X * w, src.Y * h));
                var p1 = PointToScreen(new Point(src.X * w + src.Width * w, src.Y * h + src.Height * h));
                ov.PlaceOnScreen(
                    (int)Math.Round(p0.X), (int)Math.Round(p0.Y),
                    (int)Math.Round(p1.X - p0.X), (int)Math.Round(p1.Y - p0.Y));
            }
            catch { /* janela ainda não pronta: a próxima projeção reposiciona */ }
        }

        /// <summary>Fecha a janela sobreposta da vaga <paramref name="i"/>, se existir.</summary>
        private void CloseOverlay(int i)
        {
            if (_overlays.TryGetValue(i, out var ov))
            {
                ov.CloseOverlay();
                _overlays.Remove(i);
            }
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
            foreach (var ov in _overlays.Values.ToList())
                ov.CloseOverlay();
            _overlays.Clear();

            foreach (var web in Surface.Children.OfType<WebView2>().ToList())
            {
                try { web.Dispose(); } catch { }
            }
            Surface.Children.Clear();
            _slots.Clear();
            _slotUrls.Clear();
            _currentSources = null;
            TerminalLayoutStore.Save(null); // tela limpa continua limpa ao reabrir
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

        /// <summary>
        /// Devolve (JSON, lista de ScreenSource) o layout ATUAL da tela — com a URL AO VIVO
        /// de cada navegador — para o controlador reconstruir a parede ao reabrir.
        /// </summary>
        private Task<string?> GetCurrentLayoutAsync()
        {
            var tcs = new TaskCompletionSource<string?>();
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var list = BuildCurrentLayout();
                    tcs.SetResult(list != null ? JsonSerializer.Serialize(list) : null);
                }
                catch { tcs.SetResult(null); }
            });
            return tcs.Task;
        }

        /// <summary>
        /// Monta a lista de fontes ATUAL (posições/tipo + URL AO VIVO de cada navegador).
        /// SÓ pode ser chamado na thread de UI (acessa o WebView2). Usado tanto pela
        /// consulta do controlador quanto para salvar/restaurar o layout no terminal.
        /// </summary>
        private List<ScreenSource>? BuildCurrentLayout()
        {
            if (_currentSources == null || _currentSources.Count == 0)
                return null;

            var list = new List<ScreenSource>(_currentSources.Count);
            for (int i = 0; i < _currentSources.Count; i++)
            {
                var s = _currentSources[i];
                var copy = new ScreenSource
                {
                    Kind = s.Kind, X = s.X, Y = s.Y, Width = s.Width, Height = s.Height,
                    ZIndex = s.ZIndex, Url = s.Url, Zoom = s.Zoom, Overlay = s.Overlay,
                    ColorHex = s.ColorHex, Text = s.Text, FontSize = s.FontSize,
                    ForegroundHex = s.ForegroundHex,
                };

                // URL AO VIVO do navegador (após cliques/login/navegação).
                if (s.Kind == ScreenSource.Browser && i < _slots.Count &&
                    _slots[i] is WebView2 web && web.CoreWebView2 is { } core)
                {
                    var live = core.Source;
                    if (!string.IsNullOrWhiteSpace(live) &&
                        live.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        copy.Url = live;
                }

                list.Add(copy);
            }
            return list;
        }

        /// <summary>Salva em disco o layout atual (com URLs ao vivo) para restaurar ao reabrir.</summary>
        private void SaveCurrentLayout()
        {
            try { TerminalLayoutStore.Save(BuildCurrentLayout()); }
            catch { }
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
        // Marcações: injetadas DENTRO da página (camada SVG). Um Canvas WPF ficaria
        // escondido atrás do WebView2 (airspace). Coordenadas normalizadas 0..1.
        // ----------------------------------------------------------------------------

        /// <summary>Biblioteca injetada em cada página: cria uma camada SVG fixa por cima
        /// do conteúdo e desenha caneta, seta, retângulo e marca-texto.</summary>
        private const string AnnoLibScript = @"
(function(){
  if(window.__cpeAnnoInit) return; window.__cpeAnnoInit=true;
  var NS='http://www.w3.org/2000/svg';
  function ensure(){ var s=document.getElementById('__cpeAnnoSvg');
    if(!s){ s=document.createElementNS(NS,'svg'); s.id='__cpeAnnoSvg';
      s.style.cssText='position:fixed;left:0;top:0;width:100vw;height:100vh;pointer-events:none;z-index:2147483647';
      (document.body||document.documentElement).appendChild(s); } return s; }
  var cur=null,ty='pen',sx=0,sy=0;
  function arrow(ax,ay,bx,by){ var dx=bx-ax,dy=by-ay,L=Math.hypot(dx,dy),p=ax+','+ay+' '+bx+','+by;
    if(L>=1){ var ux=dx/L,uy=dy/L,h=18,w=10,kx=bx-ux*h,ky=by-uy*h,px=-uy,py=ux;
      p+=' '+(kx+px*w)+','+(ky+py*w)+' '+bx+','+by+' '+(kx-px*w)+','+(ky-py*w); } return p; }
  window.__cpeAnnoStart=function(t,color,nx,ny){ var s=ensure(); ty=t;
    var x=nx*window.innerWidth, y=ny*window.innerHeight; sx=x; sy=y;
    if(t==='rect'){ cur=document.createElementNS(NS,'rect'); cur.setAttribute('x',x); cur.setAttribute('y',y);
      cur.setAttribute('fill','none'); cur.setAttribute('stroke',color); cur.setAttribute('stroke-width',4); }
    else if(t==='arrow'){ cur=document.createElementNS(NS,'polyline'); cur.setAttribute('fill','none');
      cur.setAttribute('stroke',color); cur.setAttribute('stroke-width',4); cur.setAttribute('points',x+','+y); }
    else if(t==='marker'){ cur=document.createElementNS(NS,'polyline'); cur.setAttribute('fill','none');
      cur.setAttribute('stroke',color); cur.setAttribute('stroke-width',18); cur.setAttribute('stroke-opacity','0.4');
      cur.setAttribute('stroke-linecap','round'); cur.setAttribute('stroke-linejoin','round'); cur.setAttribute('points',x+','+y); }
    else { cur=document.createElementNS(NS,'polyline'); cur.setAttribute('fill','none'); cur.setAttribute('stroke',color);
      cur.setAttribute('stroke-width',4); cur.setAttribute('stroke-linecap','round'); cur.setAttribute('stroke-linejoin','round');
      cur.setAttribute('points',x+','+y); }
    s.appendChild(cur); };
  window.__cpeAnnoPoint=function(nx,ny){ if(!cur)return; var x=nx*window.innerWidth, y=ny*window.innerHeight;
    if(ty==='rect'){ var mx=Math.min(sx,x),my=Math.min(sy,y); cur.setAttribute('x',mx); cur.setAttribute('y',my);
      cur.setAttribute('width',Math.abs(x-sx)); cur.setAttribute('height',Math.abs(y-sy)); }
    else if(ty==='arrow'){ cur.setAttribute('points',arrow(sx,sy,x,y)); }
    else { cur.setAttribute('points',cur.getAttribute('points')+' '+x+','+y); } };
  window.__cpeAnnoEnd=function(nx,ny){ window.__cpeAnnoPoint(nx,ny); cur=null; };
  window.__cpeAnnoClear=function(){ var s=document.getElementById('__cpeAnnoSvg'); if(s)s.innerHTML=''; cur=null; };
})();";

        private void StartAnnotation(RemoteInputEvent ev) => RunAnno(ev.TargetIndex,
            $"window.__cpeAnnoStart&&window.__cpeAnnoStart({JsStr(ev.ShapeType ?? "pen")},{JsStr(SafeColor(ev.ColorHex))},{Inv(ev.X)},{Inv(ev.Y)})");

        private void UpdateAnnotation(RemoteInputEvent ev) => RunAnno(ev.TargetIndex,
            $"window.__cpeAnnoPoint&&window.__cpeAnnoPoint({Inv(ev.X)},{Inv(ev.Y)})");

        private void EndAnnotation(RemoteInputEvent ev) => RunAnno(ev.TargetIndex,
            $"window.__cpeAnnoEnd&&window.__cpeAnnoEnd({Inv(ev.X)},{Inv(ev.Y)})");

        private void ClearAnnotations(int cell) =>
            RunAnno(cell, "window.__cpeAnnoClear&&window.__cpeAnnoClear()");

        private async void RunAnno(int cell, string js)
        {
            var web = (cell >= 0 && cell < _slots.Count) ? _slots[cell] as WebView2 : null;
            if (web?.CoreWebView2 == null) return;
            try { await web.ExecuteScriptAsync(js); } catch { /* página trocando: ignora */ }
        }

        private static string Inv(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string JsStr(string s) => "'" + (s ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'") + "'";

        private static string SafeColor(string? hex)
        {
            var h = (hex ?? string.Empty).Trim();
            return System.Text.RegularExpressions.Regex.IsMatch(h, "^#[0-9a-fA-F]{6}$") ? h : "#EF4444";
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

        /// <summary>
        /// Captura SÓ a região de uma célula (na tela) e devolve um JPEG — usado pelo
        /// streaming do controle ao vivo, para o controlador exibir o espelho exato da TV.
        /// </summary>
        private byte[]? CaptureCellJpeg(int index)
        {
            try { return Dispatcher.Invoke(() => CaptureCellJpegCore(index)); }
            catch { return null; }
        }

        private byte[]? CaptureCellJpegCore(int index)
        {
            if (index < 0 || index >= _slots.Count || _slots[index] is not FrameworkElement el)
                return null;

            double cw = el.ActualWidth, ch = el.ActualHeight;
            if (cw <= 0 || ch <= 0)
                return null;

            // Retângulo da célula em pixels físicos da tela.
            Point tl, br;
            try { tl = el.PointToScreen(new Point(0, 0)); br = el.PointToScreen(new Point(cw, ch)); }
            catch { return null; }

            int x = (int)Math.Round(tl.X), y = (int)Math.Round(tl.Y);
            int w = (int)Math.Round(br.X - tl.X), h = (int)Math.Round(br.Y - tl.Y);
            if (w <= 0 || h <= 0)
                return null;

            IntPtr hScreen = GetDC(IntPtr.Zero);
            IntPtr hDc = CreateCompatibleDC(hScreen);
            IntPtr hBmp = CreateCompatibleBitmap(hScreen, w, h);
            IntPtr old = SelectObject(hDc, hBmp);
            try
            {
                BitBlt(hDc, 0, 0, w, h, hScreen, x, y, SRCCOPY | CAPTUREBLT);
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                // Para controle ao vivo: mais nítido que a miniatura (largura ~1280).
                double scale = source.PixelWidth > 1280 ? 1280.0 / source.PixelWidth : 1.0;
                BitmapSource frame = scale < 1.0
                    ? new TransformedBitmap(source, new ScaleTransform(scale, scale))
                    : source;
                frame.Freeze();

                var encoder = new JpegBitmapEncoder { QualityLevel = 62 };
                encoder.Frames.Add(BitmapFrame.Create(frame));
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
            _liveViewServer?.Dispose();
            _layoutQueryServer?.Dispose();
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
