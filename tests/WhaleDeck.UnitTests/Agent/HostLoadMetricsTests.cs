using System.Globalization;
using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class HostLoadMetricsTests
{
    [Fact]
    public void UsesOneFiveFifteenMinuteLoadAndLogicalCoreNormalizationWithoutClamping()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var values = HostLoadMetrics.Parse("6.00 4.50 2.25 1/200 45", 4).ToDictionary(item => item.Kind);
            Assert.Equal(6, values["load.average.1"].Value);
            Assert.Equal(4.5, values["load.average.5"].Value);
            Assert.Equal(2.25, values["load.average.15"].Value);
            Assert.Equal(150, values["load.normalized"].Value);
            Assert.All(values.Values, item => Assert.Equal("Good", item.Quality));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData("0.0 0.0", 4)]
    [InlineData("-1.0 0.0 0.0", 4)]
    [InlineData("NaN 0.0 0.0", 4)]
    [InlineData("Infinity 0.0 0.0", 4)]
    [InlineData("6,00 0.0 0.0", 4)]
    [InlineData("1.0 1.0 1.0", 0)]
    public void InvalidLoadIsUnavailableNotIdle(string? text, int cores) =>
        Assert.All(HostLoadMetrics.Parse(text, cores), item => { Assert.Equal("Unavailable", item.Quality); Assert.Equal(0, item.Value); });

    [Fact]
    public void ParsesSystemdPropertyNamesNotUnstableOutputOrder()
    {
        var status = SystemdStatus.Parse("UnitFileState=enabled\nLoadState=loaded\nSubState=running\nActiveState=active\nUnapproved=ignored\n");
        Assert.Equal(4, status.Count); Assert.Equal("active", status["ActiveState"]); Assert.Equal("running", status["SubState"]);
        Assert.Empty(SystemdStatus.Parse(new string('x', 4097)));
    }
}
