using System;
using System.Text.RegularExpressions;

namespace VideoWall.Services
{
    /// <summary>
    /// Converte um endereço de live do YouTube (não listada) para a forma "embed",
    /// que reproduz o vídeo direto, sem a interface do site — ideal para a miniatura
    /// (PiP) sobreposta ao navegador. Aceita os formatos mais comuns de link:
    /// <c>youtube.com/live/ID</c>, <c>youtu.be/ID</c>, <c>watch?v=ID</c>,
    /// <c>embed/ID</c> e <c>shorts/ID</c>.
    /// </summary>
    public static class YouTubeLive
    {
        // ID de vídeo do YouTube: 11 caracteres (letras, números, "-" e "_").
        private static readonly Regex IdPattern = new(
            @"(?:youtu\.be/|youtube\.com/(?:live/|embed/|shorts/|watch\?(?:[^&]*&)*v=))([A-Za-z0-9_-]{11})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Indica se o endereço aparenta ser uma live/vídeo do YouTube.</summary>
        public static bool IsYouTube(string? url) =>
            !string.IsNullOrWhiteSpace(url) && TryGetVideoId(url, out _);

        /// <summary>
        /// Devolve a URL "embed" com autoplay e mudo (necessário para tocar sozinho na
        /// tela). Se não reconhecer um ID do YouTube, devolve o endereço original.
        /// </summary>
        public static string ToEmbedUrl(string url)
        {
            if (!TryGetVideoId(url, out var id))
                return url;

            // mute=1 é obrigatório para o autoplay funcionar nos navegadores modernos.
            return $"https://www.youtube.com/embed/{id}?autoplay=1&mute=1&playsinline=1&rel=0";
        }

        private static bool TryGetVideoId(string url, out string id)
        {
            id = string.Empty;
            if (string.IsNullOrWhiteSpace(url))
                return false;

            var match = IdPattern.Match(url);
            if (!match.Success)
                return false;

            id = match.Groups[1].Value;
            return true;
        }
    }
}
