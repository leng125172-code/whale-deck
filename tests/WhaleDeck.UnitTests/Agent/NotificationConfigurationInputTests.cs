using System.Text.Json;
using Google.Protobuf;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class NotificationConfigurationInputTests
{
    private static NotificationConfigurationRequest Request(string json = "{}") => new()
    {
        ChannelId = Guid.NewGuid().ToString("D"), OperationId = Guid.NewGuid().ToString("D"), ExpectedRevision = "EMPTY", Action = "remove",
        PrivateJson = ByteString.CopyFromUtf8(json), Context = new RequestContext { ActorSubject = "fixture-administrator" }
    };

    [Fact]
    public void IdentityAndExpectedVersionAreBoundOutsidePrivateInput()
    {
        var request = Request();
        using var input = JsonDocument.Parse(NotificationConfigurationGrpcService.BuildInput(request));
        Assert.Equal(request.ChannelId, input.RootElement.GetProperty("channelId").GetString());
        Assert.Equal(request.OperationId, input.RootElement.GetProperty("operationId").GetString());
        Assert.Equal("EMPTY", input.RootElement.GetProperty("expectedRevision").GetString());
        Assert.Equal(3, input.RootElement.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("{\"path\":\"/etc/passwd\"}")]
    [InlineData("{\"operationId\":\"overridden\"}")]
    [InlineData("{\"target\":{},\"target\":{}}")]
    [InlineData("[]")]
    [InlineData("{broken")]
    public void ArbitraryPathsReservedFieldsAndDuplicateTopLevelKeysAreRejected(string json) =>
        Assert.Throws<ArgumentException>(() => NotificationConfigurationGrpcService.BuildInput(Request(json)));

    [Fact]
    public void NestedDuplicatesAreNotSilentlyCollapsedBeforeRootValidation()
    {
        var request = Request("{\"target\":{\"provider\":\"Webhook\",\"provider\":\"Email\"},\"credentialMode\":\"remove\"}"); request.Action = "apply";
        var input = NotificationConfigurationGrpcService.BuildInput(request);
        Assert.Contains("\"provider\":\"Webhook\",\"provider\":\"Email\"", input);
    }

    [Fact]
    public void MissingActorInvalidGuidActionRevisionAndUtf8BudgetFailClosed()
    {
        var request = Request(); request.Context.ActorSubject = "";
        Assert.Throws<ArgumentException>(() => NotificationConfigurationGrpcService.BuildInput(request));
        request = Request(); request.ChannelId = Guid.Empty.ToString();
        Assert.Throws<ArgumentException>(() => NotificationConfigurationGrpcService.BuildInput(request));
        request = Request(); request.Action = "shell";
        Assert.Throws<ArgumentException>(() => NotificationConfigurationGrpcService.BuildInput(request));
        request = Request(); request.ExpectedRevision = "arbitrary";
        Assert.Throws<ArgumentException>(() => NotificationConfigurationGrpcService.BuildInput(request));
        request = Request("{\"target\":\"" + new string('中', 22000) + "\"}"); request.Action = "apply";
        Assert.Throws<ArgumentException>(() => NotificationConfigurationGrpcService.BuildInput(request));
    }
}
