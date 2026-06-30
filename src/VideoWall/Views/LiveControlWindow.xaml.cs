using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VideoWall.Network;

namespace VideoWall.Views
{
    /// <summary>
    /// Janela de "controle ao vivo": mostra o ESPELHO EXATO de uma célula do terminal
    /// (fluxo de imagens vindo da TV) e encaminha, em tempo real, toda a entrada do
    /// usuário (mouse, rolagem, teclado, navegação) para o terminal — que é o único
    /// navegador de verdade (mantém sessão/login). Assim controlador e terminal nunca
    /// divergem: o que você vê é o que está na TV.
    /// </summary>
    public partial class LiveControlWindow : Window
    {
        private readonly string _ip;
        private readonly int _targetIndex;
        private readonly double _baseZoom;   // zoom projetado da célula (geralmente 1)
        private readonly double _aspect;     // proporção largura/altura da célula
        private double _userZoom = 1.0;      // zoom relativo do apresentador

        private readonly LiveInputSender _sender = new();
        private readonly LiveViewClient _view = new();
        private bool _closing;

        private int _buttons;                // máscara de botões pressionados (DOM)
        private long _lastMoveTicks;

        /// <param name="targetIndex">Índice do navegador-alvo no layout (a célula a controlar).</param>
        /// <param name="baseZoom">Zoom projetado da célula (enviado ao terminal ao ajustar o zoom).</param>
        /// <param name="aspect">Proporção largura/altura da célula (para o espelho ter o mesmo formato).</param>
        public LiveControlWindow(string ip, string screenName, string initialUrl, int targetIndex,
            double baseZoom, double aspect)
        {
            InitializeComponent();
            _ip = ip;
            _targetIndex = targetIndex;
            _baseZoom = baseZoom > 0 ? baseZoom : 1.0;
            _aspect = aspect > 0 ? aspect : 16.0 / 9.0;
            UrlBox.Text = string.IsNullOrWhiteSpace(initialUrl) ? "https://" : initialUrl;
            TargetLabel.Text = $"→ {screenName} · navegador {targetIndex + 1}";

            // Teclado da janela inteira -> terminal (exceto quando digitando na barra de URL).
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewKeyUp += OnPreviewKeyUp;
            PreviewTextInput += OnPreviewTextInput;

            Loaded += OnLoaded;
            Closed += (_, _) => Cleanup();
            HighlightTool();
            HighlightColor();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
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

            // Recebe os frames ao vivo da célula e exibe (espelho exato da TV).
            _view.FrameReceived += OnFrame;
            _view.Start(_ip, _targetIndex);

            DrawOverlay.Focus();
        }

        /// <summary>Decodifica o frame em thread de fundo e atribui a imagem na UI.</summary>
        private void OnFrame(byte[] jpeg)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(jpeg);
                bmp.EndInit();
                bmp.Freeze();
                Dispatcher.BeginInvoke(() => { if (!_closing) Mirror.Source = bmp; });
            }
            catch { /* frame inválido: ignora */ }
        }

        // ------------------------------------------------------------------ Mouse -> terminal

        private void OnSurfaceMouseMove(object sender, MouseEventArgs e)
        {
            if (_tool != "cursor") { if (_drawing) SendAnnot("annot-point", e.GetPosition(DrawOverlay)); return; }

            long t = Environment.TickCount64;
            if (t - _lastMoveTicks < 30) return; // ~33/s
            _lastMoveTicks = t;
            SendMouse("mousemove", e.GetPosition(DrawOverlay), 0);
        }

        private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e)
        {
            DrawOverlay.Focus();
            var p = e.GetPosition(DrawOverlay);

            if (_tool != "cursor")
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    _drawing = true;
                    DrawOverlay.CaptureMouse();
                    SendAnnot("annot-start", p);
                    e.Handled = true;
                }
                return;
            }

            int dom = DomButton(e.ChangedButton);
            _buttons |= DomMask(e.ChangedButton);
            DrawOverlay.CaptureMouse();
            SendMouse("mousedown", p, dom);
            e.Handled = true;
        }

        private void OnSurfaceMouseUp(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(DrawOverlay);

            if (_tool != "cursor")
            {
                if (_drawing && e.ChangedButton == MouseButton.Left)
                {
                    _drawing = false;
                    DrawOverlay.ReleaseMouseCapture();
                    SendAnnot("annot-end", p);
                    e.Handled = true;
                }
                return;
            }

            int dom = DomButton(e.ChangedButton);
            _buttons &= ~DomMask(e.ChangedButton);
            SendMouse("mouseup", p, dom);
            DrawOverlay.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void OnSurfaceWheel(object sender, MouseWheelEventArgs e)
        {
            if (_tool != "cursor") return;
            var n = Norm(e.GetPosition(DrawOverlay));
            _sender.Send(new RemoteInputEvent
            {
                Kind = "wheel",
                X = n.X,
                Y = n.Y,
                DeltaY = -e.Delta,   // roda para cima (Delta>0) = rolar para cima (deltaY<0)
                Modifiers = Mods(),
                TargetIndex = _targetIndex,
            });
            e.Handled = true;
        }

        private void SendMouse(string kind, Point p, int button)
        {
            var n = Norm(p);
            _sender.Send(new RemoteInputEvent
            {
                Kind = kind,
                X = n.X,
                Y = n.Y,
                Button = button,
                Buttons = _buttons,
                Modifiers = Mods(),
                TargetIndex = _targetIndex,
            });
        }

        private Point Norm(Point p)
        {
            double w = DrawOverlay.ActualWidth, h = DrawOverlay.ActualHeight;
            return new Point(
                w > 0 ? Math.Clamp(p.X / w, 0, 1) : 0,
                h > 0 ? Math.Clamp(p.Y / h, 0, 1) : 0);
        }

        private static int DomButton(MouseButton b) => b switch
        {
            MouseButton.Left => 0,
            MouseButton.Middle => 1,
            MouseButton.Right => 2,
            _ => 0,
        };

        private static int DomMask(MouseButton b) => b switch
        {
            MouseButton.Left => 1,
            MouseButton.Right => 2,
            MouseButton.Middle => 4,
            _ => 0,
        };

        // --------------------------------------------------------------- Teclado -> terminal

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (UrlBox.IsKeyboardFocused || string.IsNullOrEmpty(e.Text))
                return;

            // Caracteres digitados (login, formulários): enviados como tecla com texto.
            foreach (char c in e.Text)
            {
                int vk = c >= 'a' && c <= 'z' ? c - 32 : c;
                _sender.Send(new RemoteInputEvent { Kind = "keydown", Key = c.ToString(), KeyCode = vk, Modifiers = Mods(), TargetIndex = _targetIndex });
                _sender.Send(new RemoteInputEvent { Kind = "keyup", Key = c.ToString(), KeyCode = vk, Modifiers = Mods(), TargetIndex = _targetIndex });
            }
            e.Handled = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (UrlBox.IsKeyboardFocused) return;
            if (!TryMapSpecial(e.Key, out var key, out var code, out var vk)) return;
            _sender.Send(new RemoteInputEvent { Kind = "keydown", Key = key, Code = code, KeyCode = vk, Modifiers = Mods(), TargetIndex = _targetIndex });
            e.Handled = true;
        }

        private void OnPreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (UrlBox.IsKeyboardFocused) return;
            if (!TryMapSpecial(e.Key, out var key, out var code, out var vk)) return;
            _sender.Send(new RemoteInputEvent { Kind = "keyup", Key = key, Code = code, KeyCode = vk, Modifiers = Mods(), TargetIndex = _targetIndex });
            e.Handled = true;
        }

        /// <summary>Mapeia teclas NÃO imprimíveis (as imprimíveis vão por OnPreviewTextInput).</summary>
        private static bool TryMapSpecial(Key k, out string key, out string code, out int vk)
        {
            (key, code, vk) = k switch
            {
                Key.Enter => ("Enter", "Enter", 13),
                Key.Back => ("Backspace", "Backspace", 8),
                Key.Tab => ("Tab", "Tab", 9),
                Key.Delete => ("Delete", "Delete", 46),
                Key.Escape => ("Escape", "Escape", 27),
                Key.Left => ("ArrowLeft", "ArrowLeft", 37),
                Key.Up => ("ArrowUp", "ArrowUp", 38),
                Key.Right => ("ArrowRight", "ArrowRight", 39),
                Key.Down => ("ArrowDown", "ArrowDown", 40),
                Key.Home => ("Home", "Home", 36),
                Key.End => ("End", "End", 35),
                Key.PageUp => ("PageUp", "PageUp", 33),
                Key.Next => ("PageDown", "PageDown", 34),
                _ => (string.Empty, string.Empty, 0),
            };
            return vk != 0;
        }

        private static int Mods()
        {
            var m = Keyboard.Modifiers;
            int r = 0;
            if (m.HasFlag(ModifierKeys.Alt)) r |= 1;
            if (m.HasFlag(ModifierKeys.Control)) r |= 2;
            if (m.HasFlag(ModifierKeys.Windows)) r |= 4;
            if (m.HasFlag(ModifierKeys.Shift)) r |= 8;
            return r;
        }

        // --------------------------------------------------------------- Navegação (URL)

        private void OnUrlKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Go(); e.Handled = true; }
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
            // Navega a célula NO TERMINAL; o espelho mostra o resultado.
            _sender.Send(new RemoteInputEvent { Kind = "nav", Url = url, TargetIndex = _targetIndex });
            DrawOverlay.Focus();
        }

        private void SetLive(bool live)
        {
            LiveDot.Opacity = live ? 1 : 0.3;
            LiveLabel.Text = live ? "AO VIVO" : "DESCONECTADO";
            LiveLabel.Foreground = LiveDot.Fill;
            LiveLabel.Opacity = live ? 1 : 0.5;
        }

        // --------------------------------------------- Barra: proporção, ferramentas, zoom

        private string _tool = "cursor";
        private string _colorHex = "#EF4444";
        private bool _drawing;

        /// <summary>Mantém o espelho na MESMA proporção da célula (letterbox), para as
        /// coordenadas (cliques e marcações) mapearem exatamente no terminal.</summary>
        private void OnMirrorHostSizeChanged(object sender, SizeChangedEventArgs e)
        {
            double availW = MirrorHost.ActualWidth, availH = MirrorHost.ActualHeight;
            if (availW <= 0 || availH <= 0)
                return;

            double w = availW, h = availW / _aspect;
            if (h > availH) { h = availH; w = availH * _aspect; }
            AspectBox.Width = w;
            AspectBox.Height = h;
        }

        private void OnToolClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tool)
                SetTool(tool);
        }

        private void SetTool(string tool)
        {
            _tool = tool;
            HighlightTool();
            DrawOverlay.Cursor = tool == "cursor" ? Cursors.Arrow : Cursors.Cross;
        }

        private void HighlightTool()
        {
            var active = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#3D3416")!;
            var idle = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#1B1D22")!;
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
            // Zoom é aplicado NO TERMINAL; o espelho reflete a mudança no próximo frame.
            _sender.Send(new RemoteInputEvent { Kind = "zoom", Zoom = _baseZoom * _userZoom, TargetIndex = _targetIndex });
        }

        private void OnClearAnnotations(object sender, RoutedEventArgs e)
        {
            _sender.Send(new RemoteInputEvent { Kind = "annot-clear", TargetIndex = _targetIndex });
        }

        /// <summary>Envia a marcação ao terminal (que desenha via SVG); o espelho a mostra.</summary>
        private void SendAnnot(string kind, Point p)
        {
            var n = Norm(p);
            _sender.Send(new RemoteInputEvent
            {
                Kind = kind,
                X = n.X,
                Y = n.Y,
                TargetIndex = _targetIndex,
                ShapeType = _tool,
                ColorHex = _colorHex,
            });
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void Cleanup()
        {
            if (_closing) return;
            _closing = true;

            // Fechar encerra SÓ a sessão de controle (espelho + entrada). NÃO mexe na
            // tela: o terminal continua exibindo o layout (só "Parar tela" interrompe).
            try { _view.Dispose(); } catch { }
            try { _sender.Dispose(); } catch { }
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
