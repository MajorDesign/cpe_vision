namespace VideoWall.Network
{
    /// <summary>
    /// Uma fonte dentro do layout enviado a um terminal. Posição e tamanho são
    /// NORMALIZADOS (0..1) em relação à tela do terminal, para independer da
    /// resolução. Suporta navegador, cor e texto (câmera/imagem virão depois).
    /// </summary>
    public sealed class ScreenSource
    {
        /// <summary>"browser", "color" ou "text".</summary>
        public string Kind { get; set; } = string.Empty;

        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int ZIndex { get; set; }

        // Navegador
        public string? Url { get; set; }
        public double Zoom { get; set; } = 1.0;

        /// <summary>
        /// Quando verdadeiro, o navegador é uma miniatura sobreposta (PiP, ex.: live
        /// do YouTube). O terminal o renderiza numa janela própria sempre-no-topo, pois
        /// dois WebView2 na mesma janela não respeitam a ordem de empilhamento (airspace).
        /// </summary>
        public bool Overlay { get; set; }

        // Cor
        public string? ColorHex { get; set; }

        // Texto
        public string? Text { get; set; }
        public double FontSize { get; set; } = 48;
        public string? ForegroundHex { get; set; }

        public const string Browser = "browser";
        public const string Color = "color";
        public const string Text2 = "text";

        /// <summary>Câmera/live tocada pelo VLC (nativo, leve). Usa <see cref="Url"/> como
        /// endereço do stream (link do YouTube, RTSP, HLS…).</summary>
        public const string Camera = "camera";
    }
}
