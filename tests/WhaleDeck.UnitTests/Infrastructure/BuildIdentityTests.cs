using System.Text.Json;
using WhaleDeck.Contracts;

namespace WhaleDeck.UnitTests.Infrastructure;

public sealed class BuildIdentityTests
{
    [Fact]
    public void FullRevisionComesOnlyFromAssemblyInformation()
    {
        var revision = new string('a', 40);
        var value = BuildIdentity.Parse("WhaleDeck.Api", "0.1.0+" + revision);
        Assert.Equal("0.1.0", value.Version);
        Assert.Equal(revision, value.SourceRevision);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value));
        Assert.Equal(3, json.RootElement.EnumerateObject().Count());
        Assert.Equal(revision, json.RootElement.GetProperty("sourceRevision").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.1.0")]
    [InlineData("0.1.0+abcd123")]
    [InlineData("0.1.0+AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("0.1.0+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/")]
    [InlineData("0.1.0+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.dirty")]
    public void MissingOrAmbiguousRevisionIsNotInvented(string? information)
        => Assert.Null(BuildIdentity.Parse("WhaleDeck.Worker", information).SourceRevision);

    [Fact]
    public void LoadedAssemblyIncludesARealPublicVersion()
    {
        var value = BuildIdentity.FromAssembly(typeof(BuildIdentity).Assembly);
        Assert.Equal("WhaleDeck.Contracts", value.Component);
        Assert.Equal("0.1.0", value.Version);
    }

    [Theory]
    [InlineData("--health-check")]
    [InlineData("--build-info", "--other")]
    public void InspectionDoesNotSwallowOtherApplicationArguments(params string[] arguments)
        => Assert.False(BuildIdentity.TryPrint(arguments, typeof(BuildIdentity).Assembly));
}
