using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleDeck.Agent.Services;
using WhaleDeck.Contracts.Agent.V1;

namespace WhaleDeck.UnitTests.Agent;

public sealed class FileOperationPolicyTests
{
    private const string Id = "073e1d38e73f450a822640746147f35c";
    private static FileOperationCommand Command(FileOperationKind kind = FileOperationKind.CopyFile) => new()
    {
        Kind = kind, Source = kind == FileOperationKind.CreateDirectory ? null : new() { RootId = "whaledeck.files.shared", Path = "资料/源文件.txt" },
        Target = kind == FileOperationKind.SetPermissions ? null : new() { RootId = "whaledeck.files.shared", Path = "资料/目标.txt" },
        ExpectedVersion = kind == FileOperationKind.CreateDirectory ? "ABSENT" : new('A', 64),
        Mode = kind is FileOperationKind.CreateDirectory or FileOperationKind.SetPermissions ? "0750" : ""
    };
    private static FileOperationPlan Plan(FileOperationKind kind = FileOperationKind.CopyFile) => new()
    {
        Command = Command(kind), Fingerprint = new('B', 64), Sha256 = kind == FileOperationKind.CreateDirectory ? "" : new('C', 64), SizeBytes = kind == FileOperationKind.CreateDirectory ? 0 : 3,
        SourceIdentity = kind == FileOperationKind.CreateDirectory ? null : new() { Device = ulong.MaxValue, Inode = 9007199254740993, Mode = 0x81A4, OwnerUid = uint.MaxValue - 1,
            OwnerGid = 12345, LinkCount = 1, SizeBytes = 3, ModifiedAtUnixNs = -1 },
        SourceParent = kind == FileOperationKind.CreateDirectory ? null : new() { Device = ulong.MaxValue, Inode = 9007199254740994 },
        TargetParent = kind == FileOperationKind.SetPermissions ? null : new() { Device = 123, Inode = 9007199254740995 }
    };
    [Theory]
    [InlineData(FileOperationKind.CreateDirectory)][InlineData(FileOperationKind.CopyFile)]
    [InlineData(FileOperationKind.MoveFile)][InlineData(FileOperationKind.SetPermissions)]
    public void TypedPlanRoundTripPreservesExactUInt64AndNegativeNanoseconds(FileOperationKind kind)
    {
        var expected = Plan(kind); var input = FileOperationPolicy.PlanInput(expected);
        Assert.Equal(expected, FileOperationPolicy.Plan(input, expected.Command));
        Assert.Equal(7, input.GetProperty("command").EnumerateObject().Count());
        Assert.DoesNotContain("ActorSubject", input.GetRawText(), StringComparison.Ordinal);
        if (kind != FileOperationKind.CreateDirectory)
        {
            Assert.Equal(ulong.MaxValue, input.GetProperty("sourceIdentity")[0].GetUInt64());
            Assert.Equal(9007199254740993UL, input.GetProperty("sourceIdentity")[1].GetUInt64());
            Assert.Equal(-1L, input.GetProperty("sourceIdentity")[7].GetInt64());
        }
        using var command = JsonDocument.Parse(FileOperationPolicy.CommandJson(expected.Command));
        Assert.True(command.RootElement.TryGetProperty("sourceRootId", out _));
        Assert.False(command.RootElement.TryGetProperty("SourceRootId", out _));
    }
    [Theory]
    [InlineData("/etc/passwd")][InlineData("../outside")][InlineData("a//b")][InlineData("a/../b")][InlineData("a\\b")][InlineData("a\nb")]
    public void CommandsCannotEscapeRegisteredRoots(string path)
    {
        var value = Command(); value.Source.Path = path;
        Assert.Throws<ArgumentException>(() => FileOperationPolicy.Command(value));
        value = Command(); value.Target.Path = path;
        Assert.Throws<ArgumentException>(() => FileOperationPolicy.Command(value));
    }
    [Theory]
    [InlineData("4755")][InlineData("2755")][InlineData("1777")][InlineData("755")][InlineData("0999")]
    public void PermissionsRejectSpecialBitsAndAmbiguousOctal(string mode)
    {
        var value = Command(FileOperationKind.SetPermissions); value.Mode = mode;
        Assert.Throws<ArgumentException>(() => FileOperationPolicy.Command(value));
    }
    [Fact]
    public void UnknownKindsUnexpectedLocationsAndHumanContextAreRejected()
    {
        var value = Command(); value.Kind = (FileOperationKind)99;
        Assert.Throws<ArgumentException>(() => FileOperationPolicy.Command(value));
        value = Command(FileOperationKind.CreateDirectory); value.Source = new();
        Assert.Throws<ArgumentException>(() => FileOperationPolicy.Command(value));
        value = Command(FileOperationKind.SetPermissions); value.Target = new();
        Assert.Throws<ArgumentException>(() => FileOperationPolicy.Command(value));
        Assert.Throws<ArgumentException>(() => FileOperationPolicy.Actor(null));
        Assert.Throws<ArgumentException>(() => FileOperationPolicy.Actor(new() { ActorSubject = "admin\nother" }));
        FileOperationPolicy.Actor(new() { ActorSubject = "administrator" });
    }
    [Fact]
    public void PlansRejectDifferentTargetsMetadataShapeAndOversize()
    {
        var plan = Plan(); var expected = plan.Command.Clone(); expected.Target.Path = "other";
        Assert.Throws<InvalidOperationException>(() => FileOperationPolicy.Plan(FileOperationPolicy.PlanInput(plan), expected));
        plan.SourceIdentity.LinkCount = 2;
        Assert.Throws<ArgumentException>(() => FileOperationPolicy.PlanInput(plan));
        plan = Plan(); plan.SizeBytes = FileTransferPolicy.MaximumBytes + 1;
        Assert.Throws<ArgumentException>(() => FileOperationPolicy.PlanInput(plan));
        var root = JsonNode.Parse(FileOperationPolicy.PlanInput(Plan()).GetRawText())!;
        root["command"]!["sourceRootId"] = null;
        using var invalid = JsonDocument.Parse(root.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => FileOperationPolicy.Plan(invalid.RootElement, Command()));
        root["command"]!["sourceRootId"] = "whaledeck.files.shared"; root["unexpected"] = true;
        using var extra = JsonDocument.Parse(root.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => FileOperationPolicy.Plan(extra.RootElement, Command()));
    }
    private static JsonElement Receipt(FileOperationPlan plan, string state = "Completed") => JsonSerializer.SerializeToElement(new
    {
        operationId = Id, kind = FileOperationPolicy.Kind(plan.Command.Kind), state, fingerprint = plan.Fingerprint,
        version = state == "Completed" ? new string('D', 64) : "", sizeBytes = plan.SizeBytes, sha256 = plan.Sha256,
        sourceRetained = state == "Completed" && plan.Command.Kind == FileOperationKind.MoveFile, bindingCurrent = state == "Completed"
    });
    [Theory]
    [InlineData("Completed")][InlineData("Aborted")][InlineData("Unknown")]
    public void ReceiptsAreBoundToIdKindFingerprintSizeAndDigest(string state)
    {
        var plan = Plan(FileOperationKind.MoveFile);
        Assert.Equal(state, FileOperationPolicy.Receipt(Receipt(plan, state), Id, plan).State);
        Assert.Throws<InvalidOperationException>(() => FileOperationPolicy.Receipt(Receipt(plan, state), new('a', 32), plan));
        var other = plan.Clone(); other.Fingerprint = new('E', 64);
        Assert.Throws<InvalidOperationException>(() => FileOperationPolicy.Receipt(Receipt(plan, state), Id, other));
        var node = JsonNode.Parse(Receipt(plan, state).GetRawText())!; node["state"] = "Unknown"; node["bindingCurrent"] = true;
        using var malformed = JsonDocument.Parse(node.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => FileOperationPolicy.Receipt(malformed.RootElement, Id));
    }
}
