using System.Runtime.InteropServices;
using ComputeWeave;
using ComputeWeave.Interop;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace Ukiyoe.Tests;

public sealed class UkiyoeEffectTests
{
    private static double ValueAt(YukkuriMovieMaker.Commons.Animation animation) => animation.GetValue(0, 1, 30);

    private static UkiyoePipeline.Parameters CreateParameters(
        UkiyoeQuality quality = UkiyoeQuality.Balanced,
        float lineWidth = 0.5f,
        float coherence = 0.5f,
        float lineDetail = 0.5f,
        float flatten = 0.6f,
        int paletteLevels = 6,
        float misregistration = 0.3f,
        float baren = 0.4f,
        float paper = 0.5f,
        float lineStrength = 0.85f,
        int seed = 0)
        => new(quality, lineWidth, coherence, lineDetail, flatten, paletteLevels, misregistration, baren, paper, lineStrength, 0.12f, 0.1f, 0.09f, seed);

    [Fact]
    public void DefaultParameterValuesMatchSpecification()
    {
        var effect = new UkiyoeEffect();

        Assert.Equal(100d, ValueAt(effect.Amount), 6);
        Assert.Equal(50d, ValueAt(effect.LineWidth), 6);
        Assert.Equal(50d, ValueAt(effect.Coherence), 6);
        Assert.Equal(50d, ValueAt(effect.LineDetail), 6);
        Assert.Equal(85d, ValueAt(effect.LineStrength), 6);
        Assert.Equal(60d, ValueAt(effect.Flatten), 6);
        Assert.Equal(30d, ValueAt(effect.Misregistration), 6);
        Assert.Equal(40d, ValueAt(effect.Baren), 6);
        Assert.Equal(50d, ValueAt(effect.Paper), 6);
        Assert.Equal(UkiyoeQuality.High, effect.Quality);
        Assert.Equal(6, effect.PaletteLevels);
        Assert.Equal(0, effect.Seed);
        Assert.Equal(System.Windows.Media.Color.FromArgb(255, 30, 26, 24), effect.LineColor);
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1234, 1234)]
    public void SeedClampsNegativeInputToZero(int input, int expected)
    {
        var effect = new UkiyoeEffect { Seed = input };

        Assert.Equal(expected, effect.Seed);
    }

    [Theory]
    [InlineData(int.MinValue, 2)]
    [InlineData(0, 2)]
    [InlineData(6, 6)]
    [InlineData(100, 16)]
    public void PaletteLevelsClampToAllowedRange(int input, int expected)
    {
        var effect = new UkiyoeEffect { PaletteLevels = input };

        Assert.Equal(expected, effect.PaletteLevels);
    }

    [Fact]
    public void CreateExoVideoFiltersReturnsEmpty()
    {
        var effect = new UkiyoeEffect();

        Assert.Empty(effect.CreateExoVideoFilters(0, null!));
    }

    [Theory]
    [InlineData(UkiyoeQuality.Balanced, 1024, 2, 2, 24)]
    [InlineData(UkiyoeQuality.High, 1440, 3, 2, 40)]
    [InlineData(UkiyoeQuality.Ultra, 2048, 3, 3, 64)]
    public void QualitySettingsMatchSpecification(UkiyoeQuality quality, int resolution, int etfIterations, int fdogIterations, int flattenIterations)
    {
        var settings = UkiyoeSettings.GetQuality(quality);

        Assert.Equal(resolution, settings.GridResolution);
        Assert.Equal(etfIterations, settings.EtfIterations);
        Assert.Equal(fdogIterations, settings.FdogIterations);
        Assert.Equal(flattenIterations, settings.FlattenIterations);
        Assert.Equal(0, settings.FlattenIterations % 2);
    }

    [Theory]
    [InlineData(1920, 1080, 1440)]
    [InlineData(1080, 1920, 1440)]
    [InlineData(8, 8, 1440)]
    [InlineData(4096, 16, 1024)]
    [InlineData(100, 100, 2048)]
    public void GridSizeCoversCanvas(int width, int height, int resolution)
    {
        var (gridWidth, gridHeight, cellSize) = UkiyoeSettings.GetGridSize(width, height, resolution);

        Assert.True(gridWidth >= UkiyoeSettings.MinimumGridSize);
        Assert.True(gridHeight >= UkiyoeSettings.MinimumGridSize);
        Assert.True(cellSize > 0f);
        Assert.True(gridWidth * cellSize >= width);
        Assert.True(gridHeight * cellSize >= height);
    }

    [Fact]
    public void ParameterMappingsAreMonotonicAndBounded()
    {
        Assert.Equal(UkiyoeSettings.MinimumLineSigmaPixels, UkiyoeSettings.GetLineSigmaPixels(0f), 5);
        Assert.Equal(UkiyoeSettings.MaximumLineSigmaPixels, UkiyoeSettings.GetLineSigmaPixels(1f), 5);
        Assert.Equal(UkiyoeSettings.MinimumLineSigmaPixels, UkiyoeSettings.GetLineSigmaPixels(-5f), 5);
        Assert.Equal(UkiyoeSettings.MaximumLineSigmaPixels, UkiyoeSettings.GetLineSigmaPixels(5f), 5);
        Assert.True(UkiyoeSettings.GetLineSigmaPixels(0.75f) > UkiyoeSettings.GetLineSigmaPixels(0.25f));

        Assert.Equal(UkiyoeSettings.MinimumFlowSigma, UkiyoeSettings.GetFlowSigma(0f), 5);
        Assert.Equal(UkiyoeSettings.MaximumFlowSigma, UkiyoeSettings.GetFlowSigma(1f), 5);
        Assert.True(UkiyoeSettings.GetFlowSigma(0.75f) > UkiyoeSettings.GetFlowSigma(0.25f));

        Assert.Equal(UkiyoeSettings.MinimumLineThreshold, UkiyoeSettings.GetLineThreshold(0f), 5);
        Assert.Equal(UkiyoeSettings.MinimumLineThreshold + UkiyoeSettings.LineThresholdRange, UkiyoeSettings.GetLineThreshold(1f), 5);
        Assert.True(UkiyoeSettings.GetLineThreshold(0.75f) > UkiyoeSettings.GetLineThreshold(0.25f));

        Assert.Equal(UkiyoeSettings.MaximumFlattenBeta, UkiyoeSettings.GetFlattenBeta(0f), 1);
        Assert.True(UkiyoeSettings.GetFlattenBeta(1f) <= 1.001f);
        Assert.True(UkiyoeSettings.GetFlattenBeta(0.25f) > UkiyoeSettings.GetFlattenBeta(0.75f));

        Assert.Equal(0f, UkiyoeSettings.GetShiftPixels(0f), 6);
        Assert.Equal(UkiyoeSettings.MaximumShiftPixels, UkiyoeSettings.GetShiftPixels(1f), 6);
        Assert.Equal(0f, UkiyoeSettings.GetShiftPixels(-1f), 6);
        Assert.Equal(UkiyoeSettings.MaximumShiftPixels, UkiyoeSettings.GetShiftPixels(2f), 6);
    }

    [Fact]
    public void MarginCoversReachAndAlignsToFourPixels()
    {
        var margin = UkiyoeSettings.GetMargin(10f, 3f, 6f, 2f);

        Assert.Equal(0, margin % 4);
        Assert.True(margin >= 10f + 2f * UkiyoeSettings.SurroundSigmaScale * 3f + 2f * 6f * 2f);
        Assert.True(UkiyoeSettings.GetMargin(0f, 0.6f, 1f, 1f) < margin);
    }

    [Fact]
    public void TransparentInputYieldsTransparentOutput()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = new int[width * height];
        var destination = new int[source.Length];
        Array.Fill(destination, -1);
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void OpaqueInputProducesOutput()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 32, 32, 64, 64);
        var destination = new int[source.Length];
        var parameters = CreateParameters();

        pipeline.Process(source, destination, width, height, in parameters);

        Assert.True(CountLitPixels(destination) > 0);
    }

    [Fact]
    public void GpuPipelineIsDeterministic()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateGradientSource(width, height, 24, 24, 80, 80);
        var first = new int[source.Length];
        var second = new int[source.Length];
        var parameters = CreateParameters(seed: 42);

        pipeline.Process(source, first, width, height, in parameters);
        pipeline.Process(source, second, width, height, in parameters);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentPrints()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateGradientSource(width, height, 24, 24, 80, 80);
        var first = new int[source.Length];
        var second = new int[source.Length];

        var parametersA = CreateParameters(misregistration: 1f, seed: 1);
        var parametersB = CreateParameters(misregistration: 1f, seed: 2);
        pipeline.Process(source, first, width, height, in parametersA);
        pipeline.Process(source, second, width, height, in parametersB);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void OutputAlphaStaysPremultipliedAndBounded()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateGradientSource(width, height, 24, 24, 80, 80);
        var destination = new int[source.Length];
        var parameters = CreateParameters(paletteLevels: 2, misregistration: 1f, paper: 1f, baren: 1f, lineStrength: 1f);

        pipeline.Process(source, destination, width, height, in parameters);

        foreach (var pixel in destination)
        {
            var alpha = (pixel >> 24) & 255;
            Assert.InRange((pixel >> 16) & 255, 0, alpha);
            Assert.InRange((pixel >> 8) & 255, 0, alpha);
            Assert.InRange(pixel & 255, 0, alpha);
        }
    }

    [Fact]
    public void FlattenReducesTexture()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 160;
        const int height = 160;
        var source = CreateNoisySource(width, height, 16, 16, 128, 128);
        var weak = new int[source.Length];
        var strong = new int[source.Length];

        var weakParameters = CreateParameters(flatten: 0f, misregistration: 0f, baren: 0f, paper: 0f, lineStrength: 0f);
        var strongParameters = CreateParameters(flatten: 1f, misregistration: 0f, baren: 0f, paper: 0f, lineStrength: 0f);
        pipeline.Process(source, weak, width, height, in weakParameters);
        pipeline.Process(source, strong, width, height, in strongParameters);

        Assert.True(LumaVariance(strong, width, height, 32, 32, 96, 96) < LumaVariance(weak, width, height, 32, 32, 96, 96));
    }

    [Fact]
    public void MisregistrationExpandsLitArea()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateGradientSource(width, height, 32, 32, 64, 64);
        var aligned = new int[source.Length];
        var shifted = new int[source.Length];

        var alignedParameters = CreateParameters(misregistration: 0f);
        var shiftedParameters = CreateParameters(misregistration: 1f);
        pipeline.Process(source, aligned, width, height, in alignedParameters);
        pipeline.Process(source, shifted, width, height, in shiftedParameters);

        Assert.True(CountLitPixels(shifted) > CountLitPixels(aligned));
    }

    [Fact]
    public void LineStrengthDarkensKeyBlockPixels()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateSquareSource(width, height, 40, 40, 48, 48);
        var withLine = new int[source.Length];
        var withoutLine = new int[source.Length];

        var lineParameters = CreateParameters(misregistration: 0f, lineStrength: 1f);
        var noLineParameters = CreateParameters(misregistration: 0f, lineStrength: 0f);
        pipeline.Process(source, withLine, width, height, in lineParameters);
        pipeline.Process(source, withoutLine, width, height, in noLineParameters);

        Assert.NotEqual(withLine, withoutLine);
        Assert.True(CountDarkPixels(withLine) > CountDarkPixels(withoutLine));
    }

    [Fact]
    public void GpuPipelineDoesNotAllocateManagedMemoryAfterWarmup()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var source = CreateSquareSource(width, height, 24, 24, 16, 16);
        var destination = new int[source.Length];
        var parameters = CreateParameters();
        pipeline.Process(source, destination, width, height, in parameters);
        pipeline.Process(source, destination, width, height, in parameters);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        pipeline.Process(source, destination, width, height, in parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SharedTexturePipelineMatchesPackedBufferPipeline()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 96;
        const int height = 96;
        var source = CreateGradientSource(width, height, 16, 16, 64, 64);
        var expected = new int[source.Length];
        var parameters = CreateParameters(seed: 11);
        pipeline.Process(source, expected, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var outputTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);
        pipeline.ProcessSharedAndWait(sourceTexture, outputTexture, width, height, in parameters);
        var result = new Bgra32[source.Length];
        outputTexture.CopyTo(result);

        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(unchecked((uint)expected[index]), result[index].PackedValue);
    }

    [Fact]
    public void SubmittedSharedTexturePipelineAllocationsAmortizeToZero()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 64;
        const int height = 64;
        var device = GraphicsDevice.GetDefault();
        using var source = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        using var destination = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, width, height);
        var parameters = CreateParameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, width, height, in parameters);
        pipeline.WaitForCompletion();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, width, height, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        pipeline.WaitForCompletion();

        Assert.Equal(0, minimum);
    }

    [Fact]
    public void VisibleBoundsCoverAllLitPixelsAndMatchFullRender()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 192;
        const int height = 192;
        var source = CreateGradientSource(width, height, 64, 64, 64, 48);
        var full = new int[source.Length];
        var parameters = CreateParameters(seed: 5);
        pipeline.Process(source, full, width, height, in parameters);

        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);

        pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect));
        Assert.True(rect.Width > 0 && rect.Height > 0);
        Assert.True(rect.X >= 0 && rect.Y >= 0);
        Assert.True(rect.X + rect.Width <= width && rect.Y + rect.Height <= height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (full[y * width + x] == 0)
                    continue;
                Assert.InRange(x, rect.X, rect.X + rect.Width - 1);
                Assert.InRange(y, rect.Y, rect.Y + rect.Height - 1);
            }
        }

        using var outputTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(outputTexture, width, height, rect, in parameters);
        var result = new Bgra32[rect.Width * rect.Height];
        outputTexture.CopyTo(result);

        for (var y = 0; y < rect.Height; y++)
        {
            for (var x = 0; x < rect.Width; x++)
            {
                var expected = unchecked((uint)full[(rect.Y + y) * width + rect.X + x]);
                Assert.Equal(expected, result[y * rect.Width + x].PackedValue);
            }
        }
    }

    [Fact]
    public void SimulateCachesStructureUntilInputsChange()
    {
        using var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
        {
            Assert.Skip("Direct3D 12 is unavailable.");
            return;
        }

        const int width = 128;
        const int height = 128;
        var source = CreateGradientSource(width, height, 48, 48, 48, 48);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        var sourcePixels = new Bgra32[source.Length];
        for (var index = 0; index < source.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)source[index]);
        sourceTexture.CopyFrom(sourcePixels);

        var parameters = CreateParameters(seed: 3);
        Assert.True(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters));
        Assert.False(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters));

        var seedChanged = parameters with { Seed = 4 };
        Assert.False(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in seedChanged));

        var paletteChanged = parameters with { PaletteLevels = 3 };
        Assert.False(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in paletteChanged));

        var misregistrationChanged = parameters with { Misregistration = 1f };
        Assert.False(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in misregistrationChanged));

        var flattenChanged = parameters with { Flatten = 0.9f };
        Assert.True(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in flattenChanged));

        var lineWidthChanged = flattenChanged with { LineWidth = 0.9f };
        Assert.True(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in lineWidthChanged));

        var movedSource = CreateGradientSource(width, height, 32, 32, 48, 48);
        for (var index = 0; index < movedSource.Length; index++)
            sourcePixels[index].PackedValue = unchecked((uint)movedSource[index]);
        sourceTexture.CopyFrom(sourcePixels);
        Assert.True(pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in lineWidthChanged));
    }

    [Fact]
    public void Direct2DInteropProducesPrintAfterGrowingFullHdOutput()
    {
        using var devices = new GraphicsDevices();
        using var graphicsContext = devices.CreateContext();
        using var scheduler = ComputeExternalQueueScheduler.Create();
        using var provider = UkiyoeInteropProvider.TryCreate(graphicsContext, scheduler, out var interopDevice);
        if (provider is null || interopDevice is null)
        {
            Assert.Skip("Direct3D 11 and Direct3D 12 sharing is unavailable.");
            return;
        }

        using var domain = interopDevice.RegisterExternalDomain(provider);
        using var resourceSet = UkiyoeResourceSet.Create(interopDevice, domain);
        using var pipeline = UkiyoePipeline.TryCreate(interopDevice);
        Assert.NotNull(pipeline);

        const int width = 96;
        const int height = 96;
        var pixels = CreateSquareSource(width, height, 32, 32, 32, 32);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        using var inputBitmap = graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(width, height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.None));
        try
        {
            inputBitmap.CopyFromMemory(handle.AddrOfPinnedObject(), width * sizeof(int));
        }
        finally
        {
            handle.Free();
        }

        Assert.True(resourceSet.TryEnsureSource(width, height, out _));
        var parameters = CreateParameters();
        var renderContext = provider.RenderContext;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            using (var borrow = resourceSet.BeginSourceExternalOperation())
            {
                var previousTarget = renderContext.Target;
                renderContext.Target = borrow.DangerousGetView().Bitmap;
                renderContext.BeginDraw();
                renderContext.Clear(null);
                renderContext.DrawImage(
                    inputBitmap,
                    new System.Numerics.Vector2(0f, 0f),
                    null,
                    InterpolationMode.NearestNeighbor,
                    CompositeMode.SourceCopy);
                renderContext.EndDraw();
                renderContext.Target = previousTarget;
            }

            pipeline!.Simulate(
                resourceSet.GetSourceComputeBinding(), width, height, 0, 0, width, height, in parameters);
            Assert.True(pipeline.TryGetVisibleBounds(width, height, in parameters, out var visible));
            Assert.True(resourceSet.TryEnsureOutput(
                iteration == 0 ? width : 1920,
                iteration == 0 ? height : 1080,
                out _));
            pipeline.RenderVisible(
                resourceSet.GetOutputComputeBinding(), width, height, visible, in parameters);

            if (iteration == 0)
            {
                using var retiredLease = resourceSet.AcquireOutputExternalViewLease();
                Assert.Equal(width, retiredLease.Width);
                Assert.Equal(height, retiredLease.Height);
            }
        }

        using var outputLease = resourceSet.AcquireOutputExternalViewLease();
        using var staging = graphicsContext.DeviceContext.CreateBitmap(
            new SizeI(width, height),
            new BitmapProperties1(
                new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                96f,
                96f,
                BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        staging.CopyFromBitmap(outputLease.DangerousGetView().Bitmap);
        var mapped = staging.Map(MapOptions.Read);
        try
        {
            var lit = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var actual = Marshal.ReadInt32(mapped.Bits + (nint)(y * mapped.Pitch + x * sizeof(int)));
                    var alpha = (actual >> 24) & 255;
                    Assert.InRange((actual >> 16) & 255, 0, alpha);
                    Assert.InRange((actual >> 8) & 255, 0, alpha);
                    Assert.InRange(actual & 255, 0, alpha);
                    if (alpha > 0)
                        lit++;
                }
            }
            Assert.True(lit > 0);
        }
        finally
        {
            staging.Unmap();
        }
    }

    private static int[] CreateSquareSource(int width, int height, int left, int top, int squareWidth, int squareHeight)
    {
        var source = new int[width * height];
        for (var y = top; y < top + squareHeight; y++)
        {
            for (var x = left; x < left + squareWidth; x++)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;
                source[y * width + x] = unchecked((int)0xFFC0C0C0);
            }
        }
        return source;
    }

    private static int[] CreateGradientSource(int width, int height, int left, int top, int regionWidth, int regionHeight)
    {
        var source = new int[width * height];
        for (var y = top; y < top + regionHeight; y++)
        {
            for (var x = left; x < left + regionWidth; x++)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;
                var vertical = (y - top) / (double)regionHeight;
                var r = (int)(70 + 90 * vertical);
                var g = (int)(110 + 60 * vertical);
                var b = (int)(170 + 40 * vertical);
                source[y * width + x] = unchecked((int)0xFF000000 | (r << 16) | (g << 8) | b);
            }
        }
        return source;
    }

    private static int[] CreateNoisySource(int width, int height, int left, int top, int regionWidth, int regionHeight)
    {
        var source = CreateGradientSource(width, height, left, top, regionWidth, regionHeight);
        for (var y = top; y < top + regionHeight; y++)
        {
            for (var x = left; x < left + regionWidth; x++)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;
                var hash = (uint)(x * 374761393 + y * 668265263);
                hash = (hash ^ (hash >> 13)) * 1274126177u;
                var noise = (int)((hash >> 24) & 63) - 32;
                var pixel = source[y * width + x];
                var r = Math.Clamp(((pixel >> 16) & 255) + noise, 0, 255);
                var g = Math.Clamp(((pixel >> 8) & 255) + noise, 0, 255);
                var b = Math.Clamp((pixel & 255) + noise, 0, 255);
                source[y * width + x] = unchecked((int)0xFF000000 | (r << 16) | (g << 8) | b);
            }
        }
        return source;
    }

    private static int CountLitPixels(int[] pixels)
    {
        var count = 0;
        foreach (var pixel in pixels)
        {
            if (((pixel >> 24) & 255) > 8)
                count++;
        }
        return count;
    }

    private static int CountDarkPixels(int[] pixels)
    {
        var count = 0;
        foreach (var pixel in pixels)
        {
            var alpha = (pixel >> 24) & 255;
            if (alpha <= 8)
                continue;
            var luma = 0.299 * ((pixel >> 16) & 255) + 0.587 * ((pixel >> 8) & 255) + 0.114 * (pixel & 255);
            if (luma < alpha * 0.35)
                count++;
        }
        return count;
    }

    private static double LumaVariance(int[] pixels, int width, int height, int left, int top, int regionWidth, int regionHeight)
    {
        var sum = 0.0;
        var squaredSum = 0.0;
        var count = 0;
        for (var y = top; y < top + regionHeight; y++)
        {
            for (var x = left; x < left + regionWidth; x++)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;
                var pixel = pixels[y * width + x];
                var luma = 0.299 * ((pixel >> 16) & 255) + 0.587 * ((pixel >> 8) & 255) + 0.114 * (pixel & 255);
                sum += luma;
                squaredSum += luma * luma;
                count++;
            }
        }
        if (count == 0)
            return 0.0;
        var mean = sum / count;
        return squaredSum / count - mean * mean;
    }
}
