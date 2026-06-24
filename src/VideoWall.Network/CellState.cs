namespace VideoWall.Network
{
    /// <summary>
    /// Estado atual de um navegador (célula) no terminal: a página que está aberta e a
    /// posição de rolagem. Usado para reabrir o controle ao vivo continuando de onde estava.
    /// </summary>
    public sealed class CellState
    {
        public string Url { get; set; } = string.Empty;
        public double ScrollX { get; set; }
        public double ScrollY { get; set; }

        /// <summary>Porta TCP do serviço de estado das células ao vivo.</summary>
        public const int Port = 48015;
    }
}
