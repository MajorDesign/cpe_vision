using System.IO;
using System.Text.Json;
using VideoWall.Models;

namespace VideoWall.Services
{
    /// <inheritdoc cref="IScheduleService"/>
    public class ScheduleService : IScheduleService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _filePath;

        public ScheduleService()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VideoWall");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "schedules.json");
        }

        public IReadOnlyList<ScheduleEntry> Load()
        {
            if (!File.Exists(_filePath))
                return Array.Empty<ScheduleEntry>();

            try
            {
                return JsonSerializer.Deserialize<List<ScheduleEntry>>(File.ReadAllText(_filePath))
                       ?? new List<ScheduleEntry>();
            }
            catch
            {
                return Array.Empty<ScheduleEntry>();
            }
        }

        public void Save(IEnumerable<ScheduleEntry> schedules)
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(schedules.ToList(), JsonOptions));
        }
    }
}
