using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Ukiyoe;

[VideoEffect(nameof(Texts.Ukiyoe), [VideoEffectCategories.Filtering, VideoEffectCategories.Decoration], [nameof(Texts.TagWoodblock), nameof(Texts.TagPrint), nameof(Texts.TagJapanese)], IsAviUtlSupported = false, ResourceType = typeof(Texts))]
public sealed class UkiyoeEffect : VideoEffectBase
{
    public override string Label => Texts.Ukiyoe;

    public UkiyoeEffect()
    {
        UkiyoeUpdateNotifier.EnsureCheckedOnce();
    }

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Amount), Description = nameof(Texts.AmountDescription), Order = 0, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Amount { get; } = new Animation(100, 0, 100);

    [Display(GroupName = nameof(Texts.BasicGroup), Name = nameof(Texts.Quality), Description = nameof(Texts.QualityDescription), Order = 1, ResourceType = typeof(Texts))]
    [EnumComboBox]
    public UkiyoeQuality Quality { get => _quality; set => Set(ref _quality, value); }
    private UkiyoeQuality _quality = UkiyoeQuality.High;

    [Display(GroupName = nameof(Texts.LineGroup), Name = nameof(Texts.LineWidth), Description = nameof(Texts.LineWidthDescription), Order = 10, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation LineWidth { get; } = new Animation(50, 0, 100);

    [Display(GroupName = nameof(Texts.LineGroup), Name = nameof(Texts.Coherence), Description = nameof(Texts.CoherenceDescription), Order = 11, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Coherence { get; } = new Animation(50, 0, 100);

    [Display(GroupName = nameof(Texts.LineGroup), Name = nameof(Texts.LineDetail), Description = nameof(Texts.LineDetailDescription), Order = 12, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation LineDetail { get; } = new Animation(50, 0, 100);

    [Display(GroupName = nameof(Texts.LineGroup), Name = nameof(Texts.LineStrength), Description = nameof(Texts.LineStrengthDescription), Order = 13, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation LineStrength { get; } = new Animation(85, 0, 100);

    [Display(GroupName = nameof(Texts.LineGroup), Name = nameof(Texts.LineColor), Description = nameof(Texts.LineColorDescription), Order = 14, ResourceType = typeof(Texts))]
    [ColorPicker]
    public Color LineColor
    {
        get => _lineColor;
        set => Set(ref _lineColor, value);
    }
    private Color _lineColor = Color.FromArgb(255, 30, 26, 24);

    [Display(GroupName = nameof(Texts.ColorGroup), Name = nameof(Texts.Flatten), Description = nameof(Texts.FlattenDescription), Order = 20, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Flatten { get; } = new Animation(60, 0, 100);

    [Display(GroupName = nameof(Texts.ColorGroup), Name = nameof(Texts.PaletteLevels), Description = nameof(Texts.PaletteLevelsDescription), Order = 21, ResourceType = typeof(Texts))]
    [Range(UkiyoeSettings.MinimumPaletteLevels, UkiyoeSettings.MaximumPaletteLevels)]
    [DefaultValue(6)]
    [TextBoxSlider("F0", "", UkiyoeSettings.MinimumPaletteLevels, UkiyoeSettings.MaximumPaletteLevels)]
    public int PaletteLevels
    {
        get => _paletteLevels;
        set => Set(ref _paletteLevels, UkiyoeSettings.ClampPaletteLevels(value));
    }
    private int _paletteLevels = 6;

    [Display(GroupName = nameof(Texts.CraftGroup), Name = nameof(Texts.Misregistration), Description = nameof(Texts.MisregistrationDescription), Order = 30, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Misregistration { get; } = new Animation(30, 0, 100);

    [Display(GroupName = nameof(Texts.CraftGroup), Name = nameof(Texts.Baren), Description = nameof(Texts.BarenDescription), Order = 31, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Baren { get; } = new Animation(40, 0, 100);

    [Display(GroupName = nameof(Texts.CraftGroup), Name = nameof(Texts.Paper), Description = nameof(Texts.PaperDescription), Order = 32, ResourceType = typeof(Texts))]
    [AnimationSlider("F1", "%", 0, 100)]
    public Animation Paper { get; } = new Animation(50, 0, 100);

    [Display(GroupName = nameof(Texts.CraftGroup), Name = nameof(Texts.Seed), Description = nameof(Texts.SeedDescription), Order = 33, ResourceType = typeof(Texts))]
    [Range(0, int.MaxValue)]
    [DefaultValue(0)]
    [TextBoxSlider("F0", "", 0, 10000)]
    public int Seed
    {
        get => _seed;
        set => Set(ref _seed, Math.Max(value, 0));
    }
    private int _seed;

    private IAnimatable[]? _animatables;

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        => new UkiyoeEffectProcessor(devices, this);

    protected override IEnumerable<IAnimatable> GetAnimatables()
        => _animatables ??= [Amount, LineWidth, Coherence, LineDetail, LineStrength, Flatten, Misregistration, Baren, Paper];
}
