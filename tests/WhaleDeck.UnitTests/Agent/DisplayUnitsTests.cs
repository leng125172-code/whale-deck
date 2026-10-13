using System.Globalization;
using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class DisplayUnitsTests
{
    [Theory]
    [InlineData(-1L, "容量未知")]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.0 KiB")]
    [InlineData(1048576L, "1.0 MiB")]
    [InlineData(1073741824L, "1.0 GiB")]
    [InlineData(16131004906L, "15.02 GiB")]
    [InlineData(1099511627776L, "1.0 TiB")]
    [InlineData(long.MaxValue, "8.0 EiB")]
    public void CapacityUsesAdaptiveBinaryUnits(long value, string expected) =>
        Assert.Equal(expected, DisplayUnits.Bytes(value));

    [Fact]
    public void CleanupPlanUsesReadableCapacityAndOnlyDanglingFilter()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("清理未使用的悬空镜像，预计最多回收 15.02 GiB", DockerEngine.ImageCleanupDescription(16131004906));
            var filter = DockerEngine.DanglingImageFilters();
            Assert.Single(filter);
            Assert.True(Assert.Single(filter["dangling"]).Value);
            Assert.True(filter["dangling"].ContainsKey("true"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
