using VideoWall.Models;

namespace VideoWall.Services
{
    /// <summary>
    /// Persiste e recupera layouts da parede (o "Assistente Visual"): cada layout
    /// é um conjunto nomeado de fontes com posição, tamanho e propriedades.
    ///
    /// Layouts pertencem a UMA TELA. Cada terminal tem sua própria parede — uma
    /// grade que serve para a tela da portaria não serve para a da sala de
    /// controle —, e misturá-las levava a projetar o conteúdo errado. O
    /// <c>screenId</c> é o identificador do terminal (nome da máquina).
    ///
    /// Layouts salvos antes disso não têm dono: aparecem para todas as telas, para
    /// nada se perder, e cada um deixa de ser genérico quando é salvo numa tela.
    /// </summary>
    public interface ILayoutService
    {
        /// <summary>Nomes dos layouts da tela informada, mais os antigos sem dono.</summary>
        IReadOnlyList<string> List(string? screenId);

        /// <summary>Salva (ou sobrescreve) um layout DA TELA informada.</summary>
        void Save(string name, string? screenId, IEnumerable<WallElement> elements);

        /// <summary>
        /// Carrega um layout como novos elementos prontos para uso. Procura primeiro
        /// entre os da tela e depois entre os antigos sem dono. Retorna null se não
        /// existir. Fontes de aplicativo voltam sem captura ativa (apenas com o
        /// título), cabendo ao chamador reconectá-las.
        /// </summary>
        IReadOnlyList<WallElement>? Load(string name, string? screenId);

        /// <summary>Exclui o layout informado, se existir.</summary>
        void Delete(string name, string? screenId);
    }
}
