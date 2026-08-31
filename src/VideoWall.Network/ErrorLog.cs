using System.Text;

namespace VideoWall.Network
{
    /// <summary>
    /// Registro de erros em arquivo. Existe porque uma falha em campo — num painel de
    /// operação ou numa TV sem ninguém por perto — não deixa rastro nenhum: sem isto, a
    /// única evidência de um travamento fica no Visualizador de Eventos do Windows, se
    /// alguém souber procurar.
    /// </summary>
    public static class ErrorLog
    {
        private const long MaxBytes = 1_000_000;
        private static readonly object Lock = new();

        /// <summary>Caminho do arquivo de log (em pasta sempre gravável).</summary>
        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CPE Tecnologia", "VideoWall", "erros.log");

        /// <summary>Anota um erro. Nunca lança: registrar falha não pode derrubar o app.</summary>
        public static void Write(string contexto, Exception? ex)
        {
            try
            {
                var texto = new StringBuilder()
                    .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {contexto}")
                    .AppendLine(ex?.ToString() ?? "(sem exceção)")
                    .AppendLine()
                    .ToString();

                lock (Lock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                    // Recomeça o arquivo quando fica grande: um terminal roda por meses.
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                        File.Delete(FilePath);

                    File.AppendAllText(FilePath, texto);
                }
            }
            catch
            {
                // Sem disco / sem permissão: seguir em frente é mais importante.
            }
        }
    }
}
