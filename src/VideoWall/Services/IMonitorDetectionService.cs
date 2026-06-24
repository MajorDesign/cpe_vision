using VideoWall.Models;

namespace VideoWall.Services
{
    public interface IMonitorDetectionService : IDisposable
    {
        List<MonitorInfo> GetAllMonitors();
        event EventHandler? MonitorsChanged;
    }
}
