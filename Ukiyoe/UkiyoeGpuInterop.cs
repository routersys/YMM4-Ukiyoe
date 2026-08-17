using ComputeWeave;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using YukkuriMovieMaker.Commons;

namespace Ukiyoe;

internal sealed class UkiyoeInteropProvider : ComputeExternalDirect3D11Provider
{
    private readonly ID2D1DeviceContext6 _renderContext;

    private UkiyoeInteropProvider(
        ID3D11Device1 device,
        ID3D11DeviceContext4 context,
        ID2D1DeviceContext6 renderContext,
        ComputeExternalQueueScheduler scheduler)
        : base(device.NativePointer, context.NativePointer, renderContext.NativePointer, scheduler)
    {
        _renderContext = renderContext;
    }

    public ID2D1DeviceContext6 RenderContext => _renderContext;

    public static UkiyoeInteropProvider? TryCreate(
        IGraphicsDevicesAndContext devices,
        ComputeExternalQueueScheduler scheduler,
        out GraphicsDevice? graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        graphicsDevice = null;

        ID3D11Device1? device = null;
        ID3D11DeviceContext4? context = null;
        ID2D1DeviceContext6? renderContext = null;
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
            context = devices.D3D.DeviceContext.QueryInterface<ID3D11DeviceContext4>();
            renderContext = devices.D2D.Device
                .CreateDeviceContext(DeviceContextOptions.EnableMultithreadedOptimizations)
                .QueryInterface<ID2D1DeviceContext6>();
            var provider = new UkiyoeInteropProvider(device, context, renderContext, scheduler);
            renderContext = null;
            return provider;
        }
        catch
        {
            graphicsDevice = null;
            return null;
        }
        finally
        {
            // 基底は自身の参照を取得済みなので、ここで取得した参照は返す。
            // renderContext は成功時に provider が引き取るため null にしてある。
            renderContext?.Dispose();
            context?.Dispose();
            device?.Dispose();
        }
    }

    protected override void DisposeCore()
    {
        _renderContext.Dispose();
    }
}

/// <summary>
/// External View のビットマップを Vortice の束縛へ写し、参照が変わるまで保持する。
/// </summary>
/// <remarks>
/// SharpGen の <see cref="ID2D1Bitmap1"/> はファイナライザーで Release する。素のポインタから包んだものを
/// 放置すると View の参照を奪うため、包む際に AddRef し、破棄で対にする。
/// </remarks>
internal sealed class UkiyoeBitmapBinding : IDisposable
{
    private nint _pointer;
    private ID2D1Bitmap1? _bitmap;

    public ID2D1Bitmap1 Get(ExternalDirect3D11TextureView view)
    {
        if (_pointer != view.Bitmap || _bitmap is null)
        {
            _bitmap?.Dispose();
            _bitmap = new ID2D1Bitmap1(view.Bitmap);
            _bitmap.AddRef();
            _pointer = view.Bitmap;
        }
        return _bitmap;
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _pointer = 0;
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
    private readonly SharedTextureSlot<Bgra32, Float4, ExternalDirect3D11TextureView> _source;

    [ComputeSharedTexture(
        ComputeResourceResizePolicy.GrowOnly,
        ComputeResourceAccess.ReadWrite,
        ExternalResourceAccess.Read,
        ExternalTextureUsage.Sampled,
        ComputeAlphaMode.Premultiplied,
        ComputeSharedTextureInitialOwner.Compute,
        ComputeResourceRecovery.Recompute)]
    private readonly SharedTextureSlot<Bgra32, Float4, ExternalDirect3D11TextureView> _output;
}
