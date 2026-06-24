namespace VideoWall.Models
{
    /// <summary>Bloco de cor sólida. Útil como fundo ou separador visual.</summary>
    public class ColorElement : WallElement
    {
        private string _colorHex = "#3B82F6";

        public override string Kind => "Cor";

        /// <summary>
        /// Cor em hexadecimal (ex.: "#3B82F6"). O WPF converte automaticamente
        /// esta string em um pincel (Brush) durante o data binding.
        /// </summary>
        public string ColorHex
        {
            get => _colorHex;
            set => SetProperty(ref _colorHex, value);
        }

        public override WallElement Clone()
        {
            var copy = new ColorElement { ColorHex = ColorHex };
            CopyBaseTo(copy);
            return copy;
        }
    }
}
