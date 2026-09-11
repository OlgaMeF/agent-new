namespace MB.ComTools.Apps.Setup.Mcp;

public class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>API key for machine clients (X-MCP-Key header).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Whether the MCP endpoint is enabled. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Optional audience for MCP bearer tokens. Defaults to the OIDC client id.</summary>
    public string BearerAudience { get; set; } = string.Empty;

    /// <summary>Optional scope required by personal user-context tools.</summary>
    public string UserToolsScope { get; set; } = string.Empty;

    /// <summary>Rate limit: max requests per minute per API key.</summary>
    public int RateLimitPerMinute { get; set; } = 200;

    /// <summary>Cache TTL in minutes for read-only resources.</summary>
    public int CacheTtlMinutes { get; set; } = 10;

    /// <summary>Short TTL in minutes for valid empty result sets.</summary>
    public int NegativeCacheTtlMinutes { get; set; } = 1;
}
