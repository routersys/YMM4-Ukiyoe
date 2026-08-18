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
            if (!GraphicsDevice.TryGetDevice(new ExternalAdapterIdentity(devices.DXGI.Adapter.Description.Luid), out graphicsDevice))
                return null;

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
