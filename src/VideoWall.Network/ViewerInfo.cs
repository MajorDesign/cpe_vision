using System.Text.Json.Serialization;

namespace VideoWall.Network
{
    /// <summary>
    /// Identidade de um agente Viewer (mini-PC) anunciada na rede e recebida
    /// pelo Controlador para listar as telas disponíveis.
    /// </summary>
    public sealed class ViewerInfo
    {
        /// <summary>Tipo da mensagem (filtra pacotes que não são do VideoWall).</summary>
        public string Type { get; set; } = DiscoveryConstants.MessageType;

        /// <summary>Identificador estável (nome da máquina).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Nome amigável da tela (ex.: "TV Portaria").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Endereço IPv4 do mini-PC na rede.</summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>Porta TCP para comandos (reservada para a etapa de controle).</summary>
        public int ControlPort { get; set; }

        /// <summary>Se o overlay de vídeo por hardware está LIGADO neste terminal
        /// (para o controlador mostrar o estado no botão).</summary>
        public bool HardwareOverlay { get; set; }

        /// <summary>Momento do último anúncio recebido (preenchido pelo listener).</summary>
        [JsonIgnore]
        public DateTime LastSeenUtc { get; set; }
    }
}
