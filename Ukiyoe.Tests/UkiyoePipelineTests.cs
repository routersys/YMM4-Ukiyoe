using ComputeWeave;
using ComputeWeave.Interop;

namespace Ukiyoe.Tests;

[Collection("Direct3D12")]
public sealed class UkiyoePipelineTests
{
    const int Opaque = unchecked((int)0xFFC0C0C0);

    static UkiyoePipeline CreatePipeline()
    {
        var pipeline = UkiyoePipeline.TryCreate();
        if (pipeline is null)
            Assert.Skip("Direct3D 12 is unavailable.");
        return pipeline;
    }

    static UkiyoePipeline.Parameters Parameters(
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

    static int[] Square(int width, int height, int left, int top, int squareWidth, int squareHeight)
    {
        var pixels = new int[width * height];
        for (var y = Math.Max(top, 0); y < Math.Min(top + squareHeight, height); y++)
        {
            for (var x = Math.Max(left, 0); x < Math.Min(left + squareWidth, width); x++)
                pixels[y * width + x] = Opaque;
        }

        return pixels;
    }

    static int[] Gradient(int width, int height, int left, int top, int regionWidth, int regionHeight)
    {
        var pixels = new int[width * height];
        for (var y = Math.Max(top, 0); y < Math.Min(top + regionHeight, height); y++)
        {
            var vertical = (y - top) / (double)regionHeight;
            var red = (int)(70 + 90 * vertical);
            var green = (int)(110 + 60 * vertical);
            var blue = (int)(170 + 40 * vertical);
            for (var x = Math.Max(left, 0); x < Math.Min(left + regionWidth, width); x++)
                pixels[y * width + x] = unchecked((int)0xFF000000) | red << 16 | green << 8 | blue;
        }

        return pixels;
    }

    static int[] Noisy(int width, int height, int left, int top, int regionWidth, int regionHeight)
    {
        var pixels = Gradient(width, height, left, top, regionWidth, regionHeight);
        for (var y = Math.Max(top, 0); y < Math.Min(top + regionHeight, height); y++)
        {
            for (var x = Math.Max(left, 0); x < Math.Min(left + regionWidth, width); x++)
            {
                var hash = (uint)(x * 374761393 + y * 668265263);
                hash = (hash ^ (hash >> 13)) * 1274126177u;
                var noise = (int)((hash >> 24) & 63) - 32;
                var pixel = pixels[y * width + x];
                var red = Math.Clamp(((pixel >> 16) & 255) + noise, 0, 255);
                var green = Math.Clamp(((pixel >> 8) & 255) + noise, 0, 255);
                var blue = Math.Clamp((pixel & 255) + noise, 0, 255);
                pixels[y * width + x] = unchecked((int)0xFF000000) | red << 16 | green << 8 | blue;
            }
        }

        return pixels;
    }

    static int Alpha(int pixel) => (pixel >> 24) & 255;

    static double Luma(int pixel) => 0.299 * ((pixel >> 16) & 255) + 0.587 * ((pixel >> 8) & 255) + 0.114 * (pixel & 255);

    static int LitPixels(int[] pixels) => pixels.Count(pixel => Alpha(pixel) > 8);

    static int DarkPixels(int[] pixels) => pixels.Count(pixel => Alpha(pixel) > 8 && Luma(pixel) < Alpha(pixel) * 0.35);

    static double LumaVariance(int[] pixels, int width, int left, int top, int regionWidth, int regionHeight)
    {
        var lumas = new List<double>();
        for (var y = top; y < top + regionHeight; y++)
        {
            for (var x = left; x < left + regionWidth; x++)
                lumas.Add(Luma(pixels[y * width + x]));
        }

        var mean = lumas.Average();
        return lumas.Average(luma => luma * luma) - mean * mean;
    }

    static void Upload(ReadWriteTexture2D<Bgra32, Float4> texture, int[] pixels)
        => texture.CopyFrom(pixels.Select(pixel => new Bgra32 { PackedValue = unchecked((uint)pixel) }).ToArray());

    static int[] Render(UkiyoePipeline pipeline, int[] source, int width, int height, UkiyoePipeline.Parameters parameters)
    {
        var destination = new int[source.Length];
        pipeline.Process(source, destination, width, height, in parameters);
        return destination;
    }

    static UkiyoePipeline.Parameters Changed(UkiyoePipeline.Parameters parameters, string setting)
        => setting switch
        {
            nameof(UkiyoePipeline.Parameters.Quality) => parameters with { Quality = UkiyoeQuality.High },
            nameof(UkiyoePipeline.Parameters.LineWidth) => parameters with { LineWidth = 0.95f },
            nameof(UkiyoePipeline.Parameters.Coherence) => parameters with { Coherence = 0.05f },
            nameof(UkiyoePipeline.Parameters.LineDetail) => parameters with { LineDetail = 0.95f },
            nameof(UkiyoePipeline.Parameters.Flatten) => parameters with { Flatten = 0.15f },
            nameof(UkiyoePipeline.Parameters.PaletteLevels) => parameters with { PaletteLevels = 3 },
            nameof(UkiyoePipeline.Parameters.Misregistration) => parameters with { Misregistration = 1f },
            nameof(UkiyoePipeline.Parameters.Baren) => parameters with { Baren = 1f },
            nameof(UkiyoePipeline.Parameters.Paper) => parameters with { Paper = 1f },
            nameof(UkiyoePipeline.Parameters.LineStrength) => parameters with { LineStrength = 0.2f },
            nameof(UkiyoePipeline.Parameters.LineColorR) => parameters with { LineColorR = 0.8f },
            nameof(UkiyoePipeline.Parameters.LineColorG) => parameters with { LineColorG = 0.8f },
            nameof(UkiyoePipeline.Parameters.LineColorB) => parameters with { LineColorB = 0.8f },
            nameof(UkiyoePipeline.Parameters.Seed) => parameters with { Seed = 4 },
            _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null),
        };

    static ReadWriteTexture2D<Bgra32, Float4> Uploaded(int[] pixels)
    {
        var texture = GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Bgra32, Float4>(128, 128);
        Upload(texture, pixels);
        return texture;
    }

    [Fact]
    public void ATransparentSourceStaysTransparent()
    {
        using var pipeline = CreatePipeline();
        var destination = Enumerable.Repeat(-1, 64 * 64).ToArray();
        var parameters = Parameters();

        pipeline.Process(new int[64 * 64], destination, 64, 64, in parameters);

        Assert.All(destination, pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void AnOpaqueSourceProducesAPrint()
    {
        using var pipeline = CreatePipeline();

        var rendering = Render(pipeline, Square(128, 128, 32, 32, 64, 64), 128, 128, Parameters());

        Assert.True(LitPixels(rendering) > 0);
    }

    [Fact]
    public void TheSameSettingsAlwaysProduceTheSamePrint()
    {
        using var pipeline = CreatePipeline();
        var source = Gradient(128, 128, 24, 24, 80, 80);

        var first = Render(pipeline, source, 128, 128, Parameters(seed: 42));
        var second = Render(pipeline, source, 128, 128, Parameters(seed: 42));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ADifferentSeedPrintsDifferently()
    {
        using var pipeline = CreatePipeline();
        var source = Gradient(128, 128, 24, 24, 80, 80);

        var first = Render(pipeline, source, 128, 128, Parameters(misregistration: 1f, seed: 1));
        var second = Render(pipeline, source, 128, 128, Parameters(misregistration: 1f, seed: 2));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ThePrintStaysPremultiplied()
    {
        using var pipeline = CreatePipeline();

        var rendering = Render(pipeline, Gradient(128, 128, 24, 24, 80, 80), 128, 128, Parameters(paletteLevels: 2, misregistration: 1f, paper: 1f, baren: 1f, lineStrength: 1f));

        Assert.All(rendering, pixel =>
        {
            Assert.InRange((pixel >> 16) & 255, 0, Alpha(pixel));
            Assert.InRange((pixel >> 8) & 255, 0, Alpha(pixel));
            Assert.InRange(pixel & 255, 0, Alpha(pixel));
        });
    }

    [Fact]
    public void FlatteningEvensOutTheTexture()
    {
        using var pipeline = CreatePipeline();
        var source = Noisy(160, 160, 16, 16, 128, 128);

        var weak = Render(pipeline, source, 160, 160, Parameters(flatten: 0f, misregistration: 0f, baren: 0f, paper: 0f, lineStrength: 0f));
        var strong = Render(pipeline, source, 160, 160, Parameters(flatten: 1f, misregistration: 0f, baren: 0f, paper: 0f, lineStrength: 0f));

        Assert.True(LumaVariance(strong, 160, 32, 32, 96, 96) < LumaVariance(weak, 160, 32, 32, 96, 96));
    }

    [Fact]
    public void MisregistrationSpreadsThePrintOverMorePixels()
    {
        using var pipeline = CreatePipeline();
        var source = Gradient(128, 128, 32, 32, 64, 64);

        var aligned = Render(pipeline, source, 128, 128, Parameters(misregistration: 0f));
        var shifted = Render(pipeline, source, 128, 128, Parameters(misregistration: 1f));

        Assert.True(LitPixels(shifted) > LitPixels(aligned));
    }

    [Fact]
    public void TheKeyBlockLinesDarkenThePrint()
    {
        using var pipeline = CreatePipeline();
        var source = Square(128, 128, 40, 40, 48, 48);

        var withLines = Render(pipeline, source, 128, 128, Parameters(misregistration: 0f, lineStrength: 1f));
        var withoutLines = Render(pipeline, source, 128, 128, Parameters(misregistration: 0f, lineStrength: 0f));

        Assert.NotEqual(withLines, withoutLines);
        Assert.True(DarkPixels(withLines) > DarkPixels(withoutLines));
    }

    [Fact]
    public void AWarmPipelineAllocatesNoManagedMemory()
    {
        using var pipeline = CreatePipeline();
        var source = Square(64, 64, 24, 24, 16, 16);
        var destination = new int[source.Length];
        var parameters = Parameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, 64, 64, in parameters);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, 64, 64, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, minimum);
    }

    [Fact]
    public void SharedTexturesProduceTheSamePrintAsPackedBuffers()
    {
        using var pipeline = CreatePipeline();
        var source = Gradient(96, 96, 16, 16, 64, 64);
        var parameters = Parameters(seed: 11);
        var expected = Render(pipeline, source, 96, 96, parameters);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 96, 96);
        using var outputTexture = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 96, 96);
        Upload(sourceTexture, source);

        pipeline.ProcessSharedAndWait(sourceTexture, outputTexture, 96, 96, in parameters);
        var result = new Bgra32[source.Length];
        outputTexture.CopyTo(result);

        Assert.Equal(expected.Select(pixel => unchecked((uint)pixel)), result.Select(pixel => pixel.PackedValue));
    }

    [Fact]
    public void RepeatedSharedTextureSubmissionsAllocateNoManagedMemory()
    {
        using var pipeline = CreatePipeline();
        var device = GraphicsDevice.GetDefault();
        using var source = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 64, 64);
        using var destination = InteropServices.AllocateSharedReadWriteTexture2D<Bgra32, Float4>(device, 64, 64);
        var parameters = Parameters();
        for (var iteration = 0; iteration < 4; iteration++)
            pipeline.Process(source, destination, 64, 64, in parameters);
        pipeline.WaitForCompletion();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var minimum = long.MaxValue;
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.Process(source, destination, 64, 64, in parameters);
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        pipeline.WaitForCompletion();

        Assert.Equal(0, minimum);
    }

    [Fact]
    public void TheVisibleBoundsHoldEveryLitPixelAndRenderTheSamePrint()
    {
        using var pipeline = CreatePipeline();
        var source = Gradient(192, 192, 64, 64, 64, 48);
        var parameters = Parameters(seed: 5);
        var full = Render(pipeline, source, 192, 192, parameters);
        var device = GraphicsDevice.GetDefault();
        using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(192, 192);
        Upload(sourceTexture, source);

        pipeline.Simulate(sourceTexture, 192, 192, 0, 0, 192, 192, in parameters);
        Assert.True(pipeline.TryGetVisibleBounds(192, 192, in parameters, out var rect));
        using var outputTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(outputTexture, 192, 192, rect, in parameters);
        var visible = new Bgra32[rect.Width * rect.Height];
        outputTexture.CopyTo(visible);

        Assert.True(rect is { Width: > 0, Height: > 0, X: >= 0, Y: >= 0 });
        Assert.True(rect.X + rect.Width <= 192 && rect.Y + rect.Height <= 192);
        for (var y = 0; y < 192; y++)
        {
            for (var x = 0; x < 192; x++)
            {
                var inside = x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;
                if (!inside)
                    Assert.Equal(0, full[y * 192 + x]);
                else
                    Assert.Equal(unchecked((uint)full[y * 192 + x]), visible[(y - rect.Y) * rect.Width + x - rect.X].PackedValue);
            }
        }
    }

    [Theory]
    [InlineData(nameof(UkiyoePipeline.Parameters.Flatten))]
    [InlineData(nameof(UkiyoePipeline.Parameters.LineWidth))]
    [InlineData(nameof(UkiyoePipeline.Parameters.Coherence))]
    [InlineData(nameof(UkiyoePipeline.Parameters.LineDetail))]
    public void APartialRecomputeDrawsLikeAFullRecompute(string setting)
    {
        using var incremental = CreatePipeline();
        using var reference = CreatePipeline();
        using var source = Uploaded(Gradient(128, 128, 40, 40, 48, 48));
        var device = GraphicsDevice.GetDefault();
        var first = Parameters(seed: 7);
        var second = Changed(first, setting);
        incremental.Simulate(source, 128, 128, 0, 0, 128, 128, in first);

        Assert.True(incremental.Simulate(source, 128, 128, 0, 0, 128, 128, in second));
        reference.Simulate(source, 128, 128, 0, 0, 128, 128, in second);
        Assert.True(incremental.TryGetVisibleBounds(128, 128, in second, out var rect));
        Assert.True(reference.TryGetVisibleBounds(128, 128, in second, out var referenceRect));
        Assert.Equal(referenceRect, rect);
        using var incrementalOutput = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        using var referenceOutput = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        incremental.RenderVisible(incrementalOutput, 128, 128, rect, in second);
        reference.RenderVisible(referenceOutput, 128, 128, rect, in second);
        var incrementalPixels = new Bgra32[rect.Width * rect.Height];
        var referencePixels = new Bgra32[rect.Width * rect.Height];
        incrementalOutput.CopyTo(incrementalPixels);
        referenceOutput.CopyTo(referencePixels);

        Assert.Equal(referencePixels.Select(pixel => pixel.PackedValue), incrementalPixels.Select(pixel => pixel.PackedValue));
    }

    [Theory]
    [InlineData(nameof(UkiyoePipeline.Parameters.PaletteLevels))]
    [InlineData(nameof(UkiyoePipeline.Parameters.Misregistration))]
    [InlineData(nameof(UkiyoePipeline.Parameters.Baren))]
    [InlineData(nameof(UkiyoePipeline.Parameters.Paper))]
    [InlineData(nameof(UkiyoePipeline.Parameters.LineStrength))]
    [InlineData(nameof(UkiyoePipeline.Parameters.LineColorR))]
    [InlineData(nameof(UkiyoePipeline.Parameters.LineColorG))]
    [InlineData(nameof(UkiyoePipeline.Parameters.LineColorB))]
    [InlineData(nameof(UkiyoePipeline.Parameters.Seed))]
    public void ChangingOnlyThePrintingKeepsTheStructure(string setting)
    {
        using var pipeline = CreatePipeline();
        using var source = Uploaded(Gradient(128, 128, 48, 48, 48, 48));
        var parameters = Parameters(seed: 3);
        Assert.True(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters));

        var changed = Changed(parameters, setting);

        Assert.False(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in changed), setting);
    }

    [Theory]
    [InlineData(nameof(UkiyoePipeline.Parameters.Quality))]
    [InlineData(nameof(UkiyoePipeline.Parameters.LineWidth))]
    [InlineData(nameof(UkiyoePipeline.Parameters.Coherence))]
    [InlineData(nameof(UkiyoePipeline.Parameters.LineDetail))]
    [InlineData(nameof(UkiyoePipeline.Parameters.Flatten))]
    public void ChangingTheStructureSettingsComputesTheStructureAgain(string setting)
    {
        using var pipeline = CreatePipeline();
        using var source = Uploaded(Gradient(128, 128, 48, 48, 48, 48));
        var parameters = Parameters(seed: 3);
        Assert.True(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters));
        Assert.False(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters));

        var changed = Changed(parameters, setting);

        Assert.True(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in changed), setting);
    }

    [Fact]
    public void MovingTheShapeComputesTheStructureAgain()
    {
        using var pipeline = CreatePipeline();
        using var source = Uploaded(Gradient(128, 128, 48, 48, 48, 48));
        var parameters = Parameters(seed: 3);
        Assert.True(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters));
        Assert.False(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters));

        Upload(source, Gradient(128, 128, 32, 32, 48, 48));

        Assert.True(pipeline.Simulate(source, 128, 128, 0, 0, 128, 128, in parameters));
    }
}
