using Hangfire;
using Mapster;
using MB.ComTools.Apps.Api;
using MB.ComTools.Apps.Data;
using MB.ComTools.Apps.DataServicesUI.Extensions;
using MB.ComTools.Apps.Import;
using MB.ComTools.Apps.Infrastructure.Extensions;
using MB.ComTools.Apps.Infrastructure.Middleware;
using MB.ComTools.Apps.Setup;
using MB.ComTools.Apps.Setup.Configuration;
using MB.ComTools.Apps.Setup.Mcp;
using MB.Core.Types.Contracts;
using MB.Core.Types.Identity;
using MB.ComTools.Apps.Content.Services;
using MB.ComTools.Apps.Setup.Mcp.Tools;
using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;
using Serilog;
var jsonAssets = File.ReadAllText("Configuration/appRoutes.json");
var routes = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonAssets);

var jsonPages = File.ReadAllText("Configuration/pageRoutes.json");
var pageRoutes = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonPages);

var builder = WebApplication.CreateBuilder(args);

// Read S3 credentials from VCAP_SERVICES when running inside Cloud Foundry.
// This is a no-op locally (VCAP_SERVICES is not set in development).
builder.Configuration.AddVcapS3Configuration();

// Read PostgreSQL credentials from VCAP_SERVICES when running inside Cloud Foundry.
// Sets ConnectionStrings:PostgresDbConnection and ConnectionStrings:DefaultConnection.
// This is a no-op locally (VCAP_SERVICES is not set in development).
builder.Configuration.AddVcapDbConfiguration();

builder.Configuration.AddVcapS3Configuration();
builder.Configuration.AddVcapDbConfiguration();

//GenAi Provider Configuration
builder.Configuration.AddVcapGenAiConfiguration("GenAI");

var configuration = builder.Configuration;

// Configuration
TypeAdapterConfig.GlobalSettings.EnableJsonMapping();
builder.Services.AddContractsMapping();
builder.Services.AddSecurityServices();
builder.Services.AddApplicationOptions(configuration);
builder.Services.AddServerOptions(configuration);
builder.Services.AddOptionsValidation();

// Web Services
builder.Services.AddAppMvc(pageRoutes);
builder.Services.AddAppCompression();
builder.Services.AddHttpContextAccessor();

// Logging
builder.Logging.ClearProviders();
builder.Host.UseSerilog(
    (context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();
    }
);

builder.Services.AddAppLogging();

// Identity & Authentication
builder
    .Services.AddDefaultIdentity<AppUser>(options => { })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAppAuthentication(configuration);
builder.Services.AddOpenIdConnectAccessTokenManagement();
builder.Services.AddAuthorizationHandlers();

// Infrastructure & Background Services
builder.Services.AddInfrastructureServices();
builder.Services.AddHangfireServices(configuration);

// Only run Hangfire background jobs in non-local environments (production/staging)
// Check for localhost to disable jobs during development
var isLocalEnvironment = builder.Configuration.GetValue<string>("ASPNETCORE_ENVIRONMENT") == "Development"
    || builder.Configuration.GetConnectionString("DefaultConnection")?.Contains("localhost") == true;

if (!isLocalEnvironment)
{
    builder.Services.AddHangfireServer();
}

builder.Services.AddMemoryCache();
builder.Services.AddResponseCaching();

// Application Services
builder.Services.AddContentServices();
builder.Services.AddIntegrationServices(configuration);
builder.Services.AddHomeDataServices();
builder.Services.AddSocialServices();

// UI-Optimized Services (Performance-optimierte API Services)
builder.Services.AddUIServices();

// Real-time & Caching
builder.Services.AddRealTimeServices();

// MCP Server
builder.Services.AddLearnSkillsMcpServer(builder.Configuration);
builder.Services.AddScoped<McpCourseTools>();
builder.Services.AddScoped<McpProfileTools>();

//MCP Services
builder.Services.AddHttpClient<McpClientService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();
    // Trust the ASP.NET Core dev certificate for loopback MCP calls in development.
    if (builder.Environment.IsDevelopment())
    {
        handler.ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    }
    return handler;
});

// API Documentation
builder.Services.AddSwaggerServices();

// Database Configuration
string connection =
    configuration["ConnectionStrings:PostgresDbConnection"]
    ?? throw new ArgumentNullException(
        "ConnectionStrings:PostgresDbConnection",
        "PostgresDbConnection configuration is missing or empty."
    );

var poolSettings = PostgresConnectionConfig.ReadPoolSettingsFromConfig(configuration);
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseOptimizedNpgsql(
        connection,
        builder.Environment.IsDevelopment(),
        poolSettings.MaxPoolSize,
        poolSettings.MinPoolSize
    );
});

// Direct service registration instead of HomeSignalR extensions
builder.Services.AddScoped<System.Data.IDbConnection>(provider =>
{
    var connectionString = configuration.GetConnectionString("PostgresDbConnection");
    return new Npgsql.NpgsqlConnection(connectionString);
});
builder.Services.AddScoped<
    MB.ComTools.Apps.Features.HomeLearn.Services.ISocialDataService,
    MB.ComTools.Apps.Features.HomeLearn.Services.SocialDataService
>();

//Agent Services
builder.Services.AddHttpClient<GenAiService>((sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    // Support both PascalCase and snake_case config keys
    var apiBase = configuration["GenAi:ApiBase"] ?? configuration["GenAi:api_base"];

    if (!string.IsNullOrWhiteSpace(apiBase))
    {
        client.BaseAddress = new Uri(apiBase.TrimEnd('/') + "/");
    }

    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddLearnSkillsAgent();

// External API Clients
builder.Services.AddApiClients(configuration);
builder.Services.AddLinkedInClient(configuration);

// External Video Services
builder.Services.AddScoped<
    MB.Core.Types.Interfaces.IExternalVideoService,
    MB.ComTools.Apps.Content.Services.ExternalVideoService
>();

builder.Services.AddScoped<
    MB.ComTools.Apps.Infrastructure.Services.IHangfireCleanupService,
    MB.ComTools.Apps.Infrastructure.Services.HangfireCleanupService
>();

// Token Management & HTTP Clients
builder.Services.AddTokenManagement(configuration);
builder.Services.AddJiveHttpClient(configuration);

// Server Configuration
builder.ConfigureKestrelServer();

// Security & CORS
builder.Services.AddCorsPolicies();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseSecurityHeaders(app.Environment);

// Allow iframe embedding (for maintenance mode fallback catalog)
app.Use(async (context, next) =>
{
    context.Response.Headers.Remove("X-Frame-Options");
    context.Response.Headers.Append("Content-Security-Policy", "frame-ancestors *");
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseResponseCompression();
}

// Static Files with Caching
app.UseStaticFiles(
    new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            var path = ctx.Context.Request.Path.Value?.ToLowerInvariant();

            if (
                path != null
                && (
                    path.Contains("/assets/")
                    || path.Contains("/dist/")
                    || path.EndsWith(".js") && path.Contains("-")
                    || path.EndsWith(".css") && path.Contains("-")
                )
            )
            {
                ctx.Context.Response.Headers["Cache-Control"] =
                    "public, max-age=31536000, immutable";
            }
            else if (path != null && (path.EndsWith(".js") || path.EndsWith(".css")))
            {
                ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=86400";
            }
        },
    }
);

app.UseViteDevelopmentProxy(app.Environment);

if (app.Environment.IsDevelopment())
{
    app.UseCors("Development");
}
else
{
    app.UseCors("IntranetPolicy");
}

// Use Serilog's built-in request logging for better performance
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, elapsed, ex) =>
        ex != null ? Serilog.Events.LogEventLevel.Error
        : httpContext.Response.StatusCode > 499 ? Serilog.Events.LogEventLevel.Error
        : Serilog.Events.LogEventLevel.Information;
});

app.UseResponseCaching();
app.UseCookiePolicy();

app.UseRouting();

// Subdomain Redirect Middleware - redirects retired subdomains (ecards, next, lunch) to /notavailable
app.UseSubdomainRedirect();

// App Retirement Middleware - redirects retired sites based on SiteDefinition.IsRetired
app.UseAppRetirement();

// Conditional Authentication - AFTER UseRouting so path is available
// Only load authentication for non-anonymous paths
app.UseWhen(
    context =>
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        var host = context.Request.Host.Host;
        var subdomain = host.Split('.').FirstOrDefault()?.ToLowerInvariant() ?? "";

        // Subdomains that should bypass authentication (iFrame-compatible)
        var anonymousSubdomains = Array.Empty<string>();

        // Paths that should bypass authentication
        var anonymousPaths = new[]
        {
            "/notavailable",
        };

        // Skip authentication if:
        // 1. Subdomain is in anonymous list (covers all assets)
        // 2. OR path starts with anonymous path
        var isAnonymousPath = anonymousSubdomains.Contains(subdomain) ||
                            anonymousPaths.Any(p => path.StartsWith(p));

        // Only use authentication if NOT anonymous
        return !isAnonymousPath;
    },
    appBuilder =>
    {
        appBuilder.UseAuthentication();
    }
);

// Impersonation middleware: replaces HttpContext.User when valid "Act as" cookie is present.
// Must run after authentication (to have the real admin's principal) and before authorization.
app.UseImpersonation();

// Authorization must ALWAYS run (required by ASP.NET Core for endpoints with [Authorize])
app.UseAuthorization();

// MCP Endpoint
app.UseLearnSkillsMcp();

#pragma warning disable ASP0014 // Suggest using top level route registrations
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapRazorPages();
    endpoints.MapDefaultControllerRoute();

    // Map Hubs inside endpoints
    endpoints.MapHub<ImportHub>("/importHub");
    endpoints.MapHub<LinkedInBatchHub>("/linkedInBatchHub");
});
#pragma warning restore ASP0014 // Suggest using top level route registrations

app.UseHangfireDashboard();

// Development Tools
app.UseSwaggerServices();

await app.UseStartupJobCleanupAsync();

app.Run();
