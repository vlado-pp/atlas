using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Atlas.BuildingBlocks.Observability;

public static class LoggingExtensions
{
    /// <summary>
    /// Structured logging to the console and Seq. Log events carry identifiers (ApplicationId, Market) and never
    /// personal data; request bodies are never logged.
    /// </summary>
    public static WebApplicationBuilder AddAtlasLogging(this WebApplicationBuilder builder, string serviceName)
    {
        var seqUrl = builder.Configuration["Seq:ServerUrl"] ?? "http://127.0.0.1:5341";

        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.Seq(seqUrl, formatProvider: CultureInfo.InvariantCulture));

        return builder;
    }
}
