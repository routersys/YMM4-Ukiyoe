using System.Diagnostics;
using ComputeWeave;
using Ukiyoe;

var width = 1280;
var height = 720;
var outputDirectory = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : Path.Combine(AppContext.BaseDirectory, "harness-output");
Directory.CreateDirectory(outputDirectory);

using var pipeline = UkiyoePipeline.TryCreate();
if (pipeline is null)
{
    Console.WriteLine("Direct3D 12 is unavailable.");
    return 1;
}

var source = CreateTestImage(width, height);
var destination = new int[source.Length];

if (args.Contains("--golden"))
{
    var goldenCases = new (string Name, UkiyoePipeline.Parameters Parameters)[]
    {
        ("balanced-default", new(UkiyoeQuality.Balanced, 0.5f, 0.5f, 0.5f, 0.6f, 6, 0.3f, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 0)),
        ("high-default", new(UkiyoeQuality.High, 0.5f, 0.5f, 0.5f, 0.6f, 6, 0.3f, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 0)),
        ("seed-42", new(UkiyoeQuality.Balanced, 0.5f, 0.5f, 0.5f, 0.6f, 6, 0.3f, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 42)),
        ("flat-none", new(UkiyoeQuality.Balanced, 0.5f, 0.5f, 0.5f, 0f, 6, 0.3f, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 0)),
        ("flat-full", new(UkiyoeQuality.Balanced, 0.5f, 0.5f, 0.5f, 1f, 6, 0.3f, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 0)),
        ("palette-2", new(UkiyoeQuality.Balanced, 0.5f, 0.5f, 0.5f, 0.6f, 2, 0.3f, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 0)),
        ("shift-full", new(UkiyoeQuality.Balanced, 0.5f, 0.5f, 0.5f, 0.6f, 6, 1f, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 0)),
        ("thick-line", new(UkiyoeQuality.Balanced, 1f, 1f, 0.8f, 0.6f, 6, 0.3f, 0.4f, 0.5f, 1f, 0.12f, 0.1f, 0.09f, 0)),
    };
    foreach (var (name, goldenParameters) in goldenCases)
    {
        var parameters = goldenParameters;
        pipeline.Process(source, destination, width, height, in parameters);
        var bytes = new byte[destination.Length * sizeof(int)];
        Buffer.BlockCopy(destination, 0, bytes, 0, bytes.Length);
        Console.WriteLine($"{name}: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))}");
    }
    return 0;
}

foreach (var quality in new[] { UkiyoeQuality.Balanced, UkiyoeQuality.High, UkiyoeQuality.Ultra })
{
    var parameters = new UkiyoePipeline.Parameters(quality, 0.5f, 0.5f, 0.5f, 0.6f, 6, 0.3f, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 0);
    pipeline.Process(source, destination, width, height, in parameters);
    pipeline.Process(source, destination, width, height, in parameters);
    var best = double.MaxValue;
    const int frames = 10;
    var stopwatch = new Stopwatch();
    for (var frame = 0; frame < frames; frame++)
    {
        stopwatch.Restart();
        pipeline.Process(source, destination, width, height, in parameters);
        stopwatch.Stop();
        best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
    }
    Console.WriteLine($"{quality}: {best:F2} ms/frame ({width}x{height}) litPixels={CountLit(destination)}");
}

{
    var device = GraphicsDevice.GetDefault();
    using var sourceTexture = device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
    var pixels = new Bgra32[source.Length];
    for (var index = 0; index < source.Length; index++)
        pixels[index].PackedValue = unchecked((uint)source[index]);
    sourceTexture.CopyFrom(pixels);
    var parameters = new UkiyoePipeline.Parameters(UkiyoeQuality.High, 0.5f, 0.5f, 0.5f, 0.6f, 6, 0.3f, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 0);

    pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters);
    pipeline.WaitForCompletion();
    var stopwatch = new Stopwatch();

    var variants = new (string Name, Func<bool, UkiyoePipeline.Parameters> Make)[]
    {
        ("cached", _ => parameters),
        ("flatten", flip => parameters with { Flatten = flip ? 0.70f : 0.60f }),
        ("lineWidth", flip => parameters with { LineWidth = flip ? 0.60f : 0.50f }),
        ("coherence", flip => parameters with { Coherence = flip ? 0.60f : 0.50f }),
        ("lineDetail", flip => parameters with { LineDetail = flip ? 0.60f : 0.50f }),
        ("all", flip => parameters with
        {
            Flatten = flip ? 0.70f : 0.60f,
            LineWidth = flip ? 0.60f : 0.50f,
            Coherence = flip ? 0.60f : 0.50f,
            LineDetail = flip ? 0.60f : 0.50f
        })
    };

    const int rounds = 12;
    var samples = new List<double>[variants.Length];
    for (var index = 0; index < samples.Length; index++)
        samples[index] = new List<double>(rounds);

    // Warm up every variant once so that no sample carries first-use cost.
    foreach (var (_, make) in variants)
    {
        var warmup = make(true);
        pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in warmup);
        pipeline.WaitForCompletion();
    }

    // Interleave the variants and shuffle their order every round, so that a
    // machine that drifts over the run drifts across every variant alike.
    var order = Enumerable.Range(0, variants.Length).ToArray();
    var random = new Random(17);
    for (var round = 0; round < rounds; round++)
    {
        for (var index = order.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (order[index], order[swap]) = (order[swap], order[index]);
        }

        foreach (var index in order)
        {
            // Settle the cache into this variant's baseline without measuring it,
            // so the measured call invalidates only the subgraph under test.
            var settled = variants[index].Make(false);
            pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in settled);
            pipeline.WaitForCompletion();

            var measured = variants[index].Make(true);
            stopwatch.Restart();
            pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in measured);
            pipeline.WaitForCompletion();
            stopwatch.Stop();
            samples[index].Add(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    Console.WriteLine($"structure recompute over {rounds} interleaved rounds (ms)");
    for (var index = 0; index < variants.Length; index++)
    {
        var sorted = samples[index].OrderBy(static value => value).ToArray();
        var median = sorted[sorted.Length / 2];
        Console.WriteLine(
            $"  {variants[index].Name,-11} min={sorted[0],7:F2}  median={median,7:F2}  max={sorted[^1],7:F2}");
    }

    if (pipeline.TryGetVisibleBounds(width, height, in parameters, out var rect))
    {
        using var rectOutput = device.AllocateReadWriteTexture2D<Bgra32, Float4>(rect.Width, rect.Height);
        pipeline.RenderVisible(rectOutput, width, height, rect, in parameters);
        pipeline.WaitForCompletion();
        stopwatch.Restart();
        const int rectFrames = 20;
        for (var frame = 0; frame < rectFrames; frame++)
        {
            pipeline.Simulate(sourceTexture, width, height, 0, 0, width, height, in parameters);
            pipeline.TryGetVisibleBounds(width, height, in parameters, out rect);
            pipeline.RenderVisible(rectOutput, width, height, rect, in parameters);
        }
        pipeline.WaitForCompletion();
        stopwatch.Stop();
        Console.WriteLine($"cached frame with rect {rect.Width}x{rect.Height} at ({rect.X},{rect.Y}): {stopwatch.Elapsed.TotalMilliseconds / rectFrames:F2} ms/frame");
    }
}

foreach (var flatten in new[] { 0f, 0.5f, 1f })
{
    var parameters = new UkiyoePipeline.Parameters(UkiyoeQuality.High, 0.5f, 0.5f, 0.5f, flatten, 6, 0.3f, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 0);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"flatten={flatten:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"flatten{(int)(flatten * 100):D3}.bmp"), Composite(source, destination), width, height);
}

foreach (var palette in new[] { 2, 6, 12 })
{
    var parameters = new UkiyoePipeline.Parameters(UkiyoeQuality.High, 0.5f, 0.5f, 0.5f, 0.6f, palette, 0.3f, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 0);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"palette={palette} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"palette{palette:D2}.bmp"), Composite(source, destination), width, height);
}

foreach (var shift in new[] { 0f, 0.5f, 1f })
{
    var parameters = new UkiyoePipeline.Parameters(UkiyoeQuality.High, 0.5f, 0.5f, 0.5f, 0.6f, 6, shift, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 0);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"shift={shift:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"shift{(int)(shift * 100):D3}.bmp"), Composite(source, destination), width, height);
}

foreach (var paper in new[] { 0f, 0.5f, 1f })
{
    var parameters = new UkiyoePipeline.Parameters(UkiyoeQuality.High, 0.5f, 0.5f, 0.5f, 0.6f, 6, 0.3f, 0.4f, paper, 0.85f, 0.12f, 0.1f, 0.09f, 0);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"paper={paper:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"paper{(int)(paper * 100):D3}.bmp"), Composite(source, destination), width, height);
}

foreach (var lineWidth in new[] { 0f, 0.5f, 1f })
{
    var parameters = new UkiyoePipeline.Parameters(UkiyoeQuality.High, lineWidth, 0.5f, 0.5f, 0.6f, 6, 0.3f, 0.4f, 0.5f, 0.85f, 0.12f, 0.1f, 0.09f, 0);
    pipeline.Process(source, destination, width, height, in parameters);
    Console.WriteLine($"lineWidth={lineWidth:F2} litPixels={CountLit(destination)}");
    WriteBmp(Path.Combine(outputDirectory, $"linewidth{(int)(lineWidth * 100):D3}.bmp"), Composite(source, destination), width, height);
}

WriteBmp(Path.Combine(outputDirectory, "source.bmp"), Composite(source, new int[source.Length]), width, height);
Console.WriteLine($"images written to {outputDirectory}");
return 0;

static int[] CreateTestImage(int width, int height)
{
    var pixels = new int[width * height];
    var centerX = width * 0.5;
    var centerY = height * 0.5;
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            var inRect = Math.Abs(x - centerX) < 300 && Math.Abs(y - centerY) < 220;
            var dx = x - (centerX + 160);
            var dy = y - (centerY - 60);
            var inCircle = dx * dx + dy * dy < 130 * 130;
            if (!inRect && !inCircle)
                continue;
            int r;
            int g;
            int b;
            if (inCircle)
            {
                var shade = 1.0 - Math.Sqrt(dx * dx + dy * dy) / 130.0 * 0.6;
                r = (int)(220 * shade);
                g = (int)(80 * shade);
                b = (int)(60 * shade);
            }
            else
            {
                var vertical = (y - (centerY - 220)) / 440.0;
                r = (int)(70 + 60 * vertical);
                g = (int)(110 + 70 * vertical);
                b = (int)(170 + 60 * vertical);
                if ((x / 3 + y / 7) % 9 == 0)
                {
                    r += 25;
                    g += 25;
                    b += 20;
                }
            }
            var hash = (uint)(x * 374761393 + y * 668265263);
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            var noise = (int)((hash >> 24) & 31) - 16;
            r = Math.Clamp(r + noise, 0, 255);
            g = Math.Clamp(g + noise, 0, 255);
            b = Math.Clamp(b + noise, 0, 255);
            pixels[y * width + x] = unchecked((int)0xFF000000 | (r << 16) | (g << 8) | b);
        }
    }
    return pixels;
}

static int CountLit(int[] pixels)
{
    var count = 0;
    foreach (var pixel in pixels)
    {
        if (((pixel >> 24) & 255) > 8)
            count++;
    }
    return count;
}

static int[] Composite(int[] source, int[] print)
{
    var result = new int[source.Length];
    for (var index = 0; index < source.Length; index++)
    {
        var s = source[index];
        var p = print[index];
        var sa = (s >> 24) & 255;
        var pa = (p >> 24) & 255;
        var a = Math.Min(pa + sa * (255 - pa) / 255, 255);
        var r = Over((s >> 16) & 255, (p >> 16) & 255, pa);
        var g = Over((s >> 8) & 255, (p >> 8) & 255, pa);
        var b = Over(s & 255, p & 255, pa);
        result[index] = 255 << 24 | OverWhite(r, a) << 16 | OverWhite(g, a) << 8 | OverWhite(b, a);
    }
    return result;

    static int Over(int s, int p, int pa) => Math.Min(p + s * (255 - pa) / 255, 255);

    static int OverWhite(int c, int a) => Math.Min(c + 255 - a, 255);
}

static void WriteBmp(string path, int[] pixels, int width, int height)
{
    var stride = width * 3;
    var padding = (4 - stride % 4) % 4;
    var dataSize = (stride + padding) * height;
    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var writer = new BinaryWriter(stream);
    writer.Write((byte)'B');
    writer.Write((byte)'M');
    writer.Write(54 + dataSize);
    writer.Write(0);
    writer.Write(54);
    writer.Write(40);
    writer.Write(width);
    writer.Write(height);
    writer.Write((short)1);
    writer.Write((short)24);
    writer.Write(0);
    writer.Write(dataSize);
    writer.Write(2835);
    writer.Write(2835);
    writer.Write(0);
    writer.Write(0);
    var pad = new byte[padding];
    for (var y = height - 1; y >= 0; y--)
    {
        for (var x = 0; x < width; x++)
        {
            var pixel = pixels[y * width + x];
            writer.Write((byte)(pixel & 255));
            writer.Write((byte)((pixel >> 8) & 255));
            writer.Write((byte)((pixel >> 16) & 255));
        }
        writer.Write(pad);
    }
}
