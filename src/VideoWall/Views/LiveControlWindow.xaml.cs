using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Core;
using VideoWall.Network;

namespace VideoWall.Views
{
    /// <summary>
    /// Janela de "controle ao vivo": espelha uma página web e encaminha, em tempo real,
    /// toda a entrada do usuário (rolagem, cliques, arraste, teclado e navegação) para o
    /// terminal selecionado, que reproduz os eventos na própria página.
    /// </summary>
    public partial class LiveControlWindow : Window
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        // Script injetado em cada página para capturar a entrada e enviá-la ao host.
        private const string CaptureScript = @"
(function(){
  if (window.__cpeHooked) return; window.__cpeHooked = true;
  function W(){ return window.innerWidth || 1; }
  function H(){ return window.innerHeight || 1; }
  function m(e){ return (e.altKey?1:0)|(e.ctrlKey?2:0)|(e.metaKey?4:0)|(e.shiftKey?8:0); }
  function s(o){ try{ window.chrome.webview.postMessage(JSON.stringify(o)); }catch(_){} }
  var lm=0;
  window.addEventListener('mousemove', function(e){ var t=Date.now(); if(t-lm<30)return; lm=t;
     s({Kind:'mousemove',X:e.clientX/W(),Y:e.clientY/H(),Button:e.button,Buttons:e.buttons,Modifiers:m(e)}); }, true);
  window.addEventListener('mousedown', function(e){ s({Kind:'mousedown',X:e.clientX/W(),Y:e.clientY/H(),Button:e.button,Buttons:e.buttons,Modifiers:m(e)}); }, true);
  window.addEventListener('mouseup', function(e){ s({Kind:'mouseup',X:e.clientX/W(),Y:e.clientY/H(),Button:e.button,Buttons:e.buttons,Modifiers:m(e)}); }, true);
  window.addEventListener('wheel', function(e){ s({Kind:'wheel',X:e.clientX/W(),Y:e.clientY/H(),DeltaX:e.deltaX,DeltaY:e.deltaY,Modifiers:m(e)}); }, true);
  window.addEventListener('keydown', function(e){ s({Kind:'keydown',Key:e.key,Code:e.code,KeyCode:e.keyCode,Modifiers:m(e)}); }, true);
  window.addEventListener('keyup', function(e){ s({Kind:'keyup',Key:e.key,Code:e.code,KeyCode:e.keyCode,Modifiers:m(e)}); }, true);
})();";

        private readonly string _ip;
        private readonly string _screenName;
        private readonly int _targetIndex;
        private readonly double _baseZoom;   // zoom projetado da célula (geralmente 1)
        private readonly double _aspect;     // proporção largura/altura da célula
        private double _userZoom = 1.0;      // zoom relativo do apresentador
        private readonly LiveInputSender _sender = new();
        private bool _closing;

        /// <summary>Largura lógica (CSS) em que a página é diagramada, igual à do terminal.</summary>
        private double Canonical => 1920.0 / (_baseZoom * _userZoom);

        /// <param name="targetIndex">Índice do navegador-alvo no layout (a célula a controlar).</param>
        /// <param name="baseZoom">Zoom projetado da célula (para alinhar a diagramação).</param>
        /// <param name="aspect">Proporção largura/altura da célula (para o espelho ter o mesmo formato).</param>
        public LiveControlWindow(string ip, string screenName, string initialUrl, int targetIndex,
            double baseZoom, double aspect)
        {
            InitializeComponent();
            _ip = ip;
            _screenName = screenName;
            _targetIndex = targetIndex;
            _baseZoom = baseZoom > 0 ? baseZoom : 1.0;
            _aspect = aspect > 0 ? aspect : 16.0 / 9.0;
            UrlBox.Text = string.IsNullOrWhiteSpace(initialUrl) ? "https://" : initialUrl;
            TargetLabel.Text = $"→ {screenName} · navegador {targetIndex + 1}";
            Loaded += OnLoaded;
            Closed += (_, _) => Cleanup();
            HighlightTool();
            HighlightColor();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, UserDataFolder(), null);
                await Web.EnsureCoreWebView2Async(env);

                await Web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(CaptureScript);
                Web.CoreWebView2.WebMessageReceived += OnWebMessage;
                Web.CoreWebView2.SourceChanged += (_, _) =>
                {
                    var src = Web.CoreWebView2.Source;
                    if (!UrlBox.IsKeyboardFocused)
                        UrlBox.Text = src;
                    // Navegação AUTORITATIVA: qualquer troca de página (clique, redirecionamento,
                    // barra de endereço) é refletida exatamente no terminal.
                    ForwardNavigation(src);
                };

                // Diagrama a página na MESMA largura da TV para que a rolagem/altura
                // batam com o terminal (reaplicado ao redimensionar e ao navegar).
                Web.CoreWebView2.NavigationCompleted += (_, _) => OnNavigationCompleted();
                Web.SizeChanged += (_, _) => ApplyFitZoom();
                ApplyFitZoom();
            }
            catch
            {
                Hint.Text = "Não foi possível iniciar o navegador de controle.";
                return;
            }

            // Continua de onde a célula está: pergunta ao terminal a página e a rolagem
            // atuais e começa o espelho exatamente nesse ponto (sem resetar o terminal).
            string startUrl = NormalizeUrl(UrlBox.Text);
            var state = await LiveStateClient.RequestAsync(_ip, _targetIndex);
            if (state != null && !string.IsNullOrWhiteSpace(state.Url) &&
                state.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                startUrl = state.Url;
                UrlBox.Text = state.Url;
                _pendingScrollX = state.ScrollX;
                _pendingScrollY = state.ScrollY;
                _lastNavSent = state.Url; // já é a página do terminal; não reenviar
            }

            try
            {
                await _sender.ConnectAsync(_ip);
                SetLive(true);
            }
            catch (Exception ex)
            {
                SetLive(false);
                Hint.Text = $"Falha ao conectar ao terminal: {ex.Message}";
            }

            if (!string.IsNullOrEmpty(startUrl))
                Web.CoreWebView2.Navigate(startUrl);
        }

        private double _pendingScrollX;
        private double _pendingScrollY;
        private bool _scrollRestored;

        /// <summary>Ao terminar de carregar: ajusta o zoom e, na 1ª vez, restaura a rolagem
        /// para o mesmo ponto em que o terminal estava.</summary>
        private async void OnNavigationCompleted()
        {
            ApplyFitZoom();

            if (_scrollRestored || (_pendingScrollX == 0 && _pendingScrollY == 0))
                return;
            _scrollRestored = true;

            try
            {
                var x = _pendingScrollX.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var y = _pendingScrollY.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await Web.CoreWebView2.ExecuteScriptAsync($"window.scrollTo({x},{y})");
            }
            catch { /* rolagem indisponível: ignora */ }
        }

        /// <summary>
        /// Ajusta o zoom da janela de controle para que a página seja diagramada na MESMA
        /// largura canônica usada pelo terminal. Assim a página reflui igual nos dois e a
        /// rolagem fica alinhada. Não afeta o encaminhamento de entrada (coordenadas
        /// continuam normalizadas pela largura lógica da página).
        /// </summary>
        private void ApplyFitZoom()
        {
            try
            {
                if (Web.CoreWebView2 == null)
                    return;

                double w = Web.ActualWidth;
                if (w <= 0)
                    return;

                Web.ZoomFactor = Math.Clamp(w / Canonical, 0.25, 4.0);
            }
            catch { /* zoom indisponível: mantém o atual */ }
        }

        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                var ev = JsonSerializer.Deserialize<RemoteInputEvent>(args.TryGetWebMessageAsString(), JsonOpts);
                if (ev != null)
                {
                    ev.TargetIndex = _targetIndex; // direciona para a célula controlada
                    _sender.Send(ev);
                }
            }
            catch { /* mensagem inesperada: ignora */ }
        }

        private void OnUrlKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Go();
                e.Handled = true;
            }
        }

        private void OnGo(object sender, RoutedEventArgs e) => Go();

        private void Go()
        {
            var url = NormalizeUrl(UrlBox.Text);
            if (string.IsNullOrEmpty(url))
            {
                Hint.Text = "Digite um endereço válido (ex.: www.youtube.com).";
                return;
            }

            UrlBox.Text = url;
            // Só navega o espelho; o terminal é sincronizado pelo SourceChanged
            // (ForwardNavigation), cobrindo também cliques e redirecionamentos.
            Web.CoreWebView2?.Navigate(url);
        }

        private string? _lastNavSent;

        /// <summary>Envia ao terminal a URL atual da página controlada (sem repetir).</summary>
        private void ForwardNavigation(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) ||
                !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return;

            if (string.Equals(url, _lastNavSent, StringComparison.OrdinalIgnoreCase))
                return;

            _lastNavSent = url;
            _sender.Send(new RemoteInputEvent { Kind = "nav", Url = url, TargetIndex = _targetIndex });
        }

        private void SetLive(bool live)
        {
            LiveDot.Opacity = live ? 1 : 0.3;
            LiveLabel.Text = live ? "AO VIVO" : "DESCONECTADO";
            LiveLabel.Foreground = LiveDot.Fill;
            LiveLabel.Opacity = live ? 1 : 0.5;
        }

        // ----------------------------------------------------------------------------
        // Barra de apresentação: proporção do espelho, ferramentas, cores, zoom, marcação
        // ----------------------------------------------------------------------------

        private string _tool = "cursor";
        private string _colorHex = "#EF4444";

        /// <summary>Mantém o espelho com a MESMA proporção da célula (letterbox), para que
        /// as coordenadas (cliques e marcações) mapeiem exatamente no terminal.</summary>
        private void OnMirrorHostSizeChanged(object sender, SizeChangedEventArgs e)
        {
            double availW = MirrorHost.ActualWidth, availH = MirrorHost.ActualHeight;
            if (availW <= 0 || availH <= 0)
                return;

            double w = availW, h = availW / _aspect;
            if (h > availH) { h = availH; w = availH * _aspect; }
            AspectBox.Width = w;
            AspectBox.Height = h;
            ApplyFitZoom();
        }

        private void OnToolClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tool)
                SetTool(tool);
        }

        private async void SetTool(string tool)
        {
            _tool = tool;
            HighlightTool();

            if (tool == "cursor")
            {
                DrawOverlay.Visibility = Visibility.Collapsed;
                Snapshot.Visibility = Visibility.Collapsed;
                Web.Visibility = Visibility.Visible;
                return;
            }

            // Congela a vista atual (snapshot) para desenhar por cima — o WebView2, por ser
            // uma "janela" do Windows, esconderia qualquer desenho colocado sobre ele.
            try
            {
                if (Web.CoreWebView2 != null)
                {
                    using var ms = new MemoryStream();
                    await Web.CoreWebView2.CapturePreviewAsync(
                        CoreWebView2CapturePreviewImageFormat.Png, ms);
                    ms.Position = 0;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    Snapshot.Source = bmp;
                }
            }
            catch { /* sem snapshot: desenha sobre fundo escuro mesmo assim */ }

            Web.Visibility = Visibility.Collapsed;
            Snapshot.Visibility = Visibility.Visible;
            DrawOverlay.Visibility = Visibility.Visible;
            DrawOverlay.Cursor = Cursors.Cross;
        }

        private void HighlightTool()
        {
            var active = (Brush)new BrushConverter().ConvertFromString("#3D3416")!;
            var idle = (Brush)new BrushConverter().ConvertFromString("#1B1D22")!;
            ToolCursor.Background = _tool == "cursor" ? active : idle;
            ToolPen.Background = _tool == "pen" ? active : idle;
            ToolArrow.Background = _tool == "arrow" ? active : idle;
            ToolRect.Background = _tool == "rect" ? active : idle;
            ToolMarker.Background = _tool == "marker" ? active : idle;
        }

        private void OnColorClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string hex)
            {
                _colorHex = hex;
                HighlightColor();
            }
        }

        private void HighlightColor()
        {
            ColorRed.BorderThickness = new Thickness(_colorHex == "#EF4444" ? 2 : 0);
            ColorYellow.BorderThickness = new Thickness(_colorHex == "#F2B705" ? 2 : 0);
            ColorGreen.BorderThickness = new Thickness(_colorHex == "#22C55E" ? 2 : 0);
            ColorWhite.BorderThickness = new Thickness(_colorHex == "#FFFFFF" ? 2 : 0);
        }

        private void OnZoomIn(object sender, RoutedEventArgs e) => SetZoom(_userZoom + 0.25);
        private void OnZoomOut(object sender, RoutedEventArgs e) => SetZoom(_userZoom - 0.25);
        private void OnZoomReset(object sender, RoutedEventArgs e) => SetZoom(1.0);

        private void SetZoom(double zoom)
        {
            _userZoom = Math.Clamp(zoom, 0.5, 3.0);
            ZoomLabel.Text = $"{Math.Round(_userZoom * 100)}%";
            ApplyFitZoom();
            _sender.Send(new RemoteInputEvent
            {
                Kind = "zoom",
                Zoom = _baseZoom * _userZoom,
                TargetIndex = _targetIndex,
            });
        }

        private void OnClearAnnotations(object sender, RoutedEventArgs e)
        {
            DrawOverlay.Children.Clear();
            _sender.Send(new RemoteInputEvent { Kind = "annot-clear", TargetIndex = _targetIndex });
        }

        // ---- Desenho local (espelhado no terminal) ----

        private bool _drawing;
        private Polyline? _localPen;
        private Polyline? _localArrow;
        private Rectangle? _localRect;
        private Point _localStart;

        private void OnDrawDown(object sender, MouseButtonEventArgs e)
        {
            if (_tool == "cursor")
                return;
            _drawing = true;
            DrawOverlay.CaptureMouse();
            _localStart = e.GetPosition(DrawOverlay);
            CreateLocalShape(_localStart);
            SendAnnot("annot-start", _localStart);
            e.Handled = true;
        }

        private void OnDrawMove(object sender, MouseEventArgs e)
        {
            if (!_drawing)
                return;
            var p = e.GetPosition(DrawOverlay);
            UpdateLocalShape(p);
            SendAnnot("annot-point", p);
        }

        private void OnDrawUp(object sender, MouseButtonEventArgs e)
        {
            if (!_drawing)
                return;
            _drawing = false;
            DrawOverlay.ReleaseMouseCapture();
            var p = e.GetPosition(DrawOverlay);
            UpdateLocalShape(p);
            SendAnnot("annot-end", p);
            _localPen = _localArrow = null;
            _localRect = null;
        }

        private void SendAnnot(string kind, Point p)
        {
            double w = DrawOverlay.ActualWidth, h = DrawOverlay.ActualHeight;
            _sender.Send(new RemoteInputEvent
            {
                Kind = kind,
                X = w > 0 ? p.X / w : 0,
                Y = h > 0 ? p.Y / h : 0,
                TargetIndex = _targetIndex,
                ShapeType = _tool,
                ColorHex = _colorHex,
            });
        }

        private void CreateLocalShape(Point p)
        {
            var brush = ColorBrush(_colorHex);
            if (_tool == "rect")
            {
                _localRect = new Rectangle { Stroke = brush, StrokeThickness = 3 };
                Canvas.SetLeft(_localRect, p.X);
                Canvas.SetTop(_localRect, p.Y);
                DrawOverlay.Children.Add(_localRect);
            }
            else if (_tool == "arrow")
            {
                _localArrow = NewLocalStroke(brush);
                DrawOverlay.Children.Add(_localArrow);
            }
            else if (_tool == "marker")
            {
                _localPen = NewLocalStroke(brush);
                _localPen.StrokeThickness = 18;
                _localPen.Opacity = 0.4;
                _localPen.Points.Add(p);
                DrawOverlay.Children.Add(_localPen);
            }
            else
            {
                _localPen = NewLocalStroke(brush);
                _localPen.Points.Add(p);
                DrawOverlay.Children.Add(_localPen);
            }
        }

        private void UpdateLocalShape(Point p)
        {
            if (_tool == "rect" && _localRect != null)
            {
                Canvas.SetLeft(_localRect, Math.Min(_localStart.X, p.X));
                Canvas.SetTop(_localRect, Math.Min(_localStart.Y, p.Y));
                _localRect.Width = Math.Abs(p.X - _localStart.X);
                _localRect.Height = Math.Abs(p.Y - _localStart.Y);
            }
            else if (_tool == "arrow" && _localArrow != null)
            {
                _localArrow.Points = BuildArrow(_localStart, p);
            }
            else
            {
                _localPen?.Points.Add(p);
            }
        }

        private static Polyline NewLocalStroke(Brush brush) => new()
        {
            Stroke = brush,
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };

        private static PointCollection BuildArrow(Point a, Point b)
        {
            var pc = new PointCollection { a, b };
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len >= 1)
            {
                double ux = dx / len, uy = dy / len;
                const double head = 16, wide = 9;
                double bx = b.X - ux * head, by = b.Y - uy * head;
                double px = -uy, py = ux;
                pc.Add(new Point(bx + px * wide, by + py * wide));
                pc.Add(b);
                pc.Add(new Point(bx - px * wide, by - py * wide));
            }
            return pc;
        }

        private static Brush ColorBrush(string hex)
        {
            try { return (Brush)new BrushConverter().ConvertFromString(hex)!; }
            catch { return Brushes.Red; }
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void Cleanup()
        {
            if (_closing) return;
            _closing = true;

            // Fechar encerra SÓ a sessão de controle. NÃO envia nada ao terminal: a tela
            // continua exibindo o layout (só "Parar tela" interrompe a transmissão).
            try { _sender.Dispose(); } catch { }
            try { Web.Dispose(); } catch { }
        }

        private static string UserDataFolder()
        {
            var folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CPE Tecnologia", "VideoWall", "WebView2");
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string NormalizeUrl(string text)
        {
            var url = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(url) || url.Equals("https://", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;

            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.ToString()
                : string.Empty;
        }
    }
}
