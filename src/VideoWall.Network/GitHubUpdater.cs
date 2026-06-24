using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace VideoWall.Network
{
    /// <summary>
    /// Verifica e baixa atualizações publicadas como "releases" no GitHub. É usado pelo
    /// pré-load (splash) do controlador e do terminal: compara a versão atual com a da
    /// última release e, se houver uma mais nova, baixa o instalador/binário correspondente.
    /// </summary>
    public static class GitHubUpdater
    {
        public const string Owner = "MajorDesign";
        public const string Repo = "cpe_vision";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            // O GitHub exige um User-Agent; sem isso a API responde 403.
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("cpe-videowall", "1.0"));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return http;
        }

        /// <summary>Versão da aplicação em execução (normalizada para 3 componentes).</summary>
        public static Version CurrentVersion()
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            return Normalize(v);
        }

        private static Version Normalize(Version v) =>
            new(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));

        /// <summary>
        /// Consulta a última release. Retorna a versão (do tag, ex.: "v1.2.0") e os arquivos
        /// (nome do asset → URL de download). Retorna nulo se não houver release ou sem internet.
        /// </summary>
        public static async Task<ReleaseInfo?> GetLatestAsync()
        {
            try
            {
                string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
                string json = await Http.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
                if (!TryParseTag(tag, out var version))
                    return null;

                var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("assets", out var arr))
                {
                    foreach (var a in arr.EnumerateArray())
                    {
                        string? name = a.GetProperty("name").GetString();
                        string? link = a.GetProperty("browser_download_url").GetString();
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(link))
                            assets[name] = link;
                    }
                }

                return new ReleaseInfo(version, assets);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryParseTag(string tag, out Version version)
        {
            version = new Version(0, 0, 0);
            string t = tag.TrimStart('v', 'V').Trim();
            if (!Version.TryParse(t, out var v))
                return false;
            version = Normalize(v);
            return true;
        }

        /// <summary>Baixa um arquivo da release para a pasta temporária e devolve o caminho.</summary>
        public static async Task<string> DownloadToTempAsync(string url, string fileName)
        {
            byte[] bytes = await Http.GetByteArrayAsync(url);
            string path = Path.Combine(Path.GetTempPath(), fileName);
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }

        public sealed record ReleaseInfo(Version Version, IReadOnlyDictionary<string, string> Assets);
    }
}
