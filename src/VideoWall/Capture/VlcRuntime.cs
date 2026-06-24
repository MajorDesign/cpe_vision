using LibVLCSharp.Shared;

namespace VideoWall.Capture
{
    /// <summary>
    /// Instância compartilhada do LibVLC (motor do VLC), criada sob demanda na
    /// primeira vez que uma fonte de câmera/vídeo é usada.
    /// </summary>
    internal static class VlcRuntime
    {
        private static readonly Lazy<LibVLC> Lazy = new(() =>
        {
            Core.Initialize();
            return new LibVLC();
        });

        public static LibVLC Instance => Lazy.Value;
    }
}
