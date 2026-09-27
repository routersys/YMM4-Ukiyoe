namespace Ukiyoe.Tests;

public sealed class UkiyoeEffectTests
{
    private static double ValueAt(YukkuriMovieMaker.Commons.Animation animation) => animation.GetValue(0, 1, 30);

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
}
