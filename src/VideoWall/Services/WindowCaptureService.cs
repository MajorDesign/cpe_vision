using System.Windows;
using VideoWall.Capture;
using VideoWall.Models;

namespace VideoWall.Services
{
    /// <inheritdoc cref="IWindowCaptureService"/>
    public class WindowCaptureService : IWindowCaptureService
    {
        private readonly Dictionary<WindowCaptureElement, WindowCaptureSession> _sessions = new();

        public bool IsSupported => WindowCaptureSession.IsSupported;

        public void Start(WindowCaptureElement element)
        {
            Stop(element);

            var dispatcher = Application.Current.Dispatcher;
            var session = new WindowCaptureSession(
                new IntPtr(element.WindowHandle),
                dispatcher,
                bitmap => element.Frame = bitmap);

            _sessions[element] = session;
            session.Start();
        }

        public void Stop(WindowCaptureElement element)
        {
            if (_sessions.TryGetValue(element, out var session))
            {
                session.Dispose();
                _sessions.Remove(element);
            }
        }

        public void StopAll()
        {
            foreach (var session in _sessions.Values.ToList())
            {
                session.Dispose();
            }

            _sessions.Clear();
        }
    }
}
