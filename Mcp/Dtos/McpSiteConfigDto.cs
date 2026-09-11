namespace MB.ComTools.Apps.Setup.Mcp.Dtos;

public record McpSiteConfigDto(
    string SiteUrl,
    string Title,
    string? AppBarTitle,
    bool HasSearch,
    bool HasAdminMenu,
    IReadOnlyList<string> SupportedLanguages
);
