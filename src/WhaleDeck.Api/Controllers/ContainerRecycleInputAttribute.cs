using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WhaleDeck.Api.Controllers;

/// <summary>Keep malformed recycle bodies bounded and private before ApiController's default model-state response.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ContainerRecycleInputAttribute : ActionFilterAttribute
{
    public ContainerRecycleInputAttribute() => Order = -3001;
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        context.Result = new BadRequestObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "文件回收请求字段缺失、重复或类型无效。",
            Extensions = { ["code"] = "CONTAINER_RECYCLE_INPUT_INVALID", ["traceId"] = context.HttpContext.TraceIdentifier }
        });
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ContainerRecycleQueryAttribute : ActionFilterAttribute
{
    public bool AllowTake { get; set; }
    public ContainerRecycleQueryAttribute() => Order = -3002;
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var query in context.HttpContext.Request.Query)
            if (!AllowTake || query.Key != "take" || query.Value.Count != 1 || query.Value[0] is not {} text ||
                !int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var take) || take is < 1 or > 50 || take.ToString(System.Globalization.CultureInfo.InvariantCulture) != text)
            {
                context.Result = new BadRequestObjectResult(new ProblemDetails { Status=400, Title="回收查询参数无效。", Extensions={ ["code"]="CONTAINER_RECYCLE_QUERY_INVALID", ["traceId"]=context.HttpContext.TraceIdentifier } });
                return;
            }
    }
}
