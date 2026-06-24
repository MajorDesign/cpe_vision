using VideoWall.Models;

namespace VideoWall.Services
{
    /// <summary>
    /// Gerencia as sessões de espelhamento de janela (uma por elemento de
    /// captura), iniciando e liberando os recursos de captura.
    /// </summary>
    public interface IWindowCaptureService
    {
        /// <summary>Indica se a captura de janela é suportada neste Windows.</summary>
        bool IsSupported { get; }

        /// <summary>Inicia a captura da janela associada ao elemento.</summary>
        void Start(WindowCaptureElement element);

        /// <summary>Encerra a captura associada ao elemento, se houver.</summary>
        void Stop(WindowCaptureElement element);

        /// <summary>Encerra todas as sessões de captura.</summary>
        void StopAll();
    }
}
