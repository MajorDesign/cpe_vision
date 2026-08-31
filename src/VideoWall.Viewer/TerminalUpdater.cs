using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using VideoWall.Network;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Mantém o terminal atualizado 24/7, sem ninguém na frente da TV.
    ///
    /// FONTE: procura primeiro o computador CENTRAL na rede local (descoberta por
    /// broadcast + <see cref="UpdateServer"/> na porta 48020) e, como reserva, as
    /// releases do GitHub. Assim o terminal se atualiza mesmo sem internet, e uma
    /// versão nova trafega uma única vez pela WAN.
    ///
    /// INSTALAÇÃO SEM UAC: o terminal roda como usuário comum e o app fica em
    /// Arquivos de Programas, então ele não pode se auto-substituir. O instalador
    /// registra a tarefa agendada "CPE VideoWall Update" (SYSTEM, privilégio
    /// máximo); aqui apenas baixamos o setup para a pasta de troca e disparamos a
    /// tarefa. Ela instala em silêncio (fechando este processo) e o script
    /// "reabrir.cmd" — iniciado na sessão do usuário ANTES da instalação — reabre o
    /// terminal quando o instalador termina.
    /// </summary>
    public sealed class TerminalUpdater : IDisposable
    {
        public const string SetupName = "setup-terminal.exe";
        public const string UpdateTaskName = "CPE VideoWall Update";

        /// <summary>Pasta de troca (gravável pelo usuário do quiosque; ver setup-terminal.iss).</summary>
        private static readonly string UpdateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CPE", "VideoWall", "update");

        private static string SetupPath => Path.Combine(UpdateDir, SetupName);

        /// <summary>Marca criada pela tarefa quando a instalação termina.</summary>
        private static string FlagPath => Path.Combine(UpdateDir, "pronto.flag");

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

        private readonly ControllerLocator _locator = new();
        private bool _busy;
        private bool _installing;

        public TerminalUpdater() => _locator.Start();

        /// <summary>Aguarda (até o limite) o primeiro anúncio do central na rede.</summary>
        public async Task WaitForControllerAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (_locator.Current == null && DateTime.UtcNow < deadline)
                await Task.Delay(250).ConfigureAwait(false);
        }

        /// <summary>
        /// Verifica se há versão mais nova e, havendo, baixa e dispara a instalação.
        /// Retorna verdadeiro quando a atualização foi iniciada — a partir daí o
        /// instalador fecha este processo e o reabre já atualizado.
        /// </summary>
        public async Task<bool> CheckAndUpdateAsync()
        {
            if (_busy || _installing)
                return false;

            _busy = true;
            try
            {
                Version current = GitHubUpdater.CurrentVersion();

                var source = await TryLanAsync().ConfigureAwait(false);
                if (source == null || source.Value.Version <= current)
                {
                    var github = await TryGitHubAsync().ConfigureAwait(false);
                    if (github != null && (source == null || github.Value.Version > source.Value.Version))
                        source = github;
                }

                if (source == null || source.Value.Version <= current)
                    return false;

                await DownloadAsync(source.Value.Url).ConfigureAwait(false);
                return StartInstall();
            }
            catch
            {
                // Central fora do ar / sem internet: tenta no próximo ciclo.
                return false;
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>Versão oferecida pelo central na rede local.</summary>
        private async Task<(Version Version, string Url)?> TryLanAsync()
        {
            if (_locator.Current is not { } controller || string.IsNullOrEmpty(controller.IpAddress))
                return null;

            try
            {
                string baseUrl = $"http://{controller.IpAddress}:{controller.UpdatePort}";
                string json = await Http.GetStringAsync(baseUrl + "/version").ConfigureAwait(false);

                using var doc = JsonDocument.Parse(json);
                string? text = doc.RootElement.GetProperty("version").GetString();
                if (!Version.TryParse(text, out var version))
                    return null;

                return (version, baseUrl + "/setup");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Versão publicada como release no GitHub (reserva).</summary>
        private static async Task<(Version Version, string Url)?> TryGitHubAsync()
        {
            var latest = await GitHubUpdater.GetLatestAsync().ConfigureAwait(false);
            if (latest == null || !latest.Assets.TryGetValue(SetupName, out var url))
                return null;

            return (latest.Version, url);
        }

        private static async Task DownloadAsync(string url)
        {
            Directory.CreateDirectory(UpdateDir);

            // Baixa para um arquivo temporário: a tarefa só pode ver o instalador
            // quando ele estiver completo.
            string temp = SetupPath + ".part";
            byte[] bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);

            try { File.Delete(FlagPath); } catch { }
            File.Move(temp, SetupPath, overwrite: true);
        }

        /// <summary>
        /// Programa a reabertura (na sessão do usuário) e tenta adiantar a instalação
        /// disparando a tarefa agendada, que roda como SYSTEM e instala sem UAC.
        ///
        /// Se o disparo não for permitido — o terminal roda sem elevação e nem sempre
        /// consegue acionar uma tarefa do SYSTEM — não há problema: basta o instalador
        /// estar na pasta de troca, porque a tarefa também roda sozinha a cada 5 minutos.
        /// O que NÃO se faz aqui é executar o instalador diretamente: isso abriria o
        /// pedido de UAC na TV, e num quiosque não há ninguém para clicar.
        /// </summary>
        /// <returns>
        /// Verdadeiro se a instalação já começou. Falso quando ela ficou pendente para a
        /// tarefa periódica — aí o terminal segue abrindo normalmente e a troca acontece
        /// em poucos minutos.
        /// </returns>
        private bool StartInstall()
        {
            _installing = true; // o instalador já está baixado: não baixar de novo
            StartReopenWatcher();

            if (RunSchtasks($"/run /tn \"{UpdateTaskName}\""))
                return true;

            ErrorLog.Write(
                $"Atualização {SetupName} baixada, mas não foi possível disparar a tarefa " +
                $"\"{UpdateTaskName}\". A tarefa periódica (5 min) aplicará.", null);
            return false;
        }

        /// <summary>
        /// Script que reabre o terminal depois da instalação. Roda na sessão do
        /// usuário (a tarefa roda em SYSTEM e não conseguiria abrir a janela) e
        /// sobrevive ao fechamento deste processo pelo instalador.
        /// </summary>
        private static void StartReopenWatcher()
        {
            string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
            string script = Path.Combine(UpdateDir, "reabrir.cmd");
            string exeName = Path.GetFileName(exe);

            string text =
                "@echo off\r\n" +
                "setlocal\r\n" +
                // Espera a marca de "instalação concluída" por até 10 minutos.
                "set /a n=0\r\n" +
                ":wait\r\n" +
                $"if exist \"{FlagPath}\" goto abrir\r\n" +
                "set /a n+=1\r\n" +
                "if %n% gtr 120 goto expirou\r\n" +
                "timeout /t 5 /nobreak >nul\r\n" +
                "goto wait\r\n" +
                ":expirou\r\n" +
                ":abrir\r\n" +
                $"del /q \"{FlagPath}\" 2>nul\r\n" +
                "timeout /t 3 /nobreak >nul\r\n" +
                // Nunca abre uma segunda instância (as portas de rede são exclusivas):
                // em atualizações vindas da versão anterior o próprio instalador reabre.
                $"tasklist /fi \"IMAGENAME eq {exeName}\" | find /i \"{exeName}\" >nul && goto fim\r\n" +
                $"start \"\" \"{exe}\"\r\n" +
                ":fim\r\n" +
                "del \"%~f0\"\r\n";

            try
            {
                File.WriteAllText(script, text);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{script}\"",
                    WorkingDirectory = UpdateDir,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                });
            }
            catch
            {
                // Sem o vigia, o terminal volta no próximo logon (atalho de inicialização).
            }
        }

        private static bool RunSchtasks(string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });

                if (process == null)
                    return false;

                return process.WaitForExit(20000) && process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose() => _locator.Dispose();
    }
}
