using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests.Application;

public sealed class FilePathScopeTests
{
    [Theory]
    [InlineData("a", "a", true)]
    [InlineData("a", "a/b", true)]
    [InlineData("a/b/c", "a", true)]
    [InlineData("a", "ab", false)]
    [InlineData("a/b", "a/bc", false)]
    [InlineData("a/one", "a/two", false)]
    [InlineData("目录", "目录/代码.txt", true)]
    [InlineData("目录", "目录二/代码.txt", false)]
    [InlineData("A", "a", false)]
    [InlineData("é", "é", false)]
    public void AncestryIsOrdinalSymmetricAndComponentBounded(string first, string second, bool conflicts)
    {
        var left = new FilePathScope(new("test.shared", first)); var right = new FilePathScope(new("test.shared", second));
        Assert.Equal(conflicts, left.Conflicts(right)); Assert.Equal(conflicts, right.Conflicts(left));
        Assert.Equal(FileManagementService.Hash("test.shared:" + first), left.Hash);
    }

    [Fact]
    public void DistinctRegisteredRootIdsRemainDistinctLogicalNamespaces()
    {
        Assert.False(new FilePathScope(new("first", "same/path")).Conflicts(new(new("second", "same/path"))));
        // Actual bind aliases require Agent inode checks and the global kernel
        // namespace gate; logical SQL roots do not claim physical resolution.
    }

    [Theory]
    [InlineData("")]
    [InlineData("/etc")]
    [InlineData("a//b")]
    [InlineData("a/../b")]
    [InlineData("a\\b")]
    [InlineData("a\n")]
    public void InvalidLocationsNeverBecomeLockScopes(string path) =>
        Assert.Throws<ArgumentException>(() => new FilePathScope(new("test.shared", path)));
}
