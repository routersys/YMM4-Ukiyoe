using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;

namespace Ukiyoe.Tests;

public sealed class UkiyoeEffectTests
{
    static readonly Color DefaultLineColor = Color.FromArgb(255, 30, 26, 24);

    static PropertyInfo Property(string name) => typeof(UkiyoeEffect).GetProperty(name)!;

    static T Attribute<T>(string property) where T : Attribute => Property(property).GetCustomAttribute<T>()!;

    static Animation[] Animations(UkiyoeEffect effect)
        => [effect.Amount, effect.LineWidth, effect.Coherence, effect.LineDetail, effect.LineStrength, effect.Flatten, effect.Misregistration, effect.Baren, effect.Paper];

    [Theory]
    [InlineData(nameof(UkiyoeEffect.Amount), 100d, 0d, 100d)]
    [InlineData(nameof(UkiyoeEffect.LineWidth), 50d, 0d, 100d)]
    [InlineData(nameof(UkiyoeEffect.Coherence), 50d, 0d, 100d)]
    [InlineData(nameof(UkiyoeEffect.LineDetail), 50d, 0d, 100d)]
    [InlineData(nameof(UkiyoeEffect.LineStrength), 85d, 0d, 100d)]
    [InlineData(nameof(UkiyoeEffect.Flatten), 60d, 0d, 100d)]
    [InlineData(nameof(UkiyoeEffect.Misregistration), 30d, 0d, 100d)]
    [InlineData(nameof(UkiyoeEffect.Baren), 40d, 0d, 100d)]
    [InlineData(nameof(UkiyoeEffect.Paper), 50d, 0d, 100d)]
    public void AnimatedParametersStartFromTheirDefaultsWithinTheirRange(string name, double defaultValue, double minimum, double maximum)
    {
        var effect = new UkiyoeEffect();

        var animation = (Animation)Property(name).GetValue(effect)!;

        Assert.Equal(defaultValue, animation.DefaultValue);
        Assert.Equal(minimum, animation.MinValue);
        Assert.Equal(maximum, animation.MaxValue);
        Assert.Equal(defaultValue, animation.GetValue(0, 1, EffectDescriptions.Fps));
    }

    [Fact]
    public void QualityLineColorPaletteLevelsAndSeedStartFromTheirDefaults()
    {
        var effect = new UkiyoeEffect();

        Assert.Equal(UkiyoeQuality.High, effect.Quality);
        Assert.Equal(DefaultLineColor, effect.LineColor);
        Assert.Equal(6, effect.PaletteLevels);
        Assert.Equal(0, effect.Seed);
    }

    [Theory]
    [InlineData(int.MinValue, 2)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(6, 6)]
    [InlineData(16, 16)]
    [InlineData(17, 16)]
    [InlineData(100, 16)]
    [InlineData(int.MaxValue, 16)]
    public void PaletteLevelsStayBetweenTwoAndSixteen(int value, int expected)
    {
        var effect = new UkiyoeEffect { PaletteLevels = 5 };

        effect.PaletteLevels = value;

        Assert.Equal(expected, effect.PaletteLevels);
        Assert.False(effect.HasErrors);
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1234, 1234)]
    [InlineData(10000, 10000)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void SeedNeverDropsBelowZero(int value, int expected)
    {
        var effect = new UkiyoeEffect { Seed = 5 };

        effect.Seed = value;

        Assert.Equal(expected, effect.Seed);
        Assert.False(effect.HasErrors);
    }

    [Fact]
    public void ChangingQualityLineColorPaletteLevelsOrSeedNotifiesTheEditor()
    {
        var effect = new UkiyoeEffect();
        var changed = new List<string?>();
        effect.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        effect.Quality = UkiyoeQuality.Ultra;
        effect.LineColor = Colors.Crimson;
        effect.PaletteLevels = 9;
        effect.Seed = 7;

        Assert.Equal([nameof(UkiyoeEffect.Quality), nameof(UkiyoeEffect.LineColor), nameof(UkiyoeEffect.PaletteLevels), nameof(UkiyoeEffect.Seed)], changed);
    }

    [Fact]
    public void AssigningAnUnchangedOrClampedValueDoesNotNotify()
    {
        var effect = new UkiyoeEffect { PaletteLevels = 16 };
        var changed = new List<string?>();
        effect.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        effect.Quality = UkiyoeQuality.High;
        effect.LineColor = DefaultLineColor;
        effect.PaletteLevels = 16;
        effect.PaletteLevels = 100;
        effect.Seed = 0;
        effect.Seed = -1;

        Assert.Empty(changed);
    }

    [Fact]
    public void TheLabelIsTheLocalizedEffectName()
    {
        var effect = new UkiyoeEffect();

        Assert.Equal(Texts.Ukiyoe, effect.Label);
    }

    [Fact]
    public void TheNineNumericParametersReceiveTheAnimationParameters()
    {
        var effect = new UkiyoeEffect();

        effect.SetAnimationParameters(120, EffectDescriptions.Fps);

        Assert.All(Animations(effect), animation => Assert.Equal(120, animation.Length));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void NoExoFilterIsWrittenForAviUtl(int keyFrameIndex)
    {
        var effect = new UkiyoeEffect();

        var description = new ExoOutputDescription(new VideoInfo(), string.Empty, new AviUtlDirectories(string.Empty, string.Empty));

        Assert.Empty(effect.CreateExoVideoFilters(keyFrameIndex, description));
    }

    [Fact]
    public void TheEffectIsRegisteredForFilteringAndDecorationWithoutAviUtlSupport()
    {
        var attribute = typeof(UkiyoeEffect).GetCustomAttribute<VideoEffectAttribute>()!;

        Assert.Equal(nameof(Texts.Ukiyoe), attribute.Name);
        Assert.Equal([VideoEffectCategories.Filtering, VideoEffectCategories.Decoration], attribute.Categories);
        Assert.Equal([nameof(Texts.TagWoodblock), nameof(Texts.TagPrint), nameof(Texts.TagJapanese)], attribute.Keywords);
        Assert.False(attribute.IsAviUtlSupported);
        Assert.True(attribute.IsEffectItemSupported);
        Assert.Equal(typeof(Texts), attribute.ResourceType);
        Assert.Equal(Texts.Ukiyoe, attribute.GetName());
    }

    [Theory]
    [InlineData(nameof(UkiyoeEffect.Amount), nameof(Texts.BasicGroup), nameof(Texts.Amount), nameof(Texts.AmountDescription), 0)]
    [InlineData(nameof(UkiyoeEffect.Quality), nameof(Texts.BasicGroup), nameof(Texts.Quality), nameof(Texts.QualityDescription), 1)]
    [InlineData(nameof(UkiyoeEffect.LineWidth), nameof(Texts.LineGroup), nameof(Texts.LineWidth), nameof(Texts.LineWidthDescription), 10)]
    [InlineData(nameof(UkiyoeEffect.Coherence), nameof(Texts.LineGroup), nameof(Texts.Coherence), nameof(Texts.CoherenceDescription), 11)]
    [InlineData(nameof(UkiyoeEffect.LineDetail), nameof(Texts.LineGroup), nameof(Texts.LineDetail), nameof(Texts.LineDetailDescription), 12)]
    [InlineData(nameof(UkiyoeEffect.LineStrength), nameof(Texts.LineGroup), nameof(Texts.LineStrength), nameof(Texts.LineStrengthDescription), 13)]
    [InlineData(nameof(UkiyoeEffect.LineColor), nameof(Texts.LineGroup), nameof(Texts.LineColor), nameof(Texts.LineColorDescription), 14)]
    [InlineData(nameof(UkiyoeEffect.Flatten), nameof(Texts.ColorGroup), nameof(Texts.Flatten), nameof(Texts.FlattenDescription), 20)]
    [InlineData(nameof(UkiyoeEffect.PaletteLevels), nameof(Texts.ColorGroup), nameof(Texts.PaletteLevels), nameof(Texts.PaletteLevelsDescription), 21)]
    [InlineData(nameof(UkiyoeEffect.Misregistration), nameof(Texts.CraftGroup), nameof(Texts.Misregistration), nameof(Texts.MisregistrationDescription), 30)]
    [InlineData(nameof(UkiyoeEffect.Baren), nameof(Texts.CraftGroup), nameof(Texts.Baren), nameof(Texts.BarenDescription), 31)]
    [InlineData(nameof(UkiyoeEffect.Paper), nameof(Texts.CraftGroup), nameof(Texts.Paper), nameof(Texts.PaperDescription), 32)]
    [InlineData(nameof(UkiyoeEffect.Seed), nameof(Texts.CraftGroup), nameof(Texts.Seed), nameof(Texts.SeedDescription), 33)]
    public void EveryParameterIsDisplayedInItsGroupInOrder(string property, string group, string name, string description, int order)
    {
        var display = Attribute<DisplayAttribute>(property);

        Assert.Equal(group, display.GroupName);
        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(order, display.Order);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Theory]
    [InlineData(nameof(UkiyoeEffect.Amount))]
    [InlineData(nameof(UkiyoeEffect.LineWidth))]
    [InlineData(nameof(UkiyoeEffect.Coherence))]
    [InlineData(nameof(UkiyoeEffect.LineDetail))]
    [InlineData(nameof(UkiyoeEffect.LineStrength))]
    [InlineData(nameof(UkiyoeEffect.Flatten))]
    [InlineData(nameof(UkiyoeEffect.Misregistration))]
    [InlineData(nameof(UkiyoeEffect.Baren))]
    [InlineData(nameof(UkiyoeEffect.Paper))]
    public void AnimatedParametersAreEditedAsPercentagesWithAnimationSliders(string property)
    {
        var slider = Attribute<AnimationSliderAttribute>(property);

        Assert.Equal("F1", slider.StringFormat);
        Assert.Equal("%", slider.UnitText);
        Assert.Equal(0d, slider.DefaultMin);
        Assert.Equal(100d, slider.DefaultMax);
    }

    [Fact]
    public void TheQualityIsChosenFromACombo()
    {
        Assert.NotNull(Attribute<EnumComboBoxAttribute>(nameof(UkiyoeEffect.Quality)));
        Assert.Equal([UkiyoeQuality.Balanced, UkiyoeQuality.High, UkiyoeQuality.Ultra], Enum.GetValues<UkiyoeQuality>());
    }

    [Theory]
    [InlineData(UkiyoeQuality.Balanced, nameof(Texts.QualityBalanced), nameof(Texts.QualityBalancedDescription))]
    [InlineData(UkiyoeQuality.High, nameof(Texts.QualityHigh), nameof(Texts.QualityHighDescription))]
    [InlineData(UkiyoeQuality.Ultra, nameof(Texts.QualityUltra), nameof(Texts.QualityUltraDescription))]
    public void EveryQualityIsDisplayedWithItsLocalizedName(UkiyoeQuality quality, string name, string description)
    {
        var display = typeof(UkiyoeQuality).GetField(quality.ToString())!.GetCustomAttribute<DisplayAttribute>()!;

        Assert.Equal(name, display.Name);
        Assert.Equal(description, display.Description);
        Assert.Equal(typeof(Texts), display.ResourceType);
    }

    [Fact]
    public void TheLineColorIsPickedWithAColorPicker()
    {
        Assert.NotNull(Attribute<ColorPickerAttribute>(nameof(UkiyoeEffect.LineColor)));
    }

    [Fact]
    public void ThePaletteLevelsAreEditedWithoutAUnitFromTwoToSixteen()
    {
        var slider = Attribute<TextBoxSliderAttribute>(nameof(UkiyoeEffect.PaletteLevels));
        var range = Attribute<RangeAttribute>(nameof(UkiyoeEffect.PaletteLevels));

        Assert.Equal("F0", slider.StringFormat);
        Assert.Equal(string.Empty, slider.UnitText);
        Assert.Equal(2d, slider.DefaultMin);
        Assert.Equal(16d, slider.DefaultMax);
        Assert.Equal(2, range.Minimum);
        Assert.Equal(16, range.Maximum);
        Assert.Equal(6, Attribute<DefaultValueAttribute>(nameof(UkiyoeEffect.PaletteLevels)).Value);
    }

    [Fact]
    public void TheSeedIsEditedWithoutAUnitFromZero()
    {
        var slider = Attribute<TextBoxSliderAttribute>(nameof(UkiyoeEffect.Seed));
        var range = Attribute<RangeAttribute>(nameof(UkiyoeEffect.Seed));

        Assert.Equal("F0", slider.StringFormat);
        Assert.Equal(string.Empty, slider.UnitText);
        Assert.Equal(0d, slider.DefaultMin);
        Assert.Equal(10000d, slider.DefaultMax);
        Assert.Equal(0, range.Minimum);
        Assert.Equal(int.MaxValue, range.Maximum);
        Assert.Equal(0, Attribute<DefaultValueAttribute>(nameof(UkiyoeEffect.Seed)).Value);
    }

    [Fact]
    public void EverySettingSurvivesAProjectRoundTrip()
    {
        var effect = new UkiyoeEffect { Quality = UkiyoeQuality.Ultra, LineColor = Colors.Crimson, PaletteLevels = 9, Seed = 42 };
        var values = new[] { 55d, 45d, 70d, 20d, 65d, 15d, 85d, 35d, 90d };
        foreach (var (animation, value) in Animations(effect).Zip(values))
            animation.Values[0].Value = value;

        var clone = Json.GetClone(effect)!;

        Assert.NotSame(effect, clone);
        Assert.Equal(UkiyoeQuality.Ultra, clone.Quality);
        Assert.Equal(Colors.Crimson, clone.LineColor);
        Assert.Equal(9, clone.PaletteLevels);
        Assert.Equal(42, clone.Seed);
        Assert.Equal(values, Animations(clone).Select(animation => animation.GetValue(0, 1, EffectDescriptions.Fps)));
    }
}
