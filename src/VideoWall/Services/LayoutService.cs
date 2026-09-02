using System.IO;
using System.Text.Json;
using VideoWall.Models;
using VideoWall.Models.Persistence;

namespace VideoWall.Services
{
    /// <inheritdoc cref="ILayoutService"/>
    public class LayoutService : ILayoutService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _folder;

        /// <param name="baseFolder">
        /// Pasta raiz dos layouts. Em produção fica vazio (usa %LocalAppData%); serve
        /// para exercitar o serviço em testes sem tocar nos layouts reais do operador.
        /// </param>
        public LayoutService(string? baseFolder = null)
        {
            _folder = baseFolder ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VideoWall", "Layouts");
            Directory.CreateDirectory(_folder);
        }

        public IReadOnlyList<string> List(string? screenId)
        {
            // Os da tela primeiro; os antigos (sem dono) entram só se a tela não
            // tiver um layout com o mesmo nome — assim, ao regravar um layout antigo
            // numa tela, a lista passa a mostrar o dela, sem duplicar.
            var nomes = new List<string>();
            foreach (var pasta in new[] { FolderFor(screenId), _folder })
            {
                if (!Directory.Exists(pasta))
                    continue;

                foreach (var arquivo in Directory.GetFiles(pasta, "*.json"))
                {
                    string? nome = Path.GetFileNameWithoutExtension(arquivo);
                    if (!string.IsNullOrEmpty(nome) &&
                        !nomes.Contains(nome, StringComparer.CurrentCultureIgnoreCase))
                        nomes.Add(nome);
                }
            }

            return nomes.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public void Save(string name, string? screenId, IEnumerable<WallElement> elements)
        {
            var dto = new LayoutDto
            {
                Elements = elements.Select(ToDto).ToList(),
            };

            string pasta = FolderFor(screenId);
            Directory.CreateDirectory(pasta);

            string json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(Path.Combine(pasta, Sanitize(name) + ".json"), json);
        }

        public IReadOnlyList<WallElement>? Load(string name, string? screenId)
        {
            string? path = ResolvePath(name, screenId);
            if (path == null)
                return null;

            var dto = JsonSerializer.Deserialize<LayoutDto>(File.ReadAllText(path));
            if (dto == null)
                return null;

            return dto.Elements.Select(FromDto).Where(e => e != null).Select(e => e!).ToList();
        }

        public void Delete(string name, string? screenId)
        {
            string? path = ResolvePath(name, screenId);
            if (path != null)
                File.Delete(path);
        }

        /// <summary>Pasta dos layouts de uma tela; a raiz é dos antigos, sem dono.</summary>
        private string FolderFor(string? screenId) =>
            string.IsNullOrWhiteSpace(screenId) ? _folder : Path.Combine(_folder, Sanitize(screenId));

        /// <summary>Procura o arquivo: primeiro entre os da tela, depois entre os sem dono.</summary>
        private string? ResolvePath(string name, string? screenId)
        {
            string arquivo = Sanitize(name) + ".json";
            foreach (var pasta in new[] { FolderFor(screenId), _folder })
            {
                string caminho = Path.Combine(pasta, arquivo);
                if (File.Exists(caminho))
                    return caminho;
            }
            return null;
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "layout" : name.Trim();
        }

        // ===================== Mapeamento modelo <-> DTO =====================

        private static ElementDto ToDto(WallElement e)
        {
            var dto = new ElementDto
            {
                Type = e.Kind,
                Name = e.Name,
                X = e.X,
                Y = e.Y,
                Width = e.Width,
                Height = e.Height,
                ZIndex = e.ZIndex,
                Opacity = e.Opacity,
                IsVisible = e.IsVisible,
            };

            switch (e)
            {
                case ColorElement c:
                    dto.ColorHex = c.ColorHex;
                    break;
                case TextElement t:
                    dto.Text = t.Text;
                    dto.FontSize = t.FontSize;
                    dto.ForegroundHex = t.ForegroundHex;
                    break;
                case ImageElement i:
                    dto.ImagePath = i.ImagePath;
                    break;
                case BrowserElement b:
                    dto.Url = b.Url;
                    dto.ZoomFactor = b.ZoomFactor;
                    break;
                case WindowCaptureElement w:
                    dto.WindowTitle = w.WindowTitle;
                    break;
                case CameraElement cam:
                    dto.StreamUrl = cam.StreamUrl;
                    break;
            }

            return dto;
        }

        private static WallElement? FromDto(ElementDto dto)
        {
            WallElement? element = dto.Type switch
            {
                "Cor" => new ColorElement { ColorHex = dto.ColorHex ?? "#3B82F6" },
                "Texto" => new TextElement
                {
                    Text = dto.Text ?? "Texto",
                    FontSize = dto.FontSize ?? 48,
                    ForegroundHex = dto.ForegroundHex ?? "#FFFFFF",
                },
                "Imagem" => new ImageElement { ImagePath = dto.ImagePath ?? string.Empty },
                "Navegador" => new BrowserElement { Url = dto.Url ?? "https://", ZoomFactor = dto.ZoomFactor ?? 1.0 },
                "Aplicativo" => new WindowCaptureElement { WindowTitle = dto.WindowTitle ?? string.Empty },
                "Câmera" => new CameraElement { StreamUrl = dto.StreamUrl ?? "rtsp://" },
                _ => null,
            };

            if (element == null)
                return null;

            element.Name = dto.Name;
            element.X = dto.X;
            element.Y = dto.Y;
            element.Width = dto.Width;
            element.Height = dto.Height;
            element.ZIndex = dto.ZIndex;
            element.Opacity = dto.Opacity;
            element.IsVisible = dto.IsVisible;
            return element;
        }
    }
}
