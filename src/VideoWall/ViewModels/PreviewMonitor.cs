namespace VideoWall.ViewModels
{
    /// <summary>
    /// Representação de um monitor já convertida para o espaço de coordenadas
    /// da pré-visualização (DIP, com origem em 0,0 no canto superior-esquerdo
    /// da parede virtual). Usada apenas para desenhar o contorno dos monitores.
    /// </summary>
    public class PreviewMonitor
    {
        public double Left { get; init; }
        public double Top { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public string Label { get; init; } = string.Empty;
        public bool IsPrimary { get; init; }
    }
}
