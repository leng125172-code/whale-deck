using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Development;

namespace WhaleDeck.UnitTests.Application;

public sealed class WorkspaceCoverageReportsTests
{
    [Fact]
    public async Task CoberturaReconcilesClassRowsAndDoesNotRepeatMethodRows()
    {
        const string input = """
            <coverage lines-covered="1" lines-valid="2" branches-covered="1" branches-valid="2">
              <sources><source>/private/checkouts</source></sources>
              <packages><package name="Private"><classes><class name="C" filename="/private/file.cs">
                <methods><method name="hidden"><lines><line number="1" hits="3"/></lines></method></methods>
                <lines>
                  <line number="1" hits="3" branch="True" condition-coverage="50% (1/2)"/>
                  <line number="2" hits="0" branch="False"/>
                </lines>
              </class></classes></package></packages>
            </coverage>
            """;
        var result = await Parse(input, WorkspaceTestFormat.Cobertura);
        Assert.Equal(WorkspaceTestReportState.Parsed, result.State);
        Assert.Equal(new WorkspaceCoverageCounts(new(1, 2), new(1, 2)), result.Coverage);
        Assert.Null(result.Counts);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<coverage lines-covered='0' lines-valid='0'/>", WorkspaceTestReportState.NoCoverage)]
    [InlineData("<coverage lines-covered='1' lines-valid='1'/>", WorkspaceTestReportState.Inconsistent)]
    [InlineData("<coverage lines-covered='2' lines-valid='1'/>", WorkspaceTestReportState.Invalid)]
    [InlineData("<coverage lines-covered='0' lines-valid='0' branches-covered='0'/>", WorkspaceTestReportState.Invalid)]
    [InlineData("<coverage lines-covered='-1' lines-valid='0'/>", WorkspaceTestReportState.Invalid)]
    [InlineData("<!DOCTYPE coverage [<!ENTITY data SYSTEM 'file:///etc/passwd'>]><coverage>&data;</coverage>", WorkspaceTestReportState.Invalid)]
    public async Task CoberturaWithholdsEmptyOrUnreconciledMetrics(string input, WorkspaceTestReportState state)
    {
        var result = await Parse(input, WorkspaceTestFormat.Cobertura);
        Assert.Equal(state, result.State);
        Assert.Null(result.Coverage);
    }

    [Fact]
    public async Task LcovCombinesFilesAndCountsOnlyExecutedLinesAndBranches()
    {
        const string input = """
            TN:
            SF:/private/target1.cjs
            FN:1,label
            FNDA:1,label
            FNF:1
            FNH:1
            BRDA:1,0,0,1
            BRDA:1,1,0,-
            BRF:2
            BRH:1
            DA:1,1,checksum
            DA:2,0
            LH:1
            LF:2
            end_of_record
            SF:/private/target2.cjs
            BRF:0
            BRH:0
            DA:3,1
            LH:1
            LF:1
            end_of_record
            """;
        var result = await Parse(input, WorkspaceTestFormat.Lcov);
        Assert.Equal(WorkspaceTestReportState.Parsed, result.State);
        Assert.Equal(new WorkspaceCoverageCounts(new(2, 3), new(1, 2)), result.Coverage);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LcovDoesNotPresentPartialBranchInstrumentationAsWholeArtifactCoverage()
    {
        const string input = """
            SF:one
            BRDA:1,0,0,1
            BRF:1
            BRH:1
            DA:1,1
            LH:1
            LF:1
            end_of_record
            SF:two
            DA:1,1
            LH:1
            LF:1
            end_of_record
            """;
        var result = await Parse(input, WorkspaceTestFormat.Lcov);
        Assert.Equal(WorkspaceTestReportState.Parsed, result.State);
        Assert.Equal(new WorkspaceCoverageCounts(new(2, 2), null), result.Coverage);
    }

    [Theory]
    [InlineData("TN:\n", WorkspaceTestReportState.NoCoverage)]
    [InlineData("SF:file\nDA:1,1\nLF:1\nLH:0\nend_of_record", WorkspaceTestReportState.Inconsistent)]
    [InlineData("SF:file\nDA:1,1\nLF:1\nLH:1\nBRF:1\nBRH:1\nend_of_record", WorkspaceTestReportState.Inconsistent)]
    [InlineData("SF:file\nDA:1,1\nLF:1\nLH:1", WorkspaceTestReportState.Invalid)]
    [InlineData("SF:file\nDA:1,1\nDA:1,0\nLF:2\nLH:1\nend_of_record", WorkspaceTestReportState.Invalid)]
    [InlineData("SF:file\nLF:0\nLH:0\nend_of_record\nSF:file\nLF:0\nLH:0\nend_of_record", WorkspaceTestReportState.Invalid)]
    public async Task LcovWithholdsMalformedOrContradictoryResults(string input, WorkspaceTestReportState state)
    {
        var result = await Parse(input, WorkspaceTestFormat.Lcov);
        Assert.Equal(state, result.State);
        Assert.Null(result.Coverage);
    }

    [Fact]
    public async Task LcovBoundsRecordsAndRejectsInvalidUtf8()
    {
        var oversizedLine = await Parse("SF:" + new string('x', 8192), WorkspaceTestFormat.Lcov);
        Assert.Equal(WorkspaceTestReportState.TooComplex, oversizedLine.State);
        using var invalid = new MemoryStream([255]);
        var undecodable = await WorkspaceTestReports.AnalyzeAsync(invalid, WorkspaceTestFormat.Lcov, default);
        Assert.Equal(WorkspaceTestReportState.Invalid, undecodable.State);
    }

    private static async Task<WorkspaceTestReport> Parse(string text, WorkspaceTestFormat format)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await WorkspaceTestReports.AnalyzeAsync(stream, format, default);
    }
}
