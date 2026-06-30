using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoWall.Models;
using VideoWall.Network;
using VideoWall.Services;

namespace VideoWall.ViewModels
{
    public class MainViewModel : BaseViewModel, IDisposable
    {
        private readonly IMonitorDetectionService _monitorService;
        private readonly IWallDisplayService _displayService;
        private readonly IWindowCaptureService _captureService;
        private readonly IWindowPickerService _windowPicker;
        private readonly ILayoutService _layoutService;
        private readonly IFavoritesService _favoritesService;
        private readonly ISettingsService _settingsService;
        private readonly IScheduleService _scheduleService;
        private readonly DispatcherTimer _scheduleTimer;
        private readonly DispatcherTimer _thumbnailTimer;
        private readonly DispatcherTimer _rotationTimer;
        private readonly ViewerDiscoveryListener _discovery;
        private RemoteScreen? _rotationScreen;
        private int _rotationMinutes = 5;
        private string? _rotationPickLayout;
        private string? _selectedRotationLayout;
        private bool _isRotating;
        private int _rotationIndex;
        private string _rotationStatus = "Parado";
        private bool _schedulerPaused;
        private UdpBeacon? _controllerBeacon;
        private UpdateServer? _updateServer;
        private bool _pollingThumbnails;
        private RemoteScreen? _selectedScreen;
        private string _remoteUrl = "https://";

        // Tela de referência do terminal para edição (16:9).
        private const double RemoteScreenWidth = 1920;
        private const double RemoteScreenHeight = 1080;
        private string? _mainLayoutName;
        private ScheduleEntry? _selectedSchedule;
        private int _newScheduleHour = 8;
        private int _newScheduleMinute;
        private string? _newScheduleLayout;
        private RemoteScreen? _newScheduleScreen;
        private bool _daySun, _dayMon, _dayTue, _dayWed, _dayThu, _dayFri, _daySat;
        private WallElement? _clipboardElement;
        private int _pasteCount;

        private int _monitorCount;
        private string _statusMessage = string.Empty;
        private WallElement? _selectedElement;
        private bool _isWallRunning;
        private double _dpiScale = 1.0;
        private int _elementCounter;
        private string? _selectedLayoutName;
        private string _newLayoutName = string.Empty;
        private int _gridColumns = 2;
        private int _gridRows = 2;
        private bool _snapToGrid;
        private string? _selectedGridPreset;
        private SourceFavorite? _selectedFavorite;
        private string _newFavoriteCategory = "Web";
        private string _newFavoriteName = string.Empty;
        private string _newFavoritePayload = string.Empty;

        // Geometria da parede virtual, em DIP, com origem em (0,0) para a
        // pré-visualização. WallOffset desloca as coordenadas absolutas dos
        // elementos para esse espaço 0-based.
        private double _wallWidth;
        private double _wallHeight;
        private double _wallOffsetX;
        private double _wallOffsetY;

        public ObservableCollection<MonitorInfo> Monitors { get; } = new();
        public ObservableCollection<PreviewMonitor> PreviewMonitors { get; } = new();
        public ObservableCollection<WallElement> Elements { get; } = new();
        public ObservableCollection<string> Layouts { get; } = new();
        public ObservableCollection<SourceFavorite> Favorites { get; } = new();
        public ObservableCollection<string> FavoriteCategories { get; } = new() { "Web", "Imagem" };
        public ObservableCollection<ScheduleEntry> Schedules { get; } = new();

        /// <summary>Sequência de layouts da rotação automática (troca a cada X min).</summary>
        public ObservableCollection<string> RotationLayouts { get; } = new();

        /// <summary>Telas (mini-PCs com o Viewer) encontradas na rede.</summary>
        public ObservableCollection<RemoteScreen> RemoteScreens { get; } = new();

        /// <summary>Tela de rede sendo editada (null = parede local pelos monitores).</summary>
        public RemoteScreen? SelectedScreen
        {
            get => _selectedScreen;
            set
            {
                if (_selectedScreen == value)
                    return;

                _selectedScreen = value;

                // Carrega o layout transmitido na tela selecionada (ou vazio).
                Elements.Clear();
                if (value != null)
                    foreach (var el in value.Layout)
                        Elements.Add(el.Clone());

                SelectedElement = null;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EditingTargetLabel));
                OnPropertyChanged(nameof(HasScreenSelected));
                OnPropertyChanged(nameof(OverlayStatus));
                RebuildWallGeometry();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>Verdadeiro quando há uma tela de rede selecionada.</summary>
        public bool HasScreenSelected => SelectedScreen != null;

        /// <summary>Texto do botão de overlay, refletindo o estado do terminal selecionado.</summary>
        public string OverlayStatus =>
            SelectedScreen == null ? "🎬  Overlay de vídeo (HW)"
            : SelectedScreen.OverlayOn ? "🎬  Overlay HW: LIGADO (clique p/ desligar)"
            : "🎬  Overlay HW: desligado (clique p/ ligar)";

        /// <summary>Rótulo do alvo de edição (a tela de rede selecionada).</summary>
        public string EditingTargetLabel =>
            SelectedScreen != null ? $"Tela: {SelectedScreen.Name}" : "Selecione uma tela na rede";

        public string RemoteUrl
        {
            get => _remoteUrl;
            set => SetProperty(ref _remoteUrl, value);
        }

        public ScheduleEntry? SelectedSchedule
        {
            get => _selectedSchedule;
            set
            {
                if (SetProperty(ref _selectedSchedule, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public int NewScheduleHour
        {
            get => _newScheduleHour;
            set => SetProperty(ref _newScheduleHour, Math.Clamp(value, 0, 23));
        }

        public int NewScheduleMinute
        {
            get => _newScheduleMinute;
            set => SetProperty(ref _newScheduleMinute, Math.Clamp(value, 0, 59));
        }

        public string? NewScheduleLayout
        {
            get => _newScheduleLayout;
            set
            {
                if (SetProperty(ref _newScheduleLayout, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>Tela (terminal) de destino do novo agendamento.</summary>
        public RemoteScreen? NewScheduleScreen
        {
            get => _newScheduleScreen;
            set
            {
                if (SetProperty(ref _newScheduleScreen, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        // ---------------- Rotação automática (player: troca a cada X min) ----------------

        /// <summary>Tela de destino da rotação.</summary>
        public RemoteScreen? RotationScreen
        {
            get => _rotationScreen;
            set { if (SetProperty(ref _rotationScreen, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        /// <summary>Intervalo (minutos) entre as trocas de layout na rotação.</summary>
        public int RotationMinutes
        {
            get => _rotationMinutes;
            set { if (SetProperty(ref _rotationMinutes, Math.Clamp(value, 1, 1440))) CommandManager.InvalidateRequerySuggested(); }
        }

        /// <summary>Layout escolhido para adicionar à sequência da rotação.</summary>
        public string? RotationPickLayout
        {
            get => _rotationPickLayout;
            set { if (SetProperty(ref _rotationPickLayout, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        /// <summary>Item selecionado na sequência (para remover).</summary>
        public string? SelectedRotationLayout
        {
            get => _selectedRotationLayout;
            set { if (SetProperty(ref _selectedRotationLayout, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        public bool IsRotating
        {
            get => _isRotating;
            private set { if (SetProperty(ref _isRotating, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        public string RotationStatus
        {
            get => _rotationStatus;
            private set => SetProperty(ref _rotationStatus, value);
        }

        /// <summary>Quando pausado, os agendamentos por horário não disparam.</summary>
        public bool SchedulerPaused
        {
            get => _schedulerPaused;
            private set { if (SetProperty(ref _schedulerPaused, value)) OnPropertyChanged(nameof(SchedulerToggleText)); }
        }

        public string SchedulerToggleText => SchedulerPaused ? "▶ Retomar agendador" : "⏸ Pausar agendador";

        public bool DaySun { get => _daySun; set => SetProperty(ref _daySun, value); }
        public bool DayMon { get => _dayMon; set => SetProperty(ref _dayMon, value); }
        public bool DayTue { get => _dayTue; set => SetProperty(ref _dayTue, value); }
        public bool DayWed { get => _dayWed; set => SetProperty(ref _dayWed, value); }
        public bool DayThu { get => _dayThu; set => SetProperty(ref _dayThu, value); }
        public bool DayFri { get => _dayFri; set => SetProperty(ref _dayFri, value); }
        public bool DaySat { get => _daySat; set => SetProperty(ref _daySat, value); }

        public SourceFavorite? SelectedFavorite
        {
            get => _selectedFavorite;
            set
            {
                if (SetProperty(ref _selectedFavorite, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public string NewFavoriteCategory
        {
            get => _newFavoriteCategory;
            set => SetProperty(ref _newFavoriteCategory, value);
        }

        public string NewFavoriteName
        {
            get => _newFavoriteName;
            set
            {
                if (SetProperty(ref _newFavoriteName, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public string NewFavoritePayload
        {
            get => _newFavoritePayload;
            set
            {
                if (SetProperty(ref _newFavoritePayload, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool _autoLoadLayout = true;

        public string? SelectedLayoutName
        {
            get => _selectedLayoutName;
            set
            {
                if (SetProperty(ref _selectedLayoutName, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                    // Selecionar um layout PRÉ-VISUALIZA na parede (não projeta).
                    if (_autoLoadLayout && !string.IsNullOrWhiteSpace(value))
                        LoadLayoutByName(value!, announce: false);
                }
            }
        }

        /// <summary>Define o layout selecionado sem disparar a pré-visualização automática.</summary>
        private void SetSelectedLayoutSilent(string? name)
        {
            bool prev = _autoLoadLayout;
            _autoLoadLayout = false;
            SelectedLayoutName = name;
            _autoLoadLayout = prev;
        }

        /// <summary>Carrega um layout salvo na parede (pré-visualização). Não projeta.</summary>
        private bool LoadLayoutByName(string name, bool announce)
        {
            var loaded = _layoutService.Load(name);
            if (loaded == null)
            {
                if (announce) StatusMessage = $"Layout não encontrado: {name}";
                return false;
            }
            ApplyLayout(loaded);
            if (announce) StatusMessage = $"Layout carregado: {name}";
            return true;
        }

        public string NewLayoutName
        {
            get => _newLayoutName;
            set
            {
                if (SetProperty(ref _newLayoutName, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>Layout carregado automaticamente ao abrir (fail-safe).</summary>
        public string? MainLayoutName
        {
            get => _mainLayoutName;
            private set => SetProperty(ref _mainLayoutName, value);
        }

        // ===================== Grade (Grid) =====================

        public ObservableCollection<string> GridPresets { get; } = new()
        {
            "1×1", "2×1", "2×2", "3×2", "3×3", "4×2", "4×3", "8×2",
        };

        public int GridColumns
        {
            get => _gridColumns;
            set
            {
                if (SetProperty(ref _gridColumns, Math.Clamp(value, 1, 32)))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public int GridRows
        {
            get => _gridRows;
            set
            {
                if (SetProperty(ref _gridRows, Math.Clamp(value, 1, 32)))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool SnapToGrid
        {
            get => _snapToGrid;
            set => SetProperty(ref _snapToGrid, value);
        }

        public string? SelectedGridPreset
        {
            get => _selectedGridPreset;
            set
            {
                if (SetProperty(ref _selectedGridPreset, value))
                    ApplyGridPreset(value);
            }
        }

        private bool GridIsValid => PreviewMonitors.Count > 0 && GridColumns >= 1 && GridRows >= 1;

        /// <summary>
        /// Células da grade, uma grade Colunas×Linhas POR MONITOR (em coordenadas
        /// 0-based da parede). Assim as fontes nunca caem em áreas fora das telas.
        /// </summary>
        private List<Rect> BuildGridCells()
        {
            var cells = new List<Rect>();
            foreach (var monitor in PreviewMonitors)
            {
                double cellW = monitor.Width / GridColumns;
                double cellH = monitor.Height / GridRows;
                for (int r = 0; r < GridRows; r++)
                    for (int c = 0; c < GridColumns; c++)
                        cells.Add(new Rect(monitor.Left + c * cellW, monitor.Top + r * cellH, cellW, cellH));
            }
            return cells;
        }

        private void ApplyGridPreset(string? preset)
        {
            if (string.IsNullOrWhiteSpace(preset))
                return;

            var parts = preset.Split('×', '*', 'x', 'X');
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), out int cols)
                && int.TryParse(parts[1].Trim(), out int rows))
            {
                GridColumns = cols;
                GridRows = rows;
            }
        }

        /// <summary>Ajusta um elemento para preencher exatamente a célula informada (coords 0-based).</summary>
        private void FillCell(WallElement element, Rect cell)
        {
            element.X = cell.Left - WallOffsetX;
            element.Y = cell.Top - WallOffsetY;
            element.Width = cell.Width;
            element.Height = cell.Height;
        }

        private Point ElementCenter(WallElement element) => new(
            element.X + WallOffsetX + element.Width / 2,
            element.Y + WallOffsetY + element.Height / 2);

        private static double DistanceToCenter(Rect rect, Point point)
        {
            double dx = rect.Left + rect.Width / 2 - point.X;
            double dy = rect.Top + rect.Height / 2 - point.Y;
            return dx * dx + dy * dy;
        }

        private void FillSelectedCell()
        {
            if (SelectedElement == null || !GridIsValid)
                return;

            var cells = BuildGridCells();
            if (cells.Count == 0)
                return;

            var center = ElementCenter(SelectedElement);
            var cell = cells.FirstOrDefault(r => r.Contains(center));
            if (cell == default)
                cell = cells.OrderBy(r => DistanceToCenter(r, center)).First();

            FillCell(SelectedElement, cell);
            StatusMessage = "Fonte encaixada na célula da grade.";
        }

        private void DistributeToGrid()
        {
            var cells = BuildGridCells();
            if (cells.Count == 0 || Elements.Count == 0)
                return;

            int index = 0;
            foreach (var element in Elements.OrderBy(e => e.ZIndex))
            {
                if (index >= cells.Count)
                    break;
                FillCell(element, cells[index]);
                index++;
            }

            StatusMessage = $"{Math.Min(Elements.Count, cells.Count)} fonte(s) distribuída(s) na grade {GridColumns}×{GridRows} por monitor.";
        }

        /// <summary>Faz a fonte selecionada preencher o monitor onde está (adapta ao tamanho da tela).</summary>
        private void FillSelectedScreen()
        {
            if (SelectedElement == null || PreviewMonitors.Count == 0)
                return;

            var center = ElementCenter(SelectedElement);
            var rects = PreviewMonitors.Select(m => new Rect(m.Left, m.Top, m.Width, m.Height)).ToList();
            var rect = rects.FirstOrDefault(r => r.Contains(center));
            if (rect == default)
                rect = rects.OrderBy(r => DistanceToCenter(r, center)).First();

            FillCell(SelectedElement, rect);
            StatusMessage = "Fonte ajustada ao tamanho da tela.";
        }

        // ===================== Copiar / colar fontes =====================

        private void CopySelected()
        {
            if (SelectedElement == null)
                return;

            _clipboardElement = SelectedElement.Clone();
            _pasteCount = 0;
            StatusMessage = $"Fonte copiada: {SelectedElement.Name}";
            CommandManager.InvalidateRequerySuggested();
        }

        private void Paste()
        {
            if (_clipboardElement == null)
                return;

            _pasteCount++;
            var copy = _clipboardElement.Clone();
            copy.X += _pasteCount * 30;
            copy.Y += _pasteCount * 30;
            copy.ZIndex = NextZIndex();
            _elementCounter++;

            Elements.Add(copy);
            SelectedElement = copy;

            if (copy is WindowCaptureElement capture)
                TryReconnectCapture(capture);

            StatusMessage = $"Fonte colada: {copy.Name}";
            CommandManager.InvalidateRequerySuggested();
        }

        public int MonitorCount
        {
            get => _monitorCount;
            private set => SetProperty(ref _monitorCount, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public WallElement? SelectedElement
        {
            get => _selectedElement;
            set
            {
                if (SetProperty(ref _selectedElement, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsWallRunning
        {
            get => _isWallRunning;
            private set => SetProperty(ref _isWallRunning, value);
        }

        public double WallWidth
        {
            get => _wallWidth;
            private set => SetProperty(ref _wallWidth, value);
        }

        public double WallHeight
        {
            get => _wallHeight;
            private set => SetProperty(ref _wallHeight, value);
        }

        public double WallOffsetX
        {
            get => _wallOffsetX;
            private set => SetProperty(ref _wallOffsetX, value);
        }

        public double WallOffsetY
        {
            get => _wallOffsetY;
            private set => SetProperty(ref _wallOffsetY, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand StartWallCommand { get; }
        public ICommand StopWallCommand { get; }
        public ICommand AddColorCommand { get; }
        public ICommand AddTextCommand { get; }
        public ICommand AddImageCommand { get; }
        public ICommand AddBrowserCommand { get; }
        public ICommand AddWindowCaptureCommand { get; }
        public ICommand AddCameraCommand { get; }
        public ICommand SendUrlToScreenCommand { get; }
        public ICommand SendLayoutToScreenCommand { get; }
        public ICommand ClearScreenCommand { get; }
        public ICommand RestartScreenCommand { get; }
        public ICommand ToggleOverlayCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand BringToFrontCommand { get; }
        public ICommand SendToBackCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand PasteCommand { get; }
        public ICommand FillScreenCommand { get; }
        public ICommand SaveLayoutCommand { get; }
        public ICommand LoadLayoutCommand { get; }
        public ICommand DeleteLayoutCommand { get; }
        public ICommand SetMainLayoutCommand { get; }
        public ICommand LoadLayoutByIndexCommand { get; }
        public ICommand AddScheduleCommand { get; }
        public ICommand RemoveScheduleCommand { get; }
        public ICommand AddRotationLayoutCommand { get; }
        public ICommand RemoveRotationLayoutCommand { get; }
        public ICommand StartRotationCommand { get; }
        public ICommand StopRotationCommand { get; }
        public ICommand ToggleSchedulerCommand { get; }
        public ICommand FillCellCommand { get; }
        public ICommand DistributeCommand { get; }
        public ICommand AddFavoriteCommand { get; }
        public ICommand BrowseFavoritePayloadCommand { get; }
        public ICommand SaveSelectedAsFavoriteCommand { get; }
        public ICommand AddFavoriteToWallCommand { get; }
        public ICommand RemoveFavoriteCommand { get; }

        public MainViewModel(
            IMonitorDetectionService monitorService,
            IWallDisplayService displayService,
            IWindowCaptureService captureService,
            IWindowPickerService windowPicker,
            ILayoutService layoutService,
            IFavoritesService favoritesService,
            ISettingsService settingsService,
            IScheduleService scheduleService)
        {
            _monitorService = monitorService;
            _displayService = displayService;
            _captureService = captureService;
            _windowPicker = windowPicker;
            _layoutService = layoutService;
            _favoritesService = favoritesService;
            _settingsService = settingsService;
            _scheduleService = scheduleService;
            _mainLayoutName = _settingsService.Load().MainLayoutName;
            _monitorService.MonitorsChanged += OnMonitorsChanged;

            RefreshCommand = new RelayCommand(RefreshMonitors);
            StartWallCommand = new RelayCommand(StartWall, () => !IsWallRunning && Monitors.Count > 0);
            StopWallCommand = new RelayCommand(StopWall, () => IsWallRunning);
            AddColorCommand = new RelayCommand(AddColor);
            AddTextCommand = new RelayCommand(AddText);
            AddImageCommand = new RelayCommand(AddImage);
            AddBrowserCommand = new RelayCommand(AddBrowser);
            AddWindowCaptureCommand = new RelayCommand(AddWindowCapture);
            AddCameraCommand = new RelayCommand(AddCamera);
            SendUrlToScreenCommand = new RelayCommand(SendUrlToScreen,
                () => SelectedScreen != null && !string.IsNullOrWhiteSpace(RemoteUrl));
            SendLayoutToScreenCommand = new RelayCommand(SendLayoutToScreen,
                () => SelectedScreen != null && Elements.Count > 0);
            ClearScreenCommand = new RelayCommand(ClearScreen, () => SelectedScreen != null);
            RestartScreenCommand = new RelayCommand(RestartScreen, () => SelectedScreen != null);
            ToggleOverlayCommand = new RelayCommand(ToggleOverlay, () => SelectedScreen != null);
            RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => SelectedElement != null);
            BringToFrontCommand = new RelayCommand(BringToFront, () => SelectedElement != null);
            SendToBackCommand = new RelayCommand(SendToBack, () => SelectedElement != null);
            CopyCommand = new RelayCommand(CopySelected, () => SelectedElement != null);
            PasteCommand = new RelayCommand(Paste, () => _clipboardElement != null);
            FillScreenCommand = new RelayCommand(FillSelectedScreen, () => SelectedElement != null && PreviewMonitors.Count > 0);
            SaveLayoutCommand = new RelayCommand(SaveLayout, () => !string.IsNullOrWhiteSpace(NewLayoutName));
            LoadLayoutCommand = new RelayCommand(LoadLayout, () => !string.IsNullOrWhiteSpace(SelectedLayoutName));
            DeleteLayoutCommand = new RelayCommand(DeleteLayout, () => !string.IsNullOrWhiteSpace(SelectedLayoutName));
            SetMainLayoutCommand = new RelayCommand(SetMainLayout, () => !string.IsNullOrWhiteSpace(SelectedLayoutName));
            LoadLayoutByIndexCommand = new RelayCommand<string>(LoadLayoutByIndex);
            AddScheduleCommand = new RelayCommand(AddSchedule,
                () => !string.IsNullOrWhiteSpace(NewScheduleLayout) && NewScheduleScreen != null);
            RemoveScheduleCommand = new RelayCommand(RemoveSchedule, () => SelectedSchedule != null);
            AddRotationLayoutCommand = new RelayCommand(AddRotationLayout, () => !string.IsNullOrWhiteSpace(RotationPickLayout));
            RemoveRotationLayoutCommand = new RelayCommand(RemoveRotationLayout, () => SelectedRotationLayout != null);
            StartRotationCommand = new RelayCommand(StartRotation,
                () => !IsRotating && RotationLayouts.Count >= 1 && RotationScreen != null && RotationMinutes >= 1);
            StopRotationCommand = new RelayCommand(StopRotation, () => IsRotating);
            ToggleSchedulerCommand = new RelayCommand(ToggleScheduler);
            FillCellCommand = new RelayCommand(FillSelectedCell, () => SelectedElement != null && GridIsValid);
            DistributeCommand = new RelayCommand(DistributeToGrid, () => Elements.Count > 0 && GridIsValid);
            AddFavoriteCommand = new RelayCommand(AddFavorite,
                () => !string.IsNullOrWhiteSpace(NewFavoriteName) && !string.IsNullOrWhiteSpace(NewFavoritePayload));
            BrowseFavoritePayloadCommand = new RelayCommand(BrowseFavoritePayload);
            SaveSelectedAsFavoriteCommand = new RelayCommand(SaveSelectedAsFavorite, () => CanFavorite(SelectedElement));
            AddFavoriteToWallCommand = new RelayCommand(AddFavoriteToWall, () => SelectedFavorite != null);
            RemoveFavoriteCommand = new RelayCommand(RemoveFavorite, () => SelectedFavorite != null);

            RefreshMonitors();
            RefreshLayouts();
            LoadFavorites();
            LoadSchedules();

            // Verifica os agendamentos periodicamente (prioridade Normal para não ser
            // adiada quando a interface está ocupada; roda mesmo minimizado/em segundo plano).
            _scheduleTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(15) };
            _scheduleTimer.Tick += (_, _) => CheckSchedules();
            _scheduleTimer.Start();

            // Player de rotação (troca de layout a cada X min); inicia parado.
            _rotationTimer = new DispatcherTimer();
            _rotationTimer.Tick += (_, _) => RotationTick();
            RotationLayouts.CollectionChanged += (_, _) => CommandManager.InvalidateRequerySuggested();

            // Descobre automaticamente as telas (Viewers) na rede.
            _discovery = new ViewerDiscoveryListener();
            _discovery.ViewersChanged += OnRemoteViewersChanged;
            try { _discovery.Start(); }
            catch { /* porta em uso / sem rede: segue sem descoberta */ }

            // Busca periodicamente a foto ao vivo de cada terminal (miniatura).
            _thumbnailTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _thumbnailTimer.Tick += (_, _) => PollThumbnails();
            _thumbnailTimer.Start();

            StartUpdateServices();
        }

        /// <summary>
        /// Pede a cada terminal uma foto do que está exibindo e atualiza a miniatura.
        /// Roda em paralelo por tela e nunca bloqueia a interface.
        /// </summary>
        private async void PollThumbnails()
        {
            if (_pollingThumbnails)
                return;
            _pollingThumbnails = true;
            try
            {
                var screens = RemoteScreens.ToList();
                await Task.WhenAll(screens.Select(async screen =>
                {
                    var jpeg = await ThumbnailClient.RequestAsync(screen.IpAddress);
                    var image = jpeg != null ? DecodeJpeg(jpeg) : null;
                    if (image != null)
                    {
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            screen.LiveThumbnail = image;
                            // Para a tela selecionada, recorta a foto por célula e mostra o
                            // conteúdo ao vivo de cada navegador na pré-visualização grande.
                            if (ReferenceEquals(screen, SelectedScreen) &&
                                image is System.Windows.Media.Imaging.BitmapSource source)
                                UpdateElementPreviews(source);
                        });
                    }
                }));
            }
            catch { /* rede instável: tenta de novo no próximo tick */ }
            finally { _pollingThumbnails = false; }
        }

        /// <summary>
        /// Recorta a foto ao vivo do terminal por célula e atribui cada pedaço ao
        /// navegador correspondente, mostrando o conteúdo real na pré-visualização.
        /// </summary>
        private void UpdateElementPreviews(System.Windows.Media.Imaging.BitmapSource full)
        {
            if (full.PixelWidth <= 0 || full.PixelHeight <= 0 || WallWidth <= 0 || WallHeight <= 0)
                return;

            foreach (var browser in Elements.OfType<BrowserElement>())
            {
                double nx = (browser.X + WallOffsetX) / WallWidth;
                double ny = (browser.Y + WallOffsetY) / WallHeight;
                double nw = browser.Width / WallWidth;
                double nh = browser.Height / WallHeight;

                int px = (int)Math.Round(nx * full.PixelWidth);
                int py = (int)Math.Round(ny * full.PixelHeight);
                int pw = (int)Math.Round(nw * full.PixelWidth);
                int ph = (int)Math.Round(nh * full.PixelHeight);

                px = Math.Clamp(px, 0, full.PixelWidth - 1);
                py = Math.Clamp(py, 0, full.PixelHeight - 1);
                pw = Math.Clamp(pw, 1, full.PixelWidth - px);
                ph = Math.Clamp(ph, 1, full.PixelHeight - py);

                try
                {
                    var crop = new System.Windows.Media.Imaging.CroppedBitmap(
                        full, new System.Windows.Int32Rect(px, py, pw, ph));
                    crop.Freeze();
                    browser.PreviewImage = crop;
                }
                catch { /* recorte fora dos limites: ignora */ }
            }
        }

        private static System.Windows.Media.ImageSource? DecodeJpeg(byte[] data)
        {
            try
            {
                using var ms = new System.IO.MemoryStream(data);
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        }

        /// <summary>
        /// Anuncia o central na rede e serve o binário do terminal (de
        /// "terminal-update" ao lado do executável) para os terminais se
        /// auto-atualizarem pela rede local.
        /// </summary>
        private void StartUpdateServices()
        {
            try
            {
                const int updatePort = 48020;
                string viewerExe = System.IO.Path.Combine(
                    AppContext.BaseDirectory, "terminal-update", "VideoWall.Viewer.exe");

                _updateServer = new UpdateServer(viewerExe, updatePort);
                _updateServer.Start();

                _controllerBeacon = new UdpBeacon(new ControllerInfo
                {
                    IpAddress = NetworkUtil.GetLocalIPv4(),
                    UpdatePort = updatePort,
                });
                _controllerBeacon.Start();
            }
            catch
            {
                // Sem rede / porta ocupada: segue sem servir atualizações.
            }
        }

        private void OnRemoteViewersChanged(IReadOnlyList<ViewerInfo> viewers)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                // Remove telas que saíram da rede (preserva layout das demais).
                for (int i = RemoteScreens.Count - 1; i >= 0; i--)
                {
                    if (!viewers.Any(v => v.Id == RemoteScreens[i].Id))
                    {
                        if (SelectedScreen == RemoteScreens[i])
                            SelectedScreen = null;
                        RemoteScreens.RemoveAt(i);
                    }
                }

                // Adiciona novas / atualiza dados das existentes.
                foreach (var viewer in viewers)
                {
                    var existing = RemoteScreens.FirstOrDefault(s => s.Id == viewer.Id);
                    if (existing == null)
                    {
                        var screen = new RemoteScreen(viewer);
                        RemoteScreens.Add(screen);
                        // Tela recém-descoberta (ex.: controlador acabou de abrir): pergunta
                        // ao terminal o que ele está exibindo e reconstrói a parede.
                        FetchRemoteLayout(screen);
                    }
                    else
                        existing.UpdateInfo(viewer);
                }

                // Atualiza o texto do botão de overlay (estado do terminal selecionado).
                OnPropertyChanged(nameof(OverlayStatus));
            });
        }

        /// <summary>
        /// Pergunta ao terminal o layout que ele está exibindo e o reconstrói em
        /// <see cref="RemoteScreen.Layout"/> — para o controlador, ao reabrir, continuar
        /// editando/controlando o que já está na tela (o terminal é a fonte da verdade).
        /// </summary>
        private async void FetchRemoteLayout(RemoteScreen screen)
        {
            var json = await LayoutQueryClient.RequestAsync(screen.IpAddress);
            if (string.IsNullOrWhiteSpace(json))
                return;

            List<ScreenSource>? sources;
            try { sources = System.Text.Json.JsonSerializer.Deserialize<List<ScreenSource>>(json); }
            catch { return; }
            if (sources == null || sources.Count == 0)
                return;

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                // Não sobrescreve se já há layout (ex.: usuário começou a editar).
                if (screen.Layout.Count > 0)
                    return;

                foreach (var s in sources)
                {
                    var el = SourceToElement(s);
                    if (el != null)
                        screen.Layout.Add(el);
                }

                // Se essa tela já está selecionada e a edição está vazia, carrega na parede.
                if (ReferenceEquals(screen, SelectedScreen) && Elements.Count == 0)
                    foreach (var el in screen.Layout)
                        Elements.Add(el.Clone());
            });
        }

        /// <summary>Converte uma fonte da rede (normalizada) de volta para um elemento da parede.</summary>
        private static WallElement? SourceToElement(ScreenSource s)
        {
            WallElement? el = s.Kind switch
            {
                ScreenSource.Browser => new BrowserElement
                {
                    Url = string.IsNullOrWhiteSpace(s.Url) ? "https://" : s.Url!,
                    ZoomFactor = s.Zoom <= 0 ? 1.0 : s.Zoom,
                    IsOverlay = s.Overlay,
                },
                ScreenSource.Color => new ColorElement { ColorHex = s.ColorHex ?? "#F2B705" },
                ScreenSource.Text2 => new TextElement
                {
                    Text = s.Text ?? string.Empty,
                    FontSize = s.FontSize,
                    ForegroundHex = s.ForegroundHex ?? "#FFFFFF",
                },
                _ => null,
            };
            if (el == null)
                return null;

            el.X = s.X * RemoteScreenWidth;
            el.Y = s.Y * RemoteScreenHeight;
            el.Width = Math.Max(1, s.Width * RemoteScreenWidth);
            el.Height = Math.Max(1, s.Height * RemoteScreenHeight);
            el.ZIndex = s.ZIndex;
            return el;
        }

        private async void SendUrlToScreen()
        {
            var screen = SelectedScreen;
            if (screen == null || string.IsNullOrWhiteSpace(RemoteUrl))
                return;

            string url = RemoteUrl.Trim();
            var command = new ScreenCommand { Type = ScreenCommand.ShowBrowser, Url = url, Zoom = 1.0 };
            try
            {
                await CommandSender.SendAsync(screen.IpAddress, screen.ControlPort, command);

                // Miniatura: um navegador em tela cheia.
                screen.Layout.Clear();
                screen.Layout.Add(new BrowserElement
                {
                    Url = url, X = 0, Y = 0, Width = RemoteScreenWidth, Height = RemoteScreenHeight,
                });
                StatusMessage = $"✓ Enviado para {screen.Name}: {url}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Falha ao enviar para {screen.Name}: {ex.Message}";
            }
        }

        /// <summary>
        /// Envia a parede atual (fontes navegador/cor/texto) como layout para a
        /// tela selecionada, com posições normalizadas ao tamanho da tela.
        /// </summary>
        private async void SendLayoutToScreen()
        {
            var screen = SelectedScreen;
            if (screen == null || Elements.Count == 0 || WallWidth <= 0 || WallHeight <= 0)
                return;

            var sources = new List<ScreenSource>();
            foreach (var element in Elements.OrderBy(e => e.ZIndex))
            {
                var source = new ScreenSource
                {
                    ZIndex = element.ZIndex,
                    X = (element.X + WallOffsetX) / WallWidth,
                    Y = (element.Y + WallOffsetY) / WallHeight,
                    Width = element.Width / WallWidth,
                    Height = element.Height / WallHeight,
                };

                switch (element)
                {
                    case BrowserElement b:
                        source.Kind = ScreenSource.Browser;
                        source.Url = b.Url;
                        source.Zoom = b.ZoomFactor;
                        source.Overlay = b.IsOverlay;
                        break;
                    case ColorElement c:
                        source.Kind = ScreenSource.Color;
                        source.ColorHex = c.ColorHex;
                        break;
                    case TextElement t:
                        source.Kind = ScreenSource.Text2;
                        source.Text = t.Text;
                        source.FontSize = t.FontSize;
                        source.ForegroundHex = t.ForegroundHex;
                        break;
                    default:
                        continue; // câmera/imagem/aplicativo ainda não vão para a rede
                }

                sources.Add(source);
            }

            if (sources.Count == 0)
            {
                StatusMessage = "Nenhuma fonte compatível com a tela remota (use Navegador, Cor ou Texto).";
                return;
            }

            try
            {
                await CommandSender.SendAsync(screen.IpAddress, screen.ControlPort,
                    new ScreenCommand { Type = ScreenCommand.ShowLayout, Sources = sources });

                // Miniatura: o layout atual.
                screen.Layout.Clear();
                foreach (var el in Elements)
                    screen.Layout.Add(el.Clone());

                StatusMessage = $"✓ Layout enviado para {screen.Name} ({sources.Count} fonte(s)).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Falha ao enviar layout para {screen.Name}: {ex.Message}";
            }
        }

        private async void ClearScreen()
        {
            var screen = SelectedScreen;
            if (screen == null)
                return;

            try
            {
                await CommandSender.SendAsync(screen.IpAddress, screen.ControlPort, new ScreenCommand { Type = ScreenCommand.Clear });
                screen.Layout.Clear();
                StatusMessage = $"Tela limpa: {screen.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Falha ao limpar {screen.Name}: {ex.Message}";
            }
        }

        /// <summary>
        /// Manda o terminal reiniciar — ao reabrir, o preload busca e instala a versão
        /// nova no GitHub. Resolve o caso do terminal 24/7 que não pega atualização
        /// (só verifica ao iniciar), sem precisar ir até o mini-PC.
        /// </summary>
        private async void RestartScreen()
        {
            var screen = SelectedScreen;
            if (screen == null)
                return;

            try
            {
                await CommandSender.SendAsync(screen.IpAddress, screen.ControlPort,
                    new ScreenCommand { Type = ScreenCommand.Restart });
                StatusMessage = $"Reiniciando {screen.Name}… (atualiza ao reabrir)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Falha ao reiniciar {screen.Name}: {ex.Message}";
            }
        }

        /// <summary>
        /// Liga/desliga o overlay de vídeo por hardware no terminal selecionado (o terminal
        /// inverte a preferência e reinicia para aplicar). Overlay LIGADO alivia a GPU
        /// (ajuda quando há dashboard pesado disputando), mas pode deixar o vídeo preto em
        /// algumas placas/TVs — por isso é alternável: se ficar preto, clique de novo.
        /// </summary>
        private async void ToggleOverlay()
        {
            var screen = SelectedScreen;
            if (screen == null)
                return;

            try
            {
                await CommandSender.SendAsync(screen.IpAddress, screen.ControlPort,
                    new ScreenCommand { Type = ScreenCommand.ToggleOverlay });
                StatusMessage = $"Alternando overlay de vídeo (HW) em {screen.Name}… (reinicia). Se ficar preto, clique de novo.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Falha ao alternar overlay em {screen.Name}: {ex.Message}";
            }
        }

        /// <summary>
        /// Define a escala DPI atual (obtida da janela após a inicialização) e
        /// recalcula a geometria da parede. Em 100% de escala, 1 DIP = 1 pixel.
        /// </summary>
        public void SetDpiScale(double dpiScale)
        {
            _dpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
            RebuildWallGeometry();
        }

        private void RefreshMonitors()
        {
            Monitors.Clear();

            foreach (var monitor in _monitorService.GetAllMonitors())
            {
                Monitors.Add(monitor);
            }

            MonitorCount = Monitors.Count;
            StatusMessage = $"{MonitorCount} monitor(es) detectado(s)";

            RebuildWallGeometry();
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Recalcula os limites da parede virtual (união de todos os monitores)
        /// e o contorno de cada monitor para a pré-visualização. Tudo em DIP.
        /// </summary>
        private void RebuildWallGeometry()
        {
            // A pré-visualização é SEMPRE a tela de um terminal (16:9). Os
            // monitores locais do controlador não são usados como saída.
            PreviewMonitors.Clear();
            WallOffsetX = WallOffsetY = 0;
            WallWidth = RemoteScreenWidth;
            WallHeight = RemoteScreenHeight;
            PreviewMonitors.Add(new PreviewMonitor
            {
                Left = 0,
                Top = 0,
                Width = RemoteScreenWidth,
                Height = RemoteScreenHeight,
                Label = SelectedScreen?.Name ?? string.Empty,
                IsPrimary = false,
            });
        }

        private void StartWall()
        {
            if (Monitors.Count == 0)
            {
                StatusMessage = "Nenhum monitor disponível para projetar a parede.";
                return;
            }

            _displayService.Start(Monitors, Elements, _dpiScale);
            IsWallRunning = _displayService.IsRunning;
            StatusMessage = $"Parede projetada em {Monitors.Count} monitor(es). Pressione Esc em uma saída para fechá-la.";
            CommandManager.InvalidateRequerySuggested();
        }

        private void StopWall()
        {
            _displayService.Stop();
            IsWallRunning = _displayService.IsRunning;
            StatusMessage = "Parede encerrada.";
            CommandManager.InvalidateRequerySuggested();
        }

        private void AddColor()
        {
            var element = new ColorElement
            {
                Width = 480,
                Height = 270,
                ColorHex = "#3B82F6",
            };
            AddElement(element);
        }

        private void AddText()
        {
            var element = new TextElement
            {
                Width = 600,
                Height = 120,
                Text = "Novo texto",
                FontSize = 64,
            };
            AddElement(element);
        }

        private void AddImage()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Selecionar imagem",
                Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos os arquivos|*.*",
            };

            if (dialog.ShowDialog() != true)
                return;

            var element = new ImageElement
            {
                Width = 480,
                Height = 270,
                ImagePath = dialog.FileName,
            };
            AddElement(element);
        }

        private void AddBrowser()
        {
            var element = new BrowserElement
            {
                Width = 960,
                Height = 540,
                Url = "https://www.google.com",
            };
            AddElement(element);
        }

        private void AddWindowCapture()
        {
            if (!_captureService.IsSupported)
            {
                StatusMessage = "Captura de janela não é suportada neste Windows.";
                return;
            }

            var picked = _windowPicker.PickWindow();
            if (picked == null)
                return;

            var element = new WindowCaptureElement
            {
                Width = 960,
                Height = 540,
                WindowTitle = picked.Title,
                WindowHandle = picked.Handle.ToInt64(),
            };
            AddElement(element);

            try
            {
                _captureService.Start(element);
                StatusMessage = $"Espelhando: {picked.Title}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Falha ao espelhar a janela: {ex.Message}";
            }
        }

        private void AddCamera()
        {
            var element = new CameraElement
            {
                Width = 960,
                Height = 540,
                StreamUrl = "rtsp://",
            };
            AddElement(element);
        }

        // ===================== Layouts (Assistente Visual) =====================

        private void RefreshLayouts()
        {
            string? previous = SelectedLayoutName;
            Layouts.Clear();
            foreach (var name in _layoutService.List())
                Layouts.Add(name);

            SetSelectedLayoutSilent(Layouts.Contains(previous ?? string.Empty) ? previous : Layouts.FirstOrDefault());
        }

        private void SaveLayout()
        {
            string name = NewLayoutName.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            _layoutService.Save(name, Elements);
            RefreshLayouts();
            SetSelectedLayoutSilent(name);
            StatusMessage = $"Layout salvo: {name}";
        }

        private void LoadLayout()
        {
            if (!string.IsNullOrWhiteSpace(SelectedLayoutName))
                LoadLayoutByName(SelectedLayoutName, announce: true);
        }

        private void DeleteLayout()
        {
            if (string.IsNullOrWhiteSpace(SelectedLayoutName))
                return;

            string name = SelectedLayoutName;
            _layoutService.Delete(name);

            // Se era o layout principal, limpa a referência.
            if (string.Equals(MainLayoutName, name, StringComparison.CurrentCultureIgnoreCase))
                PersistMainLayout(null);

            RefreshLayouts();
            StatusMessage = $"Layout excluído: {name}";
        }

        /// <summary>Define o layout selecionado como principal (carregado ao abrir).</summary>
        private void SetMainLayout()
        {
            if (string.IsNullOrWhiteSpace(SelectedLayoutName))
                return;

            PersistMainLayout(SelectedLayoutName);
            StatusMessage = $"Layout principal definido: {SelectedLayoutName}";
        }

        private void PersistMainLayout(string? name)
        {
            MainLayoutName = name;
            _settingsService.Save(new AppSettings { MainLayoutName = name });
        }

        /// <summary>Carrega o layout na posição informada (atalhos Ctrl+1..9).</summary>
        private void LoadLayoutByIndex(string? indexText)
        {
            if (!int.TryParse(indexText, out int index) || index < 0 || index >= Layouts.Count)
                return;

            SetSelectedLayoutSilent(Layouts[index]);
            LoadLayoutByName(Layouts[index], announce: true);
        }

        /// <summary>
        /// Fail-safe: ao abrir, carrega o layout principal se houver. Chamado
        /// pela janela após a inicialização (geometria/DPI prontos).
        /// </summary>
        public void LoadMainLayoutIfSet()
        {
            if (string.IsNullOrWhiteSpace(MainLayoutName))
                return;

            var loaded = _layoutService.Load(MainLayoutName);
            if (loaded == null)
                return;

            ApplyLayout(loaded);
            SetSelectedLayoutSilent(MainLayoutName);
            StatusMessage = $"Layout principal carregado: {MainLayoutName}";
        }

        /// <summary>
        /// Substitui as fontes atuais pelas do layout carregado, reconectando a
        /// captura das fontes de aplicativo cuja janela ainda esteja aberta.
        /// </summary>
        private void ApplyLayout(IReadOnlyList<WallElement> elements)
        {
            _captureService.StopAll();
            Elements.Clear();
            _elementCounter = 0;

            foreach (var element in elements)
            {
                Elements.Add(element);
                _elementCounter++;

                if (element is WindowCaptureElement capture)
                    TryReconnectCapture(capture);
            }

            SelectedElement = Elements.LastOrDefault();
            CommandManager.InvalidateRequerySuggested();
        }

        // ===================== Biblioteca de fontes (favoritos) =====================

        private void LoadFavorites()
        {
            Favorites.Clear();
            foreach (var fav in _favoritesService.Load()
                         .OrderBy(f => f.Category, StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Favorites.Add(fav);
            }
        }

        private void PersistFavorites()
        {
            _favoritesService.Save(Favorites);
            // Reordena mantendo a seleção.
            var selected = SelectedFavorite;
            LoadFavorites();
            SelectedFavorite = Favorites.Contains(selected!) ? selected : null;
        }

        private static bool CanFavorite(WallElement? element) =>
            element is BrowserElement or CameraElement or ImageElement or WindowCaptureElement;

        private void AddFavorite()
        {
            if (string.IsNullOrWhiteSpace(NewFavoriteName) || string.IsNullOrWhiteSpace(NewFavoritePayload))
                return;

            Favorites.Add(new SourceFavorite
            {
                Category = NewFavoriteCategory,
                Name = NewFavoriteName.Trim(),
                Payload = NewFavoritePayload.Trim(),
            });
            PersistFavorites();

            StatusMessage = $"Favorito adicionado: {NewFavoriteName}";
            NewFavoriteName = string.Empty;
            NewFavoritePayload = string.Empty;
        }

        private void BrowseFavoritePayload()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Selecionar imagem",
                Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos os arquivos|*.*",
            };

            if (dialog.ShowDialog() != true)
                return;

            NewFavoriteCategory = "Imagem";
            NewFavoritePayload = dialog.FileName;
            if (string.IsNullOrWhiteSpace(NewFavoriteName))
                NewFavoriteName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        }

        private void SaveSelectedAsFavorite()
        {
            if (!CanFavorite(SelectedElement))
                return;

            var (category, payload) = SelectedElement switch
            {
                BrowserElement b => ("Web", b.Url),
                CameraElement c => ("Câmera", c.StreamUrl),
                ImageElement i => ("Imagem", i.ImagePath),
                WindowCaptureElement w => ("Aplicativo", w.WindowTitle),
                _ => (string.Empty, string.Empty),
            };

            Favorites.Add(new SourceFavorite
            {
                Category = category,
                Name = SelectedElement!.Name,
                Payload = payload,
                // Preserva o tipo "live/PiP" para re-adicionar como overlay (e não como
                // navegador de célula inteira, que sobrecarrega o terminal).
                Overlay = SelectedElement is BrowserElement br && br.IsOverlay,
            });
            PersistFavorites();
            StatusMessage = $"Fonte salva na biblioteca: {SelectedElement.Name}";
        }

        private void AddFavoriteToWall()
        {
            if (SelectedFavorite == null)
                return;

            var fav = SelectedFavorite;

            // Live/PiP: re-adiciona como overlay (miniatura sempre-no-topo), igual ao
            // botão "Live YouTube". Evita virar um navegador de célula inteira (pesado).
            // Detecta também pela URL do player (favoritos salvos antes deste campo).
            bool isLive = fav.Category == "Web" &&
                (fav.Overlay ||
                 (fav.Payload?.IndexOf("cpe.live", StringComparison.OrdinalIgnoreCase) >= 0));
            if (isLive)
            {
                var live = AddLivePip(fav.Payload, null);
                if (!string.IsNullOrWhiteSpace(fav.Name))
                    live.Name = fav.Name;
                StatusMessage = $"Adicionado da biblioteca: {fav.Name}";
                return;
            }

            WallElement element = fav.Category switch
            {
                "Imagem" => new ImageElement { Width = 480, Height = 270, ImagePath = fav.Payload },
                "Aplicativo" => new WindowCaptureElement { Width = 960, Height = 540, WindowTitle = fav.Payload },
                _ => new BrowserElement { Width = 960, Height = 540, Url = fav.Payload },
            };

            AddElement(element);
            if (!string.IsNullOrWhiteSpace(fav.Name))
                element.Name = fav.Name;

            if (element is WindowCaptureElement capture)
                TryReconnectCapture(capture);

            StatusMessage = $"Adicionado da biblioteca: {fav.Name}";
        }

        private void RemoveFavorite()
        {
            if (SelectedFavorite == null)
                return;

            string name = SelectedFavorite.Name;
            Favorites.Remove(SelectedFavorite);
            SelectedFavorite = null;
            _favoritesService.Save(Favorites);
            StatusMessage = $"Favorito removido: {name}";
        }

        // ===================== Agendador =====================

        private void LoadSchedules()
        {
            Schedules.Clear();
            foreach (var entry in _scheduleService.Load()
                         .OrderBy(s => s.Hour).ThenBy(s => s.Minute))
            {
                Schedules.Add(entry);
            }
        }

        /// <summary>Persiste a lista de agendamentos (chamado ao adicionar/remover/editar).</summary>
        public void SaveSchedules() => _scheduleService.Save(Schedules);

        private void AddSchedule()
        {
            if (string.IsNullOrWhiteSpace(NewScheduleLayout) || NewScheduleScreen == null)
                return;

            var days = new List<int>();
            if (DaySun) days.Add(0);
            if (DayMon) days.Add(1);
            if (DayTue) days.Add(2);
            if (DayWed) days.Add(3);
            if (DayThu) days.Add(4);
            if (DayFri) days.Add(5);
            if (DaySat) days.Add(6);

            Schedules.Add(new ScheduleEntry
            {
                Hour = NewScheduleHour,
                Minute = NewScheduleMinute,
                Days = days,
                LayoutName = NewScheduleLayout!,
                ScreenId = NewScheduleScreen.Id,
                ScreenName = NewScheduleScreen.Name,
                Enabled = true,
            });

            // Mantém ordenado por horário.
            var ordered = Schedules.OrderBy(s => s.Hour).ThenBy(s => s.Minute).ToList();
            Schedules.Clear();
            foreach (var s in ordered) Schedules.Add(s);

            SaveSchedules();
            StatusMessage = $"Agendamento: {NewScheduleHour:00}:{NewScheduleMinute:00} → {NewScheduleLayout} em {NewScheduleScreen.Name}";
        }

        private void RemoveSchedule()
        {
            if (SelectedSchedule == null)
                return;

            Schedules.Remove(SelectedSchedule);
            SelectedSchedule = null;
            SaveSchedules();
            StatusMessage = "Agendamento removido.";
        }

        /// <summary>Verifica, a cada tique, se algum agendamento deve disparar agora.</summary>
        private void ToggleScheduler()
        {
            SchedulerPaused = !SchedulerPaused;
            StatusMessage = SchedulerPaused
                ? "Agendador pausado (os agendamentos por horário não vão disparar)."
                : "Agendador retomado.";
        }

        private void CheckSchedules()
        {
            if (SchedulerPaused)
                return;

            var now = DateTime.Now;

            foreach (var entry in Schedules)
            {
                if (!entry.Enabled)
                    continue;
                if (entry.Hour != now.Hour || entry.Minute != now.Minute)
                    continue;
                if (entry.Days is { Count: > 0 } && !entry.Days.Contains((int)now.DayOfWeek))
                    continue;
                // Evita disparar mais de uma vez no mesmo minuto.
                if (entry.LastFired is DateTime lf
                    && lf.Date == now.Date && lf.Hour == now.Hour && lf.Minute == now.Minute)
                    continue;

                entry.LastFired = now;
                FireSchedule(entry);
            }
        }

        private void FireSchedule(ScheduleEntry entry)
        {
            var loaded = _layoutService.Load(entry.LayoutName);
            if (loaded == null)
            {
                StatusMessage = $"Agendamento {entry.TimeText}: layout '{entry.LayoutName}' não encontrado.";
                return;
            }

            // Mira a tela (terminal) do agendamento e projeta o layout nela.
            var screen = RemoteScreens.FirstOrDefault(s => s.Id == entry.ScreenId);
            if (screen == null)
            {
                StatusMessage = $"Agendamento {entry.TimeText}: tela '{entry.ScreenText}' não está na rede.";
                return;
            }

            SelectedScreen = screen;
            ApplyLayout(loaded);
            SetSelectedLayoutSilent(entry.LayoutName);

            if (SendLayoutToScreenCommand.CanExecute(null))
                SendLayoutToScreenCommand.Execute(null);

            StatusMessage = $"Agendamento {entry.TimeText}: '{entry.LayoutName}' projetado em {screen.Name}.";
        }

        // ---------------- Rotação automática (player) ----------------

        private void AddRotationLayout()
        {
            if (string.IsNullOrWhiteSpace(RotationPickLayout))
                return;
            RotationLayouts.Add(RotationPickLayout!);
            CommandManager.InvalidateRequerySuggested();
        }

        private void RemoveRotationLayout()
        {
            if (SelectedRotationLayout != null)
                RotationLayouts.Remove(SelectedRotationLayout);
            SelectedRotationLayout = null;
        }

        private void StartRotation()
        {
            if (IsRotating || RotationLayouts.Count == 0 || RotationScreen == null || RotationMinutes < 1)
                return;

            IsRotating = true;
            _rotationIndex = 0;
            ProjectRotationCurrent();                       // mostra o 1º layout já
            _rotationTimer.Interval = TimeSpan.FromMinutes(RotationMinutes);
            _rotationTimer.Start();
        }

        private void StopRotation()
        {
            _rotationTimer.Stop();
            IsRotating = false;
            RotationStatus = "Parado";
        }

        private void RotationTick()
        {
            if (RotationLayouts.Count == 0)
            {
                StopRotation();
                return;
            }
            _rotationIndex = (_rotationIndex + 1) % RotationLayouts.Count;
            ProjectRotationCurrent();
        }

        /// <summary>Projeta o layout atual da sequência na tela da rotação.</summary>
        private void ProjectRotationCurrent()
        {
            if (_rotationIndex < 0 || _rotationIndex >= RotationLayouts.Count || RotationScreen == null)
                return;

            string name = RotationLayouts[_rotationIndex];
            var screen = RemoteScreens.FirstOrDefault(s => s.Id == RotationScreen.Id);
            if (screen == null)
            {
                RotationStatus = $"Tela '{RotationScreen.Name}' offline — aguardando…";
                return;
            }

            var loaded = _layoutService.Load(name);
            if (loaded == null)
            {
                RotationStatus = $"Layout '{name}' não encontrado.";
                return;
            }

            SelectedScreen = screen;
            ApplyLayout(loaded);
            SetSelectedLayoutSilent(name);
            if (SendLayoutToScreenCommand.CanExecute(null))
                SendLayoutToScreenCommand.Execute(null);

            RotationStatus = $"Rodando — {name} ({_rotationIndex + 1}/{RotationLayouts.Count}) · troca a cada {RotationMinutes} min";
        }

        /// <summary>Tenta religar a captura de uma fonte de aplicativo pelo título da janela.</summary>
        private void TryReconnectCapture(WindowCaptureElement capture)
        {
            var match = _windowPicker.FindByTitle(capture.WindowTitle);
            if (match == null)
                return;

            capture.WindowHandle = match.Handle.ToInt64();
            try
            {
                _captureService.Start(capture);
            }
            catch
            {
                // Janela encontrada mas indisponível para captura; segue como marcador.
            }
        }

        /// <summary>
        /// Adiciona um elemento centralizado na parede, à frente dos demais,
        /// e o seleciona.
        /// </summary>
        private void AddElement(WallElement element)
        {
            _elementCounter++;
            element.Name = $"{element.Kind} {_elementCounter}";
            element.ZIndex = NextZIndex();

            // Centraliza na parede virtual (coordenadas absolutas em DIP).
            double centerX = -WallOffsetX + WallWidth / 2;
            double centerY = -WallOffsetY + WallHeight / 2;
            element.X = centerX - element.Width / 2;
            element.Y = centerY - element.Height / 2;

            Elements.Add(element);
            SelectedElement = element;
            StatusMessage = $"Elemento adicionado: {element.Name}";
            CommandManager.InvalidateRequerySuggested();
        }

        private int NextZIndex() => Elements.Count == 0 ? 0 : Elements.Max(e => e.ZIndex) + 1;

        /// <summary>
        /// Adiciona uma live do YouTube como miniatura (PiP) no canto superior direito,
        /// sempre por cima das demais fontes. O endereço já deve vir na forma "embed".
        /// </summary>
        public BrowserElement AddLivePip(string embedUrl, System.Windows.Media.ImageSource? preview)
        {
            _elementCounter++;
            double ww = WallWidth > 0 ? WallWidth : RemoteScreenWidth;
            double wh = WallHeight > 0 ? WallHeight : RemoteScreenHeight;
            const double margin = 24;

            var element = new BrowserElement
            {
                Url = embedUrl,
                PreviewImage = preview,
                Width = 480,
                Height = 270,
                ZoomFactor = 1.0,
                IsOverlay = true,
            };
            element.Name = $"Live {_elementCounter}";
            element.ZIndex = NextZIndex();
            // Canto superior direito da parede virtual, com uma margem.
            element.X = -WallOffsetX + ww - element.Width - margin;
            element.Y = -WallOffsetY + margin;

            Elements.Add(element);
            SelectedElement = element;
            StatusMessage = $"Live adicionada: {element.Name}";
            CommandManager.InvalidateRequerySuggested();
            return element;
        }

        private void RemoveSelected()
        {
            if (SelectedElement == null)
                return;

            var removed = SelectedElement;

            // Libera os recursos de captura, se for uma fonte de aplicativo.
            if (removed is WindowCaptureElement capture)
                _captureService.Stop(capture);

            Elements.Remove(removed);
            SelectedElement = Elements.Count > 0 ? Elements[^1] : null;
            StatusMessage = $"Elemento removido: {removed.Name}";
            CommandManager.InvalidateRequerySuggested();
        }

        private void BringToFront()
        {
            if (SelectedElement == null)
                return;

            SelectedElement.ZIndex = NextZIndex();
        }

        private void SendToBack()
        {
            if (SelectedElement == null || Elements.Count == 0)
                return;

            SelectedElement.ZIndex = Elements.Min(e => e.ZIndex) - 1;
        }

        private void OnMonitorsChanged(object? sender, EventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(RefreshMonitors);
        }

        public void Dispose()
        {
            _scheduleTimer.Stop();
            _thumbnailTimer.Stop();
            _rotationTimer.Stop();
            _discovery.ViewersChanged -= OnRemoteViewersChanged;
            _discovery.Dispose();
            _controllerBeacon?.Dispose();
            _updateServer?.Dispose();
            _displayService.Stop();
            _captureService.StopAll();
            _monitorService.MonitorsChanged -= OnMonitorsChanged;

            if (_monitorService is IDisposable disposable)
            {
                disposable.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;

        public void Execute(object? parameter) => _execute((T?)parameter);
    }
}
