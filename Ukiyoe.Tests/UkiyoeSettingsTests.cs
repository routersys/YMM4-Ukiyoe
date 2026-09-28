namespace Ukiyoe.Tests;

public sealed class UkiyoeSettingsTests
{
    [Theory]
    [InlineData(UkiyoeQuality.Balanced, 1024, 2, 2, 24)]
    [InlineData(UkiyoeQuality.High, 1440, 3, 2, 40)]
    [InlineData(UkiyoeQuality.Ultra, 2048, 3, 3, 64)]
    public void EveryQualityChoosesItsGridResolutionAndIterations(UkiyoeQuality quality, int resolution, int etfIterations, int fdogIterations, int flattenIterations)
    {
        var settings = UkiyoeSettings.GetQuality(quality);

        Assert.Equal(resolution, settings.GridResolution);
        Assert.Equal(etfIterations, settings.EtfIterations);
        Assert.Equal(fdogIterations, settings.FdogIterations);
        Assert.Equal(flattenIterations, settings.FlattenIterations);
    }

    [Fact]
    public void EveryQualityFlattensAnEvenNumberOfTimes()
    {
        foreach (var quality in Enum.GetValues<UkiyoeQuality>())
            Assert.Equal(0, UkiyoeSettings.GetQuality(quality).FlattenIterations % 2);
    }

    [Theory]
    [InlineData(1920, 1080, 1440)]
    [InlineData(1080, 1920, 1440)]
    [InlineData(8, 8, 1440)]
    [InlineData(4096, 16, 1024)]
    [InlineData(100, 100, 2048)]
    public void TheGridCoversTheWholeCanvas(int width, int height, int resolution)
    {
        var (gridWidth, gridHeight, cellSize) = UkiyoeSettings.GetGridSize(width, height, resolution);

        Assert.True(gridWidth >= UkiyoeSettings.MinimumGridSize);
        Assert.True(gridHeight >= UkiyoeSettings.MinimumGridSize);
        Assert.True(cellSize > 0f);
        Assert.True(gridWidth * cellSize >= width);
        Assert.True(gridHeight * cellSize >= height);
    }

    [Theory]
    [InlineData(1920, 1080, 1440, 1440)]
    [InlineData(1080, 1920, 1440, 1440)]
    [InlineData(8, 8, 1440, 8)]
    [InlineData(4096, 16, 1024, 1024)]
    [InlineData(100, 100, 2048, 100)]
    public void TheLongSideIsSplitIntoTheResolutionOrIntoSinglePixels(int width, int height, int resolution, int cells)
    {
        var (gridWidth, gridHeight, _) = UkiyoeSettings.GetGridSize(width, height, resolution);

        Assert.InRange(Math.Max(gridWidth, gridHeight), cells + 1, cells + 2);
    }

    [Theory]
    [InlineData(-5f, UkiyoeSettings.MinimumLineSigmaPixels)]
    [InlineData(0f, UkiyoeSettings.MinimumLineSigmaPixels)]
    [InlineData(1f, UkiyoeSettings.MaximumLineSigmaPixels)]
    [InlineData(5f, UkiyoeSettings.MaximumLineSigmaPixels)]
    public void TheLineSigmaStaysWithinItsRange(float lineWidth, float expected)
        => Assert.Equal(expected, UkiyoeSettings.GetLineSigmaPixels(lineWidth), 5);

    [Fact]
    public void AWiderLineUsesALargerSigma()
        => Assert.True(UkiyoeSettings.GetLineSigmaPixels(0.75f) > UkiyoeSettings.GetLineSigmaPixels(0.25f));

    [Theory]
    [InlineData(-1f, UkiyoeSettings.MinimumFlowSigma)]
    [InlineData(0f, UkiyoeSettings.MinimumFlowSigma)]
    [InlineData(1f, UkiyoeSettings.MaximumFlowSigma)]
    [InlineData(2f, UkiyoeSettings.MaximumFlowSigma)]
    public void TheFlowSigmaStaysWithinItsRange(float coherence, float expected)
        => Assert.Equal(expected, UkiyoeSettings.GetFlowSigma(coherence), 5);

    [Fact]
    public void MoreCoherenceFollowsTheFlowFurther()
        => Assert.True(UkiyoeSettings.GetFlowSigma(0.75f) > UkiyoeSettings.GetFlowSigma(0.25f));

    [Theory]
    [InlineData(-1f, UkiyoeSettings.MinimumLineThreshold)]
    [InlineData(0f, UkiyoeSettings.MinimumLineThreshold)]
    [InlineData(1f, UkiyoeSettings.MinimumLineThreshold + UkiyoeSettings.LineThresholdRange)]
    [InlineData(2f, UkiyoeSettings.MinimumLineThreshold + UkiyoeSettings.LineThresholdRange)]
    public void TheLineThresholdStaysWithinItsRange(float lineDetail, float expected)
        => Assert.Equal(expected, UkiyoeSettings.GetLineThreshold(lineDetail), 5);

    [Fact]
    public void MoreLineDetailRaisesTheThreshold()
        => Assert.True(UkiyoeSettings.GetLineThreshold(0.75f) > UkiyoeSettings.GetLineThreshold(0.25f));

    [Theory]
    [InlineData(-1f, UkiyoeSettings.MaximumFlattenBeta)]
    [InlineData(0f, UkiyoeSettings.MaximumFlattenBeta)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 1f)]
    public void TheFidelityToTheOriginalFallsFromItsMaximumToOne(float flatten, float expected)
        => Assert.Equal(expected, UkiyoeSettings.GetFlattenBeta(flatten), 4);

    [Fact]
    public void MoreFlatteningHoldsLessToTheOriginal()
        => Assert.True(UkiyoeSettings.GetFlattenBeta(0.25f) > UkiyoeSettings.GetFlattenBeta(0.75f));

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(1f, UkiyoeSettings.MaximumShiftPixels)]
    [InlineData(2f, UkiyoeSettings.MaximumShiftPixels)]
    public void TheMisregistrationShiftStaysWithinItsRange(float misregistration, float expected)
        => Assert.Equal(expected, UkiyoeSettings.GetShiftPixels(misregistration), 6);

    [Fact]
    public void MoreMisregistrationShiftsTheBlocksFurther()
        => Assert.True(UkiyoeSettings.GetShiftPixels(0.75f) > UkiyoeSettings.GetShiftPixels(0.25f));

    [Theory]
    [InlineData(10f, 3f, 6f, 2f, 52)]
    [InlineData(0f, 0.6f, 1f, 1f, 12)]
    [InlineData(1f, 0f, 0f, 1f, 12)]
    public void TheMarginCoversTheShiftAndTheLineReachOnAFourPixelGrid(float shiftPixels, float lineSigmaPixels, float flowSigma, float cellSize, int expected)
        => Assert.Equal(expected, UkiyoeSettings.GetMargin(shiftPixels, lineSigmaPixels, flowSigma, cellSize));
}
