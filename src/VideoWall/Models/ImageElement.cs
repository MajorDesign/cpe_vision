namespace VideoWall.Models
{
    /// <summary>Imagem (arquivo local) exibida sobre a parede.</summary>
    public class ImageElement : WallElement
    {
        private string _imagePath = string.Empty;

        public override string Kind => "Imagem";

        /// <summary>
        /// Caminho do arquivo de imagem. O WPF converte automaticamente este
        /// caminho em uma fonte de imagem (ImageSource) durante o data binding.
        /// </summary>
        public string ImagePath
        {
            get => _imagePath;
            set { if (SetProperty(ref _imagePath, value)) OnPropertyChanged(nameof(Summary)); }
        }

        public override string Summary =>
            string.IsNullOrEmpty(ImagePath) ? string.Empty : System.IO.Path.GetFileName(ImagePath);

        public override WallElement Clone()
        {
            var copy = new ImageElement { ImagePath = ImagePath };
            CopyBaseTo(copy);
            return copy;
        }
    }
}
