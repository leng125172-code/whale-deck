using System.Text.Json;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerUploadRecoveryPolicyTests
{
    private const string Operation = "aa0a8865343e49bdb01a72680131bf39";
    private const string Recovery = "bb0a8865343e49bdb01a72680131bf39";
    private static ContainerUploadReference Original() => new() { ContainerId = new('a',64),InstanceId = "777:2026-10-22T01:02:03.123456789Z",
        OperationId = Operation,Owner = new('B',64),ExpectedTargetProof = new('A',64),
        Context = new() { ActorSubject = "original-admin",TraceId = "original-trace",JobId = Operation,IdempotencyKey = Operation } };
    private static ContainerUploadRecoveryRequest Request() => new() { Original = Original(),RecoveryId = Recovery,ExpectedObservationHash = new('F',64),
        Context = new() { ActorSubject = "current-admin",TraceId = "current-trace",JobId = Recovery,IdempotencyKey = Recovery } };
    private static Dictionary<string,object?> Facts() => new(StringComparer.Ordinal)
    { ["containerId"] = new string('a',64),["instanceId"] = "777:2026-10-22T01:02:03.123456789Z",["operationId"] = Operation,["targetProof"] = new string('A',64),
        ["rootState"] = "Missing",["originalRecordHash"] = "B73B451CAC7D3B4D6CD095A4CB10A7F062D17ACA014FD693F2C99DD5F542441A",
        ["sealed"] = false,["sealId"] = null,["bindingCurrent"] = false,["mayHavePublished"] = false,["retainedObjects"] = false,
        ["observationHash"] = "C7C617A490A3950C8812ED4B6E407FC7A6066BEBF2EC0E54BA45CFFE1ED52791" };
    [Fact]
    public void PythonCanonicalMissingReceiptGoldenHashAndRawNanosecondInstanceMatch()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Facts()));
        var result = ContainerUploadRecoveryPolicy.Observation(json.RootElement,Original());
        Assert.Equal("Missing",result.RootState); Assert.False(result.BindingCurrent); Assert.Empty(result.SealId);
        Assert.Equal("C7C617A490A3950C8812ED4B6E407FC7A6066BEBF2EC0E54BA45CFFE1ED52791",ContainerUploadRecoveryBinding.ObservationHash(Original(),result));
        var changed = Original(); changed.Owner = new('C',64);
        Assert.NotEqual(result.ObservationHash,ContainerUploadRecoveryBinding.ObservationHash(changed,result));
    }
    [Theory]
    [InlineData("unknown")][InlineData("duplicate")][InlineData("current")][InlineData("missing-objects")]
    [InlineData("seal")][InlineData("digest")][InlineData("scope")][InlineData("state")]
    [InlineData("type")][InlineData("null-text")]
    public void MalformedOrContradictoryHistoricalFactsFailClosed(string variant)
    {
        var facts = Facts();
        switch (variant)
        {
            case "unknown": facts["unexpected"] = "field"; break;
            case "current": facts["bindingCurrent"] = true; break;
            case "missing-objects": facts["retainedObjects"] = true; break;
            case "seal": facts["sealed"] = true; break;
            case "digest": facts["observationHash"] = new string('F',64); break;
            case "scope": facts["containerId"] = new string('b',64); break;
            case "state": facts["rootState"] = "Completed"; break;
            case "type": facts["sealed"] = "private-invalid-boolean"; break;
            case "null-text": facts["originalRecordHash"] = null; break;
        }
        var text = JsonSerializer.Serialize(facts);
        if (variant == "duplicate") text = text[..^1]+",\"sealed\":false}";
        using var json = JsonDocument.Parse(text);
        var error = Assert.Throws<InvalidOperationException>(() => ContainerUploadRecoveryPolicy.Observation(json.RootElement,Original()));
        Assert.Equal("CONTAINER_UPLOAD_RECOVERY_OUTPUT_INVALID",error.Message);
    }
    [Fact]
    public async Task ActualAgentAdvertisesRecoveryIndependentlyAndPreservesOriginalFileCapabilities()
    {
        var capabilities = await new AgentGrpcService(null!).GetCapabilities(new(),null!);
        Assert.Equal(1U,capabilities.ProtocolMajor); Assert.Equal(42U,capabilities.ProtocolMinor);
        Assert.Contains("docker.files.read",capabilities.Capabilities); Assert.Contains("docker.files.bound-uploads",capabilities.Capabilities);
        Assert.Contains("docker.files.upload-cancel-fence",capabilities.Capabilities); Assert.Contains("docker.files.upload-recovery-seal",capabilities.Capabilities);
    }

    [Fact]
    public void CurrentRecoveryContextIsDistinctAndOriginalContextStaysImmutable()
    {
        var request = Request(); ContainerUploadRecoveryPolicy.Request(request);
        Assert.Equal("original-admin",request.Original.Context.ActorSubject); Assert.Equal("current-admin",request.Context.ActorSubject);
        request.Context.JobId = Operation;
        Assert.Throws<ArgumentException>(() => ContainerUploadRecoveryPolicy.Request(request));
        request = Request(); request.Context.IdempotencyKey = Operation;
        Assert.Throws<ArgumentException>(() => ContainerUploadRecoveryPolicy.Request(request));
        request = Request(); request.RecoveryId = Operation;
        Assert.Throws<ArgumentException>(() => ContainerUploadRecoveryPolicy.Request(request));
    }
}
