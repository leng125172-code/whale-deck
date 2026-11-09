using System.Text;
using System.Text.Json;
using WhaleDeck.Application.Development;

namespace WhaleDeck.UnitTests.Application;

public sealed class WorkspaceTestReportsTests
{
    [Fact]
    public async Task NestedJUnitCountsCasesAndNeverReturnsUntrustedContent()
    {
        const string input = """
            <testsuites tests="4" failures="1" errors="1" skipped="1">
              <testcase name="ordinary"/>
              <testsuite tests="3">
                <testcase name="secret-from-case"><failure message="secret-from-message">secret-from-stack</failure></testcase>
                <testsuite tests="2">
                  <testcase name="case"><error>secret-from-error</error></testcase>
                  <testcase name="case"><skipped/></testcase>
                </testsuite>
                <system-out>secret-from-stdout</system-out>
              </testsuite>
            </testsuites>
            """;
        var result = await Parse(input);
        Assert.Equal(WorkspaceTestReportState.Parsed, result.State);
        Assert.Equal(new WorkspaceTestCounts(1, 1, 1, 1, 0), result.Counts);
        Assert.Equal([new(2, WorkspaceTestOutcome.Failed), new(3, WorkspaceTestOutcome.Error)], result.Issues);
        Assert.Matches("^[a-f0-9]{64}$", result.ArtifactSha256!);
        Assert.DoesNotContain("secret-from", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("<testsuite/>", WorkspaceTestReportState.NoCases)]
    [InlineData("<testsuite tests='2'><testcase name='case'/></testsuite>", WorkspaceTestReportState.Inconsistent)]
    [InlineData("<testsuite tests='0'><testcase name='case'/></testsuite>", WorkspaceTestReportState.Inconsistent)]
    [InlineData("<testsuite><testcase name='case' status='unknown'/></testsuite>", WorkspaceTestReportState.Inconsistent)]
    [InlineData("<testsuite><testcase name='case' failure='only an attribute'/></testsuite>", WorkspaceTestReportState.Inconsistent)]
    [InlineData("<testsuite><testcase name='case'><failure xmlns='unknown'/></testcase></testsuite>", WorkspaceTestReportState.Inconsistent)]
    [InlineData("<testsuite><details><testcase name='case'/></details></testsuite>", WorkspaceTestReportState.Inconsistent)]
    [InlineData("<testsuite><testcase name='case'><skipped/><failure/></testcase></testsuite>", WorkspaceTestReportState.Invalid)]
    [InlineData("<testsuite><testcase name='case' status='notrun'><failure/></testcase></testsuite>", WorkspaceTestReportState.Invalid)]
    [InlineData("<testsuite><testcase name='case'/>", WorkspaceTestReportState.Invalid)]
    [InlineData("<testsuite><testcase/></testsuite>", WorkspaceTestReportState.Invalid)]
    [InlineData("<html><script>no</script></html>", WorkspaceTestReportState.Invalid)]
    [InlineData("<!DOCTYPE testsuite [<!ENTITY read SYSTEM 'file:///etc/passwd'>]><testsuite>&read;</testsuite>", WorkspaceTestReportState.Invalid)]
    public async Task IncompleteOrUnsafeDocumentsNeverReportParsed(string input, WorkspaceTestReportState state)
    {
        var result = await Parse(input);
        Assert.Equal(state, result.State);
    }

    [Theory]
    [InlineData("NotExecuted", "Failed", "2", "0", WorkspaceTestReportState.Parsed)]
    [InlineData("NotExecuted", "Failed", "2", "1", WorkspaceTestReportState.Inconsistent)]
    [InlineData("Mystery", "Failed", "2", "0", WorkspaceTestReportState.Inconsistent)]
    [InlineData("NotExecuted", "Error", "2", "0", WorkspaceTestReportState.Inconsistent)]
    [InlineData("NotExecuted", "Failed", "3", "0", WorkspaceTestReportState.Inconsistent)]
    public async Task TrxUsesCaseEvidenceAndValidatesSupportedSummary(
        string outcome, string summary, string executed, string timeout, WorkspaceTestReportState state)
    {
        var result = await Parse($"""
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult executionId="539dc038-d289-435c-9342-c0f7f9ff10fd" outcome="Passed"/>
                <UnitTestResult executionId="ee03c873-61a7-4857-9edc-ce2ad314a818" outcome="Failed"/>
                <UnitTestResult executionId="23393934-171f-4878-af7c-4012c9e452f0" outcome="{outcome}"/>
              </Results>
              <ResultSummary outcome="{summary}">
                <Counters total="3" executed="{executed}" passed="1" failed="1" error="0" notExecuted="0" timeout="{timeout}"/>
              </ResultSummary>
            </TestRun>
            """, WorkspaceTestFormat.Trx);
        Assert.Equal(state, result.State);
        Assert.Equal(3, result.Counts!.Total);
        Assert.Equal(new WorkspaceTestIssue(2, WorkspaceTestOutcome.Failed), result.Issues![0]);
    }

    [Fact]
    public async Task TrxRejectsDuplicateResultIdentity()
    {
        var result = await Parse("""
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult executionId="ee03c873-61a7-4857-9edc-ce2ad314a818" outcome="Passed"/>
                <UnitTestResult executionId="ee03c873-61a7-4857-9edc-ce2ad314a818" outcome="Passed"/>
              </Results>
              <ResultSummary outcome="Completed"><Counters total="2" passed="2" failed="0"/></ResultSummary>
            </TestRun>
            """, WorkspaceTestFormat.Trx);
        Assert.Equal(WorkspaceTestReportState.Invalid, result.State);
        Assert.Null(result.Counts);
    }

    [Fact]
    public async Task ReportsBudgetAndTruncationWithoutTurningPartialCasesIntoSuccess()
    {
        var cases = string.Concat(Enumerable.Repeat("<testcase name='case'><failure/></testcase>", 103));
        var result = await Parse($"<testsuite>{cases}</testsuite>");
        Assert.Equal(103, result.Counts!.Failed);
        Assert.Equal(100, result.Issues!.Count);
        Assert.True(result.IssuesTruncated);

        var nested = string.Concat(Enumerable.Repeat("<testsuite>", 40)) +
            string.Concat(Enumerable.Repeat("</testsuite>", 40));
        Assert.Equal(WorkspaceTestReportState.TooComplex, (await Parse(nested)).State);

        using var oversized = new MemoryStream(new byte[WorkspaceTestReports.MaxArtifactBytes + 1]);
        result = await WorkspaceTestReports.AnalyzeAsync(oversized, WorkspaceTestFormat.Trx, default);
        Assert.Equal(WorkspaceTestReportState.TooLarge, result.State);
        Assert.Null(result.ArtifactSha256);
        Assert.Equal(WorkspaceTestReports.MaxArtifactBytes + 1, oversized.Position);
    }

    [Fact]
    public async Task DistinguishesMissingAndUnsupportedAndPropagatesCancellation()
    {
        Assert.Equal(WorkspaceTestReportState.Missing,
            (await WorkspaceTestReports.AnalyzeAsync(null, WorkspaceTestFormat.Trx, default)).State);
        Assert.Equal(WorkspaceTestReportState.Unsupported,
            (await WorkspaceTestReports.AnalyzeAsync(null, (WorkspaceTestFormat)100, default)).State);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WorkspaceTestReports.AnalyzeAsync(null, WorkspaceTestFormat.Trx, new CancellationToken(true)));
    }

    private static async Task<WorkspaceTestReport> Parse(string xml, WorkspaceTestFormat format = WorkspaceTestFormat.JUnit)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);
        return await WorkspaceTestReports.AnalyzeAsync(stream, format, default);
    }
}
