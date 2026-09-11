using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;

namespace MB.ComTools.Apps.Setup.Mcp;

/// <summary>
/// Enforces API key authentication for requests to the /mcp endpoint.
/// Clients must supply the key in the X-MCP-Key header.
/// Valid bearer tokens use the dedicated MCP bearer scheme (user-context tools).
/// </summary>
public class McpApiKeyMiddleware
{
    private const string ApiKeyHeader = "X-MCP-Key";
    private readonly RequestDelegate _next;
    private readonly McpOptions _options;
    private readonly ILogger<McpApiKeyMiddleware> _logger;
    private readonly IAuthenticationService _authenticationService;

    public McpApiKeyMiddleware(
        RequestDelegate next,
        McpOptions options,
        ILogger<McpApiKeyMiddleware> logger,
        IAuthenticationService authenticationService)
    {
        _next = next;
        _options = options;
        _logger = logger;
        _authenticationService = authenticationService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only guard the /mcp path
        if (!context.Request.Path.StartsWithSegments("/mcp"))
        {
            await _next(context);
            return;
        }

        // If MCP is disabled, return 503
        if (!_options.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("MCP endpoint is disabled.");
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var result = await _authenticationService.AuthenticateAsync(
                context,
                McpServerExtensions.McpBearerScheme);

            if (result.Succeeded && result.Principal is not null)
            {
                context.User = result.Principal;
                await _next(context);
                return;
            }

            _logger.LogWarning("MCP request with invalid bearer token from {IP}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid bearer token.");
            return;
        }

        if (context.Request.Headers.ContainsKey("Authorization"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Use a bearer token or X-MCP-Key.");
            return;
        }

        // Require X-MCP-Key for machine clients
        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey)
            || string.IsNullOrWhiteSpace(providedKey))
        {
            _logger.LogWarning("MCP request without API key from {IP}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing X-MCP-Key header.");
            return;
        }

        // Constant-time comparison to prevent timing attacks
        if (!CryptographicEquals(providedKey!, _options.ApiKey))
        {
            _logger.LogWarning("MCP request with invalid API key from {IP}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid API key.");
            return;
        }

        await _next(context);
    }

    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var result = 0;
        for (var i = 0; i < a.Length; i++)
            result |= a[i] ^ b[i];
        return result == 0;
    }
}

public static class McpMiddlewareExtensions
{
    public static IApplicationBuilder UseMcpApiKeyAuth(this IApplicationBuilder app)
        => app.UseMiddleware<McpApiKeyMiddleware>();
}
