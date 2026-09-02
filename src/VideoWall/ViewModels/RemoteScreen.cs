using System.Collections.ObjectModel;
using System.Windows.Media;
using VideoWall.Models;
using VideoWall.Network;

namespace VideoWall.ViewModels
{
    /// <summary>
    /// Uma tela de rede (terminal) do ponto de vista do controlador: dados de
    /// identidade + o layout atualmente transmitido (usado na miniatura e ao
    /// reabrir a tela para edição).
    /// </summary>
    public sealed class RemoteScreen : BaseViewModel
    {
        public RemoteScreen(ViewerInfo info)
        {
            Info = info;
        }

        public ViewerInfo Info { get; private set; }

        public string Id => Info.Id;
        public string Name => Info.Name;
        public string IpAddress => Info.IpAddress;
        public int ControlPort => Info.ControlPort;

        /// <summary>Overlay de vídeo por hardware ligado neste terminal (vindo do anúncio).</summary>
        public bool OverlayOn => Info.HardwareOverlay;

        /// <summary>Endereço + versão do terminal, como aparece na lista de telas.</summary>
        public string Address
        {
            get
            {
                string texto = string.IsNullOrEmpty(Info.Version)
                    ? Info.IpAddress
                    : $"{Info.IpAddress}  ·  v{Info.Version}";
                return Outdated ? texto + "  ·  atualizando…" : texto;
            }
        }

        private bool _outdated;

        /// <summary>
        /// Esta tela está numa versão anterior à que o central serve. Fica à vista na
        /// lista porque uma parede com telas em versões diferentes se comporta de um
        /// jeito na tela A e de outro na B — e isso precisa ser óbvio, não descoberto.
        /// </summary>
        public bool Outdated
        {
            get => _outdated;
            set { if (SetProperty(ref _outdated, value)) OnPropertyChanged(nameof(Address)); }
        }

        /// <summary>Layout atualmente transmitido (coords em tela 16:9, p/ a miniatura).</summary>
        public ObservableCollection<WallElement> Layout { get; } = new();

        private ImageSource? _liveThumbnail;

        /// <summary>Foto ao vivo do que o terminal está exibindo (atualizada periodicamente).</summary>
        public ImageSource? LiveThumbnail
        {
            get => _liveThumbnail;
            set => SetProperty(ref _liveThumbnail, value);
        }

        public void UpdateInfo(ViewerInfo info)
        {
            Info = info;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(IpAddress));
            OnPropertyChanged(nameof(Address));
            OnPropertyChanged(nameof(OverlayOn));
        }

        // Exibido em ComboBox/listas (evita aparecer o nome do tipo).
        public override string ToString() => Name;
    }
}
