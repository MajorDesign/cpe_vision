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

        public LayoutService()
        {
            _folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VideoWall", "Layouts");
            Directory.CreateDirectory(_folder);
        }

        public IReadOnlyList<string> List()
        {
            if (!Directory.Exists(_folder))
                return Array.Empty<string>();

            return Directory.GetFiles(_folder, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public void Save(string name, IEnumerable<WallElement> elements)
        {
            var dto = new LayoutDto
            {
                Elements = elements.Select(ToDto).ToList(),
            };

            string json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(PathFor(name), json);
        }

        public IReadOnlyList<WallElement>? Load(string name)
        {
            string path = PathFor(name);
            if (!File.Exists(path))
                return null;

            var dto = JsonSerializer.Deserialize<LayoutDto>(File.ReadAllText(path));
            if (dto == null)
                return null;

            return dto.Elements.Select(FromDto).Where(e => e != null).Select(e => e!).ToList();
        }

        public void Delete(string name)
        {
            string path = PathFor(name);
            if (File.Exists(path))
                File.Delete(path);
        }

        private string PathFor(string name) => Path.Combine(_folder, Sanitize(name) + ".json");

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
