using System.Text;
using System.Text.Json;
using Grpc.Core;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class ContainerRecycleArchiveExecutionTests
{
    [Theory][InlineData("plan")][InlineData("reserve")][InlineData("apply")][InlineData("inspect")][InlineData("discard")]
    public async Task ExactInvocationBindingPrecedesAnyOperationBytes(string action)
    {
        using var bytes=new MemoryStream();using var writer=new StreamWriter(bytes,leaveOpen:true);var name="container-recycle-archive:"+action;
        await FileTransferPipe.OpenBeforeReady(writer,name,"{\"privateOperation\":true}",default);Assert.Equal(0,bytes.Length);
        using var canceled=new CancellationTokenSource();canceled.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>FileTransferPipe.CommitAfterReady(writer,name,"{}",canceled.Token));Assert.Equal(0,bytes.Length);
        await FileTransferPipe.CommitAfterReady(writer,name,"{}",default);Assert.True(bytes.Length>0);
    }
    [Theory][InlineData("plan")][InlineData("reserve")][InlineData("apply")][InlineData("inspect")][InlineData("discard")]
    public void HelperAndStopRemainFixedToDriverIdAndOriginalInvocation(string action)
    {
        var method=typeof(FileTransferRunner).GetMethod("Start",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;var id=new string('a',32);var invocation=new string('b',32);
        var start=Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null,["container-recycle-archive:"+action,id,null]));
        Assert.Equal(new[]{"-n","/usr/local/libexec/whaledeck-privileged","container-file-recycle-archives",action,id},start.ArgumentList);
        var stop=Assert.IsType<System.Diagnostics.ProcessStartInfo>(method.Invoke(null,["container-recycle-archive:"+action,id,invocation]));Assert.Equal(new[]{"-n","/usr/local/libexec/whaledeck-privileged","container-file-recycle-archives-stop",id,invocation},stop.ArgumentList);Assert.False(start.UseShellExecute);Assert.Equal(2,start.Environment.Count);
    }
    [Fact]
    public async Task InvalidActionNeverStartsAProcess()=>await Assert.ThrowsAsync<ArgumentException>(()=>new FileTransferRunner().ContainerRecycleArchiveOnceAsync("shell","{}",default));
    [Fact]
    public void FullArchivePlanParsesWithinBoundedDepthWithoutRelaxingLegacyResponses()
    {
        var raw=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ContainerRecycleArchiveWire.Plan(ContainerRecycleArchivePolicyTests.Plan())));
        using var doc=FileTransferJson.Parse(raw,true);Assert.Equal(19,doc.RootElement.EnumerateObject().Count());Assert.ThrowsAny<JsonException>(()=>FileTransferJson.Parse(raw,false));
        var deep=Encoding.UTF8.GetBytes(new string('[',17)+"0"+new string(']',17));Assert.ThrowsAny<JsonException>(()=>FileTransferJson.Parse(deep,true));
    }
    [Fact]
    public async Task BindingMismatchCannotReachRootRunner()
    {
        var q=ContainerRecycleArchivePolicyTests.Request();var hash=ContainerRecycleArchiveBinding.Hash(q);q.Context.TraceId="changed-after-signing";
        var error=await Assert.ThrowsAsync<RpcException>(()=>new ContainerRecycleArchiveGrpcService(null!).Apply(q,new Context(ContainerRecycleArchiveBinding.Service+"Apply",null,hash)));Assert.Equal(StatusCode.PermissionDenied,error.StatusCode);
    }
    [Theory][InlineData("CONTAINER_RECYCLE_ARCHIVE_NOT_FOUND",StatusCode.NotFound)][InlineData("CONTAINER_RECYCLE_ARCHIVE_SOURCE_NOT_FOUND",StatusCode.FailedPrecondition)][InlineData("CONTAINER_RECYCLE_ARCHIVE_COPY_CAPACITY",StatusCode.ResourceExhausted)][InlineData("CONTAINER_RECYCLE_ARCHIVE_COPY_CHANGED",StatusCode.Aborted)]
    public void ExactAbsenceAndSourceLossCannotBeConfused(string code,StatusCode expected)=>Assert.Equal(expected,ContainerRecycleArchivePolicy.Failure(code).StatusCode);
    private sealed class Context : ServerCallContext
    {
        private readonly string _method;
        private readonly Metadata _headers = [];
        public Context(string method, string? token, string? hash)
        {
            _method = method;
            if (token is not null) _headers.Add("x-whaledeck-capability", token);
            if (hash is not null) _headers.Add(ContainerRecycleArchiveBinding.Header, hash);
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
