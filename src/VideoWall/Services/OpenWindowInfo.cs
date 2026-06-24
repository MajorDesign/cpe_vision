namespace VideoWall.Services
{
    /// <summary>Janela de nível superior aberta, candidata a ser espelhada.</summary>
    public sealed class OpenWindowInfo
    {
        public IntPtr Handle { get; init; }
        public string Title { get; init; } = string.Empty;

        public override string ToString() => Title;
    }
}
