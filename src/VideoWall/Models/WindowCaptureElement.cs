using System.Windows.Media;

namespace VideoWall.Models
{
    /// <summary>
    /// Fonte do tipo "software": espelha ao vivo a janela de um aplicativo já
    /// aberto (via Windows.Graphics.Capture). O frame mais recente é exposto em
    /// <see cref="Frame"/> e exibido tanto na pré-visualização quanto nas telas.
    /// </summary>
    public class WindowCaptureElement : WallElement
    {
        private string _windowTitle = string.Empty;
        private ImageSource? _frame;

        public override string Kind => "Aplicativo";

        /// <summary>Título da janela capturada (apenas informativo).</summary>
        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        /// <summary>Handle (HWND) da janela de origem.</summary>
        public long WindowHandle { get; set; }

        /// <summary>Último frame capturado da janela; vinculado a um <c>Image</c>.</summary>
        public ImageSource? Frame
        {
            get => _frame;
            set => SetProperty(ref _frame, value);
        }

        public override WallElement Clone()
        {
            var copy = new WindowCaptureElement { WindowTitle = WindowTitle, WindowHandle = WindowHandle };
            CopyBaseTo(copy);
            return copy;
        }
    }
}
