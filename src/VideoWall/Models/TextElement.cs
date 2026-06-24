namespace VideoWall.Models
{
    /// <summary>Texto livre exibido sobre a parede.</summary>
    public class TextElement : WallElement
    {
        private string _text = "Texto";
        private double _fontSize = 48;
        private string _foregroundHex = "#FFFFFF";

        public override string Kind => "Texto";

        public string Text
        {
            get => _text;
            set { if (SetProperty(ref _text, value)) OnPropertyChanged(nameof(Summary)); }
        }

        public override string Summary => _text;

        public double FontSize
        {
            get => _fontSize;
            set => SetProperty(ref _fontSize, Math.Max(1, value));
        }

        /// <summary>Cor do texto em hexadecimal (ex.: "#FFFFFF").</summary>
        public string ForegroundHex
        {
            get => _foregroundHex;
            set => SetProperty(ref _foregroundHex, value);
        }

        public override WallElement Clone()
        {
            var copy = new TextElement { Text = Text, FontSize = FontSize, ForegroundHex = ForegroundHex };
            CopyBaseTo(copy);
            return copy;
        }
    }
}
