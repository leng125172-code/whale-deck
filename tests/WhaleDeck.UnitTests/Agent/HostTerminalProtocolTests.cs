using Google.Protobuf;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class HostTerminalProtocolTests
{
    private const string Session = "73e72d0d5b3347918561789ddbeacbe8";
    private static HostTerminalFrame Open() => new() { SessionId = Session, Kind = "open", Columns = 80, Rows = 24, Context = new() { ActorSubject = "admin" } };
    [Fact]
    public void FixedHostOpenHasNoExecutableUserPathOrPrivateInput()
    {
        var line = HostTerminalProtocol.Open(Open());
        Assert.Equal("{\"type\":\"open\",\"columns\":80,\"rows\":24}", line);
        Assert.DoesNotContain("admin", line, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(0, 24)]
    [InlineData(401, 24)]
    [InlineData(80, 0)]
    [InlineData(80, 401)]
    public void InvalidDimensionsReject(uint columns, uint rows)
    {
        var request = Open(); request.Columns = columns; request.Rows = rows;
        Assert.Throws<ArgumentException>(() => HostTerminalProtocol.Open(request));
    }
    [Theory]
    [InlineData("../x")]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("73E72D0D5B3347918561789DDBEACBE8")]
    [InlineData("")]
    public void InvalidOrNoncanonicalSessionRejects(string id)
    {
        var request = Open(); request.SessionId = id;
        Assert.Throws<ArgumentException>(() => HostTerminalProtocol.Open(request));
    }
    [Theory]
    [InlineData("")]
    [InlineData("admin\n")]
    public void MissingOrUnsafeActorRejects(string actor)
    {
        var request = Open(); request.Context.ActorSubject = actor;
        Assert.Throws<ArgumentException>(() => HostTerminalProtocol.Open(request));
    }
    [Fact]
    public void InputIsBinaryBoundedAndCannotChangeTargetOrActor()
    {
        var request = new HostTerminalFrame { Kind = "input", Data = ByteString.CopyFrom([0, 1, 255]) };
        Assert.Equal("{\"type\":\"input\",\"data\":\"AAH/\"}", HostTerminalProtocol.Input(request, Session));
        request.SessionId = Guid.NewGuid().ToString("N");
        Assert.Throws<ArgumentException>(() => HostTerminalProtocol.Input(request, Session));
        request.SessionId = Session; request.Context = new();
        Assert.Throws<ArgumentException>(() => HostTerminalProtocol.Input(request, Session));
        request.Context = null; request.Data = ByteString.CopyFrom(new byte[4097]);
        Assert.Throws<ArgumentException>(() => HostTerminalProtocol.Input(request, Session));
    }
    [Theory]
    [InlineData("command")]
    [InlineData("exec")]
    [InlineData("open")]
    public void NoGeneralCommandOrSecondOpen(string kind) => Assert.Throws<ArgumentException>(() => HostTerminalProtocol.Input(new() { Kind = kind }, Session));
    [Fact]
    public void ResizeAndCloseHaveNoCommandOrIdentityPayload()
    {
        Assert.Equal("{\"type\":\"resize\",\"columns\":120,\"rows\":40}", HostTerminalProtocol.Input(new() { Kind = "resize", Columns = 120, Rows = 40 }, Session));
        Assert.Equal("{\"type\":\"close\"}", HostTerminalProtocol.Input(new() { Kind = "close" }, Session));
        Assert.Throws<ArgumentException>(() => HostTerminalProtocol.Input(new() { Kind = "close", Data = ByteString.CopyFromUtf8("x") }, Session));
    }
    [Fact]
    public void OutputIsBoundedAndContainsOnlyProtocolFields()
    {
        var output = HostTerminalProtocol.Output("{\"type\":\"output\",\"data\":\"aGk=\",\"ignored\":\"not exposed\"}");
        Assert.Equal("hi", output.Data.ToStringUtf8()); Assert.Empty(output.Code);
        Assert.Throws<InvalidOperationException>(() => HostTerminalProtocol.Output("{\"type\":\"command\"}"));
        Assert.Throws<InvalidOperationException>(() => HostTerminalProtocol.Output("{\"type\":\"exit\",\"code\":\"private detail\"}"));
        Assert.Throws<InvalidOperationException>(() => HostTerminalProtocol.Output(new string('x', 8193)));
    }
}
