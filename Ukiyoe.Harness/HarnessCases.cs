using System.Windows.Media;

namespace Ukiyoe.Harness;

internal static class HarnessCases
{
    public static IEnumerable<(string Name, UkiyoeEffect Effect, IReadOnlyList<int> Frames)> All()
    {
        yield return ("default", Create(), [0]);
        yield return ("default-frames-0-8", Create(), Enumerable.Range(0, 9).ToArray());
        yield return ("amount-0", Create(effect => effect.Amount.Values[0].Value = 0), [0]);
        yield return ("quality-balanced", Create(effect => effect.Quality = UkiyoeQuality.Balanced), [0]);
        yield return ("quality-ultra", Create(effect => effect.Quality = UkiyoeQuality.Ultra), [0]);
        yield return ("line-width-0", Create(effect => effect.LineWidth.Values[0].Value = 0), [0]);
        yield return ("line-width-100", Create(effect => effect.LineWidth.Values[0].Value = 100), [0]);
        yield return ("coherence-0", Create(effect => effect.Coherence.Values[0].Value = 0), [0]);
        yield return ("coherence-100", Create(effect => effect.Coherence.Values[0].Value = 100), [0]);
        yield return ("line-detail-100", Create(effect => effect.LineDetail.Values[0].Value = 100), [0]);
        yield return ("line-strength-0", Create(effect => effect.LineStrength.Values[0].Value = 0), [0]);
        yield return ("flatten-0", Create(effect => effect.Flatten.Values[0].Value = 0), [0]);
        yield return ("flatten-100", Create(effect => effect.Flatten.Values[0].Value = 100), [0]);
        yield return ("palette-levels-2", Create(effect => effect.PaletteLevels = 2), [0]);
        yield return ("palette-levels-16", Create(effect => effect.PaletteLevels = 16), [0]);
        yield return ("misregistration-0", Create(effect => effect.Misregistration.Values[0].Value = 0), [0]);
        yield return ("misregistration-100", Create(effect => effect.Misregistration.Values[0].Value = 100), [0]);
        yield return ("baren-100", Create(effect => effect.Baren.Values[0].Value = 100), [0]);
        yield return ("paper-100", Create(effect => effect.Paper.Values[0].Value = 100), [0]);
        yield return ("seed-42", Create(effect => effect.Seed = 42), [0]);
        yield return ("line-color", Create(effect => effect.LineColor = Color.FromArgb(255, 200, 20, 20)), [0]);
    }

    public static IEnumerable<(string Name, Func<UkiyoeEffect> Create, Action<UkiyoeEffect> Change, int Frame)> Transitions()
    {
        yield return ("line-width-50-to-100", () => Create(), effect => effect.LineWidth.Values[0].Value = 100, 0);
        yield return ("coherence-50-to-100", () => Create(), effect => effect.Coherence.Values[0].Value = 100, 0);
        yield return ("line-detail-50-to-100", () => Create(), effect => effect.LineDetail.Values[0].Value = 100, 0);
        yield return ("flatten-60-to-0", () => Create(), effect => effect.Flatten.Values[0].Value = 0, 0);
        yield return ("quality-high-to-ultra", () => Create(), effect => effect.Quality = UkiyoeQuality.Ultra, 0);
        yield return ("palette-levels-6-to-2", () => Create(), effect => effect.PaletteLevels = 2, 0);
        yield return ("misregistration-30-to-100", () => Create(), effect => effect.Misregistration.Values[0].Value = 100, 0);
        yield return ("baren-40-to-100", () => Create(), effect => effect.Baren.Values[0].Value = 100, 0);
        yield return ("paper-50-to-100", () => Create(), effect => effect.Paper.Values[0].Value = 100, 0);
        yield return ("line-strength-85-to-0", () => Create(), effect => effect.LineStrength.Values[0].Value = 0, 0);
        yield return ("line-color-change", () => Create(), effect => effect.LineColor = Color.FromArgb(255, 200, 20, 20), 0);
        yield return ("seed-0-to-42", () => Create(), effect => effect.Seed = 42, 0);
        yield return ("amount-100-to-0", () => Create(), effect => effect.Amount.Values[0].Value = 0, 0);
    }

    public static IEnumerable<(string Name, UkiyoeEffect Effect)> Benchmarks()
    {
        yield return ("quality-balanced", Create(effect => effect.Quality = UkiyoeQuality.Balanced));
        yield return ("default", Create());
        yield return ("quality-ultra", Create(effect => effect.Quality = UkiyoeQuality.Ultra));
        yield return ("flatten-100", Create(effect => effect.Flatten.Values[0].Value = 100));
        yield return ("amount-0", Create(effect => effect.Amount.Values[0].Value = 0));
    }

    public static UkiyoeEffect Create(Action<UkiyoeEffect>? configure = null)
    {
        var effect = new UkiyoeEffect();
        configure?.Invoke(effect);
        return effect;
    }
}
