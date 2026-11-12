using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace EmailSenderService.OpenApi;

/// <summary>
/// OpenAPI allows one operation per path and verb, while the send endpoint has two actions told
/// apart by <c>[Consumes]</c>. <see cref="Resolve"/> keeps the JSON action as the operation and
/// remembers the others; the filter then adds each one's form fields as another request body
/// media type, so the multipart variant shows up with its file field in the same operation.
/// </summary>
public sealed class ContentTypeVariantsOperationFilter : IOperationFilter
{
    private const string VariantsKey = "ContentTypeVariants";

    public static ApiDescription Resolve(IEnumerable<ApiDescription> descriptions)
    {
        var all = descriptions.ToList();
        var primary = all.FirstOrDefault(d => d.ParameterDescriptions.Any(p => p.Source == BindingSource.Body))
                      ?? all[0];
        primary.Properties[VariantsKey] = all.Where(d => d != primary).ToList();
        return primary;
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.ApiDescription.Properties.TryGetValue(VariantsKey, out var value)
            || value is not List<ApiDescription> variants
            || operation.RequestBody is not OpenApiRequestBody requestBody)
        {
            return;
        }

        foreach (var variant in variants)
        {
            var formFields = variant.ParameterDescriptions
                .Where(p => p.Source == BindingSource.Form || p.Source == BindingSource.FormFile)
                .ToList();

            if (formFields.Count == 0)
            {
                continue;
            }

            var schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema>(),
                Required = new HashSet<string>()
            };

            foreach (var field in formFields)
            {
                schema.Properties[field.Name] = context.SchemaGenerator.GenerateSchema(
                    field.Type, context.SchemaRepository, field.PropertyInfo(), field.ParameterInfo());

                // ApiExplorer does not flag [Required] form properties, so read the attribute.
                if (field.IsRequired || field.PropertyInfo()?.IsDefined(typeof(RequiredAttribute), inherit: true) == true)
                {
                    schema.Required.Add(field.Name);
                }
            }

            requestBody.Content ??= new Dictionary<string, OpenApiMediaType>();
            foreach (var format in variant.SupportedRequestFormats)
            {
                requestBody.Content[format.MediaType] = new OpenApiMediaType { Schema = schema };
            }
        }
    }
}
