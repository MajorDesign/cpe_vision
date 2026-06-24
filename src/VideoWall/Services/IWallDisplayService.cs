using System.Collections.ObjectModel;
using VideoWall.Models;

namespace VideoWall.Services
{
    /// <summary>
    /// Gerencia o ciclo de vida das janelas de saída (uma por monitor) que
    /// compõem fisicamente a parede de vídeo.
    /// </summary>
    public interface IWallDisplayService
    {
        /// <summary>Indica se a parede está atualmente projetada nos monitores.</summary>
        bool IsRunning { get; }

        /// <summary>
        /// Abre uma janela de saída sobre cada monitor informado, cada uma
        /// renderizando apenas os elementos que a intersectam (parede virtual).
        /// </summary>
        /// <param name="monitors">Monitores de destino.</param>
        /// <param name="elements">Coleção compartilhada de elementos da parede.</param>
        /// <param name="dpiScale">Fator de escala DPI atual do sistema.</param>
        void Start(IEnumerable<MonitorInfo> monitors, ObservableCollection<WallElement> elements, double dpiScale);

        /// <summary>Fecha todas as janelas de saída abertas.</summary>
        void Stop();
    }
}
