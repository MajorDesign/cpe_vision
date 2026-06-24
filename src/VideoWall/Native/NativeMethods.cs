using System.Runtime.InteropServices;

namespace VideoWall.Native
{
    public delegate bool MonitorEnumDelegate(
        IntPtr hMonitor,
        IntPtr hdcMonitor,
        ref RECT lprcMonitor,
        IntPtr dwData);

    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr lprcClip,
            MonitorEnumDelegate lpfnEnum,
            IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(
            IntPtr hMonitor,
            ref MONITORINFOEX lpmi);
    }
}
