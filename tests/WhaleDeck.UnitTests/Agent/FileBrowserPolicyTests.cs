using WhaleDeck.Agent.Services;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileBrowserPolicyTests
{
    [Theory]
    [InlineData("")][InlineData(" ")][InlineData("a\n")][InlineData("a/b")][InlineData("a\\b")]
    public void SearchTermsMustBeNonemptyNames(string query)
    {
        Assert.Throws<ArgumentException>(() => FileBrowserService.Validate(new("files.shared", "", query)));
        Assert.Throws<ArgumentException>(() => FileTransferPolicy.Search(new() { Location = new() { RootId = "files.shared", Path = "" }, Query = query, Kind = "All", Take = 50 }));
    }
    [Fact]
    public void RpcAndApplicationBudgetsAgree()
    {
        var command = new FileSearchCommand("files.shared", "目录", "中" + new string('a', 253), "File", false, "abc.DEF_123-", 100);
        FileBrowserService.Validate(command);
        FileTransferPolicy.Search(new() { Location = new() { RootId = command.RootId, Path = command.Path }, Query = command.Query, Kind = command.Kind, Cursor = command.Cursor, Take = 100 });
        foreach (var bad in new[] { command with { Query = new('中', 86) }, command with { Cursor = new('a', 32769) }, command with { Cursor = "a/b" },
            command with { Take = 0 }, command with { Take = 101 }, command with { Kind = "Symlink" }, command with { Path = "../escape" } })
        {
            Assert.Throws<ArgumentException>(() => FileBrowserService.Validate(bad));
            Assert.Throws<ArgumentException>(() => FileTransferPolicy.Search(new() { Location = new() { RootId = bad.RootId, Path = bad.Path }, Query = bad.Query, Kind = bad.Kind, Cursor = bad.Cursor, Take = (uint)bad.Take }));
        }
    }
}
