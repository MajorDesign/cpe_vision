using System.Text.Json.Serialization;

namespace VideoWall.Models
{
    /// <summary>
    /// Agendamento que carrega um layout em um horário. Quando <see cref="Days"/>
    /// está vazio, repete todos os dias; senão, apenas nos dias indicados
    /// (0 = Domingo … 6 = Sábado).
    /// </summary>
    public class ScheduleEntry
    {
        public int Hour { get; set; }
        public int Minute { get; set; }
        public List<int> Days { get; set; } = new();
        public string LayoutName { get; set; } = string.Empty;

        /// <summary>Tela (terminal) de destino: id estável + nome para exibição.</summary>
        public string ScreenId { get; set; } = string.Empty;
        public string ScreenName { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        [JsonIgnore]
        public string ScreenText => string.IsNullOrEmpty(ScreenName) ? "(sem tela)" : ScreenName;

        /// <summary>Marca o último disparo, para evitar repetição no mesmo minuto.</summary>
        [JsonIgnore]
        public DateTime? LastFired { get; set; }

        [JsonIgnore]
        public string TimeText => $"{Hour:00}:{Minute:00}";

        [JsonIgnore]
        public string DaysText =>
            Days == null || Days.Count == 0
                ? "Todos os dias"
                : string.Join(", ", Days.OrderBy(d => d).Select(DayName));

        private static readonly string[] DayNames = { "Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb" };

        public static string DayName(int day) => day >= 0 && day < 7 ? DayNames[day] : "?";
    }
}
