using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

// Windows.Graphics.Capture reads one window's own composed content, including Shell views drawn
// with DirectComposition, and never the screen behind it. PrintWindow cannot render those views.
internal static class WindowCapture
{
    private static readonly Guid GraphicsCaptureItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid DxgiDeviceId = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    public static async Task SavePngAsync(nint window, string path)
    {
        var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        nint itemPointer = interop.CreateForWindow(window, GraphicsCaptureItemId);
        GraphicsCaptureItem item;
        try { item = GraphicsCaptureItem.FromAbi(itemPointer); }
        finally { Marshal.Release(itemPointer); }

        using var device = CreateDevice();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        // Shell views paint after navigation completes; keep the newest frame until the window is idle.
        SoftwareBitmap? latest = null;
        Exception? failure = null;
        var arrived = new SemaphoreSlim(0);
        pool.FrameArrived += async (sender, _) =>
        {
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null) return;
                var copy = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied);
                Interlocked.Exchange(ref latest, copy)?.Dispose();
            }
            catch (Exception ex) { failure = ex; }
            arrived.Release();
        };
        session.StartCapture();
        if (!await arrived.WaitAsync(TimeSpan.FromSeconds(10))) throw new TimeoutException("No captured frame.");
        var settle = System.Diagnostics.Stopwatch.StartNew();
        while (settle.Elapsed < TimeSpan.FromSeconds(6) && await arrived.WaitAsync(TimeSpan.FromMilliseconds(800))) { }
        session.Dispose();
        if (failure is not null) throw failure;
        using var bitmap = Interlocked.Exchange(ref latest, null)!;
        using var stream = await FileRandomAccessStream.OpenAsync(Path.GetFullPath(path), FileAccessMode.ReadWrite,
            StorageOpenOptions.None, FileOpenDisposition.CreateAlways);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
    }

    private static IDirect3DDevice CreateDevice()
    {
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out nint d3dDevice, out _, out nint context));
        try
        {
            Guid dxgiId = DxgiDeviceId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in dxgiId, out nint dxgiDevice));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out nint inspectable));
                try { return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
                finally { Marshal.Release(inspectable); }
            }
            finally { Marshal.Release(dxgiDevice); }
        }
        finally
        {
            Marshal.Release(context);
            Marshal.Release(d3dDevice);
        }
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid interfaceId);
        nint CreateForMonitor(nint monitor, in Guid interfaceId);
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags,
        nint featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
