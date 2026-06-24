using VideoWall.Models;

namespace VideoWall.Services
{
    /// <summary>Carrega e salva as configurações persistentes do aplicativo.</summary>
    public interface ISettingsService
    {
        AppSettings Load();
        void Save(AppSettings settings);
    }
}
