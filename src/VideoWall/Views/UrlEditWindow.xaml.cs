using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace VideoWall.Views
{
    /// <summary>
    /// Diálogo para editar o endereço (URL) de uma fonte navegador, com pré-visualização
    /// ao vivo da página. Ao salvar, captura uma miniatura do conteúdo exibido para que
    /// o controlador mostre a prévia no lugar do ícone genérico.
    /// </summary>
    public partial class UrlEditWindow : Window
    {
        /// <summary>URL confirmada pelo usuário.</summary>
        public string ResultUrl { get; private set; } = string.Empty;

        /// <summary>Miniatura capturada da página (pode ser nula se não foi possível capturar).</summary>
        public ImageSource? ResultPreview { get; private set; }

        private DispatcherTimer? _fullscreenTimer;

        public UrlEditWindow(string currentUrl)
        {
            InitializeComponent();
            UrlBox.Text = string.IsNullOrWhiteSpace(currentUrl) ? "https://" : currentUrl;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            UrlBox.Focus();
            UrlBox.SelectAll();
            try
            {
                // A pasta de dados do WebView2 PRECISA ser gravável. Quando o controlador
                // roda instalado (Arquivos de Programas), a pasta padrão fica num local
                // somente leitura e o navegador trava — por isso usamos LocalAppData.
                // --disable-gpu evita a tela preta do WebView2 em VM/acesso remoto/algumas
                // placas (a pré-visualização renderizava escura). --autoplay-policy deixa
                // a live tocar sozinha. Opções explícitas garantem que valham nesta janela.
                var options = new CoreWebView2EnvironmentOptions(
                    "--autoplay-policy=no-user-gesture-required --disable-gpu");
                var env = await CoreWebView2Environment.CreateAsync(null, UserDataFolder(), options);
                await Web.EnsureCoreWebView2Async(env);

                // Permite que endereços de live do YouTube (player.html via host virtual)
                // sejam exibidos na pré-visualização — sem isso, embed direto dá Erro 153.
                Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VideoWall.Network.YouTubeLive.VirtualHost,
                    VideoWall.Network.YouTubeLive.EnsurePlayerFolder(),
                    CoreWebView2HostResourceAccessKind.Allow);

                // Mantém a live tocando e remove popups quando cai na página do YouTube.
                try { _ = Web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    VideoWall.Network.YouTubeLive.KeepPlayingScript); } catch { }

                // Entra em tela cheia sozinho nas páginas do YouTube (mesmo resultado do
                // terminal): o vídeo preenche o quadro, sem cabeçalho.
                _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _fullscreenTimer.Tick += (_, _) => EnsureFullscreen();
                _fullscreenTimer.Start();

                // Mantém a barra de endereço acompanhando a navegação real (buscas,
                // cliques em links) para que o que é salvo seja a página exibida.
                Web.CoreWebView2.SourceChanged += (_, _) =>
                {
                    if (!UrlBox.IsKeyboardFocused)
                        UrlBox.Text = Web.CoreWebView2.Source;
                };

                Navigate();
            }
            catch
            {
                Hint.Text = "Não foi possível iniciar a pré-visualização (o endereço ainda será salvo).";
            }
        }

        private static string UserDataFolder()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CPE Tecnologia", "VideoWall", "WebView2");
            Directory.CreateDirectory(folder);
            return folder;
        }

        private void OnLoad(object sender, RoutedEventArgs e) => Navigate();

        private void OnUrlKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Navigate();
                e.Handled = true;
            }
        }

        private void Navigate()
        {
            var url = NormalizeUrl(UrlBox.Text);
            if (string.IsNullOrEmpty(url))
            {
                Hint.Text = "Digite um endereço válido (ex.: www.youtube.com).";
                return;
            }

            UrlBox.Text = url; // mostra o endereço completo (com https://)
            if (Web.CoreWebView2 != null)
            {
                try { Web.CoreWebView2.Navigate(url); }
                catch { Hint.Text = "Não foi possível abrir esse endereço."; }
            }
        }

        /// <summary>
        /// Completa o endereço com "https://" quando o usuário digita só o domínio
        /// (ex.: "www.youtube.com"). Retorna vazio se não for um endereço web válido.
        /// </summary>
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

        private async void OnSave(object sender, RoutedEventArgs e)
        {
            // Prioriza a página realmente exibida no preview (captura buscas e cliques
            // dentro da página); recorre ao texto digitado se ainda não há navegação.
            var current = Web.CoreWebView2?.Source;
            ResultUrl = (!string.IsNullOrWhiteSpace(current) &&
                         current.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                ? current!
                : NormalizeUrl(UrlBox.Text);

            SaveButton.IsEnabled = false;
            Hint.Text = "Enviando para a tela...";

            ResultPreview = await CaptureAsync();

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Captura uma imagem do conteúdo atual do navegador. Nunca lança e nunca trava:
        /// se demorar demais ou falhar, retorna nulo e o salvamento prossegue mesmo assim.
        /// </summary>
        private async Task<ImageSource?> CaptureAsync()
        {
            if (Web.CoreWebView2 == null)
                return null;

            try
            {
                using var stream = new MemoryStream();
                var capture = Web.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png, stream);

                if (await Task.WhenAny(capture, Task.Delay(4000)) != capture)
                    return null; // captura demorou demais

                await capture;
                stream.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Se a página atual for do YouTube e ainda não estiver em tela cheia, envia a
        /// tecla "F" (atalho de tela cheia do player) via CDP. Repetir é seguro.
        /// </summary>
        private async void EnsureFullscreen()
        {
            var core = Web.CoreWebView2;
            if (core == null) return;

            try
            {
                var src = core.Source ?? string.Empty;
                if (src.IndexOf("youtube.com", StringComparison.OrdinalIgnoreCase) < 0)
                    return;
                if (core.ContainsFullScreenElement)
                    return;

                const string down = "{\"type\":\"keyDown\",\"windowsVirtualKeyCode\":70,\"key\":\"f\",\"code\":\"KeyF\"}";
                const string up = "{\"type\":\"keyUp\",\"windowsVirtualKeyCode\":70,\"key\":\"f\",\"code\":\"KeyF\"}";
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", down);
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", up);
            }
            catch { /* tenta de novo no próximo tick */ }
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _fullscreenTimer?.Stop(); } catch { }
            // Libera o processo do WebView2 para não deixar o controlador instável.
            try { Web.Dispose(); }
            catch { /* já liberado */ }
            base.OnClosed(e);
        }
    }
}
