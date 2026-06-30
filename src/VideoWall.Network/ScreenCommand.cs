namespace VideoWall.Network
{
    /// <summary>Comando enviado do Controlador para um terminal (o que exibir).</summary>
    public sealed class ScreenCommand
    {
        /// <summary>"show-browser", "show-layout", "clear", "live-start" ou "live-stop".</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>URL a exibir (quando Type = "show-browser").</summary>
        public string? Url { get; set; }

        /// <summary>Fator de zoom da página (1.0 = 100%).</summary>
        public double Zoom { get; set; } = 1.0;

        /// <summary>Fontes do layout (quando Type = "show-layout").</summary>
        public List<ScreenSource>? Sources { get; set; }

        public const string ShowBrowser = "show-browser";
        public const string ShowLayout = "show-layout";
        public const string Clear = "clear";

        /// <summary>Inicia o modo "controle ao vivo": o terminal abre uma página em
        /// tela cheia que passa a receber entrada (mouse/rolagem/teclado) do controlador.</summary>
        public const string LiveStart = "live-start";

        /// <summary>Encerra o modo "controle ao vivo".</summary>
        public const string LiveStop = "live-stop";

        /// <summary>Reinicia o terminal (ele reabre pelo preload e busca a versão nova) —
        /// permite atualizar terminais 24/7 pelo controlador, sem ir até cada mini-PC.</summary>
        public const string Restart = "restart";

        /// <summary>Porta TCP padrão do canal de comando dos terminais.</summary>
        public const int DefaultPort = 48011;
    }
}
