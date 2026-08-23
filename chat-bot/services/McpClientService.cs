using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;

namespace MB.ComTools.Apps.Content.Services;

public class McpClientService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<McpClientService> _logger;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private string? _sessionId;
    private long _requestId = 1;

    public McpClientService(
        HttpClient httpClient,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        ILogger<McpClientService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    // ---------------------------------------------------------------
    // Course tools
    // ---------------------------------------------------------------

    public Task<string?> SearchCoursesAsync(
        string? query,
        int limit,
        int offset,
        string? platform = null,
        string? clusterName = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["query"] = Normalize(query),
            ["limit"] = limit,
            ["offset"] = offset,
        };

        AddIfPresent(args, "platform", platform);
        AddIfPresent(args, "clusterName", clusterName);

        return CallToolAsync("search_courses", args, cancellationToken);
    }

    public Task<string?> GetCourseAsync(
        string courseId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["courseId"] = courseId
        };

        return CallToolAsync("get_course", args, cancellationToken);
    }

    public Task<string?> GetCoursesByTagAsync(
        string tagName,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["tagName"] = tagName
        };

        AddIfPresent(args, "language", language);

        return CallToolAsync("get_courses_by_tag", args, cancellationToken);
    }

    // ---------------------------------------------------------------
    // Skill tools
    // ---------------------------------------------------------------

    public Task<string?> GetSkillHierarchyAsync(CancellationToken cancellationToken = default)
    {
        return CallToolAsync("search_skills", new Dictionary<string, object?>(), cancellationToken);
    }

    public Task<string?> GetSkillAsync(
        string skillName,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["skillName"] = skillName
        };

        return CallToolAsync("get_skill", args, cancellationToken);
    }

    public Task<string?> GetTagsAsync(
        CancellationToken cancellationToken = default)
    {
        return CallToolAsync(
            "GetTags",
            new Dictionary<string, object?>(),
            cancellationToken);
    }

    // ---------------------------------------------------------------
    // Profile tools
    // ---------------------------------------------------------------

    public Task<string?> SearchProfilesAsync(
        string? query,
        int limit,
        int offset,
        string? divisionId = null,
        string? departmentId = null,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["query"] = Normalize(query),
            ["limit"] = limit,
            ["offset"] = offset,
        };

        AddIfPresent(args, "divisionId", divisionId);
        AddIfPresent(args, "departmentId", departmentId);

        return CallToolAsync("search_profiles", args, cancellationToken);
    }

    public Task<string?> GetProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["profileId"] = profileId,
        };

        return CallToolAsync("get_profile", args, cancellationToken);
    }

    public Task<string?> GetProfileSkillsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, object?>
        {
            ["query"] = query
        };

        return CallToolAsync(
            "get_profile_skills",
            args,
            cancellationToken);
    }

    // ---------------------------------------------------------------
    // Structure, collections and user context
    // ---------------------------------------------------------------

    /// <summary>
    /// The MCP tool name is the method name of the server-side handler. In
    /// McpSupportingResources this handler is called GetDivisions, not
    /// get_divisions as MCP.md states.
    /// </summary>
    public Task<string?> GetDivisionsAsync(CancellationToken cancellationToken = default)
    {
        return CallToolAsync(
            "GetDivisions",
            new Dictionary<string, object?>(),
            cancellationToken);
    }

    /// <summary>
    /// MCP.md documents no parameters for this tool, so it is called without
    /// arguments. Topic matching happens on the caller side.
    /// </summary>
    public Task<string?> SearchCollectionsAsync(
        CancellationToken cancellationToken = default)
    {
        return CallToolAsync(
            "search_collections",
            new Dictionary<string, object?>(),
            cancellationToken);
    }

    public Task<string?> GetMyBookmarksAsync(
        CancellationToken cancellationToken = default)
    {
        return CallToolAsync(
            "get_my_bookmarks",
            new Dictionary<string, object?>(),
            cancellationToken);
    }

    public Task<string?> GetMyProgressAsync(
        CancellationToken cancellationToken = default)
    {
        return CallToolAsync(
            "get_my_progress",
            new Dictionary<string, object?>(),
            cancellationToken);
    }

    // ---------------------------------------------------------------
    // Transport
    // ---------------------------------------------------------------

    private async Task<string?> CallToolAsync(
        string toolName,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var mcpUrl = ResolveMcpUrl();
        var mcpKey = _configuration["Mcp:ApiKey"];

        if (string.IsNullOrWhiteSpace(mcpUrl))
        {
            _logger.LogWarning("MCP call skipped because Mcp:ServerUrl is missing and request context is unavailable.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(mcpKey))
        {
            _logger.LogWarning("MCP call skipped because Mcp:ApiKey is missing.");
            return null;
        }

        if (!await EnsureInitializedAsync(mcpUrl, mcpKey, cancellationToken))
        {
            return null;
        }

        var payload = new
        {
            jsonrpc = "2.0",
            id = NextRequestId(),
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments,
            }
        };

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, mcpUrl);
        AddAcceptHeaders(request);
        request.Headers.TryAddWithoutValidation("X-MCP-Key", mcpKey);
        ForwardUserAuthorization(request);

        if (!string.IsNullOrWhiteSpace(_sessionId))
        {
            request.Headers.TryAddWithoutValidation("mcp-session-id", _sessionId);
        }

        request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        CaptureSessionId(response);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                    "MCP tool {Tool} failed with status {StatusCode}",
                toolName,
                    (int)response.StatusCode);

            return null;
        }

        return responseBody;
    }

    private async Task<bool> EnsureInitializedAsync(string mcpUrl, string mcpKey, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_sessionId))
        {
            return true;
        }

        // Tool calls of one agent round run in parallel; without this gate every
        // one of them would open its own MCP session.
        await _initGate.WaitAsync(cancellationToken);

        try
        {
            if (!string.IsNullOrWhiteSpace(_sessionId))
            {
                return true;
            }

            var initPayload = new
            {
                jsonrpc = "2.0",
                id = NextRequestId(),
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "skills-platform-chat",
                        version = "1.0.0"
                    }
                }
            };

            var initJson = JsonSerializer.Serialize(initPayload, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, mcpUrl);
            AddAcceptHeaders(request);
            request.Headers.TryAddWithoutValidation("X-MCP-Key", mcpKey);
            ForwardUserAuthorization(request);
            request.Content = new StringContent(initJson, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            CaptureSessionId(response);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "MCP initialize failed with status {StatusCode}",
                    (int)response.StatusCode);

                return false;
            }

            return !string.IsNullOrWhiteSpace(_sessionId);
        }
        finally
        {
            _initGate.Release();
        }
    }

    private string? ResolveMcpUrl()
    {
        var configured = _configuration["Mcp:ServerUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            return null;
        }

        return $"{request.Scheme}://{request.Host}/mcp";
    }

    // User-context tools (get_my_bookmarks) are authorized by the caller's OIDC
    // token, not by the machine key.
    private void ForwardUserAuthorization(HttpRequestMessage request)
    {
        var incoming = _httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(incoming))
        {
            return;
        }

        request.Headers.TryAddWithoutValidation("Authorization", incoming);
    }

    private void CaptureSessionId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("mcp-session-id", out var values))
        {
            _sessionId = values.FirstOrDefault();
        }
    }

    private static void AddAcceptHeaders(HttpRequestMessage request)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    }

    private static void AddIfPresent(
        Dictionary<string, object?> args,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            args[name] = value.Trim();
        }
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Shorten(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 500 ? value : value[..500];
    }

    private long NextRequestId()
    {
        return Interlocked.Increment(ref _requestId);
    }
}
