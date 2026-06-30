namespace VideoWall.Viewer
{
    /// <summary>
    /// Janela sobreposta (PiP) sempre-no-topo no terminal — pode hospedar um navegador
    /// (WebView2, para lives do YouTube embed) ou o VLC (câmeras/lives nativas, leves).
    /// </summary>
    internal interface IOverlay
    {
        /// <summary>Define o endereço/stream exibido (recarrega só quando muda).</summary>
        void SetUrl(string url);

        /// <summary>Posiciona a janela na célula (pixels físicos) e a mostra no topo.</summary>
        void PlaceOnScreen(int x, int y, int w, int h);

        /// <summary>Libera os recursos e fecha a janela.</summary>
        void CloseOverlay();
    }
}
