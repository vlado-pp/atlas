using System.Text.Json.Serialization;
using Atlas.ProviderStubs;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KestrelServerOptions>(o => o.Limits.MaxRequestBodySize = 40 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddOptions<StubRulesOptions>().Bind(builder.Configuration.GetSection(StubRulesOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RuleBook>();
builder.Services.AddSingleton<CaseStore>();

var app = builder.Build();

app.MapGet("/", () => Results.Text(
    "Atlas provider stubs: IDNow at /idnow, World-Check at /refinitiv, control API at /_control (see README)."));
app.MapIdNow();
app.MapWorldCheck();
app.MapControl();

await app.RunAsync();
