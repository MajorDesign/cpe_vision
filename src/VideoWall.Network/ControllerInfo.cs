namespace VideoWall.Network
{
    /// <summary>
    /// Identidade do computador central (Controlador), anunciada na rede para
    /// que os terminais saibam onde buscar atualizações automaticamente.
    /// </summary>
    public sealed class ControllerInfo
    {
        public const string ControllerType = "videowall-controller";

        public string Type { get; set; } = ControllerType;

        /// <summary>IPv4 do computador central.</summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>Porta HTTP do servidor de atualização.</summary>
        public int UpdatePort { get; set; }
    }
}
