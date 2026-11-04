using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WhaleDeck.Application.Models;
using WhaleDeck.Domain.Entities;
using WhaleDeck.Infrastructure.Persistence;

namespace WhaleDeck.UnitTests.Application;

/// <summary>Audit projection and awaited save boundary only; no database persistence claim.</summary>
public sealed class ContainerRecursiveAclHistoryAuditTests
{
    private sealed class SaveGate(bool fail=false):SaveChangesInterceptor
    {
        public int Calls;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,InterceptionResult<int> result,CancellationToken cancellationToken=default)
        {Calls++;if(fail)throw new InvalidOperationException("audit unavailable");return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task PrivateMetadataNeverEntersAuditAndEachPageAwaitsSave(bool read)
    {
        var gate=new SaveGate();await using var db=new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=unused_projection_fixture").AddInterceptors(gate).Options);
        var actor=new TerminalActor("subject",null,[],"binding","trace",null);var location=new ContainerFileLocationDto(new('a',64),"777:2026-11-04T01:02:03Z","work/private-name");
        var summary=new ContainerRecursiveAclHistorySummaryDto(new('a',32),new('B',64),0,"666:2026-11-03T01:02:03Z","access",new('C',64),2,1,1,0);
        var audit=new ContainerRecursiveAclHistoryAudit(db);
        if(read)await audit.ReadAsync(actor,location,summary.CaptureId,summary.HistoryDigest,0,1,new(location,summary,0,[new("child.bin","File",416,[new("user_obj",null,6),new("group_obj",null,4),new("other",null,0)],[])],false,1),default);
        else await audit.ListAsync(actor,location,"",25,"",new(location,new('D',64),[summary],"",1,0,true),default);
        Assert.Equal(1,gate.Calls);var row=Assert.Single(db.ChangeTracker.Entries<AuditEvent>()).Entity;
        Assert.Equal("containers.files.recursive-acl-history-"+(read?"read":"list"),row.Action);Assert.Equal(actor.Subject,row.ActorSubject);Assert.Equal(location.ContainerId,row.TargetId);
        foreach(var raw in new[]{"private-name",location.InstanceId,"child.bin","accessAcl","owner","sourceIdentity","plan"})Assert.DoesNotContain(raw,row.DetailJson);
        using var json=JsonDocument.Parse(Assert.IsType<string>(row.DetailJson));var value=json.RootElement;
        Assert.Equal(64,value.GetProperty("pathHash").GetString()!.Length);Assert.Equal(1,value.GetProperty("returnedCount").GetInt32());
        if(read){Assert.Equal(0,value.GetProperty("offset").GetInt32());Assert.Equal(1,value.GetProperty("take").GetInt32());Assert.Equal(summary.HistoryDigest,value.GetProperty("historyDigest").GetString());Assert.Equal(64,value.GetProperty("captureHash").GetString()!.Length);}
        else Assert.Equal(1,value.GetProperty("scannedCount").GetInt32());
    }
    [Fact]public async Task AuditStorageFailureIsNotConvertedToSuccess()
    {
        var gate=new SaveGate(true);await using var db=new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql("Host=127.0.0.1;Database=unused_projection_fixture").AddInterceptors(gate).Options);
        var location=new ContainerFileLocationDto(new('a',64),"777:2026-11-04T01:02:03Z","work/private-name");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new ContainerRecursiveAclHistoryAudit(db).ListAsync(new("subject",null,[],"binding","trace",null),location,"",25,"",new(location,new('D',64),[],"",0,0,true),default));
        Assert.Equal(1,gate.Calls);
    }
}
