using System.Windows;
using Telemetry;

namespace Ukiyoe.Tests;

public sealed class HostIntegrationTests
{
    [Fact]
    public void OutsideAWpfApplicationNoTelemetryIsStartedOrSent()
    {
        Assert.Null(Application.Current);

        UkiyoeTelemetry.EnsureStartedOnce();
        UkiyoeTelemetry.Report(new InvalidOperationException());

        Assert.Null(ProcessState.Read("DrainClaimed"));
        Assert.Null(ProcessState.Read("SentCount"));
    }

    [Fact]
    public void OutsideAWpfApplicationTheEffectCanStillBeCreated()
    {
        Assert.Null(Application.Current);

        var effect = new UkiyoeEffect();

        Assert.Equal(Texts.Ukiyoe, effect.Label);
        Assert.Null(ProcessState.Read("DrainClaimed"));
    }
}
