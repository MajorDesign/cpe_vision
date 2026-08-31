using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VideoWall.Network;

namespace VideoWall.Services
{
    /// <summary>
    /// Mantém no central uma cópia do instalador mais recente do TERMINAL, para
    /// servi-la aos terminais pela rede local (<see cref="UpdateServer"/>).
    ///
    /// Só o central precisa de internet: ele consulta as releases do GitHub, baixa
    /// o setup-terminal.exe uma única vez e o entrega às telas pela LAN.
    /// Sem internet, usa a cópia manual em "terminal-update" ao lado do executável.
    /// </summary>
    public sealed class TerminalPackageService : IDisposable
    {
        public const string SetupName = "setup-terminal.exe";

        /// <summary>Intervalo entre consultas ao GitHub.</summary>
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CPE Tecnologia", "VideoWall", "terminal-update");

        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        /// <summary>Instalador em cache (baixado do GitHub).</summary>
        private static string CachePath => Path.Combine(CacheDir, SetupName);

        /// <summary>Cópia manual, para implantação sem internet no central.</summary>
        private static string BundledPath =>
            Path.Combine(AppContext.BaseDirectory, "terminal-update", SetupName);

        public void Start() => _loop = Task.Run(() => LoopAsync(_cts.Token));

        /// <summary>
        /// Instalador a servir: o mais NOVO entre o cache e a cópia manual.
        /// A versão vem do próprio arquivo (o .iss carrega a versão do terminal).
        /// </summary>
        public TerminalPackage? Current()
        {
            var cached = Read(CachePath);
            var bundled = Read(BundledPath);

            if (cached == null) return bundled;
            if (bundled == null) return cached;
            return bundled.Version > cached.Version ? bundled : cached;
        }

        private static TerminalPackage? Read(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                string? text = FileVersionInfo.GetVersionInfo(path).FileVersion;
                if (!Version.TryParse(text, out var v))
                    return null;

                return new TerminalPackage(
                    new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build)),
                    path);
            }
            catch
            {
                return null;
            }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await SyncAsync().ConfigureAwait(false);

                try { await Task.Delay(CheckInterval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>Baixa o instalador do terminal se a release do GitHub for mais nova.</summary>
        private async Task SyncAsync()
        {
            try
            {
                var latest = await GitHubUpdater.GetLatestAsync().ConfigureAwait(false);
                if (latest == null || !latest.Assets.TryGetValue(SetupName, out var url))
                    return;

                var current = Current();
                if (current != null && current.Version >= latest.Version)
                    return;

                string temp = await GitHubUpdater
                    .DownloadToTempAsync(url, SetupName)
                    .ConfigureAwait(false);

                Directory.CreateDirectory(CacheDir);
                File.Move(temp, CachePath, overwrite: true);
            }
            catch
            {
                // Sem internet / release indisponível: tenta no próximo ciclo.
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
