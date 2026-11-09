using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhaleDeck.Application.Development;

public static class WorkspaceRunReportExport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    public static byte[] ToJson(WorkspaceRunReport report) => JsonSerializer.SerializeToUtf8Bytes(report, Json);

    public static string ToMarkdown(WorkspaceRunReport report)
    {
        var output = new StringBuilder();
        output.Append("# 开发空间运行报告\n\n");
        Field(output, "报告版本", Number(report.SchemaVersion));
        Field(output, "工作空间", report.WorkspaceId.ToString("D"));
        Field(output, "运行", report.RunId.ToString("D"));
        Field(output, "任务", report.JobId.ToString("D"));
        Field(output, "跟踪 ID", report.TraceId);
        Field(output, "执行主体", report.ActorSubject);
        Field(output, "开始 UTC", Time(report.StartedAtUtc));
        Field(output, "报告结算 UTC", Time(report.FinalizedAtUtc));
        Field(output, "保留至 UTC", Time(report.RetainUntilUtc));
        Field(output, "运行状态", report.Outcome.ToString());
        Field(output, "运行退出码", Number(report.ExitCode));

        output.Append("\n## 源码与环境\n\n");
        Field(output, "源码指纹", report.Source.Fingerprint);
        Field(output, "Git 提交", report.Source.GitCommit);
        Field(output, "工作树有未提交修改", Fact(report.Source.Dirty));
        Field(output, "运行期间源码变化", Fact(report.Source.ChangedDuringRun));
        Field(output, "环境摘要", report.Environment.Digest);
        Field(output, "环境版本", Number(report.Environment.Version));
        foreach (var tool in report.Environment.Tools)
            Field(output, "工具 " + Code(tool.Name), tool.Version);

        output.Append("\n## 步骤与测试\n");
        if (report.Steps.IsEmpty) output.Append("\n暂无步骤记录。\n");
        foreach (var step in report.Steps)
        {
            output.Append("\n### ").Append(Code(step.Key)).Append("\n\n");
            Field(output, "步骤 ID", step.StepId.ToString("D"));
            Field(output, "步骤状态", step.Outcome.ToString());
            Field(output, "开始 UTC", Time(step.StartedAtUtc));
            Field(output, "结束 UTC", Time(step.FinishedAtUtc));
            Field(output, "耗时毫秒", step.DurationMilliseconds?.ToString("0.####", CultureInfo.InvariantCulture));
            Field(output, "退出码", Number(step.ExitCode));

            foreach (var test in step.Tests)
            {
                output.Append("\n#### 测试工件 ").Append(Code(test.ArtifactId.ToString("D"))).Append("\n\n");
                Field(output, "格式", test.Format.ToString());
                Field(output, "工件核对", test.Integrity.ToString());
                if (test.Result is not { } result) continue;
                Field(output, "解析状态", result.State.ToString());
                Field(output, "工件 SHA256", result.ArtifactSha256);
                if (result.Counts is not { } counts) continue;
                Field(output, "观测用例", Number(counts.Total));
                Field(output, "通过", Number(counts.Passed));
                Field(output, "失败", Number(counts.Failed));
                Field(output, "错误", Number(counts.Errors));
                Field(output, "跳过", Number(counts.Skipped));
                Field(output, "其他", Number(counts.Other));
                foreach (var issue in result.Issues ?? [])
                    Field(output, "问题用例 " + Number(issue.CaseOrdinal), issue.Outcome.ToString());
                if (result.IssuesTruncated) output.Append("- 问题用例列表已截断。\n");
            }
        }

        return output.ToString();
    }

    private static string? Time(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);
    private static string? Number(long? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static string? Fact(bool? value) => value is true ? "是" : value is false ? "否" : null;

    private static void Field(StringBuilder output, string label, string? value) =>
        output.Append("- ").Append(label).Append(' ').Append(value is null ? "未知" : Code(value)).Append('\n');

    private static string Code(string value)
    {
        var fenceLength = 1;
        var consecutive = 0;
        foreach (var character in value)
        {
            consecutive = character == '`' ? consecutive + 1 : 0;
            fenceLength = Math.Max(fenceLength, consecutive + 1);
        }
        var fence = new string('`', fenceLength);
        return fence + " " + value + " " + fence;
    }
}
