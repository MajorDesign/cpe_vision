using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace VideoWall.Capture
{
    /// <summary>
    /// Glue de interop entre o Direct3D 11 (Win32/COM) e as APIs WinRT de captura
    /// (Windows.Graphics.Capture). Cria o dispositivo gráfico WinRT e o item de
    /// captura a partir do handle (HWND) de uma janela.
    /// </summary>
    internal static class Direct3D11Helper
    {
        // IID de IDXGIDevice.
        private static readonly Guid DxgiDeviceGuid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

        // IID de GraphicsCaptureItem (usado pela interface de interop).
        private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        private const int D3D_DRIVER_TYPE_HARDWARE = 1;
        private const int D3D_DRIVER_TYPE_WARP = 5;
        private const int D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
        private const int D3D11_SDK_VERSION = 7;

        [DllImport("d3d11.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int D3D11CreateDevice(
            IntPtr pAdapter, int driverType, IntPtr software, int flags,
            IntPtr pFeatureLevels, int featureLevels, int sdkVersion,
            out IntPtr ppDevice, out int pFeatureLevel, out IntPtr ppImmediateContext);

        [DllImport("d3d11.dll", ExactSpelling = true, PreserveSig = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
            IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
        }

        /// <summary>Cria um <see cref="IDirect3DDevice"/> WinRT pronto para a captura.</summary>
        public static IDirect3DDevice CreateDevice()
        {
            int hr = D3D11CreateDevice(
                IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero, 0, D3D11_SDK_VERSION,
                out IntPtr devicePtr, out _, out IntPtr contextPtr);

            if (hr != 0)
            {
                // Sem GPU compatível: tenta o renderizador por software (WARP).
                hr = D3D11CreateDevice(
                    IntPtr.Zero, D3D_DRIVER_TYPE_WARP, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                    IntPtr.Zero, 0, D3D11_SDK_VERSION,
                    out devicePtr, out _, out contextPtr);
                Marshal.ThrowExceptionForHR(hr);
            }

            try
            {
                Guid dxgiIid = DxgiDeviceGuid;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(devicePtr, ref dxgiIid, out IntPtr dxgiDevicePtr));
                try
                {
                    Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePtr, out IntPtr graphicsDevicePtr));
                    try
                    {
                        return MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevicePtr);
                    }
                    finally
                    {
                        Marshal.Release(graphicsDevicePtr);
                    }
                }
                finally
                {
                    Marshal.Release(dxgiDevicePtr);
                }
            }
            finally
            {
                if (contextPtr != IntPtr.Zero)
                    Marshal.Release(contextPtr);
                Marshal.Release(devicePtr);
            }
        }

        /// <summary>Cria um <see cref="GraphicsCaptureItem"/> para a janela informada.</summary>
        public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
        {
            var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
            var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();

            Guid iid = GraphicsCaptureItemGuid;
            IntPtr itemPtr = interop.CreateForWindow(hwnd, ref iid);
            try
            {
                return GraphicsCaptureItem.FromAbi(itemPtr);
            }
            finally
            {
                Marshal.Release(itemPtr);
            }
        }
    }
}
