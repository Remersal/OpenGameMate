using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT;

namespace OpenGameMate.Capture;

public sealed record ScreenCaptureResult(
    string Path,
    int SourceWidth,
    int SourceHeight,
    int OutputWidth,
    int OutputHeight,
    long FileBytes);

public sealed class PrimaryDisplayCapture
{
    private const uint D3D11CreateDeviceBgraSupport = 0x20;
    private const uint D3D11SdkVersion = 7;
    private const uint MonitorDefaultToPrimary = 1;
    private const int D3DDriverTypeHardware = 1;

    private static readonly Guid GraphicsCaptureItemInterfaceId =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid GraphicsCaptureItemInteropId =
        new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid DxgiDeviceId =
        new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    public PrimaryDisplayCapture()
    {
        TemporaryScreenshotPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "OpenGameMate",
            "primary-display.png");
    }

    public string TemporaryScreenshotPath { get; }

    public async Task<ScreenCaptureResult> CaptureAsync(
        int maxWidth = 1920,
        int maxHeight = 1080,
        CancellationToken cancellationToken = default)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new PlatformNotSupportedException("Windows Graphics Capture is not supported on this system.");
        }

        var monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
        if (monitor == nint.Zero)
        {
            throw new InvalidOperationException("Unable to resolve the Windows primary monitor.");
        }

        var item = CreateItemForMonitor(monitor);
        var sourceWidth = item.Size.Width;
        var sourceHeight = item.Size.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new InvalidOperationException("The primary monitor returned an invalid capture size.");
        }

        var direct3DDevice = CreateDirect3DDevice();
        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            direct3DDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);
        using var session = framePool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;

        var frameSource = new TaskCompletionSource<Direct3D11CaptureFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            var frame = sender.TryGetNextFrame();
            if (frame is not null && !frameSource.TrySetResult(frame))
            {
                frame.Dispose();
            }
        }

        framePool.FrameArrived += OnFrameArrived;
        try
        {
            session.StartCapture();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var frame = await frameSource.Task.WaitAsync(timeout.Token);
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
            var output = CalculateOutputSize(sourceWidth, sourceHeight, maxWidth, maxHeight);
            await SavePngAsync(bitmap, output.Width, output.Height);

            var fileBytes = new FileInfo(TemporaryScreenshotPath).Length;
            return new(
                TemporaryScreenshotPath,
                sourceWidth,
                sourceHeight,
                output.Width,
                output.Height,
                fileBytes);
        }
        finally
        {
            framePool.FrameArrived -= OnFrameArrived;
        }
    }

    public bool DeleteTemporaryScreenshot()
    {
        if (!File.Exists(TemporaryScreenshotPath))
        {
            return false;
        }

        File.Delete(TemporaryScreenshotPath);
        return true;
    }

    public static (int Width, int Height) CalculateOutputSize(
        int sourceWidth,
        int sourceHeight,
        int maxWidth,
        int maxHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || maxWidth <= 0 || maxHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "All dimensions must be positive.");
        }

        var scale = Math.Min(1d, Math.Min((double)maxWidth / sourceWidth, (double)maxHeight / sourceHeight));
        return (
            Math.Max(1, (int)Math.Round(sourceWidth * scale)),
            Math.Max(1, (int)Math.Round(sourceHeight * scale)));
    }

    private async Task SavePngAsync(SoftwareBitmap bitmap, int width, int height)
    {
        var directory = System.IO.Path.GetDirectoryName(TemporaryScreenshotPath)
            ?? throw new InvalidOperationException("Temporary screenshot path has no parent directory.");
        Directory.CreateDirectory(directory);

        using (File.Create(TemporaryScreenshotPath))
        {
        }

        var file = await StorageFile.GetFileFromPathAsync(TemporaryScreenshotPath);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        encoder.BitmapTransform.ScaledWidth = (uint)width;
        encoder.BitmapTransform.ScaledHeight = (uint)height;
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        await encoder.FlushAsync();
    }

    private static unsafe GraphicsCaptureItem CreateItemForMonitor(nint monitor)
    {
        nint className = nint.Zero;
        nint factory = nint.Zero;
        nint itemPointer = nint.Zero;
        const string runtimeClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(
                runtimeClassName,
                runtimeClassName.Length,
                out className));

            var interopId = GraphicsCaptureItemInteropId;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, ref interopId, out factory));

            var vtable = *(nint**)factory;
            var createForMonitor =
                (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)vtable[4];
            var itemId = GraphicsCaptureItemInterfaceId;
            Marshal.ThrowExceptionForHR(createForMonitor(factory, monitor, &itemId, &itemPointer));

            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            if (itemPointer != nint.Zero)
            {
                Marshal.Release(itemPointer);
            }

            if (factory != nint.Zero)
            {
                Marshal.Release(factory);
            }

            if (className != nint.Zero)
            {
                WindowsDeleteString(className);
            }
        }
    }

    private static IDirect3DDevice CreateDirect3DDevice()
    {
        nint d3dDevice = nint.Zero;
        nint immediateContext = nint.Zero;
        nint dxgiDevice = nint.Zero;
        nint inspectableDevice = nint.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(
                nint.Zero,
                D3DDriverTypeHardware,
                nint.Zero,
                D3D11CreateDeviceBgraSupport,
                nint.Zero,
                0,
                D3D11SdkVersion,
                out d3dDevice,
                out _,
                out immediateContext));

            var dxgiId = DxgiDeviceId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, ref dxgiId, out dxgiDevice));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectableDevice));
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectableDevice);
        }
        finally
        {
            if (inspectableDevice != nint.Zero)
            {
                Marshal.Release(inspectableDevice);
            }

            if (dxgiDevice != nint.Zero)
            {
                Marshal.Release(dxgiDevice);
            }

            if (immediateContext != nint.Zero)
            {
                Marshal.Release(immediateContext);
            }

            if (d3dDevice != nint.Zero)
            {
                Marshal.Release(d3dDevice);
            }
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(
        string sourceString,
        int length,
        out nint hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(
        nint activatableClassId,
        ref Guid iid,
        out nint factory);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        nint adapter,
        int driverType,
        nint software,
        uint flags,
        nint featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out nint device,
        out int featureLevel,
        out nint immediateContext);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint graphicsDevice);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
