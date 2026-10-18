using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.BuildingBlocks.Security;

/// <summary>
/// Development-only bearer tokens of the form <c>dev:{customer|staff|service}:{id}</c>.
/// Stands in for OIDC so that every request still carries an individual identity into the access log.
/// </summary>
public sealed class DevelopmentTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevToken";

    private const string BearerPrefix = "Bearer ";
    private static readonly HashSet<string> ActorTypes = ["customer", "staff", "service"];

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var parts = header[BearerPrefix.Length..].Trim().Split(':', 3);
        if (parts.Length != 3 || parts[0] != "dev" || !ActorTypes.Contains(parts[1]) || string.IsNullOrWhiteSpace(parts[2]))
        {
            return Task.FromResult(AuthenticateResult.Fail("Expected a development token 'dev:{customer|staff|service}:{id}'."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, parts[2]),
                new Claim(AtlasAuthentication.ActorTypeClaim, parts[1]),
            ],
            SchemeName);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
