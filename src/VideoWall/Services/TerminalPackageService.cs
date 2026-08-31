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

        /// <summary>
        /// Versão do instalador, gravada ao lado dele. A autoridade sobre a versão é a
        /// TAG da release, não o arquivo: instaladores gerados antes da 1.39 saíram SEM
        /// versão embutida, e confiar só neles fazia o central anunciar "0.0.0" (nenhum
        /// terminal atualizava) e rebaixar o mesmo arquivo a cada ciclo.
        /// </summary>
        private static string VersionFileFor(string setupPath) =>
            Path.Combine(Path.GetDirectoryName(setupPath)!, "version.txt");

        public void Start() => _loop = Task.Run(() => LoopAsync(_cts.Token));

        /// <summary>
        /// Instalador a servir: o mais NOVO entre o cache e a cópia manual.
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

                var version = ReadVersionFile(path) ?? ReadEmbeddedVersion(path);
                if (version == null)
                    return null;

                return new TerminalPackage(version, path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Versão anotada ao lado do instalador (a tag da release que o gerou).</summary>
        private static Version? ReadVersionFile(string setupPath)
        {
            try
            {
                string file = VersionFileFor(setupPath);
                if (!File.Exists(file))
                    return null;
                return Version.TryParse(File.ReadAllText(file).Trim(), out var v) ? Normalize(v) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Versão embutida no instalador (existe a partir da 1.39).</summary>
        private static Version? ReadEmbeddedVersion(string setupPath)
        {
            try
            {
                string? text = FileVersionInfo.GetVersionInfo(setupPath).FileVersion;
                return Version.TryParse(text, out var v) ? Normalize(v) : null;
            }
            catch
            {
                return null;
            }
        }

        private static Version Normalize(Version v) =>
            new(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));

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

                // Anota a versão da release: sem isso, um instalador sem versão embutida
                // faria o central anunciar "0.0.0" e rebaixar o arquivo a cada ciclo.
                File.WriteAllText(VersionFileFor(CachePath), latest.Version.ToString());
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
