using System.IO;
using System.Text.Json;
using VideoWall.Models;

namespace VideoWall.Services
{
    /// <inheritdoc cref="IFavoritesService"/>
    public class FavoritesService : IFavoritesService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _filePath;

        public FavoritesService()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VideoWall");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "favorites.json");
        }

        public IReadOnlyList<SourceFavorite> Load()
        {
            if (!File.Exists(_filePath))
                return Array.Empty<SourceFavorite>();

            try
            {
                return JsonSerializer.Deserialize<List<SourceFavorite>>(File.ReadAllText(_filePath))
                       ?? new List<SourceFavorite>();
            }
            catch
            {
                return Array.Empty<SourceFavorite>();
            }
        }

        public void Save(IEnumerable<SourceFavorite> favorites)
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(favorites.ToList(), JsonOptions));
        }
    }
}
