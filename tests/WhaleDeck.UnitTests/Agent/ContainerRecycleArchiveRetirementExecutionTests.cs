using System.Text;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecycleArchiveRetirementExecutionTests
{
    [Theory][InlineData("plan")][InlineData("reserve")][InlineData("apply")][InlineData("inspect")][InlineData("discard")]
    public async Task ExactInvocationBindingPrecedesAnyOperationBytes(string action)
    {
        using var bytes=new MemoryStream();using var writer=new StreamWriter(bytes,leaveOpen:true);var name="container-recycle-archive-retirement:"+action;
        await FileTransferPipe.OpenBeforeReady(writer,name,"{\"privateOperation\":true}",default);Assert.Equal(0,bytes.Length);
        using var canceled=new CancellationTokenSource();canceled.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>FileTransferPipe.CommitAfterReady(writer,name,"{}",canceled.Token));Assert.Equal(0,bytes.Length);
        await FileTransferPipe.CommitAfterReady(writer,name,"{}",default);Assert.True(bytes.Length>0);
    }
    [Theory][InlineData("plan")][InlineData("reserve")][InlineData("apply")][InlineData("inspect")][InlineData("discard")]
    public void HelperAndStopRemainFixedToDriverIdAndOriginalInvocation(string action)
    {
        var method=typeof(FileTransferRunner).GetMethod("Start",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;var id=new string('a',32);var invocation=new string('b',32);
        var start=Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null,["container-recycle-archive-retirement:"+action,id,null]));
        Assert.Equal(new[]{"-n","/usr/local/libexec/whaledeck-privileged","container-file-recycle-archive-retirements",action,id},start.ArgumentList);
        var stop=Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null,["container-recycle-archive-retirement:"+action,id,invocation]));Assert.Equal(new[]{"-n","/usr/local/libexec/whaledeck-privileged","container-file-recycle-archive-retirements-stop",id,invocation},stop.ArgumentList);Assert.False(start.UseShellExecute);Assert.Equal(2,start.Environment.Count);
    }
    [Fact]
    public async Task InvalidActionNeverStartsAProcess()=>await Assert.ThrowsAsync<ArgumentException>(()=>new FileTransferRunner().ContainerRecycleArchiveRetirementOnceAsync("shell","{}",default));
    [Fact]
    public void NestedRetirementPlanHasItsOwnBoundedDepth()
    {
        var q = Request();
        var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ContainerRecycleArchiveRetirementWire.Plan(q.Plan)));
        using var doc = FileTransferJson.Parse(raw, false, true);
        Assert.Equal(20, doc.RootElement.EnumerateObject().Count());
        Assert.ThrowsAny<JsonException>(() => FileTransferJson.Parse(raw, false));
        var deep = Encoding.UTF8.GetBytes(new string('[', 21) + "0" + new string(']', 21));
        Assert.ThrowsAny<JsonException>(() => FileTransferJson.Parse(deep, false, true));
    }
    private static ContainerRecycleArchiveRetirementRequest Request()
    {
        var q = ContainerRecycleArchiveRetirementContractTests.Request(); q.Context.JobId = q.OperationId;
        q.Plan.PublishedIdentity.Device = q.Plan.Command.Archive.Plan.ParentIdentity.Device;
        q.Plan.Fingerprint = ContainerRecycleArchiveRetirementWire.Fingerprint(q.Plan); return q;
    }
    [Theory][InlineData("reserve")][InlineData("apply")][InlineData("inspect")][InlineData("discard")]
    public async Task BindingMismatchCannotReachRootRunner(string action)
    {
        var q = Request(); var hash = ContainerRecycleArchiveRetirementBinding.Hash(q); q.Context.TraceId = "changed-after-signing";
        var service = new ContainerRecycleArchiveRetirementGrpcService(null!);
        var context = new Context(ContainerRecycleArchiveRetirementBinding.Service + action, null, hash);
        var error = await Assert.ThrowsAsync<RpcException>(() => action switch {
            "reserve" => service.Reserve(q, context), "apply" => service.Apply(q, context),
            "inspect" => service.Inspect(q, context), _ => service.Discard(q, context) });
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
    }
    [Fact]
    public async Task PlanAlsoRequiresBindingBeforeRunner()
    {
        var q = new ContainerRecycleArchiveRetirementPlanRequest { Command = Request().Plan.Command, Context = new() { ActorSubject = "actor", TraceId = "trace" } };
        await Assert.ThrowsAsync<RpcException>(() => new ContainerRecycleArchiveRetirementGrpcService(null!).Plan(q, new Context("Plan", null, new string('0', 64))));
    }
    [Fact]
    public async Task SemanticPolicyRunsBeforeBindingOrRunner()
    {
        var q = Request(); q.Context.JobId = new string('1', 32);
        await Assert.ThrowsAsync<ArgumentException>(() => new ContainerRecycleArchiveRetirementGrpcService(null!).Apply(q, null!));
    }
    [Theory][InlineData(65537, "a")][InlineData(32769, "é")][InlineData(1, "\n")][InlineData(1, "\r")]
    public async Task OversizedOrMultilineBodyNeverStartsAProcess(int count, string text)
        => await Assert.ThrowsAsync<ArgumentException>(() => new FileTransferRunner().ContainerRecycleArchiveRetirementOnceAsync("apply", string.Concat(Enumerable.Repeat(text, count)), default));
    [Fact]
    public async Task PreCanceledCallNeverStartsAProcess()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileTransferRunner().ContainerRecycleArchiveRetirementOnceAsync("apply", "{}", cancellation.Token));
    }
    [Theory]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_FOUND", StatusCode.NotFound)]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_NOT_FOUND", StatusCode.Unavailable)]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_RETENTION_NOT_FOUND", StatusCode.Unavailable)]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_NOT_DUE", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_FENCE_CHANGED", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_COPY_CHANGED", StatusCode.Aborted)]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_SOURCE_BUSY", StatusCode.ResourceExhausted)]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_RETENTION_TIME_RANGE", StatusCode.InvalidArgument)]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_TIMEOUT", StatusCode.DeadlineExceeded)]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_ACTION_INVALID", StatusCode.InvalidArgument)]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_REQUEST_INVALID", StatusCode.InvalidArgument)]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_MODULE_UNSAFE", StatusCode.FailedPrecondition)]
    [InlineData("CONTAINER_RECYCLE_ARCHIVE_RETIREMENT_INVOCATION_INVALID", StatusCode.FailedPrecondition)]
    [InlineData("private stderr", StatusCode.Unavailable)]
    public void AncestorAbsenceNeverBecomesRetirementAbsence(string code, StatusCode expected)
        => Assert.Equal(expected, ContainerRecycleArchiveRetirementPolicy.Failure(code).StatusCode);
    private sealed class Context : ServerCallContext
    {
        private readonly string _method;
        private readonly Metadata _headers = [];
        public Context(string method, string? token, string? hash)
        {
            _method = method;
            if (token is not null) _headers.Add("x-whaledeck-capability", token);
            if (hash is not null) _headers.Add(ContainerRecycleArchiveRetirementBinding.Header, hash);
        }
        protected override string MethodCore => _method;
        protected override string HostCore => "localhost";
        protected override string PeerCore => "unix";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddSeconds(45);
        protected override Metadata RequestHeadersCore => _headers;
        protected override CancellationToken CancellationTokenCore => default;
        protected override Metadata ResponseTrailersCore => [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => null!;
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
