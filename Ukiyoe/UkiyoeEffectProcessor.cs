using System.Numerics;
using ComputeWeave;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;

namespace Ukiyoe;

internal sealed class UkiyoeEffectProcessor : VideoEffectProcessorBase
{
    private readonly IGraphicsDevicesAndContext _devices;
    private readonly UkiyoeEffect _item;
    private ComputeExternalQueueScheduler? _scheduler;
    private UkiyoeInteropProvider? _interopProvider;
    private ComputeInteropDomain? _interopDomain;
    private UkiyoeResourceSet? _resourceSet;
    private ExternalTextureLease<UkiyoeExternalView>? _outputLease;
    private int _sourceWidth;
    private int _sourceHeight;
    private UkiyoePipeline? _pipeline;
    private UkiyoeCustomEffect? _effect;
    private Crop? _outputCrop;
    private ID2D1Image? _outputCropOutput;
    private AffineTransform2D? _outputTransform;
    private ID2D1Image? _outputTransformOutput;
    private bool _isFirst = true;
    private bool _hasOutput;
    private bool _hasOutputOffset;
    private bool _hasCropRect;
    private bool _hasRenderState;
    private Vector2 _outputOffset;
    private Vector4 _cropRect;
    private Parameters _parameters;
    private RenderState _renderState;

    public UkiyoeEffectProcessor(IGraphicsDevicesAndContext devices, UkiyoeEffect item)
        : base(devices)
    {
        _devices = devices;
        _item = item;
    }

    public override DrawDescription Update(EffectDescription effectDescription)
    {
        if (IsPassThroughEffect || _effect is null || _outputCrop is null || _outputTransform is null || _outputTransformOutput is null || _resourceSet is null || _interopProvider is null || _pipeline is null || input is null)
            return effectDescription.DrawDescription;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;
        var parameters = new Parameters(
            (float)(_item.Amount.GetValue(frame, length, fps) / 100.0),
            _item.Quality,
            (float)(_item.LineWidth.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Coherence.GetValue(frame, length, fps) / 100.0),
            (float)(_item.LineDetail.GetValue(frame, length, fps) / 100.0),
            (float)(_item.LineStrength.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Flatten.GetValue(frame, length, fps) / 100.0),
            _item.PaletteLevels,
            (float)(_item.Misregistration.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Baren.GetValue(frame, length, fps) / 100.0),
            (float)(_item.Paper.GetValue(frame, length, fps) / 100.0),
            _item.LineColor,
            _item.Seed);

        if (_isFirst || _parameters.Amount != parameters.Amount)
            _effect.Amount = parameters.Amount;

        if (parameters.Amount <= 0f)
        {
            _effect.Amount = 0f;
            _parameters = parameters;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var bounds = _devices.DeviceContext.GetImageLocalBounds(input);
        var widthValue = Math.Ceiling((double)bounds.Right - bounds.Left);
        var heightValue = Math.Ceiling((double)bounds.Bottom - bounds.Top);
        if (!double.IsFinite(widthValue) || !double.IsFinite(heightValue) ||
            !float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) ||
            widthValue <= 0d || heightValue <= 0d)
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        var longSide = Math.Max(widthValue, heightValue);
        var marginLimit = (UkiyoeSettings.MaximumCanvasSize - longSide) / 2d;
        var quality = UkiyoeSettings.GetQuality(parameters.Quality);
        var cellEstimate = (float)Math.Max((longSide + 128d) / quality.GridResolution, 1d);
        var margin = UkiyoeSettings.GetMargin(
            UkiyoeSettings.GetShiftPixels(Math.Clamp(parameters.Misregistration, 0f, 1f)),
            UkiyoeSettings.GetLineSigmaPixels(Math.Clamp(parameters.LineWidth, 0f, 1f)),
            UkiyoeSettings.GetFlowSigma(Math.Clamp(parameters.Coherence, 0f, 1f)),
            cellEstimate);
        if (marginLimit < margin)
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }
        var canvasWidthValue = widthValue + margin * 2d;
        var canvasHeightValue = heightValue + margin * 2d;
        if (canvasWidthValue * canvasHeightValue > int.MaxValue)
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }
        var canvasWidth = (int)canvasWidthValue;
        var canvasHeight = (int)canvasHeightValue;
        var itemWidth = (int)widthValue;
        var itemHeight = (int)heightValue;

        if (!EnsureSource(itemWidth, itemHeight))
        {
            _effect.Amount = 0f;
            _isFirst = true;
            return effectDescription.DrawDescription;
        }

        RenderInput(new Vortice.RawRectF(bounds.Left, bounds.Top, bounds.Left + itemWidth, bounds.Top + itemHeight));

        var pipelineParameters = new UkiyoePipeline.Parameters(
            parameters.Quality,
            Math.Clamp(parameters.LineWidth, 0f, 1f),
            Math.Clamp(parameters.Coherence, 0f, 1f),
            Math.Clamp(parameters.LineDetail, 0f, 1f),
            Math.Clamp(parameters.Flatten, 0f, 1f),
            UkiyoeSettings.ClampPaletteLevels(parameters.PaletteLevels),
            Math.Clamp(parameters.Misregistration, 0f, 1f),
            Math.Clamp(parameters.Baren, 0f, 1f),
            Math.Clamp(parameters.Paper, 0f, 1f),
            Math.Clamp(parameters.LineStrength, 0f, 1f),
            parameters.LineColor.R / 255f,
            parameters.LineColor.G / 255f,
            parameters.LineColor.B / 255f,
            Math.Max(parameters.Seed, 0));

        var structureChanged = _pipeline.Simulate(
            _resourceSet.GetSourceComputeBinding(),
            canvasWidth,
            canvasHeight,
            margin,
            margin,
            itemWidth,
            itemHeight,
            in pipelineParameters);

        if (!_pipeline.TryGetVisibleBounds(canvasWidth, canvasHeight, in pipelineParameters, out var rect))
        {
            _effect.Amount = 0f;
            _parameters = parameters;
            _isFirst = true;
            _hasRenderState = false;
            return effectDescription.DrawDescription;
        }

        if (!OutputCovers(rect.Width, rect.Height))
            _outputCrop.SetInput(0, null, true);
        if (!EnsureOutput(rect.Width, rect.Height, out var outputChanged))
        {
            _effect.Amount = 0f;
            _parameters = parameters;
            _isFirst = true;
            _hasRenderState = false;
            return effectDescription.DrawDescription;
        }
        var renderState = new RenderState(
            pipelineParameters.LineDetail,
            pipelineParameters.LineStrength,
            pipelineParameters.PaletteLevels,
            pipelineParameters.Misregistration,
            pipelineParameters.Baren,
            pipelineParameters.Paper,
            pipelineParameters.LineColorR,
            pipelineParameters.LineColorG,
            pipelineParameters.LineColorB,
            pipelineParameters.Seed,
            rect);
        if (structureChanged || outputChanged || !_hasOutput || !_hasRenderState || _renderState != renderState)
        {
            _pipeline.RenderVisible(
                _resourceSet.GetOutputComputeBinding(),
                canvasWidth,
                canvasHeight,
                rect,
                in pipelineParameters);
            _renderState = renderState;
            _hasRenderState = true;
        }

        _outputLease ??= _resourceSet.AcquireOutputExternalViewLease();

        if (outputChanged || !_hasOutput)
        {
            _outputCrop.SetInput(0, _outputLease.DangerousGetView().Bitmap, true);
            _effect.SetInput(1, _outputTransformOutput, true);
        }
        var cropRect = new Vector4(0f, 0f, rect.Width, rect.Height);
        if (!_hasCropRect || _cropRect != cropRect)
        {
            _outputCrop.Rectangle = cropRect;
            _cropRect = cropRect;
            _hasCropRect = true;
        }
        var outputOffset = new Vector2(bounds.Left - margin + rect.X, bounds.Top - margin + rect.Y);
        if (!_hasOutputOffset || _outputOffset != outputOffset)
        {
            _outputTransform.TransformMatrix = Matrix3x2.CreateTranslation(outputOffset);
            _outputOffset = outputOffset;
            _hasOutputOffset = true;
        }
        _hasOutput = true;
        _parameters = parameters;
        _isFirst = false;
        return effectDescription.DrawDescription;
    }

    private bool EnsureSource(int width, int height)
    {
        if (_sourceWidth == width && _sourceHeight == height)
            return true;

        _sourceWidth = 0;
        _sourceHeight = 0;
        if (!_resourceSet!.TryEnsureSource(width, height, out _))
            return false;

        _sourceWidth = width;
        _sourceHeight = height;
        return true;
    }

    private bool OutputCovers(int width, int height)
        => _outputLease is { IsDisposed: false } lease &&
           lease.Width >= width &&
           lease.Height >= height;

    private bool EnsureOutput(int width, int height, out bool changed)
    {
        changed = false;
        if (OutputCovers(width, height))
            return true;

        _outputLease?.Dispose();
        _outputLease = null;
        return _resourceSet!.TryEnsureOutput(width, height, out changed);
    }

    private void RenderInput(Vortice.RawRectF bounds)
    {
        var renderContext = _interopProvider!.RenderContext;
        using var borrow = _resourceSet!.BeginSourceExternalOperation();
        var previousTarget = renderContext.Target;
        renderContext.Target = borrow.DangerousGetView().Bitmap;
        renderContext.BeginDraw();
        renderContext.Clear(null);
        renderContext.DrawImage(
            input,
            new Vector2(-bounds.Left, -bounds.Top),
            null,
            InterpolationMode.NearestNeighbor,
            CompositeMode.SourceCopy);
        renderContext.EndDraw();
        renderContext.Target = previousTarget;
    }

    private void ReleaseInterop()
    {
        _outputLease?.Dispose();
        _outputLease = null;
        _pipeline?.Dispose();
        _pipeline = null;
        _resourceSet?.Dispose();
        _resourceSet?.WaitForDisposal();
        _resourceSet = null;
        _interopDomain?.Dispose();
        _interopDomain?.WaitForDisposal();
        _interopDomain = null;
        _interopProvider?.Dispose();
        _interopProvider = null;
        _scheduler?.Dispose();
        _scheduler = null;
        _sourceWidth = 0;
        _sourceHeight = 0;
    }

    protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
    {
        var scheduler = ComputeExternalQueueScheduler.Create();
        var interopProvider = UkiyoeInteropProvider.TryCreate(devices, scheduler, out var interopDevice);
        if (interopProvider is null || interopDevice is null)
        {
            scheduler.Dispose();
            return null;
        }

        _scheduler = scheduler;

        try
        {
            _interopProvider = interopProvider;
            _interopDomain = interopDevice.RegisterExternalDomain(interopProvider);
            _resourceSet = UkiyoeResourceSet.Create(interopDevice, _interopDomain);
            _pipeline = UkiyoePipeline.TryCreate(interopDevice);
        }
        catch
        {
            ReleaseInterop();
            throw;
        }

        if (_pipeline is null)
        {
            ReleaseInterop();
            return null;
        }

        UkiyoeCustomEffect? effect = null;
        Crop? outputCrop = null;
        ID2D1Image? outputCropOutput = null;
        AffineTransform2D? outputTransform = null;
        ID2D1Image? outputTransformOutput = null;
        ID2D1Image? output = null;
        try
        {
            effect = new UkiyoeCustomEffect(devices);
            if (!effect.IsEnabled)
            {
                effect.Dispose();
                ReleaseInterop();
                return null;
            }
            outputCrop = new Crop(devices.DeviceContext);
            outputCropOutput = outputCrop.Output;
            outputTransform = new AffineTransform2D(devices.DeviceContext)
            {
                BorderMode = BorderMode.Hard,
            };
            outputTransform.SetInput(0, outputCropOutput, true);
            outputTransformOutput = outputTransform.Output;
            output = effect.Output;
            _effect = effect;
            _outputCrop = outputCrop;
            _outputCropOutput = outputCropOutput;
            _outputTransform = outputTransform;
            _outputTransformOutput = outputTransformOutput;
            disposer.Collect(effect);
            disposer.Collect(outputCrop);
            disposer.Collect(outputCropOutput);
            disposer.Collect(outputTransform);
            disposer.Collect(outputTransformOutput);
            disposer.Collect(output);
            return output;
        }
        catch
        {
            output?.Dispose();
            outputTransformOutput?.Dispose();
            outputTransform?.Dispose();
            outputCropOutput?.Dispose();
            outputCrop?.Dispose();
            effect?.Dispose();
            ReleaseInterop();
            throw;
        }
    }

    protected override void setInput(ID2D1Image? inputImage)
    {
        _effect?.SetInput(0, inputImage, true);
        if (!_hasOutput)
            _effect?.SetInput(1, inputImage, true);
    }

    protected override void ClearEffectChain()
    {
        _effect?.SetInput(0, null, true);
        _effect?.SetInput(1, null, true);
        _outputCrop?.SetInput(0, null, true);
        _isFirst = true;
        _hasOutput = false;
        _hasOutputOffset = false;
        _hasCropRect = false;
        _hasRenderState = false;
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                ClearEffectChain();
                ReleaseInterop();
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    private readonly record struct RenderState(
        float LineDetail,
        float LineStrength,
        int PaletteLevels,
        float Misregistration,
        float Baren,
        float Paper,
        float LineColorR,
        float LineColorG,
        float LineColorB,
        int Seed,
        UkiyoePipeline.PixelRect Rect);

    private readonly record struct Parameters(
        float Amount,
        UkiyoeQuality Quality,
        float LineWidth,
        float Coherence,
        float LineDetail,
        float LineStrength,
        float Flatten,
        int PaletteLevels,
        float Misregistration,
        float Baren,
        float Paper,
        System.Windows.Media.Color LineColor,
        int Seed);
}
