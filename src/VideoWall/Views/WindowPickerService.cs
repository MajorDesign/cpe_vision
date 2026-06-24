using System.Text;
using System.Windows;
using VideoWall.Native;
using VideoWall.Services;

namespace VideoWall.Views
{
    /// <inheritdoc cref="IWindowPickerService"/>
    public class WindowPickerService : IWindowPickerService
    {
        public OpenWindowInfo? PickWindow()
        {
            var windows = EnumerateWindows();

            var dialog = new WindowPickerWindow(windows)
            {
                Owner = Application.Current.MainWindow,
            };

            return dialog.ShowDialog() == true ? dialog.SelectedWindow : null;
        }

        public OpenWindowInfo? FindByTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;

            var windows = EnumerateWindows();
            return windows.FirstOrDefault(w => string.Equals(w.Title, title, StringComparison.CurrentCultureIgnoreCase))
                ?? windows.FirstOrDefault(w => w.Title.Contains(title, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>Lista as janelas de nível superior visíveis e com título.</summary>
        private static List<OpenWindowInfo> EnumerateWindows()
        {
            var result = new List<OpenWindowInfo>();
            IntPtr self = Application.Current.MainWindow != null
                ? new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle
                : IntPtr.Zero;

            WindowEnumNative.EnumWindows((hWnd, _) =>
            {
                if (hWnd == self)
                    return true;
                if (!WindowEnumNative.IsWindowVisible(hWnd))
                    return true;

                int length = WindowEnumNative.GetWindowTextLength(hWnd);
                if (length == 0)
                    return true;

                // Ignora janelas-ferramenta (sem botão na barra de tarefas).
                int exStyle = WindowEnumNative.GetWindowLong(hWnd, WindowEnumNative.GWL_EXSTYLE);
                if ((exStyle & WindowEnumNative.WS_EX_TOOLWINDOW) != 0)
                    return true;

                // Ignora janelas UWP "fantasma" (cloaked).
                if (WindowEnumNative.DwmGetWindowAttribute(hWnd, WindowEnumNative.DWMWA_CLOAKED,
                        out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                var sb = new StringBuilder(length + 1);
                WindowEnumNative.GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title))
                    return true;

                result.Add(new OpenWindowInfo { Handle = hWnd, Title = title });
                return true;
            }, IntPtr.Zero);

            return result.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
