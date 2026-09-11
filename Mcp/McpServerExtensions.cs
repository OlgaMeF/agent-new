using MB.ComTools.Apps.Setup.Mcp.Resources;
using MB.ComTools.Apps.Setup.Mcp.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;

namespace MB.ComTools.Apps.Setup.Mcp;

public static class McpServerExtensions
{
    public const string McpBearerScheme = "McpBearer";

    /// <summary>
    /// Registers the MCP server for learn-skills.
    /// Call from Program.cs: builder.Services.AddLearnSkillsMcpServer(builder.Configuration)
    /// </summary>
    public static IServiceCollection AddLearnSkillsMcpServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var mcpOptions = configuration
            .GetSection(McpOptions.SectionName)
            .Get<McpOptions>() ?? new McpOptions();

        services.AddSingleton(mcpOptions);

        if (!mcpOptions.Enabled)
            return services;

        var authority = configuration["OidcService:OAuthAuthority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException(
                "OidcService:OAuthAuthority is required when the MCP endpoint is enabled.");
        }

        var audience = string.IsNullOrWhiteSpace(mcpOptions.BearerAudience)
            ? configuration["OidcService:ClientId"]
            : mcpOptions.BearerAudience;

        services
            .AddAuthentication()
            .AddJwtBearer(McpBearerScheme, options =>
            {
                options.Authority = authority;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidateAudience = !string.IsNullOrWhiteSpace(audience),
                    ValidAudience = audience
                };
            });

        services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<McpCourseTools>()
            .WithTools<McpProfileTools>()
            .WithTools<McpUserTools>()
            .WithTools<McpCourseResource>()
            .WithTools<McpProfileResource>()
            .WithTools<McpSupportingResources>();

        services.AddMemoryCache(); // ensure IMemoryCache is registered (safe to call multiple times)
        services.AddSingleton<McpCacheService>();

        return services;
    }

    /// <summary>
    /// Adds the MCP API key middleware and maps the /mcp route.
    /// Call AFTER app.UseAuthorization().
    /// </summary>
    public static WebApplication UseLearnSkillsMcp(this WebApplication app)
    {
        var mcpOptions = app.Services.GetRequiredService<McpOptions>();

        if (!mcpOptions.Enabled)
            return app;

        app.UseMcpApiKeyAuth();  // Task 02 middleware
        app.MapMcp("/mcp");

        return app;
    }
}
