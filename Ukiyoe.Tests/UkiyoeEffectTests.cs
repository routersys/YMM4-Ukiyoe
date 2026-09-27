using System.Runtime.InteropServices;
using ComputeWeave;
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
                using var sourceBitmap = new ID2D1Bitmap1(borrow.DangerousGetView().AddRefBitmap());
                renderContext.Target = sourceBitmap;
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
        using var outputBitmap = new ID2D1Bitmap1(outputLease.DangerousGetView().AddRefBitmap());
        staging.CopyFromBitmap(outputBitmap);
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
}
