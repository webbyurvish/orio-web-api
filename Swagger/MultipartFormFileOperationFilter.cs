using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PKeetDashboard.API.Swagger;

/// <summary>
/// Fixes Swagger for multipart/form-data file upload endpoints.
/// Adds RequestBody with file + title for the resume upload action.
/// </summary>
public class MultipartFormFileOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var controller = context.ApiDescription.ActionDescriptor.RouteValues["controller"];
        var name = context.ApiDescription.ActionDescriptor.RouteValues["action"];
        if (string.Equals(controller, "Resumes", StringComparison.OrdinalIgnoreCase) == false ||
            string.Equals(name, "Upload", StringComparison.OrdinalIgnoreCase) == false)
            return;

        operation.RequestBody = new OpenApiRequestBody
        {
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, OpenApiSchema>
                        {
                            ["file"] = new OpenApiSchema
                            {
                                Type = "string",
                                Format = "binary",
                                Description = "PDF resume file"
                            },
                            ["title"] = new OpenApiSchema
                            {
                                Type = "string",
                                Description = "Resume title (optional, defaults to filename)"
                            }
                        },
                        Required = new HashSet<string> { "file" }
                    }
                }
            }
        };
        operation.Parameters?.Clear();
    }
}
