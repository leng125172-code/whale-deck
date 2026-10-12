using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class HostProcessSourceTests
{
    private const string RequiredStatus = "Name:\tworker process\nPPid:\t1\nUid:\t1000\t1000\t1000\t1000\nState:\tS (sleeping)\nThreads:\t8\n";

    [Fact]
    public void FixedStatusFieldsSupplyNameAndMemoryWithoutCommandLineOrEnvironment()
    {
        using var reader = new StringReader(RequiredStatus + "VmRSS:\t12 kB\nVmSize:\t32 kB\nCmdline:\tPRIVATE_TEST_VALUE\nEnviron:\tPRIVATE_TEST_VALUE\n");
        var value = HostProcessSource.ParseStatus(reader);
        Assert.Equal("worker process", value.Name);
        Assert.Equal(1, value.ParentPid);
        Assert.Equal(1000U, value.UserId);
        Assert.Equal("S", value.State);
        Assert.Equal(8, value.ThreadCount);
        Assert.Equal(12 * 1024, value.MemoryBytes);
        Assert.Equal(32 * 1024, value.VirtualMemoryBytes);
        Assert.DoesNotContain("PRIVATE_TEST_VALUE", value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KernelThreadsCanOmitAddressSpaceFields()
    {
        using var reader = new StringReader(RequiredStatus);
        var value = HostProcessSource.ParseStatus(reader);
        Assert.Equal(0, value.MemoryBytes);
        Assert.Equal(0, value.VirtualMemoryBytes);
    }

    [Theory]
    [InlineData("VmRSS:\t-1 kB\n")]
    [InlineData("VmRSS:\tinvalid kB\n")]
    [InlineData("VmSize:\t18446744073709551615 kB\n")]
    public void InvalidOrOverflowingMemoryIsNotReportedAsValid(string extra)
    {
        using var reader = new StringReader(RequiredStatus + extra);
        Assert.Throws<IOException>(() => HostProcessSource.ParseStatus(reader));
    }

    [Fact]
    public void IncompleteOrOverBoundStatusIsRejected()
    {
        using var missing = new StringReader("Name:\tworker\nState:\tS\nThreads:\t1\n");
        Assert.Throws<IOException>(() => HostProcessSource.ParseStatus(missing));
        using var overBound = new StringReader(string.Concat(Enumerable.Repeat("Ignored:\tvalue\n", 128)) + RequiredStatus);
        Assert.Throws<IOException>(() => HostProcessSource.ParseStatus(overBound));
    }
}
