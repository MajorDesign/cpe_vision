using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VideoWall.Models;
using VideoWall.Services;
using VideoWall.ViewModels;

namespace VideoWall.Views
{
    public partial class MainWindow : Window
    {
        private const double MinSize = 40;     // tamanho mínimo do elemento (DIP da parede)
        private const double HandlePixels = 14; // zona de pega das bordas (em pixels de tela)

        private enum Corner { None, TopLeft, TopRight, BottomLeft, BottomRight }

        private readonly MainViewModel _viewModel;

        // Estado de mover/redimensionar na pré-visualização.
        private WallElement? _draggingElement;
        private WallElement? _resizeElement;
        private Corner _resizeCorner;
        private double _grabOffsetX;
        private double _grabOffsetY;

        private WallElement? _trackedSelection;

        // Estado de tela cheia da janela do controlador.
        private bool _isFullscreen;
        private WindowStyle _savedStyle;
        private WindowState _savedState;
        private ResizeMode _savedResize;

        public MainWindow()
        {
            InitializeComponent();

            var monitorService = new MonitorDetectionService();
            var displayService = new WallDisplayService();
            var captureService = new WindowCaptureService();
            var windowPicker = new WindowPickerService();
            var layoutService = new LayoutService();
            var favoritesService = new FavoritesService();
            var settingsService = new SettingsService();
            var scheduleService = new ScheduleService();
            _viewModel = new MainViewModel(monitorService, displayService, captureService, windowPicker, layoutService, favoritesService, settingsService, scheduleService);
            DataContext = _viewModel;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            VersionLabel.Text = "v" + Network.GitHubUpdater.CurrentVersion();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var dpi = VisualTreeHelper.GetDpi(this);
            _viewModel.SetDpiScale(dpi.DpiScaleX);

            // Fail-safe: carrega o layout principal ao abrir, se houver.
            _viewModel.LoadMainLayoutIfSet();
        }

        /// <summary>Seleciona o item da lista sob o cursor ao clicar com o botão direito.</summary>
        private void ElementsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not ListBoxItem)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is ListBoxItem item)
                item.IsSelected = true;
        }

        private void RenameSource_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedElement is not WallElement element)
                return;

            var dialog = new RenameWindow(element.Name) { Owner = this };
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.NewName))
                element.Name = dialog.NewName.Trim();
        }

        /// <summary>
        /// Abre o editor de URL de um navegador (duplo-clique na pré-visualização).
        /// Confirma o endereço e guarda a miniatura capturada da página.
        /// </summary>
        private void EditBrowserUrl(Models.BrowserElement browser)
        {
            var dialog = new UrlEditWindow(browser.Url) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            if (!string.IsNullOrWhiteSpace(dialog.ResultUrl))
                browser.Url = dialog.ResultUrl.Trim();

            if (dialog.ResultPreview != null)
                browser.PreviewImage = dialog.ResultPreview;

            // Envia o layout atualizado para o terminal imediatamente, para que a
            // página escolhida abra na tela de rede sem precisar de outro clique.
            if (_viewModel.SendLayoutToScreenCommand.CanExecute(null))
                _viewModel.SendLayoutToScreenCommand.Execute(null);
        }

        /// <summary>Adiciona um Texto e já abre a janela para escrever.</summary>
        private void AddText_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AddTextCommand.Execute(null);
            if (_viewModel.SelectedElement is Models.TextElement text)
                EditText(text);
        }

        /// <summary>Edita o conteúdo/tamanho/cor de um Texto (duplo-clique na pré-visualização).</summary>
        private void EditText(Models.TextElement text)
        {
            var dialog = new TextEditWindow(text) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            text.Text = dialog.ResultText;
            text.FontSize = dialog.ResultFontSize;
            text.ForegroundHex = dialog.ResultColorHex;

            // Atualiza no terminal na hora.
            if (_viewModel.SendLayoutToScreenCommand.CanExecute(null))
                _viewModel.SendLayoutToScreenCommand.Execute(null);
        }

        private void LiveControl_Click(object sender, RoutedEventArgs e)
        {
            var screen = _viewModel.SelectedScreen;
            if (screen == null)
                return;

            // Fontes na MESMA ordem em que o controlador as envia ao terminal — é por essa
            // posição que identificamos a célula a controlar.
            var sendable = _viewModel.Elements
                .OrderBy(el => el.ZIndex)
                .Where(el => el is Models.BrowserElement or Models.ColorElement or Models.TextElement)
                .ToList();

            // Controla o navegador SELECIONADO (clique nele na parede); senão, o primeiro.
            var target = _viewModel.SelectedElement as Models.BrowserElement
                         ?? sendable.OfType<Models.BrowserElement>().FirstOrDefault();
            if (target == null)
            {
                MessageBox.Show(this,
                    "Adicione e selecione um navegador na parede para controlar ao vivo.",
                    "Controle ao vivo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int index = sendable.IndexOf(target);
            if (index < 0)
                index = 0;

            // Garante que a parede atual está projetada (a célula precisa existir na tela).
            if (_viewModel.SendLayoutToScreenCommand.CanExecute(null))
                _viewModel.SendLayoutToScreenCommand.Execute(null);

            // Proporção da célula (para o espelho ter o mesmo formato e as marcações/cliques
            // mapearem exatamente no terminal).
            double aspect = target.Height > 0 ? target.Width / target.Height : 16.0 / 9.0;

            var window = new LiveControlWindow(screen.IpAddress, screen.Name, target.Url, index,
                target.ZoomFactor, aspect)
            {
                Owner = this
            };
            window.Show();
        }

        private void RemoveSource_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.RemoveSelectedCommand.CanExecute(null))
                _viewModel.RemoveSelectedCommand.Execute(null);
        }

        private void TestCamera_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedElement is not CameraElement camera)
                return;

            if (!Uri.TryCreate(camera.StreamUrl, UriKind.Absolute, out var uri)
                || string.IsNullOrEmpty(uri.Host))
            {
                MessageBox.Show(this,
                    "Informe uma URL de stream válida (ex.: rtsp://usuario:senha@host:554/stream).",
                    "Câmera", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            new CameraTestWindow(camera.StreamUrl) { Owner = this }.Show();
        }

        private void OpenScheduler_Click(object sender, RoutedEventArgs e)
        {
            var window = new SchedulerWindow(_viewModel) { Owner = this };
            window.ShowDialog();
        }

        // ===================== Tela cheia (F11 alterna · Esc sai) =====================

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _isFullscreen)
            {
                ExitFullscreen();
                e.Handled = true;
            }

            base.OnPreviewKeyDown(e);
        }

        private void ToggleFullscreen()
        {
            if (_isFullscreen)
                ExitFullscreen();
            else
                EnterFullscreen();
        }

        private void EnterFullscreen()
        {
            if (_isFullscreen)
                return;

            _savedStyle = WindowStyle;
            _savedState = WindowState;
            _savedResize = ResizeMode;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;   // garante re-maximizar cobrindo a tela inteira
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
        }

        private void ExitFullscreen()
        {
            if (!_isFullscreen)
                return;

            WindowStyle = _savedStyle;
            ResizeMode = _savedResize;
            WindowState = _savedState;
            _isFullscreen = false;
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            TrackSelection(null);
            _viewModel.Dispose();
            base.OnClosed(e);
        }

        // ===================== Sincronização do retângulo de seleção =====================

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.SelectedElement):
                    TrackSelection(_viewModel.SelectedElement);
                    UpdateSelectionVisual();
                    break;

                case nameof(MainViewModel.GridColumns):
                case nameof(MainViewModel.GridRows):
                case nameof(MainViewModel.SnapToGrid):
                case nameof(MainViewModel.WallWidth):
                case nameof(MainViewModel.WallHeight):
                    RebuildGrid();
                    break;
            }
        }

        /// <summary>Desenha as linhas internas da grade — uma grade por monitor — no preview.</summary>
        private void RebuildGrid()
        {
            GridCanvas.Children.Clear();

            if (!_viewModel.SnapToGrid)
                return;

            int cols = _viewModel.GridColumns;
            int rows = _viewModel.GridRows;
            var brush = new SolidColorBrush(Color.FromArgb(120, 137, 180, 250));

            foreach (var monitor in _viewModel.PreviewMonitors)
            {
                double cellW = monitor.Width / cols;
                double cellH = monitor.Height / rows;

                for (int c = 1; c < cols; c++)
                {
                    GridCanvas.Children.Add(new Line
                    {
                        X1 = monitor.Left + c * cellW, Y1 = monitor.Top,
                        X2 = monitor.Left + c * cellW, Y2 = monitor.Top + monitor.Height,
                        Stroke = brush, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 6, 4 },
                    });
                }

                for (int r = 1; r < rows; r++)
                {
                    GridCanvas.Children.Add(new Line
                    {
                        X1 = monitor.Left, Y1 = monitor.Top + r * cellH,
                        X2 = monitor.Left + monitor.Width, Y2 = monitor.Top + r * cellH,
                        Stroke = brush, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 6, 4 },
                    });
                }
            }
        }

        private void TrackSelection(WallElement? element)
        {
            if (_trackedSelection != null)
                _trackedSelection.PropertyChanged -= OnSelectedElementPropertyChanged;

            _trackedSelection = element;

            if (_trackedSelection != null)
                _trackedSelection.PropertyChanged += OnSelectedElementPropertyChanged;
        }

        private void OnSelectedElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(WallElement.X) or nameof(WallElement.Y)
                or nameof(WallElement.Width) or nameof(WallElement.Height))
            {
                UpdateSelectionVisual();
            }
        }

        private void UpdateSelectionVisual()
        {
            var el = _viewModel.SelectedElement;
            if (el == null)
            {
                SelectionRect.Visibility = Visibility.Collapsed;
                return;
            }

            Canvas.SetLeft(SelectionRect, el.X + _viewModel.WallOffsetX);
            Canvas.SetTop(SelectionRect, el.Y + _viewModel.WallOffsetY);
            SelectionRect.Width = el.Width;
            SelectionRect.Height = el.Height;
            SelectionRect.Visibility = Visibility.Visible;
        }

        // ===================== Mover / redimensionar na pré-visualização =====================

        private void PreviewSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(PreviewSurface);
            var hit = HitTest(point);
            if (hit == null)
                return;

            _viewModel.SelectedElement = hit;
            PreviewSurface.Focus(); // permite excluir a fonte com a tecla Delete

            // Duplo-clique num navegador: abre o editor de URL com pré-visualização.
            if (e.ClickCount == 2 && hit is Models.BrowserElement browser)
            {
                EditBrowserUrl(browser);
                e.Handled = true;
                return;
            }

            // Duplo-clique num texto: abre a janela para escrever.
            if (e.ClickCount == 2 && hit is Models.TextElement text)
            {
                EditText(text);
                e.Handled = true;
                return;
            }

            var corner = GetResizeCorner(hit, point);
            if (corner != Corner.None)
            {
                _resizeElement = hit;
                _resizeCorner = corner;
            }
            else
            {
                _draggingElement = hit;
                _grabOffsetX = point.X - (hit.X + _viewModel.WallOffsetX);
                _grabOffsetY = point.Y - (hit.Y + _viewModel.WallOffsetY);
            }

            PreviewSurface.CaptureMouse();
            e.Handled = true;
        }

        private void PreviewSurface_MouseMove(object sender, MouseEventArgs e)
        {
            var point = e.GetPosition(PreviewSurface);
            bool pressed = e.LeftButton == MouseButtonState.Pressed;

            if (_resizeElement != null && pressed)
            {
                ResizeTo(_resizeElement, SnapPoint(point));
                return;
            }

            if (_draggingElement != null && pressed)
            {
                double newX = point.X - _viewModel.WallOffsetX - _grabOffsetX;
                double newY = point.Y - _viewModel.WallOffsetY - _grabOffsetY;
                (newX, newY) = SnapTopLeft(newX, newY, _draggingElement.Width, _draggingElement.Height);
                _draggingElement.X = newX;
                _draggingElement.Y = newY;
                return;
            }

            UpdateHoverCursor(point);
        }

        private void PreviewSurface_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Seleciona a fonte sob o cursor para o menu de contexto agir sobre ela.
            var hit = HitTest(e.GetPosition(PreviewSurface));
            if (hit != null)
                _viewModel.SelectedElement = hit;
        }

        private void PreviewSurface_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _viewModel.RemoveSelectedCommand.CanExecute(null))
            {
                _viewModel.RemoveSelectedCommand.Execute(null);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C
                     && _viewModel.CopyCommand.CanExecute(null))
            {
                _viewModel.CopyCommand.Execute(null);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V
                     && _viewModel.PasteCommand.CanExecute(null))
            {
                _viewModel.PasteCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void PreviewSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingElement == null && _resizeElement == null)
                return;

            _draggingElement = null;
            _resizeElement = null;
            _resizeCorner = Corner.None;
            PreviewSurface.ReleaseMouseCapture();
            PreviewSurface.Cursor = Cursors.Arrow;
            e.Handled = true;
        }

        private void UpdateHoverCursor(Point point)
        {
            var hit = HitTest(point);
            if (hit == null)
            {
                PreviewSurface.Cursor = Cursors.Arrow;
                return;
            }

            PreviewSurface.Cursor = GetResizeCorner(hit, point) switch
            {
                Corner.TopLeft or Corner.BottomRight => Cursors.SizeNWSE,
                Corner.TopRight or Corner.BottomLeft => Cursors.SizeNESW,
                _ => Cursors.Hand,
            };
        }

        private void ResizeTo(WallElement el, Point point)
        {
            double offX = _viewModel.WallOffsetX;
            double offY = _viewModel.WallOffsetY;
            double left = el.X + offX;
            double top = el.Y + offY;
            double right = left + el.Width;
            double bottom = top + el.Height;

            switch (_resizeCorner)
            {
                case Corner.BottomRight:
                    el.Width = Math.Max(MinSize, point.X - left);
                    el.Height = Math.Max(MinSize, point.Y - top);
                    break;

                case Corner.BottomLeft:
                {
                    double newLeft = Math.Min(point.X, right - MinSize);
                    el.X = newLeft - offX;
                    el.Width = right - newLeft;
                    el.Height = Math.Max(MinSize, point.Y - top);
                    break;
                }

                case Corner.TopRight:
                {
                    double newTop = Math.Min(point.Y, bottom - MinSize);
                    el.Y = newTop - offY;
                    el.Height = bottom - newTop;
                    el.Width = Math.Max(MinSize, point.X - left);
                    break;
                }

                case Corner.TopLeft:
                {
                    double newLeft = Math.Min(point.X, right - MinSize);
                    double newTop = Math.Min(point.Y, bottom - MinSize);
                    el.X = newLeft - offX;
                    el.Y = newTop - offY;
                    el.Width = right - newLeft;
                    el.Height = bottom - newTop;
                    break;
                }
            }
        }

        /// <summary>Detecta se o ponto está sobre um canto do elemento (zona de redimensionamento).</summary>
        private Corner GetResizeCorner(WallElement el, Point point)
        {
            double left = el.X + _viewModel.WallOffsetX;
            double top = el.Y + _viewModel.WallOffsetY;
            double right = left + el.Width;
            double bottom = top + el.Height;

            double scale = GetPreviewScale();
            double threshold = Math.Min(HandlePixels / scale, Math.Min(el.Width, el.Height) / 3.0);

            bool nearLeft = Math.Abs(point.X - left) <= threshold;
            bool nearRight = Math.Abs(point.X - right) <= threshold;
            bool nearTop = Math.Abs(point.Y - top) <= threshold;
            bool nearBottom = Math.Abs(point.Y - bottom) <= threshold;

            if (nearTop && nearLeft) return Corner.TopLeft;
            if (nearTop && nearRight) return Corner.TopRight;
            if (nearBottom && nearLeft) return Corner.BottomLeft;
            if (nearBottom && nearRight) return Corner.BottomRight;
            return Corner.None;
        }

        /// <summary>Escala atual da pré-visualização (pixels de tela por unidade da parede).</summary>
        private double GetPreviewScale()
        {
            if (_viewModel.WallWidth <= 0)
                return 1;

            try
            {
                var transform = PreviewSurface.TransformToAncestor(this);
                var bounds = transform.TransformBounds(new Rect(0, 0, _viewModel.WallWidth, _viewModel.WallHeight));
                return bounds.Width > 0 ? bounds.Width / _viewModel.WallWidth : 1;
            }
            catch
            {
                return 1;
            }
        }

        // ===================== Encaixe na grade (snap) =====================

        /// <summary>Monitor (0-based) que contém o ponto, ou null.</summary>
        private PreviewMonitor? MonitorAt(Point p)
        {
            return _viewModel.PreviewMonitors.FirstOrDefault(m =>
                p.X >= m.Left && p.X <= m.Left + m.Width &&
                p.Y >= m.Top && p.Y <= m.Top + m.Height);
        }

        /// <summary>Encaixa o canto superior-esquerdo na grade do monitor onde o elemento está.</summary>
        private (double X, double Y) SnapTopLeft(double x, double y, double width, double height)
        {
            if (!_viewModel.SnapToGrid)
                return (x, y);

            double l0 = x + _viewModel.WallOffsetX;
            double t0 = y + _viewModel.WallOffsetY;
            var mon = MonitorAt(new Point(l0 + width / 2, t0 + height / 2)) ?? MonitorAt(new Point(l0, t0));
            if (mon == null)
                return (x, y);

            double cellW = mon.Width / _viewModel.GridColumns;
            double cellH = mon.Height / _viewModel.GridRows;
            l0 = mon.Left + Math.Round((l0 - mon.Left) / cellW) * cellW;
            t0 = mon.Top + Math.Round((t0 - mon.Top) / cellH) * cellH;
            return (l0 - _viewModel.WallOffsetX, t0 - _viewModel.WallOffsetY);
        }

        /// <summary>Encaixa um ponto na interseção da grade do monitor onde ele está.</summary>
        private Point SnapPoint(Point point)
        {
            if (!_viewModel.SnapToGrid)
                return point;

            var mon = MonitorAt(point);
            if (mon == null)
                return point;

            double cellW = mon.Width / _viewModel.GridColumns;
            double cellH = mon.Height / _viewModel.GridRows;
            double x = mon.Left + Math.Clamp(Math.Round((point.X - mon.Left) / cellW), 0, _viewModel.GridColumns) * cellW;
            double y = mon.Top + Math.Clamp(Math.Round((point.Y - mon.Top) / cellH), 0, _viewModel.GridRows) * cellH;
            return new Point(x, y);
        }

        /// <summary>Retorna o elemento mais à frente (maior ZIndex) sob o ponto informado.</summary>
        private WallElement? HitTest(Point point)
        {
            return _viewModel.Elements
                .Where(el => el.IsVisible
                             && point.X >= el.X + _viewModel.WallOffsetX
                             && point.X <= el.X + _viewModel.WallOffsetX + el.Width
                             && point.Y >= el.Y + _viewModel.WallOffsetY
                             && point.Y <= el.Y + _viewModel.WallOffsetY + el.Height)
                .OrderByDescending(el => el.ZIndex)
                .FirstOrDefault();
        }
    }
}
