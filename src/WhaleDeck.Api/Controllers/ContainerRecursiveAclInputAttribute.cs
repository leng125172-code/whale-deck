using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WhaleDeck.Api.Controllers;

/// <summary>Keep malformed recursive ACL bodies bounded and private before ApiController's default model-state response.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ContainerRecursiveAclInputAttribute : ActionFilterAttribute
{
    public ContainerRecursiveAclInputAttribute() => Order = -3001;
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        context.Result = new BadRequestObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "递归ACL请求字段缺失、重复或类型无效。",
            Extensions = { ["code"] = "CONTAINER_RECURSIVE_ACL_INPUT_INVALID", ["traceId"] = context.HttpContext.TraceIdentifier }
        });
    }
}
