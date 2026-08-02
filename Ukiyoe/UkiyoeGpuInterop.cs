using System.Threading;
using ComputeWeave;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using YukkuriMovieMaker.Commons;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace Ukiyoe;

internal sealed class UkiyoeQueueScheduler : ComputeExternalQueueScheduler
{
    private int _entered;

    protected override void EnterCore()
    {
        if (Interlocked.CompareExchange(ref _entered, 1, 0) != 0)
            throw new InvalidOperationException();
    }

    protected override void ExitCore()
    {
        if (Interlocked.Exchange(ref _entered, 0) != 1)
            throw new InvalidOperationException();
    }

    protected override void DisposeCore()
    {
    }
}

internal sealed class UkiyoeExternalView(ID3D11Texture2D texture, ID2D1Bitmap1 bitmap) : IDisposable
{
    private readonly ID3D11Texture2D _texture = texture;
    private readonly ID2D1Bitmap1 _bitmap = bitmap;

    public ID2D1Bitmap1 Bitmap => _bitmap;

    public void Dispose()
    {
        _bitmap.Dispose();
        _texture.Dispose();
    }
}

internal sealed class UkiyoeInteropProvider : IComputeExternalInteropProvider<UkiyoeExternalView>
{
    private readonly ID3D11Device1 _device;
    private readonly ID3D11Device5 _device5;
    private readonly ID3D11DeviceContext4 _context;
    private readonly ID2D1DeviceContext6 _renderContext;
    private readonly UkiyoeQueueScheduler _scheduler;
    private readonly long _adapterLuid;
    private ID3D11Fence? _fence;
    private bool _disposed;

    private UkiyoeInteropProvider(
        ID3D11Device1 device,
        ID3D11Device5 device5,
        ID3D11DeviceContext4 context,
        ID2D1DeviceContext6 renderContext,
        UkiyoeQueueScheduler scheduler,
        long adapterLuid)
    {
        _device = device;
        _device5 = device5;
        _context = context;
        _renderContext = renderContext;
        _scheduler = scheduler;
        _adapterLuid = adapterLuid;
    }

    public ExternalAdapterIdentity AdapterIdentity => new(_adapterLuid);

    public ComputeExternalQueueScheduler Scheduler => _scheduler;

    public ExternalInteropCapabilities Capabilities =>
        ExternalInteropCapabilities.SharedFence |
        ExternalInteropCapabilities.SharedTexture2D |
        ExternalInteropCapabilities.SingleImmediateContextOrdering |
        ExternalInteropCapabilities.PersistentExternalViewOrdering;

    public ID2D1DeviceContext6 RenderContext => _renderContext;

    public static UkiyoeInteropProvider? TryCreate(IGraphicsDevicesAndContext devices, out GraphicsDevice? graphicsDevice)
    {
        graphicsDevice = null;

        ID3D11Device1? device = null;
        ID3D11Device5? device5 = null;
        ID3D11DeviceContext4? context = null;
        ID2D1DeviceContext6? renderContext = null;
        UkiyoeQueueScheduler? scheduler = null;
        try
        {
            var adapterLuidText = devices.DXGI.Adapter.Description.Luid.ToString();
            using var enumerator = GraphicsDevice
                .QueryDevices(candidate => string.Equals(candidate.Luid.ToString(), adapterLuidText, StringComparison.Ordinal))
                .GetEnumerator();
            if (!enumerator.MoveNext())
                return null;

            graphicsDevice = enumerator.Current;
            device = devices.D3D.Device.QueryInterface<ID3D11Device1>();
            device5 = devices.D3D.Device.QueryInterface<ID3D11Device5>();
            context = devices.D3D.DeviceContext.QueryInterface<ID3D11DeviceContext4>();
            renderContext = devices.D2D.Device
                .CreateDeviceContext(DeviceContextOptions.EnableMultithreadedOptimizations)
                .QueryInterface<ID2D1DeviceContext6>();
            scheduler = new UkiyoeQueueScheduler();
            return new UkiyoeInteropProvider(device, device5, context, renderContext, scheduler, graphicsDevice.Luid.ToInt64());
        }
        catch
        {
            scheduler?.Dispose();
            renderContext?.Dispose();
            context?.Dispose();
            device5?.Dispose();
            device?.Dispose();
            graphicsDevice = null;
            return null;
        }
    }

    public void Initialize(in ExternalTimelineInitialization initialization)
    {
        _fence = _device5.OpenSharedFence<ID3D11Fence>(initialization.SharedFenceHandle.DangerousGetHandle());
    }

    public void EnqueueSignal(ulong value)
    {
        _context.Signal(_fence!, value);
    }

    public void FlushAfterSignal()
    {
        _context.Flush();
    }

    public void EnqueueWait(ulong value)
    {
        _context.Wait(_fence!, value);
    }

    public UkiyoeExternalView OpenSharedTexture(BorrowedSharedHandle resourceHandle, in ExternalTextureDescriptor descriptor)
    {
        ID3D11Texture2D? texture = null;
        ID2D1Bitmap1? bitmap = null;
        try
        {
            texture = _device.OpenSharedResource1<ID3D11Texture2D>(resourceHandle.DangerousGetHandle());
            using var surface = texture.QueryInterface<IDXGISurface>();
            var pixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
            var options = descriptor.ExternalUsage is ExternalTextureUsage.RenderTarget
                ? BitmapOptions.Target
                : BitmapOptions.None;
            bitmap = _renderContext.CreateBitmapFromDxgiSurface(
                surface,
                new BitmapProperties1(pixelFormat, 96f, 96f, options));
            var view = new UkiyoeExternalView(texture, bitmap);
            texture = null;
            bitmap = null;
            return view;
        }
        finally
        {
            bitmap?.Dispose();
            texture?.Dispose();
        }
    }

    public void OnDeviceTerminal(Exception reason)
    {
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _fence?.Dispose();
        _renderContext.Dispose();
        _context.Dispose();
        _device5.Dispose();
        _device.Dispose();
        _scheduler.Dispose();
    }
}

[ComputeInteropResourceSet]
internal sealed partial class UkiyoeResourceSet
{
    [ComputeSharedTexture(
        ComputeResourceResizePolicy.Exact,
        ComputeResourceAccess.ReadWrite,
        ExternalResourceAccess.Write,
        ExternalTextureUsage.RenderTarget,
        ComputeAlphaMode.Premultiplied,
        ComputeSharedTextureInitialOwner.External,
        ComputeResourceRecovery.RecreateFromHost)]
    private readonly SharedTextureSlot<Bgra32, Float4, UkiyoeExternalView> _source;

    [ComputeSharedTexture(
        ComputeResourceResizePolicy.GrowOnly,
        ComputeResourceAccess.ReadWrite,
        ExternalResourceAccess.Read,
        ExternalTextureUsage.Sampled,
        ComputeAlphaMode.Premultiplied,
        ComputeSharedTextureInitialOwner.Compute,
        ComputeResourceRecovery.Recompute)]
    private readonly SharedTextureSlot<Bgra32, Float4, UkiyoeExternalView> _output;
}
