using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WhaleDeck.Api.Controllers;

/// <summary>Keep malformed recycle bodies bounded and private before ApiController's default model-state response.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ContainerRecycleArchiveInputAttribute : ActionFilterAttribute
{
    public ContainerRecycleArchiveInputAttribute() => Order = -3001;
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        context.Result = new BadRequestObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "归档复制恢复请求字段缺失、重复或类型无效。",
            Extensions = { ["code"] = "CONTAINER_RECYCLE_ARCHIVE_INPUT_INVALID", ["traceId"] = context.HttpContext.TraceIdentifier }
        });
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ContainerRecycleArchiveQueryAttribute : ActionFilterAttribute
{
    public bool AllowTake { get; set; }
    public bool AllowAfter { get; set; }
    public ContainerRecycleArchiveQueryAttribute() => Order = -3002;
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var query in context.HttpContext.Request.Query)
        {
            var text = query.Value.Count == 1 ? query.Value[0] : null;
            var valid = AllowTake && query.Key == "take" && int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var take)
                && take is >= 1 and <= 50 && take.ToString(System.Globalization.CultureInfo.InvariantCulture) == text;
            valid |= AllowAfter && query.Key == "after" && Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty && id.ToString("D") == text;
            if (!valid) { context.Result = new BadRequestObjectResult(new ProblemDetails { Status=400, Title="归档恢复查询参数无效。", Extensions={ ["code"]="CONTAINER_RECYCLE_ARCHIVE_QUERY_INVALID", ["traceId"]=context.HttpContext.TraceIdentifier } }); return; }
        }
    }
}
