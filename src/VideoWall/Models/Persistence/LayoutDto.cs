namespace VideoWall.Models.Persistence
{
    /// <summary>Layout salvo da parede (conjunto de fontes e suas propriedades).</summary>
    public class LayoutDto
    {
        public string Version { get; set; } = "1";
        public List<ElementDto> Elements { get; set; } = new();
    }

    /// <summary>
    /// Representação serializável de um elemento da parede. Reúne todos os
    /// campos possíveis; apenas os relevantes ao <see cref="Type"/> são usados.
    /// </summary>
    public class ElementDto
    {
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int ZIndex { get; set; }
        public double Opacity { get; set; } = 1.0;
        public bool IsVisible { get; set; } = true;

        // Específicos por tipo (nulos quando não se aplicam).
        public string? ColorHex { get; set; }
        public string? Text { get; set; }
        public double? FontSize { get; set; }
        public string? ForegroundHex { get; set; }
        public string? ImagePath { get; set; }
        public string? Url { get; set; }
        public double? ZoomFactor { get; set; }
        public string? WindowTitle { get; set; }
        public string? StreamUrl { get; set; }
    }
}
