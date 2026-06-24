using VideoWall.Models;

namespace VideoWall.Services
{
    /// <summary>Persiste a biblioteca de fontes favoritas.</summary>
    public interface IFavoritesService
    {
        IReadOnlyList<SourceFavorite> Load();
        void Save(IEnumerable<SourceFavorite> favorites);
    }
}
