using System.Text;
using WhaleDeck.Application.Services;

namespace WhaleDeck.UnitTests;

public sealed class TerminalFramePolicyTests
{
    [Theory]
    [InlineData("{}")][InlineData("[]")][InlineData("null")][InlineData("{\"kind\":\"open\"}")]
    [InlineData("{\"kind\":\"close\",\"exec\":\"bash\"}")]
    [InlineData("{\"kind\":\"input\",\"data\":\"\"}")]
    [InlineData("{\"kind\":\"input\",\"data\":\"%%%\"}")]
    [InlineData("{\"kind\":\"input\",\"data\":42}")]
    [InlineData("{\"kind\":\"close\",\"kind\":\"close\"}")]
    [InlineData("{\"kind\":\"resize\",\"columns\":401,\"rows\":24}")]
    [InlineData("{\"kind\":\"resize\",\"columns\":80,\"rows\":1}")]
    [InlineData("{\"kind\":\"resize\",\"columns\":true,\"rows\":24}")]
    [InlineData("{\"kind\":\"input\",\"data\":\"YQ==\",\"target\":\"other\"}")]
    public void InvalidOrTargetChangingInputIsRejected(string json) => Assert.Throws<ArgumentException>(() => TerminalFramePolicy.Input(Encoding.UTF8.GetBytes(json)));
    [Fact]
    public void BinaryInputResizeAndCloseAreSupported()
    {
        Assert.Equal(new byte[] { 0, 255 }, TerminalFramePolicy.Input(Encoding.UTF8.GetBytes("{\"kind\":\"input\",\"data\":\"AP8=\"}")).Data);
        var resize = TerminalFramePolicy.Input(Encoding.UTF8.GetBytes("{\"kind\":\"resize\",\"columns\":120,\"rows\":40}"));
        Assert.Equal(120u, resize.Columns); Assert.Equal(40u, resize.Rows);
        Assert.Equal("close", TerminalFramePolicy.Input(Encoding.UTF8.GetBytes("{\"kind\":\"close\"}")).Kind);
    }
    [Theory]
    [InlineData("{}")][InlineData("{\"token\":\"bad\"}")][InlineData("{\"token\":42}")][InlineData("{\"token\":\"\",\"extra\":1}")]
    public void InvalidGrantFramesAreRejected(string json) => Assert.Throws<ArgumentException>(() => TerminalFramePolicy.Grant(Encoding.UTF8.GetBytes(json)));
    [Fact]
    public void ValidGrantAndBudgetAreExact()
    {
        var token = new string('A', 64); Assert.Equal(token, TerminalFramePolicy.Grant(Encoding.UTF8.GetBytes("{\"token\":\"" + token + "\"}")));
        Assert.Throws<ArgumentException>(() => TerminalFramePolicy.Input(new byte[8193]));
        Assert.Throws<ArgumentException>(() => TerminalFramePolicy.Grant(new byte[257]));
        Assert.Throws<ArgumentException>(() => TerminalFramePolicy.Input(Encoding.UTF8.GetBytes("{\"kind\":\"input\",\"data\":\"" + Convert.ToBase64String(new byte[4097]) + "\"}")));
    }
}
