using System;
using System.IO;
using System.Text.RegularExpressions;

namespace VideoWall.Network
{
    /// <summary>
    /// Suporte a lives do YouTube (não listadas) exibidas como navegador na parede.
    ///
    /// Navegar DIRETO para <c>youtube.com/embed/ID</c> dá "Erro 153" — o player
    /// exige estar dentro de um &lt;iframe&gt; numa página com origem/referrer válidos.
    /// Por isso geramos uma página local (<c>player.html</c>) servida por um host
    /// virtual do WebView2 (<see cref="VirtualHost"/>) que carrega a live num iframe.
    /// Tanto o controlador (pré-visualização) quanto o terminal mapeiam esse host
    /// para a mesma pasta, então o endereço gerado funciona nos dois.
    /// </summary>
    public static class YouTubeLive
    {
        /// <summary>Host virtual que serve o <c>player.html</c> (origem da página).</summary>
        public const string VirtualHost = "cpe.live";

        // ID de vídeo do YouTube: 11 caracteres (letras, números, "-" e "_").
        private static readonly Regex IdPattern = new(
            @"(?:youtu\.be/|youtube\.com/(?:live/|embed/|shorts/|watch\?(?:[^&]*&)*v=))([A-Za-z0-9_-]{11})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Script injetado nos navegadores que exibem lives. Quando a página é do
        /// YouTube (fallback para a página normal, sem embed), ele:
        ///  - força o vídeo a tocar (mudo, permitido sem gesto);
        ///  - remove popups (YouTube Premium, consentimento) que dão sensação de travado;
        ///  - injeta CSS que ESCONDE o cabeçalho/barra do site e expande o player para
        ///    ocupar todo o quadro, com o vídeo em "contain" (sem cortar a imagem) —
        ///    o resultado é o vídeo em tela cheia reduzido dentro da miniatura.
        /// Injetar com AddScriptToExecuteOnDocumentCreatedAsync.
        /// </summary>
        public const string KeepPlayingScript =
@"(function(){
  if (location.hostname.indexOf('youtube.com') < 0) return;
  var STYLE_ID = '__cpeLiveStyle';
  function injectStyle(){
    if (document.getElementById(STYLE_ID)) return;
    var s = document.createElement('style');
    s.id = STYLE_ID;
    s.textContent =
      'html,body{margin:0!important;padding:0!important;overflow:hidden!important;background:#000!important}' +
      'ytd-masthead,#masthead-container,#masthead,tp-yt-app-header,#chips-wrapper,' +
      'ytd-mealbar-promo-renderer,ytmusic-mealbar-promo-renderer,#secondary,#below,#comments,' +
      'ytd-watch-metadata,ytd-watch-next-secondary-results-renderer{display:none!important}' +
      '#movie_player,.html5-video-player,#player,#player-container,#player-container-inner,' +
      '#player-container-outer,#full-bleed-container,ytd-watch-flexy #player{' +
      'position:fixed!important;top:0!important;left:0!important;width:100vw!important;height:100vh!important;' +
      'max-width:none!important;max-height:none!important;margin:0!important;z-index:2147483647!important}' +
      '.html5-video-container{width:100%!important;height:100%!important}' +
      'video.html5-main-video,.html5-video-container video{width:100%!important;height:100%!important;' +
      'top:0!important;left:0!important;object-fit:contain!important}';
    (document.head || document.documentElement).appendChild(s);
  }
  var qualityFixed = false;
  function go(){
    try {
      injectStyle();
      var v = document.querySelector('video');
      if (v) { v.muted = true; if (v.paused) { var p = v.play(); if (p && p.catch) p.catch(function(){}); } }
      // Limita a qualidade (uma vez) para aliviar a GPU do terminal quando há um
      // dashboard pesado na parede — sem isso a live concorre e fica bufferizando.
      if (!qualityFixed) {
        var mp = document.getElementById('movie_player');
        if (mp) {
          // Qualidade MÍNIMA ('tiny' = 144p): o mais leve possível na GPU/rede, para a
          // live não travar mesmo com um dashboard pesado disputando recursos na parede.
          try { if (mp.setPlaybackQualityRange) mp.setPlaybackQualityRange('tiny', 'tiny'); } catch(_){}
          try { if (mp.setPlaybackQuality) mp.setPlaybackQuality('tiny'); } catch(_){}
          qualityFixed = true;
        }
      }
      ['ytd-mealbar-promo-renderer','ytmusic-mealbar-promo-renderer','ytd-popup-container tp-yt-paper-dialog']
        .forEach(function(s){ document.querySelectorAll(s).forEach(function(e){ try{ e.remove(); }catch(_){} }); });
      document.querySelectorAll('button, tp-yt-paper-button, yt-button-shape button').forEach(function(b){
        var t = (b.textContent || '').trim().toLowerCase();
        if (t === 'agora não' || t === 'agora nao' || t === 'no thanks' || t === 'dispensar' || t === 'no, thanks') {
          try { b.click(); } catch(_){}
        }
      });
      // NÃO disparar 'resize' aqui: fazê-lo em loop obriga o YouTube a recalcular o
      // player toda hora e re-bufferizar (a live ficava 'sempre carregando').
    } catch(e){}
  }
  injectStyle();
  document.addEventListener('DOMContentLoaded', injectStyle);
  // Verifica a cada 2s (play + popups). Sem dispatch de resize -> sem re-buffer.
  setInterval(go, 2000);
})();";

        /// <summary>Indica se o endereço aparenta ser uma live/vídeo do YouTube.</summary>
        public static bool IsYouTube(string? url) =>
            !string.IsNullOrWhiteSpace(url) && TryGetVideoId(url, out _);

        /// <summary>
        /// Converte um link de live/vídeo do YouTube no endereço do player local
        /// (iframe hospedado), que toca direto sem a interface do site. Demais
        /// endereços — e endereços já apontando para o player — passam inalterados.
        /// </summary>
        public static string ToPlayerUrl(string url)
        {
            if (!TryGetVideoId(url, out var id))
                return url;

            return $"https://{VirtualHost}/player.html?v={id}";
        }

        /// <summary>
        /// Garante que a pasta com o <c>player.html</c> existe (gravada em LocalAppData,
        /// para funcionar mesmo com o app instalado em Arquivos de Programas) e devolve
        /// o caminho dela. Mapeie esse caminho para <see cref="VirtualHost"/> no WebView2.
        /// </summary>
        public static string EnsurePlayerFolder()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CPE Tecnologia", "VideoWall", "player");
            Directory.CreateDirectory(folder);

            var file = Path.Combine(folder, "player.html");
            // Reescreve se faltar ou se o conteúdo mudou (atualizações do player).
            if (!File.Exists(file) || File.ReadAllText(file) != PlayerHtml)
                File.WriteAllText(file, PlayerHtml);

            return folder;
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

        // Página hospedeira: lê ?v=ID e toca a live. Como a página tem origem própria
        // (host virtual), o player recebe o referrer e não dá Erro 153. autoplay exige
        // mute=1. Se a live tiver INCORPORAÇÃO DESATIVADA (onError do player), cai
        // automaticamente para a página normal do YouTube, que toca mesmo assim.
        private const string PlayerHtml =
@"<!doctype html>
<html lang=""pt-br"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<style>
  html,body{margin:0;width:100%;height:100%;background:#000;overflow:hidden}
  #player{position:fixed;inset:0;width:100%;height:100%;border:0}
</style>
</head>
<body>
<div id=""player""></div>
<script>
  var params = new URLSearchParams(location.search);
  var id = (params.get('v') || '').replace(/[^A-Za-z0-9_-]/g, '');
  var watchUrl = 'https://www.youtube.com/watch?v=' + id;
  var fellBack = false;
  function fallback() {
    if (!fellBack && id) { fellBack = true; location.replace(watchUrl); }
  }
  if (id) {
    // Carrega a API do player do YouTube.
    var tag = document.createElement('script');
    tag.src = 'https://www.youtube.com/iframe_api';
    document.head.appendChild(tag);

    // Se a incorporação estiver bloqueada (onError) ou a API não carregar (timeout),
    // cai para a página normal do YouTube.
    var guard = setTimeout(fallback, 8000);
    window.onYouTubeIframeAPIReady = function () {
      try {
        new YT.Player('player', {
          videoId: id,
          width: '100%', height: '100%',
          playerVars: { autoplay: 1, mute: 1, playsinline: 1, rel: 0 },
          events: {
            onReady: function (e) { clearTimeout(guard); try { e.target.playVideo(); } catch (_) {} },
            onError: function () { clearTimeout(guard); fallback(); }
          }
        });
      } catch (_) { clearTimeout(guard); fallback(); }
    };
  }
</script>
</body>
</html>";
    }
}
