using Microsoft.OpenApi;

namespace Atlas.Onboarding.Api;

/// <summary>
/// OpenAPI document (built-in generator) plus Swagger UI on top of it, Development only. The bearer scheme makes the
/// UI's "Authorize" button accept the development tokens (<c>dev:customer:device-1</c>, <c>dev:staff:teller-12</c>).
/// </summary>
public static class OpenApiExtensions
{
    private const string DevTokenScheme = "DevToken";

    public static IServiceCollection AddAtlasOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Atlas Onboarding API";
            document.Info.Description =
                "Submit returns 202 and a status resource (ADR-0003). Authorize with a development token, without the 'Bearer' prefix: " +
                "dev:customer:device-1 (applications), dev:staff:teller-12 (branch), dev:service:accounts (internal).";

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[DevTokenScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "Development token: dev:{customer|staff|service}:{id}",
            };

            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(DevTokenScheme, document)] = [],
            });

            return Task.CompletedTask;
        }));

    public static WebApplication MapAtlasOpenApi(this WebApplication app)
    {
        app.MapOpenApi().AllowAnonymous();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "Atlas Onboarding API");
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "Atlas Onboarding API";
            });
        }

        return app;
    }
}
