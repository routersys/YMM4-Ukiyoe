namespace Ukiyoe;

internal static class UkiyoeSettings
{
    public const float MinimumLineSigma = 0.5f;
    public const float MaximumLineSigmaPixels = 3.0f;
    public const float MinimumLineSigmaPixels = 0.6f;
    public const float SurroundSigmaScale = 1.6f;
    public const float DogSharpness = 0.99f;
    public const float LineGain = 40f;
    public const float MinimumLineThreshold = 0.1f;
    public const float LineThresholdRange = 0.85f;
    public const float LineSoftness = 0.1f;
    public const float MinimumFlowSigma = 1f;
    public const float MaximumFlowSigma = 6f;
    public const float EtfFalloff = 1f;
    public const float EtfMagnitudeFloor = 0.02f;
    public const int EtfRadius = 5;
    public const float MaximumFlattenBeta = 100000f;
    public const float FlattenBetaDecay = 11.512925f;
    public const float FlattenEpsilon = 0.001f;
    public const float FlattenSigma = 0.14f;
    public const float FlattenLumaScale = 0.3f;
    public const float FlattenGradientWeight = 0.4f;
    public const int FlattenRadius = 2;
    public const int FlattenGroupDim = 8;
    public const int FlattenTileDim = FlattenGroupDim + 2 * FlattenRadius;
    public const int FlattenTileCount = FlattenTileDim * FlattenTileDim;
    public const float QuantizeEdgeWidth = 0.18f;
    public const float ChromaScale = 0.9f;
    public const float MaximumShiftPixels = 10f;
    public const float BarenStrength = 0.12f;
    public const float BarenRingFrequency = 0.5f;
    public const float BarenCellSize = 140f;
    public const float PaperStrength = 0.3f;
    public const float PaperEdgeStrength = 0.35f;
    public const float AlphaThreshold = 0.004f;
    public const int MinimumPaletteLevels = 2;
    public const int MaximumPaletteLevels = 16;
    public const int MinimumGridSize = 4;
    public const int MaximumCanvasSize = 8192;
    public const int MarginPadding = 8;
    public const int ScratchLength = 8;
    public const int ScratchMaxGradient = 0;
    public const int ScratchLitCount = 1;
    public const int ScratchBoundsMinX = 2;
    public const int ScratchBoundsMinY = 3;
    public const int ScratchBoundsMaxX = 4;
    public const int ScratchBoundsMaxY = 5;
    public const int ScratchMaskHashSum = 6;
    public const int ScratchMaskHashMix = 7;

    public static QualitySettings GetQuality(UkiyoeQuality quality)
        => quality switch
        {
            UkiyoeQuality.Balanced => new QualitySettings(1024, 2, 2, 24),
            UkiyoeQuality.Ultra => new QualitySettings(2048, 3, 3, 64),
            _ => new QualitySettings(1440, 3, 2, 40),
        };

    public static (int Width, int Height, float CellSize) GetGridSize(int width, int height, int resolution)
    {
        var longSide = Math.Max(Math.Max(width, height), 1);
        var cellSize = longSide / (float)Math.Max(Math.Min(resolution, longSide), MinimumGridSize);
        var gridWidth = Math.Max((int)Math.Ceiling(width / cellSize) + 1, MinimumGridSize);
        var gridHeight = Math.Max((int)Math.Ceiling(height / cellSize) + 1, MinimumGridSize);
        return (gridWidth, gridHeight, cellSize);
    }

    public static float GetLineSigmaPixels(float lineWidth)
        => MinimumLineSigmaPixels + Math.Clamp(lineWidth, 0f, 1f) * (MaximumLineSigmaPixels - MinimumLineSigmaPixels);

    public static float GetFlowSigma(float coherence)
        => MinimumFlowSigma + Math.Clamp(coherence, 0f, 1f) * (MaximumFlowSigma - MinimumFlowSigma);

    public static float GetLineThreshold(float lineDetail)
        => MinimumLineThreshold + Math.Clamp(lineDetail, 0f, 1f) * LineThresholdRange;

    public static float GetFlattenBeta(float flatten)
        => MaximumFlattenBeta * MathF.Exp(-Math.Clamp(flatten, 0f, 1f) * FlattenBetaDecay);

    public static float GetShiftPixels(float misregistration)
        => Math.Clamp(misregistration, 0f, 1f) * MaximumShiftPixels;

    public static int GetMargin(float shiftPixels, float lineSigmaPixels, float flowSigma, float cellSize)
    {
        var lineReach = (2f * SurroundSigmaScale * lineSigmaPixels + 2f * flowSigma * cellSize);
        return ((int)MathF.Ceiling(shiftPixels + lineReach) + MarginPadding + 3) & ~3;
    }

    public static int ClampPaletteLevels(int levels)
        => Math.Clamp(levels, MinimumPaletteLevels, MaximumPaletteLevels);

    internal readonly record struct QualitySettings(int GridResolution, int EtfIterations, int FdogIterations, int FlattenIterations);
}
