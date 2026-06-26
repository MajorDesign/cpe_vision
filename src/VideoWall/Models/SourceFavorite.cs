namespace VideoWall.Models
{
    /// <summary>
    /// Fonte favorita salva na biblioteca, reutilizável para montar paredes
    /// rapidamente. A <see cref="Category"/> define o tipo gerado ao adicionar à
    /// parede e o significado de <see cref="Payload"/>.
    /// </summary>
    public class SourceFavorite
    {
        /// <summary>"Web", "Câmera", "Imagem" ou "Aplicativo".</summary>
        public string Category { get; set; } = "Web";

        /// <summary>Nome amigável exibido na biblioteca.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Conteúdo: URL (Web), URL RTSP (Câmera), caminho (Imagem) ou título da janela (Aplicativo).</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>
        /// Quando verdadeiro, a fonte Web é uma miniatura sobreposta (live/PiP): ao
        /// adicionar à parede, vira um overlay (não um navegador de célula inteira).
        /// </summary>
        public bool Overlay { get; set; }
    }
}
