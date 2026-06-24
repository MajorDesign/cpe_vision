namespace VideoWall.Network
{
    /// <summary>Constantes do protocolo de descoberta na rede local.</summary>
    public static class DiscoveryConstants
    {
        /// <summary>Porta UDP usada para os anúncios (beacons) dos viewers.</summary>
        public const int Port = 48010;

        /// <summary>Identifica o tipo da mensagem, para ignorar pacotes alheios.</summary>
        public const string MessageType = "videowall-viewer";

        /// <summary>Intervalo entre anúncios do viewer.</summary>
        public static readonly TimeSpan BeaconInterval = TimeSpan.FromSeconds(2);

        /// <summary>Tempo sem anúncio após o qual um viewer é considerado offline.</summary>
        public static readonly TimeSpan OfflineTimeout = TimeSpan.FromSeconds(6);
    }
}
