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

        /// <summary>
        /// Tela (terminal) dona do layout principal. Layouts pertencem a uma tela;
        /// sem isto, ao reabrir, o controlador procuraria o nome entre os layouts
        /// antigos sem dono e não o encontraria.
        /// </summary>
        public string? MainLayoutScreenId { get; set; }
    }
}
