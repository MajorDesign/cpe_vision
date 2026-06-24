namespace VideoWall.Network
{
    /// <summary>
    /// Um evento de entrada (mouse, rolagem, teclado ou navegação) enviado do
    /// controlador para o terminal durante o "controle ao vivo". As coordenadas são
    /// normalizadas (0..1) para funcionar independentemente da resolução da tela.
    /// </summary>
    public sealed class RemoteInputEvent
    {
        /// <summary>
        /// "mousemove", "mousedown", "mouseup", "wheel", "keydown", "keyup" ou "nav".
        /// </summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>
        /// Índice do navegador-alvo dentro do layout enviado (mesma ordem das fontes).
        /// Permite controlar uma célula específica de uma grade sem afetar as demais.
        /// </summary>
        public int TargetIndex { get; set; }

        /// <summary>Posição horizontal normalizada (0 = esquerda, 1 = direita).</summary>
        public double X { get; set; }

        /// <summary>Posição vertical normalizada (0 = topo, 1 = base).</summary>
        public double Y { get; set; }

        /// <summary>Botão do mouse no padrão DOM: 0 = esquerdo, 1 = meio, 2 = direito.</summary>
        public int Button { get; set; }

        /// <summary>Máscara de botões pressionados no padrão DOM (1 esq, 2 dir, 4 meio).</summary>
        public int Buttons { get; set; }

        /// <summary>Deslocamento horizontal da roda (rolagem).</summary>
        public double DeltaX { get; set; }

        /// <summary>Deslocamento vertical da roda (rolagem).</summary>
        public double DeltaY { get; set; }

        /// <summary>Tecla lógica (ex.: "a", "Enter", "ArrowDown").</summary>
        public string? Key { get; set; }

        /// <summary>Código físico da tecla (ex.: "KeyA", "Enter").</summary>
        public string? Code { get; set; }

        /// <summary>Código virtual da tecla (Windows VK), usado pelo CDP.</summary>
        public int KeyCode { get; set; }

        /// <summary>Máscara de modificadores no padrão CDP: 1 Alt, 2 Ctrl, 4 Meta, 8 Shift.</summary>
        public int Modifiers { get; set; }

        /// <summary>URL a navegar (quando Kind = "nav").</summary>
        public string? Url { get; set; }

        /// <summary>Nível de zoom da página (quando Kind = "zoom").</summary>
        public double Zoom { get; set; } = 1.0;

        /// <summary>Tipo de marcação: "pen", "arrow" ou "rect" (eventos "annot-*").</summary>
        public string? ShapeType { get; set; }

        /// <summary>Cor da marcação em hexadecimal (ex.: "#EF4444").</summary>
        public string? ColorHex { get; set; }

        /// <summary>Porta TCP do canal persistente de entrada ao vivo.</summary>
        public const int LivePort = 48013;
    }
}
