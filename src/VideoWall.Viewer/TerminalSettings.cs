using System;
using System.IO;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Preferências locais do terminal, lidas no início (antes de criar o WebView2).
    /// Hoje guarda só se o OVERLAY DE VÍDEO por hardware está ligado: por padrão fica
    /// DESLIGADO (vídeo composto pela GPU — sempre visível); ligado, o vídeo usa o plano
    /// de overlay (mais leve na GPU, mas pode ficar preto em algumas placas/TVs).
    /// </summary>
    internal static class TerminalSettings
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CPE Tecnologia", "VideoWall", "hwoverlay.txt");

        /// <summary>True = overlay de vídeo por hardware LIGADO.</summary>
        public static bool HardwareVideoOverlay
        {
            get
            {
                try { return File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() == "1"; }
                catch { return false; }
            }
        }

        public static void SetHardwareVideoOverlay(bool on)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, on ? "1" : "0");
            }
            catch { /* sem permissão de escrita: mantém o padrão */ }
        }
    }
}
