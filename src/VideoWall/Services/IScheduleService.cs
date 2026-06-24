using VideoWall.Models;

namespace VideoWall.Services
{
    /// <summary>Persiste os agendamentos de troca de layout.</summary>
    public interface IScheduleService
    {
        IReadOnlyList<ScheduleEntry> Load();
        void Save(IEnumerable<ScheduleEntry> schedules);
    }
}
