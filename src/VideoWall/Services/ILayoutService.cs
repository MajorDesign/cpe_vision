using VideoWall.Models;

namespace VideoWall.Services
{
    /// <summary>
    /// Persiste e recupera layouts da parede (o "Assistente Visual"): cada layout
    /// é um conjunto nomeado de fontes com posição, tamanho e propriedades.
    /// </summary>
    public interface ILayoutService
    {
        /// <summary>Nomes dos layouts salvos.</summary>
        IReadOnlyList<string> List();

        /// <summary>Salva (ou sobrescreve) um layout com o nome informado.</summary>
        void Save(string name, IEnumerable<WallElement> elements);

        /// <summary>
        /// Carrega um layout como novos elementos prontos para uso. Retorna null
        /// se o layout não existir. Fontes de aplicativo voltam sem captura ativa
        /// (apenas com o título), cabendo ao chamador reconectá-las.
        /// </summary>
        IReadOnlyList<WallElement>? Load(string name);

        /// <summary>Exclui o layout informado, se existir.</summary>
        void Delete(string name);
    }
}
