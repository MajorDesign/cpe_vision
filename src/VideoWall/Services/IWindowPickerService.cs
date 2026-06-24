namespace VideoWall.Services
{
    /// <summary>
    /// Apresenta ao usuário a lista de janelas abertas e devolve a escolhida
    /// (ou <c>null</c> se cancelado).
    /// </summary>
    public interface IWindowPickerService
    {
        OpenWindowInfo? PickWindow();

        /// <summary>
        /// Procura uma janela aberta cujo título corresponda ao informado
        /// (exato e, em seguida, parcial). Usado ao restaurar layouts.
        /// </summary>
        OpenWindowInfo? FindByTitle(string title);
    }
}
