namespace VideoWall.Models
{
    /// <summary>Configurações persistentes do aplicativo.</summary>
    public class AppSettings
    {
        /// <summary>
        /// Layout carregado automaticamente ao abrir (fail-safe). Nulo se não
        /// houver um layout principal definido.
        /// </summary>
        public string? MainLayoutName { get; set; }
    }
}
