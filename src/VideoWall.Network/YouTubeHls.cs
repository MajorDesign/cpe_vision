using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VideoWall.Network
{
    /// <summary>
    /// Extrai o endereço do manifesto HLS (.m3u8) de uma live do YouTube, lendo a página
    /// watch. O VLC toca esse .m3u8 direto, de forma confiável — sem depender do extrator
    /// interno do VLC (que falha em lives). O endereço EXPIRA (algumas horas), então quem
    /// usa deve re-extrair periodicamente (ex.: quando o VLC parar).
    /// </summary>
    public static class YouTubeHls
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly Regex HlsRx = new(
            "\"hlsManifestUrl\":\"(https[^\"]+)\"", RegexOptions.Compiled);

        static YouTubeHls()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        /// <summary>Devolve a URL do manifesto HLS da live, ou null se não encontrar.</summary>
        public static async Task<string?> ExtractAsync(string youtubeUrl)
        {
            try
            {
                string html = await Http.GetStringAsync(youtubeUrl);
                var m = HlsRx.Match(html);
                if (!m.Success)
                    return null;

                // Desescapa o JSON (\/ -> /, & -> &).
                return m.Groups[1].Value
                    .Replace("\\/", "/")
                    .Replace("\\u0026", "&");
            }
            catch
            {
                return null;
            }
        }
    }
}
