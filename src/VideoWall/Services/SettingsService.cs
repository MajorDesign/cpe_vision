using System.IO;
using System.Text.Json;
using VideoWall.Models;

namespace VideoWall.Services
{
    /// <inheritdoc cref="ISettingsService"/>
    public class SettingsService : ISettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _filePath;

        public SettingsService()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VideoWall");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "settings.json");
        }

        public AppSettings Load()
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            try
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath)) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
    }
}
