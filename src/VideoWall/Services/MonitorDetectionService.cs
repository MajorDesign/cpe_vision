using System.Runtime.InteropServices;
using Microsoft.Win32;
using VideoWall.Models;
using VideoWall.Native;

namespace VideoWall.Services
{
    public class MonitorDetectionService : IMonitorDetectionService
    {
        public event EventHandler? MonitorsChanged;

        public MonitorDetectionService()
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        public List<MonitorInfo> GetAllMonitors()
        {
            var monitors = new List<MonitorInfo>();
            int index = 0;

            NativeMethods.EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
                {
                    var monitorInfoEx = new MONITORINFOEX();
                    monitorInfoEx.CbSize = Marshal.SizeOf<MONITORINFOEX>();

                    if (NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfoEx))
                    {
                        index++;

                        var monitor = new MonitorInfo
                        {
                            Index = index,
                            DeviceName = monitorInfoEx.SzDevice?.Trim('\0') ?? $"Monitor {index}",
                            IsPrimary = (monitorInfoEx.DwFlags & MONITORINFOEX.MONITORINFOF_PRIMARY) != 0,
                            X = monitorInfoEx.RcMonitor.Left,
                            Y = monitorInfoEx.RcMonitor.Top,
                            Width = monitorInfoEx.RcMonitor.Width,
                            Height = monitorInfoEx.RcMonitor.Height,
                            WorkAreaX = monitorInfoEx.RcWork.Left,
                            WorkAreaY = monitorInfoEx.RcWork.Top,
                            WorkAreaWidth = monitorInfoEx.RcWork.Width,
                            WorkAreaHeight = monitorInfoEx.RcWork.Height,
                        };

                        monitors.Add(monitor);
                    }

                    return true;
                },
                IntPtr.Zero);

            return monitors;
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            MonitorsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            GC.SuppressFinalize(this);
        }
    }
}
