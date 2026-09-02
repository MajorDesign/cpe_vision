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
        private ScheduleServer? _scheduleServer;
        private TerminalScheduler? _scheduler;
        private System.Windows.Threading.DispatcherTimer? _layoutSaveTimer;

        // Páginas que saíram do layout mas continuam VIVAS (ocultas), por URL projetada.
        // É o que permite a rotação de layouts sem recarregar nem deslogar as páginas.
        private readonly Dictionary<string, WebView2> _parked = new(StringComparer.Ordinal);
        private readonly List<string> _parkOrder = new();   // ordem de uso, p/ descartar as antigas

        /// <summary>
        /// Quantas páginas ficam vivas fora do ar. Precisa cobrir a soma das células dos
        /// layouts que se alternam (ex.: 4 câmeras + 4 do sistema): abaixo disso, a página
        /// do sistema seria descartada no meio da rotação e voltaria pedindo login.
        /// </summary>
        private const int MaxParkedPages = 8;

        // Cortina da troca de layout: esconde o carregamento até a parede ficar pronta.
        private CurtainWindow? _curtain;
        private readonly List<Task> _pendingReady = new();
        private static readonly TimeSpan CurtainMaxWait = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan VideoReadyWait = TimeSpan.FromSeconds(12);

        // Servidores que não conseguiram subir (porta ocupada) e seguem sendo tentados.
        private readonly List<(string Nome, Action Iniciar)> _pendingServers = new();
        private System.Windows.Threading.DispatcherTimer? _serverRetryTimer;

        // Auto-update com o terminal aberto (quiosque 24/7 raramente reinicia).
        private TerminalUpdater? _updater;
        private System.Windows.Threading.DispatcherTimer? _updateTimer;
        private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromMinutes(30);

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
                HardwareOverlay = TerminalSettings.HardwareVideoOverlay,
                Version = GitHubUpdater.CurrentVersion().ToString(),
            };
            _beacon = new UdpBeacon(info);
            _beacon.Start();

            _commandServer = new CommandServer(ScreenCommand.DefaultPort);
            _commandServer.CommandReceived += cmd => Dispatcher.BeginInvoke(() => ApplyCommand(cmd));
            StartServer("comandos", () => _commandServer.Start());

            // Canal persistente para o "controle ao vivo" (mouse/rolagem/teclado).
            _liveInputServer = new LiveInputServer();
            _liveInputServer.InputReceived += ev => Dispatcher.BeginInvoke(() => InjectInput(ev));
            StartServer("controle ao vivo", () => _liveInputServer.Start());

            // Serve a miniatura ao vivo (foto da própria tela) quando o controlador pede.
            _thumbnailServer = new ThumbnailServer(CaptureScreenJpeg);
            StartServer("miniatura", () => _thumbnailServer.Start());

            // Informa o estado atual de cada célula (página + rolagem) para o controle ao
            // vivo reabrir continuando de onde estava.
            _liveStateServer = new LiveStateServer(GetCellStateAsync);
            StartServer("estado da célula", () => _liveStateServer.Start());

            // Transmite os frames de uma célula para o controle ao vivo (espelho exato da TV).
            _liveViewServer = new LiveViewServer(CaptureCellJpeg);
            StartServer("espelho de vídeo", () => _liveViewServer.Start());

            // Responde o layout atual (fontes + URLs ao vivo) para o controlador reconstruir
            // a parede ao reabrir — o terminal é a fonte da verdade.
            _layoutQueryServer = new LayoutQueryServer(GetCurrentLayoutAsync);
            StartServer("layout atual", () => _layoutQueryServer.Start());

            // A PROGRAMAÇÃO (horários e rotação) é executada pela própria tela: ela
            // continua trocando com o controlador fechado, sobrevive a reinício e
            // qualquer controlador pode lê-la de volta.
            _scheduler = new TerminalScheduler(sources => ApplyLayout(sources));
            _scheduleServer = new ScheduleServer(
                () => _scheduler!.Current,
                nova => Dispatcher.BeginInvoke(() => _scheduler!.Replace(nova)));
            StartServer("programação", () => _scheduleServer.Start());

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

            // Auto-update 24/7: o terminal fica ligado por semanas, então não basta
            // verificar no pré-load. A cada meia hora ele consulta o central (e o
            // GitHub como reserva) e se atualiza sozinho. O primeiro ciclo sai numa
            // hora aleatória para as 8 telas não reiniciarem todas juntas.
            _updater = new TerminalUpdater();
            // Instalador baixado numa execução anterior e ainda não aplicado: deixa o
            // vigia armado para reabrir a tela quando a tarefa encerrar este processo.
            _updater.ArmReopenIfUpdatePending();
            _updateTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMinutes(Random.Shared.Next(3, 31)) };
            _updateTimer.Tick += async (_, _) =>
            {
                _updateTimer!.Interval = UpdateCheckInterval;
                await _updater!.CheckAndUpdateAsync();
            };
            _updateTimer.Start();
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

                case ScreenCommand.Update:
                    // O central percebeu que esta tela está atrasada.
                    _ = _updater?.CheckAndUpdateAsync(forcar: true);
                    break;

                case ScreenCommand.ToggleOverlay:
                    // Inverte a preferência do overlay de vídeo e reinicia para aplicar.
                    TerminalSettings.SetHardwareVideoOverlay(!TerminalSettings.HardwareVideoOverlay);
                    RestartSelf();
                    break;
            }
        }

        /// <summary>Reabre o terminal: a nova instância passa pelo preload e busca a versão
        /// nova (permite atualizar terminais 24/7 pelo controlador).</summary>
        private void RestartSelf()
        {
            SaveCurrentLayout(); // garante o estado mais recente (com login/URLs ao vivo)
            try
            {
                var exe = Environment.ProcessPath ??
                          System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
                RelaunchAfterExit(exe);
            }
            catch { /* não conseguiu relançar: ainda assim encerra */ }
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Reabre o terminal SÓ DEPOIS que este processo terminar.
        ///
        /// Iniciar a instância nova antes de encerrar a atual (como era feito) fazia a
        /// nova encontrar as SEIS portas ainda ocupadas pela antiga. Como cada servidor
        /// falhava em silêncio, ela subia sem escutar nada: a tela continuava aparecendo
        /// na rede — o anúncio é só envio, não depende de porta de escuta — mas não
        /// respondia a comando, miniatura nem consulta de layout, e só um reinício
        /// manual no mini-PC resolvia.
        /// </summary>
        private static void RelaunchAfterExit(string exe)
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CPE Tecnologia", "VideoWall");
            Directory.CreateDirectory(dir);

            string script = System.IO.Path.Combine(dir, "reiniciar.cmd");
            int pid = Environment.ProcessId;

            string text =
                "@echo off\r\n" +
                ":wait\r\n" +
                $"tasklist /fi \"PID eq {pid}\" | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                // Folga para o Windows liberar de fato as portas do processo encerrado.
                "timeout /t 3 /nobreak >nul\r\n" +
                $"start \"\" \"{exe}\"\r\n" +
                "del \"%~f0\"\r\n";

            File.WriteAllText(script, text);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{script}\"",
                WorkingDirectory = dir,
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            });
        }

        /// <summary>
        /// Sobe um servidor do terminal e, se a porta estiver ocupada, INSISTE em vez de
        /// desistir calado. Um terminal sem servidores é o pior estado possível: aparece
        /// verde na rede e não obedece a nada. A porta costuma ser liberada em segundos
        /// (instância anterior encerrando), então a tela se recupera sozinha.
        /// </summary>
        private void StartServer(string nome, Action iniciar)
        {
            try
            {
                iniciar();
            }
            catch
            {
                _pendingServers.Add((nome, iniciar));
                EnsureServerRetryTimer();
            }
        }

        private void EnsureServerRetryTimer()
        {
            if (_serverRetryTimer != null)
                return;

            _serverRetryTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(5) };
            _serverRetryTimer.Tick += (_, _) =>
            {
                for (int i = _pendingServers.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _pendingServers[i].Iniciar();
                        _pendingServers.RemoveAt(i);
                    }
                    catch { /* ainda ocupada: tenta no próximo ciclo */ }
                }

                if (_pendingServers.Count == 0)
                {
                    _serverRetryTimer?.Stop();
                    _serverRetryTimer = null;
                }
            };
            _serverRetryTimer.Start();
        }

        // ----------------------------------------------------------------------------
        // Renderização incremental do layout
        // ----------------------------------------------------------------------------

        private void ApplyLayout(IReadOnlyList<ScreenSource> sources)
        {
            // Alguma célula vai ter de CARREGAR página nova? Então baixa a cortina antes
            // de mexer na parede: o carregamento (vídeo quebrado, cada quadro entrando em
            // tela cheia por vez) acontece atrás dela, e a parede só reaparece pronta.
            // Quando tudo vem do estacionamento — caso normal a partir da 2ª volta da
            // rotação — não há o que preparar e a troca é instantânea, sem cortina.
            bool preparar = NeedsPreparation(sources);
            if (preparar)
                ShowCurtain();

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

            if (preparar)
                _ = RevealWhenReadyAsync();
            else
                HideCurtain();
        }

        /// <summary>
        /// Verdadeiro se alguma célula terá de carregar uma página do zero — ou seja, se
        /// a troca seria visível como "bastidor". Página que continua na mesma vaga, ou
        /// que está estacionada, entra pronta e não exige cortina.
        /// </summary>
        private bool NeedsPreparation(IReadOnlyList<ScreenSource> sources)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                var src = sources[i];
                if (src.Kind != ScreenSource.Browser || string.IsNullOrEmpty(src.Url))
                    continue;

                bool mesmaVaga = i < _slotUrls.Count &&
                                 string.Equals(_slotUrls[i], src.Url, StringComparison.Ordinal);
                if (!mesmaVaga && !_parked.ContainsKey(ParkKey(i, src.Url)))
                    return true;
            }
            return false;
        }

        private void ShowCurtain()
        {
            _curtain ??= new CurtainWindow(this);
            _curtain.Cover(this);
        }

        private void HideCurtain()
        {
            try { _curtain?.Hide(); } catch { }
        }

        /// <summary>
        /// Espera as páginas novas ficarem apresentáveis e então levanta a cortina.
        /// Sempre há um teto de tempo: página que não carrega não pode deixar a parede
        /// preta para sempre.
        /// </summary>
        private async Task RevealWhenReadyAsync()
        {
            var esperas = _pendingReady.ToList();
            _pendingReady.Clear();

            // A cortina precisa ficar acima das janelas das lives, que também são topmost.
            _curtain?.BringToTop();

            try
            {
                if (esperas.Count > 0)
                    await Task.WhenAny(Task.WhenAll(esperas), Task.Delay(CurtainMaxWait));
            }
            catch { /* alguma página falhou: revela mesmo assim */ }

            HideCurtain();
        }

        /// <summary>
        /// Conclui quando a página está apresentável: carregada e, sendo vídeo, já em tela
        /// cheia (é o ajuste que o usuário via acontecendo ao vivo).
        /// </summary>
        private static Task PageReadyAsync(WebView2 web, string url)
        {
            var tcs = new TaskCompletionSource();
            bool ehVideo = url.Contains("cpe.live", StringComparison.OrdinalIgnoreCase) ||
                           url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase);

            void Ligar(CoreWebView2 core)
            {
                core.NavigationCompleted += async (_, _) =>
                {
                    if (tcs.Task.IsCompleted)
                        return;

                    if (ehVideo)
                    {
                        // Espera o player assumir a tela cheia (o "ajuste quebrado" que
                        // aparecia na parede). Se não conseguir, o teto libera.
                        var limite = DateTime.UtcNow + VideoReadyWait;
                        while (DateTime.UtcNow < limite)
                        {
                            try { if (core.ContainsFullScreenElement) break; } catch { break; }
                            await Task.Delay(250);
                        }
                    }

                    await Task.Delay(300); // respiro para o primeiro quadro estabilizar
                    tcs.TrySetResult();
                };
            }

            if (web.CoreWebView2 is { } pronto)
                Ligar(pronto);
            else
                web.CoreWebView2InitializationCompleted += (_, e) =>
                {
                    if (e.IsSuccess && web.CoreWebView2 is { } core)
                        Ligar(core);
                    else
                        tcs.TrySetResult();
                };

            return tcs.Task;
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
                // Mesma página que já está na vaga: não mexe em nada.
                if (existing is WebView2 web && string.Equals(_slotUrls[i], src.Url, StringComparison.Ordinal))
                {
                    web.Tag = src.Zoom; // zoom relativo desejado pelo usuário
                    ApplyCanonicalZoom(web);
                    return web;
                }

                // A vaga vai exibir OUTRA página. Reaproveita a versão estacionada, se
                // existir — ela volta exatamente como estava (logada, rolada, no mesmo
                // ponto do sistema). SetSlot estaciona a página que estava aqui.
                if (TryUnpark(ParkKey(i, src.Url!), out var estacionada))
                {
                    SetSlot(i, estacionada!);
                    _slotUrls[i] = src.Url;
                    // NÃO redefine o zoom: preserva o que o operador ajustou ao vivo.
                    estacionada!.Tag ??= src.Zoom;
                    ApplyCanonicalZoom(estacionada);
                    return estacionada;
                }

                // Primeira vez nesta página: cria uma nova em vez de renavegar a atual —
                // renavegar destruiria a página que está saindo, que é justamente a que
                // queremos guardar viva para quando o layout voltar.
                var nw = new WebView2 { Tag = src.Zoom };
                // A cortina só sobe quando esta página estiver apresentável.
                _pendingReady.Add(PageReadyAsync(nw, src.Url!));
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
                    // Esconde a interface do YouTube (logo, cabeçalho, título) e mantém a
                    // live tocando, quando a célula cai na página "watch" — o script se
                    // desliga sozinho em qualquer página que não seja do YouTube.
                    try { _ = nw.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(YouTubeLive.KeepPlayingScript); } catch { }
                    try { nw.CoreWebView2.ProcessFailed += (_, e) => OnBrowserProcessFailed(nw, e); } catch { }
                };
                // ...e coloca o player em tela cheia dentro da célula, para não sobrar
                // nada do site em volta do vídeo na TV.
                var fullscreen = new YouTubeFullscreen(nw);
                nw.Unloaded += (_, _) => fullscreen.Dispose();
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

        /// <summary>
        /// Um processo do WebView2 morreu (a aba, a GPU ou o navegador inteiro).
        ///
        /// Sem tratar, a célula simplesmente apaga ou pisca e não fica rastro nenhum —
        /// numa TV sem ninguém por perto isso vira "a tela pisca de vez em quando", sem
        /// como investigar. Aqui a falha vai para o log com o tipo e o motivo, e a
        /// página volta sozinha quando o que morreu foi só a aba.
        /// </summary>
        private void OnBrowserProcessFailed(WebView2 web, CoreWebView2ProcessFailedEventArgs e)
        {
            string alvo = _slots.IndexOf(web) is var idx && idx >= 0 ? $"célula {idx}" : "célula desconhecida";
            ErrorLog.Write(
                $"WebView2 falhou na {alvo}: tipo={e.ProcessFailedKind}, motivo={e.Reason}, " +
                $"saída={e.ExitCode}, processo={e.ProcessDescription}", null);

            // Processo de GPU ou utilitário: o WebView2 se recompõe sozinho (pode piscar).
            // Só a morte da aba exige recarregar para a célula não ficar em branco.
            if (e.ProcessFailedKind != CoreWebView2ProcessFailedKind.RenderProcessExited)
                return;

            try { web.CoreWebView2?.Reload(); }
            catch (Exception ex) { ErrorLog.Write("Falha ao recarregar a célula após queda do navegador", ex); }
        }

        private void EnsureSlotCapacity(int i)
        {
            while (_slots.Count <= i)
            {
                _slots.Add(null);
                _slotUrls.Add(null);
            }
        }

        /// <summary>Coloca um novo elemento na vaga, aposentando o anterior.</summary>
        private void SetSlot(int i, FrameworkElement element)
        {
            RetireSlotElement(i);
            _slots[i] = element;
            if (!Surface.Children.Contains(element))
                Surface.Children.Add(element);
            element.Visibility = Visibility.Visible;
        }

        private void RemoveSlot(int i)
        {
            RetireSlotElement(i);
            CloseOverlay(i);
            _slots.RemoveAt(i);
            _slotUrls.RemoveAt(i);
        }

        /// <summary>
        /// Tira o elemento da vaga: navegadores vão para o ESTACIONAMENTO (ficam vivos,
        /// ocultos); o resto é descartado.
        /// </summary>
        private void RetireSlotElement(int i)
        {
            var old = _slots[i];
            if (old == null)
                return;

            if (old is WebView2 web && !string.IsNullOrEmpty(_slotUrls[i]))
                Park(ParkKey(i, _slotUrls[i]!), web);
            else
                Surface.Children.Remove(old);

            _slots[i] = null;
        }

        /// <summary>
        /// Chave do estacionamento: a VAGA mais a página.
        ///
        /// Só a URL não serve. É comum a parede repetir o mesmo endereço em várias
        /// células — quatro quadros do mesmo sistema, cada um aberto numa parte do
        /// projeto e com seu próprio zoom. Indexando só pela URL, as quatro páginas
        /// disputariam a mesma chave e três seriam descartadas: voltariam do zero,
        /// pedindo login e perdendo o enquadramento.
        /// </summary>
        private static string ParkKey(int slot, string url) => slot + "|" + url;

        /// <summary>
        /// Guarda a página VIVA, apenas oculta, em vez de destruí-la.
        ///
        /// É o que permite alternar layouts (rotação) sem deslogar: destruir o WebView2
        /// leva junto a sessão que o site mantém na aba — em sistemas que guardam o token
        /// em memória/sessionStorage, voltar ao layout caía na tela de login. Oculta, a
        /// página também para de consumir GPU, o que ajuda quando há live na parede.
        /// </summary>
        private void Park(string chave, WebView2 web)
        {
            web.Visibility = Visibility.Collapsed;

            // Oculta, a página continua baixando vídeo (o Chromium não pausa mídia por
            // estar escondida). Numa parede com lives isso seria banda e CPU jogadas
            // fora — pausa aqui e o KeepPlayingScript volta a tocar ao reaparecer.
            RunScript(web, "document.querySelectorAll('video').forEach(function(v){try{v.pause()}catch(e){}})");

            // Já havia página estacionada nesta vaga/endereço: fica com a mais recente.
            if (_parked.TryGetValue(chave, out var anterior) && !ReferenceEquals(anterior, web))
                Discard(anterior);

            _parked[chave] = web;
            _parkOrder.Remove(chave);
            _parkOrder.Add(chave);

            // Teto de memória: cada página viva custa RAM no mini-PC.
            while (_parkOrder.Count > MaxParkedPages)
            {
                string maisAntiga = _parkOrder[0];
                _parkOrder.RemoveAt(0);
                if (_parked.Remove(maisAntiga, out var velha))
                    Discard(velha);
            }
        }

        /// <summary>Recupera a página estacionada daquela vaga/endereço, se existir.</summary>
        private bool TryUnpark(string chave, out WebView2? web)
        {
            web = null;
            if (string.IsNullOrEmpty(chave) || !_parked.Remove(chave, out var achada))
                return false;

            _parkOrder.Remove(chave);
            achada.Visibility = Visibility.Visible;
            RunScript(achada, "document.querySelectorAll('video').forEach(function(v){try{v.play()}catch(e){}})");
            web = achada;
            return true;
        }

        /// <summary>Executa um script na página, se ela já estiver pronta. Nunca lança.</summary>
        private static async void RunScript(WebView2 web, string js)
        {
            try
            {
                if (web.CoreWebView2 is { } core)
                    await core.ExecuteScriptAsync(js);
            }
            catch { /* página descartada / ainda inicializando */ }
        }

        private void Discard(WebView2 web)
        {
            Surface.Children.Remove(web);
            try { web.Dispose(); } catch { }
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
            // "Parar tela" é uma parada deliberada: nada fica vivo em segundo plano
            // consumindo memória do mini-PC (as páginas acima já foram descartadas).
            _parked.Clear();
            _parkOrder.Clear();
            _pendingReady.Clear();
            HideCurtain(); // tela limpa não fica escondida atrás da cortina
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
            _scheduleServer?.Dispose();
            _scheduler?.Dispose();
            _beacon?.Dispose();
            _updateTimer?.Stop();
            _updater?.Dispose();
            _serverRetryTimer?.Stop();
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
