using System.Runtime.InteropServices;
using ComputeSharp;

namespace Ukiyoe;

internal sealed class UkiyoePipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<int> _scratch;
    private readonly ReadBackBuffer<int> _scratchReadBack;
    private ReadWriteBuffer<Float4>? _colorIn;
    private ReadWriteBuffer<Float4>? _colorA;
    private ReadWriteBuffer<Float4>? _colorB;
    private ReadWriteBuffer<float>? _gray;
    private ReadWriteBuffer<float>? _gradientMagnitude;
    private ReadWriteBuffer<float>? _response;
    private ReadWriteBuffer<float>? _line;
    private ReadWriteBuffer<Float2>? _tangentA;
    private ReadWriteBuffer<Float2>? _tangentB;
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

    private UkiyoePipeline(GraphicsDevice device)
    {
        _device = device;
        _scratch = device.AllocateReadWriteBuffer<int>(UkiyoeSettings.ScratchLength);
        _scratchReadBack = device.AllocateReadBackBuffer<int>(UkiyoeSettings.ScratchLength);
    }

    public static UkiyoePipeline? TryCreate()
    {
        try
        {
            return new UkiyoePipeline(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static UkiyoePipeline? TryCreate(GraphicsDevice device)
    {
        try
        {
            return new UkiyoePipeline(device);
        }
        catch
        {
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
        using (ComputeContext context = _device.CreateComputeContext())
            RecordFullPipeline(in context, sourceTexture, outputTexture, width, height, in parameters);
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
        using ComputeContext context = _device.CreateComputeContext();
        RecordFullPipeline(in context, source, destination, width, height, in parameters);
        context.Submit();
    }

    internal void ProcessSharedAndWait(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureGridFor(width, height, parameters.Quality);
        using ComputeContext context = _device.CreateComputeContext();
        RecordFullPipeline(in context, source, destination, width, height, in parameters);
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
        EnsureGridFor(canvasWidth, canvasHeight, parameters.Quality);
        var derived = Derive(canvasWidth, canvasHeight, in parameters);
        using (ComputeContext context = _device.CreateComputeContext())
        {
            RecordSilhouetteStage(in context, source, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, in derived);
            RecordMaskHashStage(in context);
        }
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

        using (ComputeContext context = _device.CreateComputeContext())
            RecordStructureStage(in context, in derived);
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
        using ComputeContext context = _device.CreateComputeContext();
        RecordRenderStage(in context, output, rect, in derived, in parameters);
    }

    private void RecordFullPipeline(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        _structureKey = null;
        var derived = Derive(width, height, in parameters);
        RecordSilhouetteStage(in context, source, 0, 0, width, height, in derived);
        RecordStructureStage(in context, in derived);
        RecordRenderStage(in context, output, new PixelRect(0, 0, width, height), in derived, in parameters);
    }

    private void RecordSilhouetteStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        in DerivedValues derived)
    {
        context.For(_gridWidth, _gridHeight, new SilhouetteShader(
            source, _colorIn!, _gray!, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, _gridWidth, _gridHeight, derived.CellSize));
        context.Barrier(_colorIn!);
        context.Barrier(_gray!);
    }

    private void RecordMaskHashStage(in ComputeContext context)
    {
        context.For(1, new MaskHashResetShader(_scratch));
        context.Barrier(_scratch);
        context.For(_gridWidth, _gridHeight, new MaskHashShader(_colorIn!, _scratch, _gridWidth, _gridHeight));
        context.Barrier(_scratch);
    }

    private void RecordStructureStage(in ComputeContext context, in DerivedValues derived)
    {
        var gridWidth = _gridWidth;
        var gridHeight = _gridHeight;

        context.For(1, new InitScratchShader(_scratch));
        context.Barrier(_scratch);
        context.For(gridWidth, gridHeight, new GradientShader(_gray!, _tangentA!, _gradientMagnitude!, _scratch, gridWidth, gridHeight));
        context.Barrier(_tangentA!);
        context.Barrier(_gradientMagnitude!);
        context.Barrier(_scratch);

        var tangentIn = _tangentA!;
        var tangentOut = _tangentB!;
        for (var iteration = 0; iteration < derived.EtfIterations; iteration++)
        {
            context.For(gridWidth, gridHeight, new EtfShader(tangentIn, tangentOut, _gradientMagnitude!, _scratch, gridWidth, gridHeight));
            context.Barrier(tangentOut);
            (tangentIn, tangentOut) = (tangentOut, tangentIn);
        }
        var tangent = tangentIn;

        context.For(gridWidth, gridHeight, new CopyColorShader(_colorIn!, _colorA!, gridWidth, gridHeight));
        context.Barrier(_colorA!);
        var iterateIn = _colorA!;
        var iterateOut = _colorB!;
        for (var iteration = 0; iteration < derived.FlattenIterations; iteration++)
        {
            context.For(gridWidth, gridHeight, new FlattenShader(
                _colorIn!, iterateIn, iterateOut, _gradientMagnitude!, _scratch, gridWidth, gridHeight, derived.FlattenBeta));
            context.Barrier(iterateOut);
            (iterateIn, iterateOut) = (iterateOut, iterateIn);
        }

        for (var iteration = 0; iteration < derived.FdogIterations; iteration++)
        {
            context.For(gridWidth, gridHeight, new DogShader(
                _gray!, tangent, _response!, gridWidth, gridHeight, derived.LineSigmaCells, derived.DogExtent));
            context.Barrier(_response!);
            context.For(gridWidth, gridHeight, new FlowAccumulateShader(
                _response!, tangent, _line!, gridWidth, gridHeight, derived.FlowSigma, derived.FlowExtent));
            context.Barrier(_line!);
            if (iteration < derived.FdogIterations - 1)
            {
                context.For(gridWidth, gridHeight, new SuperimposeShader(_line!, _gray!, gridWidth, gridHeight, derived.LineThreshold));
                context.Barrier(_gray!);
            }
        }
    }

    private void RecordRenderStage(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> output,
        PixelRect rect,
        in DerivedValues derived,
        in Parameters parameters)
    {
        context.For(rect.Width, rect.Height, new RenderShader(
            _colorA!, _line!, output,
            rect.X, rect.Y, rect.Width, rect.Height, _gridWidth, _gridHeight,
            derived.CellSize,
            UkiyoeSettings.ClampPaletteLevels(parameters.PaletteLevels),
            derived.ShiftPixels,
            Math.Clamp(parameters.Baren, 0f, 1f),
            Math.Clamp(parameters.Paper, 0f, 1f),
            derived.LineThreshold,
            Math.Clamp(parameters.LineStrength, 0f, 1f),
            parameters.LineColorR,
            parameters.LineColorG,
            parameters.LineColorB,
            parameters.Seed));
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

        DisposeGridBuffers();
        var gridLength = gridWidth * gridHeight;
        _colorIn = _device.AllocateReadWriteBuffer<Float4>(gridLength);
        _colorA = _device.AllocateReadWriteBuffer<Float4>(gridLength);
        _colorB = _device.AllocateReadWriteBuffer<Float4>(gridLength);
        _gray = _device.AllocateReadWriteBuffer<float>(gridLength);
        _gradientMagnitude = _device.AllocateReadWriteBuffer<float>(gridLength);
        _response = _device.AllocateReadWriteBuffer<float>(gridLength);
        _line = _device.AllocateReadWriteBuffer<float>(gridLength);
        _tangentA = _device.AllocateReadWriteBuffer<Float2>(gridLength);
        _tangentB = _device.AllocateReadWriteBuffer<Float2>(gridLength);
        _cachedLitCount = 0;
        _cachedBoundsMinX = int.MaxValue;
        _cachedBoundsMinY = int.MaxValue;
        _cachedBoundsMaxX = int.MinValue;
        _cachedBoundsMaxY = int.MinValue;
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

    private void DisposeGridBuffers()
    {
        _colorIn?.Dispose();
        _colorA?.Dispose();
        _colorB?.Dispose();
        _gray?.Dispose();
        _gradientMagnitude?.Dispose();
        _response?.Dispose();
        _line?.Dispose();
        _tangentA?.Dispose();
        _tangentB?.Dispose();
        _colorIn = null;
        _colorA = null;
        _colorB = null;
        _gray = null;
        _gradientMagnitude = null;
        _response = null;
        _line = null;
        _tangentA = null;
        _tangentB = null;
        _cachedLitCount = 0;
        _cachedBoundsMinX = int.MaxValue;
        _cachedBoundsMinY = int.MaxValue;
        _cachedBoundsMaxX = int.MinValue;
        _cachedBoundsMaxY = int.MinValue;
        _structureKey = null;
        _gridWidth = 0;
        _gridHeight = 0;
    }

    public void Dispose()
    {
        DisposeGridBuffers();
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = null;
        _packedOutput = null;
        _packedWidth = 0;
        _packedHeight = 0;
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

    private readonly record struct DerivedValues(
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
