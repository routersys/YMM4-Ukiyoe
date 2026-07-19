using ComputeSharp;

namespace Ukiyoe;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FillIntShader(
    ReadWriteBuffer<int> values,
    int length,
    int value) : IComputeShader
{
    private readonly ReadWriteBuffer<int> values = values;
    private readonly int length = length;
    private readonly int value = value;

    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= length)
            return;
        values[index] = value;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitScratchShader(
    ReadWriteBuffer<int> scratch) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        scratch[UkiyoeSettings.ScratchMaxGradient] = 0;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct MaskHashResetShader(
    ReadWriteBuffer<int> scratch) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        scratch[UkiyoeSettings.ScratchLitCount] = 0;
        scratch[UkiyoeSettings.ScratchBoundsMinX] = 2147483647;
        scratch[UkiyoeSettings.ScratchBoundsMinY] = 2147483647;
        scratch[UkiyoeSettings.ScratchBoundsMaxX] = -2147483648;
        scratch[UkiyoeSettings.ScratchBoundsMaxY] = -2147483648;
        scratch[UkiyoeSettings.ScratchMaskHashSum] = 0;
        scratch[UkiyoeSettings.ScratchMaskHashMix] = 0;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SilhouetteShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<Float4> colorIn,
    ReadWriteBuffer<float> gray,
    int sourceOffsetX,
    int sourceOffsetY,
    int sourceWidth,
    int sourceHeight,
    int gridWidth,
    int gridHeight,
    float cellSize) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<Float4> colorIn = colorIn;
    private readonly ReadWriteBuffer<float> gray = gray;
    private readonly int sourceOffsetX = sourceOffsetX;
    private readonly int sourceOffsetY = sourceOffsetY;
    private readonly int sourceWidth = sourceWidth;
    private readonly int sourceHeight = sourceHeight;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float cellSize = cellSize;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var centerX = (gx + 0.5f) * cellSize;
        var centerY = (gy + 0.5f) * cellSize;
        var x0 = Hlsl.Max((int)(centerX - cellSize * 0.5f), sourceOffsetX);
        var x1 = Hlsl.Min((int)Hlsl.Ceil(centerX + cellSize * 0.5f), sourceOffsetX + sourceWidth);
        var y0 = Hlsl.Max((int)(centerY - cellSize * 0.5f), sourceOffsetY);
        var y1 = Hlsl.Min((int)Hlsl.Ceil(centerY + cellSize * 0.5f), sourceOffsetY + sourceHeight);

        var sum = new Float4(0f, 0f, 0f, 0f);
        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                sum += source[new Int2(x - sourceOffsetX, y - sourceOffsetY)];
                count++;
            }
        }

        var index = gy * gridWidth + gx;
        if (count == 0)
        {
            colorIn[index] = new Float4(0f, 0f, 0f, 0f);
            gray[index] = 1f;
            return;
        }

        var average = sum / count;
        var alpha = average.W;
        var straight = alpha > UkiyoeSettings.AlphaThreshold
            ? new Float3(average.X / alpha, average.Y / alpha, average.Z / alpha)
            : new Float3(0f, 0f, 0f);
        straight = Hlsl.Clamp(straight, new Float3(0f, 0f, 0f), new Float3(1f, 1f, 1f));
        colorIn[index] = new Float4(straight, alpha);
        var luma = 0.299f * straight.X + 0.587f * straight.Y + 0.114f * straight.Z;
        gray[index] = luma * alpha + (1f - alpha);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct MaskHashShader(
    ReadWriteBuffer<Float4> colorIn,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> colorIn = colorIn;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        var color = colorIn[index];
        var quantized = ((uint)(color.X * 255f + 0.5f) << 24)
            | ((uint)(color.Y * 255f + 0.5f) << 16)
            | ((uint)(color.Z * 255f + 0.5f) << 8)
            | (uint)(color.W * 255f + 0.5f);
        if (quantized == 0u)
            return;

        var mixed = ((uint)index * 0x9E3779B9u) ^ (quantized * 0x85EBCA6Bu);
        mixed ^= mixed >> 16;
        mixed *= 0x85EBCA6Bu;
        mixed ^= mixed >> 13;
        Hlsl.InterlockedAdd(ref scratch[UkiyoeSettings.ScratchMaskHashSum], (int)mixed);
        Hlsl.InterlockedXor(ref scratch[UkiyoeSettings.ScratchMaskHashMix], (int)(mixed * 0xC2B2AE35u));
        if (color.W > UkiyoeSettings.AlphaThreshold)
        {
            Hlsl.InterlockedAdd(ref scratch[UkiyoeSettings.ScratchLitCount], 1);
            Hlsl.InterlockedMin(ref scratch[UkiyoeSettings.ScratchBoundsMinX], gx);
            Hlsl.InterlockedMin(ref scratch[UkiyoeSettings.ScratchBoundsMinY], gy);
            Hlsl.InterlockedMax(ref scratch[UkiyoeSettings.ScratchBoundsMaxX], gx);
            Hlsl.InterlockedMax(ref scratch[UkiyoeSettings.ScratchBoundsMaxY], gy);
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct GradientShader(
    ReadWriteBuffer<float> gray,
    ReadWriteBuffer<Float2> tangent,
    ReadWriteBuffer<float> gradientMagnitude,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<float> gray = gray;
    private readonly ReadWriteBuffer<Float2> tangent = tangent;
    private readonly ReadWriteBuffer<float> gradientMagnitude = gradientMagnitude;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;

    private float Sample(int x, int y)
    {
        x = Hlsl.Clamp(x, 0, gridWidth - 1);
        y = Hlsl.Clamp(y, 0, gridHeight - 1);
        return gray[y * gridWidth + x];
    }

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var topLeft = Sample(gx - 1, gy - 1);
        var top = Sample(gx, gy - 1);
        var topRight = Sample(gx + 1, gy - 1);
        var left = Sample(gx - 1, gy);
        var right = Sample(gx + 1, gy);
        var bottomLeft = Sample(gx - 1, gy + 1);
        var bottom = Sample(gx, gy + 1);
        var bottomRight = Sample(gx + 1, gy + 1);

        var dx = (topRight + 2f * right + bottomRight - topLeft - 2f * left - bottomLeft) * 0.25f;
        var dy = (bottomLeft + 2f * bottom + bottomRight - topLeft - 2f * top - topRight) * 0.25f;
        var magnitude = Hlsl.Sqrt(dx * dx + dy * dy);
        var index = gy * gridWidth + gx;
        gradientMagnitude[index] = magnitude;
        tangent[index] = magnitude > 0f
            ? new Float2(-dy / magnitude, dx / magnitude)
            : new Float2(0f, 0f);
        Hlsl.InterlockedMax(ref scratch[UkiyoeSettings.ScratchMaxGradient], Hlsl.AsInt(magnitude));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct EtfShader(
    ReadWriteBuffer<Float2> tangentIn,
    ReadWriteBuffer<Float2> tangentOut,
    ReadWriteBuffer<float> gradientMagnitude,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<Float2> tangentIn = tangentIn;
    private readonly ReadWriteBuffer<Float2> tangentOut = tangentOut;
    private readonly ReadWriteBuffer<float> gradientMagnitude = gradientMagnitude;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        var center = tangentIn[index];
        var maxGradient = Hlsl.Max(Hlsl.AsFloat(scratch[UkiyoeSettings.ScratchMaxGradient]), 1e-6f);
        var centerMagnitude = gradientMagnitude[index] / maxGradient;
        if (centerMagnitude < UkiyoeSettings.EtfMagnitudeFloor)
        {
            var tensorXX = 0f;
            var tensorXY = 0f;
            var tensorYY = 0f;
            for (var dy = -UkiyoeSettings.EtfRadius; dy <= UkiyoeSettings.EtfRadius; dy++)
            {
                for (var dx = -UkiyoeSettings.EtfRadius; dx <= UkiyoeSettings.EtfRadius; dx++)
                {
                    if (dx * dx + dy * dy > UkiyoeSettings.EtfRadius * UkiyoeSettings.EtfRadius)
                        continue;
                    var nx = gx + dx;
                    var ny = gy + dy;
                    if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                        continue;
                    var neighborIndex = ny * gridWidth + nx;
                    var neighborMagnitude = gradientMagnitude[neighborIndex] / maxGradient;
                    if (neighborMagnitude < UkiyoeSettings.EtfMagnitudeFloor)
                        continue;
                    var neighbor = tangentIn[neighborIndex];
                    var magnitudeWeight = 0.5f * (1f + Hlsl.Tanh(UkiyoeSettings.EtfFalloff * (neighborMagnitude - centerMagnitude)));
                    tensorXX += magnitudeWeight * neighbor.X * neighbor.X;
                    tensorXY += magnitudeWeight * neighbor.X * neighbor.Y;
                    tensorYY += magnitudeWeight * neighbor.Y * neighbor.Y;
                }
            }
            if (tensorXX + tensorYY < 1e-9f)
            {
                tangentOut[index] = new Float2(0f, 0f);
                return;
            }
            var trace = tensorXX + tensorYY;
            var determinant = tensorXX * tensorYY - tensorXY * tensorXY;
            var eigenvalue = 0.5f * trace + Hlsl.Sqrt(Hlsl.Max(0.25f * trace * trace - determinant, 0f));
            var eigenX = eigenvalue - tensorYY;
            var eigenY = tensorXY;
            if (Hlsl.Abs(eigenX) + Hlsl.Abs(eigenY) < 1e-12f)
            {
                eigenX = tensorXY;
                eigenY = eigenvalue - tensorXX;
            }
            var eigenLength = Hlsl.Sqrt(eigenX * eigenX + eigenY * eigenY);
            tangentOut[index] = eigenLength > 1e-9f ? new Float2(eigenX / eigenLength, eigenY / eigenLength) : new Float2(1f, 0f);
            return;
        }

        var sum = new Float2(0f, 0f);
        for (var dy = -UkiyoeSettings.EtfRadius; dy <= UkiyoeSettings.EtfRadius; dy++)
        {
            for (var dx = -UkiyoeSettings.EtfRadius; dx <= UkiyoeSettings.EtfRadius; dx++)
            {
                if (dx * dx + dy * dy > UkiyoeSettings.EtfRadius * UkiyoeSettings.EtfRadius)
                    continue;
                var nx = gx + dx;
                var ny = gy + dy;
                if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                    continue;
                var neighborIndex = ny * gridWidth + nx;
                var neighbor = tangentIn[neighborIndex];
                var dot = center.X * neighbor.X + center.Y * neighbor.Y;
                var magnitudeWeight = 0.5f * (1f + Hlsl.Tanh(UkiyoeSettings.EtfFalloff * (gradientMagnitude[neighborIndex] / maxGradient - centerMagnitude)));
                var directionWeight = Hlsl.Abs(dot);
                var sign = dot > 0f ? 1f : -1f;
                sum += neighbor * (sign * magnitudeWeight * directionWeight);
            }
        }

        var length = Hlsl.Sqrt(sum.X * sum.X + sum.Y * sum.Y);
        tangentOut[index] = length > 1e-6f ? sum / length : center;
    }
}

[ThreadGroupSize(8, 8, 1)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FlattenShader(
    ReadWriteBuffer<Float4> colorIn,
    ReadWriteBuffer<Float4> iterateIn,
    ReadWriteBuffer<Float4> iterateOut,
    ReadWriteBuffer<float> gradientMagnitude,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight,
    float beta) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> colorIn = colorIn;
    private readonly ReadWriteBuffer<Float4> iterateIn = iterateIn;
    private readonly ReadWriteBuffer<Float4> iterateOut = iterateOut;
    private readonly ReadWriteBuffer<float> gradientMagnitude = gradientMagnitude;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float beta = beta;

    [GroupShared(144)]
    private static readonly Float4[] tileColor = null!;

    [GroupShared(144)]
    private static readonly float[] tileGradient = null!;

    [GroupShared(144)]
    private static readonly Float4[] tileIterate = null!;

    public void Execute()
    {
        var originX = GridIds.X * UkiyoeSettings.FlattenGroupDim - UkiyoeSettings.FlattenRadius;
        var originY = GridIds.Y * UkiyoeSettings.FlattenGroupDim - UkiyoeSettings.FlattenRadius;
        for (var slot = GroupIds.Index; slot < UkiyoeSettings.FlattenTileCount; slot += UkiyoeSettings.FlattenGroupDim * UkiyoeSettings.FlattenGroupDim)
        {
            var tx = originX + slot % UkiyoeSettings.FlattenTileDim;
            var ty = originY + slot / UkiyoeSettings.FlattenTileDim;
            if (tx >= 0 && tx < gridWidth && ty >= 0 && ty < gridHeight)
            {
                var sampleIndex = ty * gridWidth + tx;
                tileColor[slot] = colorIn[sampleIndex];
                tileGradient[slot] = gradientMagnitude[sampleIndex];
                tileIterate[slot] = iterateIn[sampleIndex];
            }
            else
            {
                tileColor[slot] = new Float4(0f, 0f, 0f, 0f);
                tileGradient[slot] = 0f;
                tileIterate[slot] = new Float4(0f, 0f, 0f, 0f);
            }
        }
        Hlsl.GroupMemoryBarrierWithGroupSync();

        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var centerTile = (GroupIds.Y + UkiyoeSettings.FlattenRadius) * UkiyoeSettings.FlattenTileDim + GroupIds.X + UkiyoeSettings.FlattenRadius;
        var original = tileColor[centerTile];
        var current = tileIterate[centerTile];
        var centerFeature = UkiyoeShaderMath.AffinityFeature(original);
        var maxGradient = Hlsl.Max(Hlsl.AsFloat(scratch[UkiyoeSettings.ScratchMaxGradient]), 1e-6f);
        var centerGradient = tileGradient[centerTile] / maxGradient;
        var invTwoSigmaSquared = 0.5f / (UkiyoeSettings.FlattenSigma * UkiyoeSettings.FlattenSigma);

        var numerator = original * beta;
        var denominator = beta;
        for (var dy = -UkiyoeSettings.FlattenRadius; dy <= UkiyoeSettings.FlattenRadius; dy++)
        {
            for (var dx = -UkiyoeSettings.FlattenRadius; dx <= UkiyoeSettings.FlattenRadius; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                var nx = gx + dx;
                var ny = gy + dy;
                if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                    continue;
                var neighborTile = centerTile + dy * UkiyoeSettings.FlattenTileDim + dx;
                var neighborOriginal = tileColor[neighborTile];
                var feature = centerFeature - UkiyoeShaderMath.AffinityFeature(neighborOriginal);
                var alphaDelta = original.W - neighborOriginal.W;
                var featureDistance = feature.X * feature.X + feature.Y * feature.Y + feature.Z * feature.Z + alphaDelta * alphaDelta;
                var neighborGradient = tileGradient[neighborTile] / maxGradient;
                var edge = Hlsl.Max(centerGradient, neighborGradient);
                var affinity = Hlsl.Exp(-Hlsl.Max(featureDistance, UkiyoeSettings.FlattenGradientWeight * edge * edge) * invTwoSigmaSquared);
                var neighborCurrent = tileIterate[neighborTile];
                var difference = current - neighborCurrent;
                var l1 = Hlsl.Abs(difference.X) + Hlsl.Abs(difference.Y) + Hlsl.Abs(difference.Z) + Hlsl.Abs(difference.W);
                var weight = affinity / Hlsl.Max(l1, UkiyoeSettings.FlattenEpsilon);
                numerator += neighborCurrent * weight;
                denominator += weight;
            }
        }

        iterateOut[gy * gridWidth + gx] = numerator / denominator;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CopyColorShader(
    ReadWriteBuffer<Float4> source,
    ReadWriteBuffer<Float4> destination,
    int gridWidth,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> source = source;
    private readonly ReadWriteBuffer<Float4> destination = destination;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;
        var index = gy * gridWidth + gx;
        destination[index] = source[index];
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct DogShader(
    ReadWriteBuffer<float> gray,
    ReadWriteBuffer<Float2> tangent,
    ReadWriteBuffer<float> response,
    int gridWidth,
    int gridHeight,
    float sigmaCenter,
    int extent) : IComputeShader
{
    private readonly ReadWriteBuffer<float> gray = gray;
    private readonly ReadWriteBuffer<Float2> tangent = tangent;
    private readonly ReadWriteBuffer<float> response = response;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float sigmaCenter = sigmaCenter;
    private readonly int extent = extent;

    private float SampleGray(float x, float y)
    {
        var fx = Hlsl.Clamp(x, 0f, gridWidth - 1f);
        var fy = Hlsl.Clamp(y, 0f, gridHeight - 1f);
        var ix = (int)fx;
        var iy = (int)fy;
        var tx = fx - ix;
        var ty = fy - iy;
        var ix1 = Hlsl.Min(ix + 1, gridWidth - 1);
        var iy1 = Hlsl.Min(iy + 1, gridHeight - 1);
        var top = Hlsl.Lerp(gray[iy * gridWidth + ix], gray[iy * gridWidth + ix1], tx);
        var bottom = Hlsl.Lerp(gray[iy1 * gridWidth + ix], gray[iy1 * gridWidth + ix1], tx);
        return Hlsl.Lerp(top, bottom, ty);
    }

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        var flow = tangent[index];
        if (flow.X == 0f && flow.Y == 0f)
        {
            response[index] = 0f;
            return;
        }

        var direction = new Float2(flow.Y, -flow.X);
        var sigmaSurround = sigmaCenter * UkiyoeSettings.SurroundSigmaScale;
        var invCenter = 0.5f / (sigmaCenter * sigmaCenter);
        var invSurround = 0.5f / (sigmaSurround * sigmaSurround);
        var sum = 0f;
        var centerNorm = 0f;
        var surroundNorm = 0f;
        for (var t = -extent; t <= extent; t++)
        {
            var weightCenter = Hlsl.Exp(-t * t * invCenter);
            var weightSurround = Hlsl.Exp(-t * t * invSurround);
            centerNorm += weightCenter;
            surroundNorm += weightSurround;
        }
        for (var t = -extent; t <= extent; t++)
        {
            var value = SampleGray(gx + direction.X * t, gy + direction.Y * t);
            var weightCenter = Hlsl.Exp(-t * t * invCenter) / centerNorm;
            var weightSurround = Hlsl.Exp(-t * t * invSurround) / surroundNorm;
            sum += value * (weightCenter - UkiyoeSettings.DogSharpness * weightSurround);
        }
        response[index] = sum;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FlowAccumulateShader(
    ReadWriteBuffer<float> response,
    ReadWriteBuffer<Float2> tangent,
    ReadWriteBuffer<float> lineResponse,
    int gridWidth,
    int gridHeight,
    float sigmaFlow,
    int extent) : IComputeShader
{
    private readonly ReadWriteBuffer<float> response = response;
    private readonly ReadWriteBuffer<Float2> tangent = tangent;
    private readonly ReadWriteBuffer<float> lineResponse = lineResponse;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float sigmaFlow = sigmaFlow;
    private readonly int extent = extent;

    private float SampleResponse(float x, float y)
    {
        var fx = Hlsl.Clamp(x, 0f, gridWidth - 1f);
        var fy = Hlsl.Clamp(y, 0f, gridHeight - 1f);
        var ix = (int)fx;
        var iy = (int)fy;
        var tx = fx - ix;
        var ty = fy - iy;
        var ix1 = Hlsl.Min(ix + 1, gridWidth - 1);
        var iy1 = Hlsl.Min(iy + 1, gridHeight - 1);
        var top = Hlsl.Lerp(response[iy * gridWidth + ix], response[iy * gridWidth + ix1], tx);
        var bottom = Hlsl.Lerp(response[iy1 * gridWidth + ix], response[iy1 * gridWidth + ix1], tx);
        return Hlsl.Lerp(top, bottom, ty);
    }

    private Float2 SampleTangent(float x, float y)
    {
        var ix = Hlsl.Clamp((int)Hlsl.Round(x), 0, gridWidth - 1);
        var iy = Hlsl.Clamp((int)Hlsl.Round(y), 0, gridHeight - 1);
        return tangent[iy * gridWidth + ix];
    }

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        var invFlow = 0.5f / (sigmaFlow * sigmaFlow);
        var sum = response[index];
        var weightSum = 1f;
        for (var side = 0; side < 2; side++)
        {
            var sign = side == 0 ? 1f : -1f;
            var position = new Float2(gx, gy);
            var previous = SampleTangent(position.X, position.Y) * sign;
            if (previous.X == 0f && previous.Y == 0f)
                continue;
            for (var s = 1; s <= extent; s++)
            {
                position += previous;
                if (position.X < 0f || position.X >= gridWidth || position.Y < 0f || position.Y >= gridHeight)
                    break;
                var weight = Hlsl.Exp(-s * s * invFlow);
                sum += SampleResponse(position.X, position.Y) * weight;
                weightSum += weight;
                var next = SampleTangent(position.X, position.Y);
                if (next.X == 0f && next.Y == 0f)
                    break;
                if (next.X * previous.X + next.Y * previous.Y < 0f)
                    next = -next;
                previous = next;
            }
        }
        lineResponse[index] = sum / weightSum;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SuperimposeShader(
    ReadWriteBuffer<float> lineResponse,
    ReadWriteBuffer<float> gray,
    int gridWidth,
    int gridHeight,
    float threshold) : IComputeShader
{
    private readonly ReadWriteBuffer<float> lineResponse = lineResponse;
    private readonly ReadWriteBuffer<float> gray = gray;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float threshold = threshold;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        var value = 1f + Hlsl.Tanh(UkiyoeSettings.LineGain * lineResponse[index]);
        if (value < threshold)
            gray[index] = 0f;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RenderShader(
    ReadWriteBuffer<Float4> flatColor,
    ReadWriteBuffer<float> lineResponse,
    ReadWriteTexture2D<Bgra32, Float4> output,
    int rectOffsetX,
    int rectOffsetY,
    int rectWidth,
    int rectHeight,
    int gridWidth,
    int gridHeight,
    float cellSize,
    int paletteLevels,
    float shiftPixels,
    float baren,
    float paper,
    float lineThreshold,
    float lineStrength,
    float lineColorR,
    float lineColorG,
    float lineColorB,
    int seed) : IComputeShader
{
    private readonly ReadWriteBuffer<Float4> flatColor = flatColor;
    private readonly ReadWriteBuffer<float> lineResponse = lineResponse;
    private readonly ReadWriteTexture2D<Bgra32, Float4> output = output;
    private readonly int rectOffsetX = rectOffsetX;
    private readonly int rectOffsetY = rectOffsetY;
    private readonly int rectWidth = rectWidth;
    private readonly int rectHeight = rectHeight;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float cellSize = cellSize;
    private readonly int paletteLevels = paletteLevels;
    private readonly float shiftPixels = shiftPixels;
    private readonly float baren = baren;
    private readonly float paper = paper;
    private readonly float lineThreshold = lineThreshold;
    private readonly float lineStrength = lineStrength;
    private readonly float lineColorR = lineColorR;
    private readonly float lineColorG = lineColorG;
    private readonly float lineColorB = lineColorB;
    private readonly int seed = seed;

    private Float4 SampleColor(float x, float y)
    {
        var fx = Hlsl.Clamp(x, 0f, gridWidth - 1f);
        var fy = Hlsl.Clamp(y, 0f, gridHeight - 1f);
        var ix = (int)fx;
        var iy = (int)fy;
        var tx = fx - ix;
        var ty = fy - iy;
        var ix1 = Hlsl.Min(ix + 1, gridWidth - 1);
        var iy1 = Hlsl.Min(iy + 1, gridHeight - 1);
        var top = Hlsl.Lerp(flatColor[iy * gridWidth + ix], flatColor[iy * gridWidth + ix1], tx);
        var bottom = Hlsl.Lerp(flatColor[iy1 * gridWidth + ix], flatColor[iy1 * gridWidth + ix1], tx);
        return Hlsl.Lerp(top, bottom, ty);
    }

    private float SampleLine(float x, float y)
    {
        var fx = Hlsl.Clamp(x, 0f, gridWidth - 1f);
        var fy = Hlsl.Clamp(y, 0f, gridHeight - 1f);
        var ix = (int)fx;
        var iy = (int)fy;
        var tx = fx - ix;
        var ty = fy - iy;
        var ix1 = Hlsl.Min(ix + 1, gridWidth - 1);
        var iy1 = Hlsl.Min(iy + 1, gridHeight - 1);
        var top = Hlsl.Lerp(lineResponse[iy * gridWidth + ix], lineResponse[iy * gridWidth + ix1], tx);
        var bottom = Hlsl.Lerp(lineResponse[iy1 * gridWidth + ix], lineResponse[iy1 * gridWidth + ix1], tx);
        return Hlsl.Lerp(top, bottom, ty);
    }

    public void Execute()
    {
        if (ThreadIds.X >= rectWidth || ThreadIds.Y >= rectHeight)
            return;
        var px = ThreadIds.X + rectOffsetX + 0.5f;
        var py = ThreadIds.Y + rectOffsetY + 0.5f;
        var cellX = px / cellSize - 0.5f;
        var cellY = py / cellSize - 0.5f;

        var baseSample = SampleColor(cellX, cellY);
        var chosen = baseSample;
        var chosenDarkness = UkiyoeShaderMath.Darkness(baseSample);
        var chosenLevel = UkiyoeShaderMath.DarknessLevel(chosenDarkness, paletteLevels);
        var alpha = baseSample.W;
        var shiftCells = shiftPixels / cellSize;
        if (shiftCells > 0f)
        {
            for (var level = 1; level < paletteLevels; level++)
            {
                var offset = UkiyoeShaderMath.LayerOffset(level, seed) * shiftCells;
                var sample = SampleColor(cellX - offset.X, cellY - offset.Y);
                var darkness = UkiyoeShaderMath.Darkness(sample);
                if (UkiyoeShaderMath.DarknessLevel(darkness, paletteLevels) >= level && level > chosenLevel)
                {
                    chosen = sample;
                    chosenDarkness = darkness;
                    chosenLevel = level;
                    alpha = Hlsl.Max(alpha, sample.W);
                }
            }
        }

        var lineValue = 1f + Hlsl.Tanh(UkiyoeSettings.LineGain * SampleLine(cellX, cellY));
        var line = Hlsl.Saturate((lineThreshold - lineValue) / UkiyoeSettings.LineSoftness) * lineStrength;
        var fiber = UkiyoeShaderMath.FiberField((int)(px * 2f), (int)(py * 2f), seed);
        line *= 1f - paper * UkiyoeSettings.PaperEdgeStrength * fiber;

        if (alpha <= UkiyoeSettings.AlphaThreshold && line <= 0f)
        {
            output[ThreadIds.XY] = new Float4(0f, 0f, 0f, 0f);
            return;
        }

        var quantizedDarkness = UkiyoeShaderMath.SoftQuantize(chosenDarkness, paletteLevels);
        var luma = 0.299f * chosen.X + 0.587f * chosen.Y + 0.114f * chosen.Z;
        var cb = 0.5f + ((chosen.Z - luma) * 0.564f) * UkiyoeSettings.ChromaScale;
        var cr = 0.5f + ((chosen.X - luma) * 0.713f) * UkiyoeSettings.ChromaScale;

        var ring = UkiyoeShaderMath.BarenRing(px, py, seed);
        var midtone = 4f * quantizedDarkness * (1f - quantizedDarkness);
        quantizedDarkness -= baren * UkiyoeSettings.BarenStrength * ring * midtone;
        quantizedDarkness *= 1f - paper * UkiyoeSettings.PaperStrength * fiber;
        quantizedDarkness = Hlsl.Saturate(quantizedDarkness);

        var y = 1f - quantizedDarkness;
        var r = Hlsl.Saturate(y + 1.403f * (cr - 0.5f));
        var g = Hlsl.Saturate(y - 0.344f * (cb - 0.5f) - 0.714f * (cr - 0.5f));
        var b = Hlsl.Saturate(y + 1.773f * (cb - 0.5f));

        var pigmentAlpha = alpha * (1f - line);
        var outAlpha = Hlsl.Saturate(line + pigmentAlpha);
        var outR = Hlsl.Min(lineColorR * line + r * pigmentAlpha, outAlpha);
        var outG = Hlsl.Min(lineColorG * line + g * pigmentAlpha, outAlpha);
        var outB = Hlsl.Min(lineColorB * line + b * pigmentAlpha, outAlpha);
        output[ThreadIds.XY] = new Float4(outR, outG, outB, outAlpha);
    }
}

internal static class UkiyoeShaderMath
{
    public static float Hash01(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value * 2.3283064e-10f;
    }

    public static float LatticeHash(int x, int y, uint seed, uint salt)
        => Hash01(((uint)x * 0x9E3779B9u) ^ ((uint)y * 0x85EBCA6Bu) ^ (seed * 0xC2B2AE35u) ^ salt);

    public static float ValueNoise(float x, float y, uint seed, uint salt)
    {
        var ix = (int)Hlsl.Floor(x);
        var iy = (int)Hlsl.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        var sx = fx * fx * (3f - 2f * fx);
        var sy = fy * fy * (3f - 2f * fy);
        var v00 = LatticeHash(ix, iy, seed, salt);
        var v10 = LatticeHash(ix + 1, iy, seed, salt);
        var v01 = LatticeHash(ix, iy + 1, seed, salt);
        var v11 = LatticeHash(ix + 1, iy + 1, seed, salt);
        return Hlsl.Lerp(Hlsl.Lerp(v00, v10, sx), Hlsl.Lerp(v01, v11, sx), sy);
    }

    public static float FiberField(int gx, int gy, int seed)
    {
        var s = (uint)seed;
        var value = 0.5f * ValueNoise(gx * 0.11f, gy * 0.31f, s, 0x1B873593u)
            + 0.3f * ValueNoise(gx * 0.37f, gy * 0.13f, s, 0xCC9E2D51u)
            + 0.2f * ValueNoise(gx * 0.53f, gy * 0.53f, s, 0xE6546B64u);
        return Hlsl.Saturate((value - 0.3f) * 1.8f);
    }

    public static Float3 AffinityFeature(Float4 color)
    {
        var luma = 0.299f * color.X + 0.587f * color.Y + 0.114f * color.Z;
        var cb = (color.Z - luma) * 0.564f + 0.5f;
        var cr = (color.X - luma) * 0.713f + 0.5f;
        return new Float3(UkiyoeSettings.FlattenLumaScale * luma, cb, cr);
    }

    public static float Darkness(Float4 color)
    {
        var luma = 0.299f * color.X + 0.587f * color.Y + 0.114f * color.Z;
        return (1f - luma) * color.W;
    }

    public static int DarknessLevel(float darkness, int levels)
    {
        var index = (int)(darkness * levels);
        return Hlsl.Clamp(index, 0, levels - 1);
    }

    public static float SoftQuantize(float value, int levels)
    {
        var scaled = Hlsl.Clamp(value, 0f, 0.9999f) * levels;
        var bin = Hlsl.Floor(scaled);
        var fraction = scaled - bin;
        var width = UkiyoeSettings.QuantizeEdgeWidth;
        var rise = Hlsl.SmoothStep(1f - width, 1f, fraction);
        var fall = Hlsl.SmoothStep(0f, width, fraction);
        var transition = 0.5f * (rise - (1f - fall));
        return Hlsl.Saturate((bin + 0.5f + transition) / levels);
    }

    public static Float2 LayerOffset(int level, int seed)
    {
        var angle = Hash01(((uint)level * 0x9E3779B9u) ^ ((uint)seed * 0xC2B2AE35u) ^ 0x51ED270Bu) * 6.2831853f;
        return new Float2(Hlsl.Cos(angle), Hlsl.Sin(angle));
    }

    public static float BarenRing(float px, float py, int seed)
    {
        var cellX = (int)Hlsl.Floor(px / UkiyoeSettings.BarenCellSize);
        var cellY = (int)Hlsl.Floor(py / UkiyoeSettings.BarenCellSize);
        var jitterX = LatticeHash(cellX, cellY, (uint)seed, 0x27D4EB2Fu) - 0.5f;
        var jitterY = LatticeHash(cellX, cellY, (uint)seed, 0x165667B1u) - 0.5f;
        var centerX = (cellX + 0.5f + jitterX * 0.6f) * UkiyoeSettings.BarenCellSize;
        var centerY = (cellY + 0.5f + jitterY * 0.6f) * UkiyoeSettings.BarenCellSize;
        var dx = px - centerX;
        var dy = py - centerY;
        var distance = Hlsl.Sqrt(dx * dx + dy * dy);
        var phase = LatticeHash(cellX, cellY, (uint)seed, 0x85EBCA6Bu) * 6.2831853f;
        var ring = 0.5f + 0.5f * Hlsl.Cos(distance * UkiyoeSettings.BarenRingFrequency + phase);
        var patch = ValueNoise(px / UkiyoeSettings.BarenCellSize * 1.7f, py / UkiyoeSettings.BarenCellSize * 1.7f, (uint)seed, 0x51ED270Bu);
        return ring * Hlsl.SmoothStep(0.35f, 0.75f, patch);
    }
}
