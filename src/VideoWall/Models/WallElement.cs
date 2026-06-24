namespace VideoWall.Models
{
    /// <summary>
    /// Elemento posicionável na parede de vídeo (videowall).
    ///
    /// As coordenadas (X, Y) e o tamanho (Width, Height) são expressos no
    /// sistema de coordenadas da PAREDE VIRTUAL — um plano único e contínuo que
    /// atravessa todos os monitores. Cada janela de saída renderiza apenas o
    /// recorte da parede que cai sobre o seu monitor, de modo que um elemento
    /// pode aparecer estendido entre vários monitores e/ou sobreposto a outros.
    ///
    /// Unidade: pixels independentes de dispositivo (DIP). Em escala de exibição
    /// 100% (96 DPI), 1 DIP equivale a 1 pixel físico.
    /// </summary>
    public abstract class WallElement : ObservableObject
    {
        private string _name = string.Empty;
        private double _x;
        private double _y;
        private double _width = 320;
        private double _height = 180;
        private int _zIndex;
        private double _opacity = 1.0;
        private bool _isVisible = true;

        /// <summary>Identificador único e imutável do elemento.</summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>Rótulo amigável exibido na lista de elementos.</summary>
        public abstract string Kind { get; }

        /// <summary>Resumo do conteúdo (ex.: URL) exibido na lista de fontes.</summary>
        public virtual string Summary => string.Empty;

        /// <summary>Cria uma cópia independente deste elemento (novo Id).</summary>
        public abstract WallElement Clone();

        /// <summary>Copia as propriedades comuns para o destino (usado por Clone).</summary>
        protected void CopyBaseTo(WallElement target)
        {
            target.Name = Name;
            target.X = X;
            target.Y = Y;
            target.Width = Width;
            target.Height = Height;
            target.ZIndex = ZIndex;
            target.Opacity = Opacity;
            target.IsVisible = IsVisible;
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        public double Width
        {
            get => _width;
            set => SetProperty(ref _width, Math.Max(1, value));
        }

        public double Height
        {
            get => _height;
            set => SetProperty(ref _height, Math.Max(1, value));
        }

        /// <summary>Ordem de empilhamento. Valores maiores ficam à frente (sobreposição).</summary>
        public int ZIndex
        {
            get => _zIndex;
            set => SetProperty(ref _zIndex, value);
        }

        /// <summary>Opacidade de 0.0 (transparente) a 1.0 (opaco).</summary>
        public double Opacity
        {
            get => _opacity;
            set => SetProperty(ref _opacity, Math.Clamp(value, 0.0, 1.0));
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }
    }
}
