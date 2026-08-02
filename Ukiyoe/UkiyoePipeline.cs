using System.Runtime.InteropServices;
using ComputeWeave;

namespace Ukiyoe;

internal sealed class UkiyoePipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly UkiyoePipelineHost _host;
    private readonly ReadWriteBuffer<int> _scratch;
    private readonly ReadBackBuffer<int> _scratchReadBack;
    private StructureKey? _structureKey;
    private int _cachedLitCount;
    private int _cachedBoundsMinX;
    private int _cachedBoundsMinY;
    private int _cachedBoundsMaxX;
    private int _cachedBoundsMaxY;
    private int _gridWidth;
    private int _gridHeight;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedSource;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedOutput;
    private int _packedWidth;
    private int _packedHeight;

    private UkiyoePipeline(GraphicsDevice device, UkiyoePipelineHost host)
    {
        _device = device;
        _host = host;
        _scratch = device.AllocateReadWriteBuffer<int>(UkiyoeSettings.ScratchLength);
        _scratchReadBack = device.AllocateReadBackBuffer<int>(UkiyoeSettings.ScratchLength);
    }

    public static UkiyoePipeline? TryCreate()
    {
        try
        {
            return TryCreate(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static UkiyoePipeline? TryCreate(GraphicsDevice device)
    {
        UkiyoePipelineHost? host = null;
        try
        {
            host = UkiyoePipelineHost.Create(device, UkiyoeSettings.MaximumPendingSubmissions);
            return new UkiyoePipeline(device, host);
        }
        catch
        {
            host?.Dispose();
            host?.WaitForDisposal();
            return null;
        }
    }

    internal void WaitForCompletion()
    {
        _device.For(1, new FillIntShader(_scratch, 0, 0));
    }

    public void Process(ReadOnlySpan<int> source, Span<int> destination, int width, int height, in Parameters parameters)
    {
        var pixelCount = checked(width * height);
        EnsureGridFor(width, height, parameters.Quality);
        EnsurePackedTextures(width, height);
        var sourceTexture = _packedSource!;
        var outputTexture = _packedOutput!;
        sourceTexture.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(source[..pixelCount]));
        SubmitFullPipeline(sourceTexture, outputTexture, width, height, in parameters).Wait();
        outputTexture.CopyTo(MemoryMarshal.Cast<int, Bgra32>(destination[..pixelCount]));
    }

    public void Process(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, parameters.Quality);
        _ = SubmitFullPipeline(source, destination, width, height, in parameters);
    }

    internal void ProcessSharedAndWait(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, parameters.Quality);
        SubmitFullPipeline(source, destination, width, height, in parameters).Wait();
    }

    internal bool Simulate(
        ReadWriteTexture2D<Bgra32, Float4> source,
        int canvasWidth,
        int canvasHeight,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in Parameters parameters)
    {
        var derived = BeginSimulate(canvasWidth, canvasHeight, in parameters);
        _host.RecordSilhouetteAndMaskHash(
            source, _scratch, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, _gridWidth, _gridHeight, in derived).Wait();
        return CompleteSimulate(canvasWidth, canvasHeight, in parameters, in derived);
    }

    internal bool Simulate(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> source,
        int canvasWidth,
        int canvasHeight,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in Parameters parameters)
    {
        var derived = BeginSimulate(canvasWidth, canvasHeight, in parameters);
        _host.RecordSharedSilhouetteAndMaskHash(
            source, _scratch, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, _gridWidth, _gridHeight, in derived).Wait();
        return CompleteSimulate(canvasWidth, canvasHeight, in parameters, in derived);
    }

    private DerivedValues BeginSimulate(int canvasWidth, int canvasHeight, in Parameters parameters)
    {
        EnsureGridFor(canvasWidth, canvasHeight, parameters.Quality);
        return Derive(canvasWidth, canvasHeight, in parameters);
    }

    private bool CompleteSimulate(int canvasWidth, int canvasHeight, in Parameters parameters, in DerivedValues derived)
    {
        _scratchReadBack.CopyFrom(_scratch);
        var hashed = _scratchReadBack.Span;
        _cachedLitCount = hashed[UkiyoeSettings.ScratchLitCount];
        _cachedBoundsMinX = hashed[UkiyoeSettings.ScratchBoundsMinX];
        _cachedBoundsMinY = hashed[UkiyoeSettings.ScratchBoundsMinY];
        _cachedBoundsMaxX = hashed[UkiyoeSettings.ScratchBoundsMaxX];
        _cachedBoundsMaxY = hashed[UkiyoeSettings.ScratchBoundsMaxY];
        var key = new StructureKey(
            hashed[UkiyoeSettings.ScratchMaskHashSum],
            hashed[UkiyoeSettings.ScratchMaskHashMix],
            canvasWidth,
            canvasHeight,
            parameters.Quality,
            parameters.LineWidth,
            parameters.Coherence,
            parameters.LineDetail,
            parameters.Flatten);
        if (_structureKey == key)
            return false;

        _host.RecordStructure(_scratch, _gridWidth, _gridHeight, in derived).Wait();
        _structureKey = key;
        return true;
    }

    internal bool TryGetVisibleBounds(int canvasWidth, int canvasHeight, in Parameters parameters, out PixelRect rect)
    {
        rect = default;
        if (_cachedLitCount <= 0 || _cachedBoundsMinX > _cachedBoundsMaxX)
            return false;

        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        var cellSize = derived.CellSize;
        var margin = UkiyoeSettings.GetMargin(derived.ShiftPixels, derived.LineSigmaPixels, derived.FlowSigma, cellSize);
        var left = Math.Clamp(((int)(_cachedBoundsMinX * cellSize) - margin) & ~3, 0, canvasWidth);
        var top = Math.Clamp(((int)(_cachedBoundsMinY * cellSize) - margin) & ~3, 0, canvasHeight);
        var right = Math.Clamp((int)MathF.Ceiling((_cachedBoundsMaxX + 1) * cellSize) + margin, 0, canvasWidth);
        var bottom = Math.Clamp((int)MathF.Ceiling((_cachedBoundsMaxY + 1) * cellSize) + margin, 0, canvasHeight);
        var width = Math.Min((right - left + 3) & ~3, canvasWidth - left);
        var height = Math.Min((bottom - top + 3) & ~3, canvasHeight - top);
        if (width <= 0 || height <= 0)
            return false;

        rect = new PixelRect(left, top, width, height);
        return true;
    }

    internal void RenderVisible(
        ReadWriteTexture2D<Bgra32, Float4> output,
        int canvasWidth,
        int canvasHeight,
        PixelRect rect,
        in Parameters parameters)
    {
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        _host.RecordRender(output, in rect, _gridWidth, _gridHeight, in derived, in parameters).Wait();
    }

    internal void RenderVisible(
        ComputeResourceBinding<ReadWriteTexture2D<Bgra32, Float4>> output,
        int canvasWidth,
        int canvasHeight,
        PixelRect rect,
        in Parameters parameters)
    {
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        _host.RecordSharedRender(output, in rect, _gridWidth, _gridHeight, in derived, in parameters).Wait();
    }

    private ComputeSubmission SubmitFullPipeline(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        _structureKey = null;
        var derived = Derive(width, height, in parameters);
        return _host.RecordFullPipeline(source, output, _scratch, width, height, _gridWidth, _gridHeight, in derived, in parameters);
    }

    private DerivedValues Derive(int width, int height, in Parameters parameters)
    {
        var settings = UkiyoeSettings.GetQuality(parameters.Quality);
        var (_, _, cellSize) = UkiyoeSettings.GetGridSize(width, height, settings.GridResolution);
        var lineSigmaPixels = UkiyoeSettings.GetLineSigmaPixels(parameters.LineWidth);
        var lineSigmaCells = Math.Max(lineSigmaPixels / cellSize, UkiyoeSettings.MinimumLineSigma);
        var flowSigma = UkiyoeSettings.GetFlowSigma(parameters.Coherence);
        return new DerivedValues(
            cellSize,
            settings.EtfIterations,
            settings.FdogIterations,
            settings.FlattenIterations,
            lineSigmaPixels,
            lineSigmaCells,
            (int)MathF.Ceiling(2f * lineSigmaCells * UkiyoeSettings.SurroundSigmaScale),
            flowSigma,
            (int)MathF.Ceiling(2f * flowSigma),
            UkiyoeSettings.GetLineThreshold(parameters.LineDetail),
            UkiyoeSettings.GetFlattenBeta(parameters.Flatten),
            UkiyoeSettings.GetShiftPixels(parameters.Misregistration));
    }

    private void EnsureGridFor(int width, int height, UkiyoeQuality quality)
    {
        var settings = UkiyoeSettings.GetQuality(quality);
        var (gridWidth, gridHeight, _) = UkiyoeSettings.GetGridSize(width, height, settings.GridResolution);
        EnsureGrid(gridWidth, gridHeight);
    }

    private void EnsureGrid(int gridWidth, int gridHeight)
    {
        if (_gridWidth == gridWidth && _gridHeight == gridHeight)
            return;

        var gridLength = gridWidth * gridHeight;
        if (!_host.TryEnsureGrid(
                new UkiyoeGridResources.Plan(
                    gridLength, gridLength, gridLength, gridLength, gridLength, gridLength, gridLength, gridLength, gridLength),
                out _))
            throw new InvalidOperationException();

        _cachedLitCount = 0;
        _cachedBoundsMinX = int.MaxValue;
        _cachedBoundsMinY = int.MaxValue;
        _cachedBoundsMaxX = int.MinValue;
        _cachedBoundsMaxY = int.MinValue;
        _structureKey = null;
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
    }

    private void EnsurePackedTextures(int width, int height)
    {
        if (_packedWidth == width && _packedHeight == height)
            return;

        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedOutput = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedWidth = width;
        _packedHeight = height;
    }

    public void Dispose()
    {
        _host.Dispose();
        _host.WaitForDisposal();
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = null;
        _packedOutput = null;
        _packedWidth = 0;
        _packedHeight = 0;
        _structureKey = null;
        _gridWidth = 0;
        _gridHeight = 0;
        _scratchReadBack.Dispose();
        _scratch.Dispose();
    }

    internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

    private readonly record struct StructureKey(
        int MaskHashSum,
        int MaskHashMix,
        int CanvasWidth,
        int CanvasHeight,
        UkiyoeQuality Quality,
        float LineWidth,
        float Coherence,
        float LineDetail,
        float Flatten);

    internal readonly record struct DerivedValues(
        float CellSize,
        int EtfIterations,
        int FdogIterations,
        int FlattenIterations,
        float LineSigmaPixels,
        float LineSigmaCells,
        int DogExtent,
        float FlowSigma,
        int FlowExtent,
        float LineThreshold,
        float FlattenBeta,
        float ShiftPixels);

    internal readonly record struct Parameters(
        UkiyoeQuality Quality,
        float LineWidth,
        float Coherence,
        float LineDetail,
        float Flatten,
        int PaletteLevels,
        float Misregistration,
        float Baren,
        float Paper,
        float LineStrength,
        float LineColorR,
        float LineColorG,
        float LineColorB,
        int Seed);
}
