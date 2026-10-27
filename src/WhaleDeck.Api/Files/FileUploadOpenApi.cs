using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace WhaleDeck.Api.Files;

/// <summary>Raw upload streams have no MVC body parameter from which a binary schema could be inferred.</summary>
public static class FileUploadOpenApi
{
    public static Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.HttpMethod == "PUT" &&
            context.Description.ActionDescriptor.RouteValues.TryGetValue("controller", out var controller) && controller is "Files" or "ContainerUploads" &&
            context.Description.ActionDescriptor.RouteValues.TryGetValue("action", out var action) && action == "Content")
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/octet-stream"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } }
                }
            };
        }
        return Task.CompletedTask;
    }
}
