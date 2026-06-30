using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VideoWall.Network;

namespace VideoWall.Viewer
{
    /// <summary>
    /// Persiste em disco o layout que o terminal está exibindo (fontes + URLs ao vivo),
    /// para RESTAURAR ao reabrir — depois de uma atualização, do botão de overlay, ou
    /// de uma queda de energia — sem precisar reprojetar tudo do controlador. A sessão/
    /// login fica na pasta do WebView2 (LocalAppData), então a página volta logada.
    /// </summary>
    internal static class TerminalLayoutStore
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CPE Tecnologia", "VideoWall", "last-layout.json");

        public static void Save(IReadOnlyList<ScreenSource>? sources)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(dir);

                if (sources == null || sources.Count == 0)
                {
                    if (File.Exists(FilePath)) File.Delete(FilePath);
                    return;
                }

                File.WriteAllText(FilePath, JsonSerializer.Serialize(sources));
            }
            catch { /* sem permissão / disco cheio: ignora */ }
        }

        public static List<ScreenSource>? Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return null;
                return JsonSerializer.Deserialize<List<ScreenSource>>(File.ReadAllText(FilePath));
            }
            catch { return null; }
        }
    }
}
