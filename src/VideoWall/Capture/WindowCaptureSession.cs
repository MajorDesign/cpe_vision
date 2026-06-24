using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace VideoWall.Capture
{
    /// <summary>
    /// Captura ao vivo de uma janela via Windows.Graphics.Capture. Cada frame é
    /// convertido em pixels BGRA e escrito num <see cref="WriteableBitmap"/> da
    /// thread de UI, que pode ser exibido por uma ou mais telas simultaneamente.
    /// </summary>
    public sealed class WindowCaptureSession : IDisposable
    {
        private readonly IntPtr _hwnd;
        private readonly Dispatcher _dispatcher;
        private readonly Action<ImageSource> _onBitmapReady;

        private IDirect3DDevice? _device;
        private GraphicsCaptureItem? _item;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;
        private WriteableBitmap? _bitmap;
        private SizeInt32 _size;
        private bool _disposed;

        /// <param name="hwnd">Handle da janela a capturar.</param>
        /// <param name="dispatcher">Dispatcher da thread de UI (onde o bitmap vive).</param>
        /// <param name="onBitmapReady">Chamado na UI quando o bitmap (re)cria; entregue ao elemento.</param>
        public WindowCaptureSession(IntPtr hwnd, Dispatcher dispatcher, Action<ImageSource> onBitmapReady)
        {
            _hwnd = hwnd;
            _dispatcher = dispatcher;
            _onBitmapReady = onBitmapReady;
        }

        /// <summary>Indica se a captura de tela é suportada neste Windows.</summary>
        public static bool IsSupported => GraphicsCaptureSession.IsSupported();

        public void Start()
        {
            _device = Direct3D11Helper.CreateDevice();
            _item = Direct3D11Helper.CreateItemForWindow(_hwnd);
            _item.Closed += (_, _) => Dispose();

            _size = _item.Size;
            EnsureBitmap(_size);

            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
            _framePool.FrameArrived += OnFrameArrived;

            _session = _framePool.CreateCaptureSession(_item);
            _session.StartCapture();
        }

        private void EnsureBitmap(SizeInt32 size)
        {
            int w = Math.Max(1, size.Width);
            int h = Math.Max(1, size.Height);

            _dispatcher.Invoke(() =>
            {
                if (_disposed)
                    return;

                _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                _onBitmapReady(_bitmap);
            });
        }

        // async void: handler de evento. A conversão é aguardada (await) e não
        // bloqueada — bloquear aqui causaria deadlock no callback do framepool.
        private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            if (_disposed)
                return;

            using var frame = sender.TryGetNextFrame();
            if (frame == null)
                return;

            // A janela mudou de tamanho: recria o pool e o bitmap.
            var contentSize = frame.ContentSize;
            if ((contentSize.Width != _size.Width || contentSize.Height != _size.Height)
                && contentSize.Width > 0 && contentSize.Height > 0)
            {
                _size = contentSize;
                EnsureBitmap(_size);
                sender.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
            }

            SoftwareBitmap softwareBitmap;
            try
            {
                softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                    frame.Surface, BitmapAlphaMode.Premultiplied);
            }
            catch
            {
                return;
            }

            using (softwareBitmap)
            {
                int w = softwareBitmap.PixelWidth;
                int h = softwareBitmap.PixelHeight;

                // Buffer próprio por frame para evitar tearing com a escrita assíncrona na UI.
                var pixels = new byte[w * h * 4];
                softwareBitmap.CopyToBuffer(pixels.AsBuffer());

                _ = _dispatcher.BeginInvoke(() =>
                {
                    if (_disposed || _bitmap == null)
                        return;
                    if (_bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
                        return; // bitmap ainda sendo recriado para o novo tamanho

                    _bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
                });
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_framePool != null)
                _framePool.FrameArrived -= OnFrameArrived;

            try { _session?.Dispose(); } catch { }
            try { _framePool?.Dispose(); } catch { }

            _session = null;
            _framePool = null;
            _item = null;
            _device = null;
        }
    }
}
