using MB.ComTools.Apps.Data;
using MB.ComTools.Apps.Setup.Mcp;
using MB.ComTools.Apps.Setup.Mcp.Dtos;
using MB.Core.Types;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace MB.ComTools.Apps.Setup.Mcp.Resources;

/// <summary>
/// MCP Resources for learn-skills: divisions, tags, and site configuration.
/// </summary>
public class McpSupportingResources
{
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<McpSupportingResources> _logger;
    private readonly McpCacheService _cache;

    public McpSupportingResources(
        AppDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<McpSupportingResources> logger,
        McpCacheService cache)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _cache = cache;
    }

    /// <summary>
    /// Resource: learn-skills://divisions
    /// Returns all divisions with nested departments.
    /// </summary>
    [McpServerTool, Description("Returns all organizational divisions with their nested departments. Use this to discover valid divisionId and departmentId values for filtering courses and profiles.")]
    public async Task<McpDivisionListResponse> get_divisions()
    {
        return await _cache.GetOrCreateAsync(
            "divisions:list",
            "divisions",
            async () =>
            {
                try
                {
                    _logger.LogInformation("Loading divisions for learn-skills");

                    // Load all divisions with their departments
                    var divisions = await _context
                        .Set<MB.Core.Types.Learning.LearnDivision>()
                        .AsNoTracking()
                        .Where(d => !d.IsDeleted)
                        .OrderBy(d => d.Title)
                        .ToListAsync();

                    // Load all departments grouped by division
                    var departments = await _context
                        .Set<MB.Core.Types.Learning.LearnDepartment>()
                        .AsNoTracking()
                        .Where(d => !d.IsDeleted)
                        .OrderBy(d => d.Title)
                        .ToListAsync();

                    var departmentsByDivision = departments.GroupBy(d => d.DivisionId).ToDictionary(g => g.Key);

                    // Build response DTOs
                    var divisionDtos = divisions.Select(d => new McpDivisionDto(
                        Id: d.BaseEntryId,
                        Title: d.Title,
                        Departments: (departmentsByDivision.TryGetValue(d.BaseEntryId, out var depts)
                            ? depts.Select(dep => new McpDepartmentDto(
                                Id: dep.BaseEntryId,
                                Title: dep.Title))
                            : Enumerable.Empty<McpDepartmentDto>()).ToList().AsReadOnly(),
                        DeepLink: BuildDeepLink($"/learn-skills/d/{d.BaseEntryId}")
                    )).ToList().AsReadOnly();

                    return new McpDivisionListResponse(
                        Divisions: divisionDtos,
                        GeneratedAt: DateTimeOffset.UtcNow);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading divisions");
                    throw;
                }
            });
    }

    /// <summary>
    /// Resource: learn-skills://tags
    /// Returns all tags for learn-skills with translations.
    /// </summary>
    [McpServerTool, Description("Returns all skill tags for learn-skills with multilingual translations and parent-child relationships. Use this as an alternative to search_skills when you need a flat list with full translation data.")]
    public async Task<McpTagListResponse> get_tags()
    {
        return await _cache.GetOrCreateAsync(
            "tags:list",
            "tags",
            async () =>
            {
                try
                {
                    _logger.LogInformation("Loading tags for learn-skills");

                    // Load all tags for learn-skills site, including translations
                    var tags = await _context
                        .Set<MB.Core.Types.Tags.Tag>()
                        .AsNoTracking()
                        .Where(t => (t.SiteUrl == "learn-skills" || t.SiteUrl == null) && !t.IsDeleted)
                        .Include(t => t.Translations)
                        .OrderBy(t => t.Name)
                        .ToListAsync();

                    // Build response DTOs
                    var tagDtos = tags.Select(t => new McpTagDto(
                        Id: t.BaseEntryId,
                        Name: t.Name,
                        Type: (int)t.Type,
                        Group: t.Group,
                        ParentTagId: t.ParentTagId,
                        Translations: t.Translations
                            .Select(tr => new McpTagTranslationDto(
                                Language: LanguageCodeToString((LanguageCode)tr.Language),
                                Name: tr.Name))
                            .ToList()
                            .AsReadOnly()
                    )).ToList().AsReadOnly();

                    return new McpTagListResponse(
                        Tags: tagDtos,
                        GeneratedAt: DateTimeOffset.UtcNow);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading tags");
                    throw;
                }
            });
    }

    /// <summary>
    /// Resource: learn-skills://site-config
    /// Returns site configuration for learn-skills.
    /// </summary>
    [McpServerTool, Description("Returns the site configuration for learn-skills, including available platforms, cluster definitions, and feature flags. Useful for understanding what filter values are valid.")]
    public async Task<McpSiteConfigDto> get_site_config()
    {
        return await _cache.GetOrCreateAsync(
            "site-config",
            "site-config",
            async () =>
            {
                try
                {
                    _logger.LogInformation("Loading site config for learn-skills");

                    // Load site definition for learn-skills
                    var siteDefinition = await _context
                        .Set<MB.Core.Types.Content.SiteDefinition>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.SiteUrl == "learn-skills" && !s.IsDeleted);

                    if (siteDefinition == null)
                    {
                        _logger.LogWarning("Site definition not found for learn-skills");
                        throw new InvalidOperationException("Site definition for learn-skills not found");
                    }

                    // Deserialize supported languages from LanguagesJson
                    var supportedLanguages = DeserializeLanguages(siteDefinition.LanguagesJson);

                    return new McpSiteConfigDto(
                        SiteUrl: siteDefinition.SiteUrl,
                        Title: siteDefinition.Title,
                        AppBarTitle: siteDefinition.AppBarTitle,
                        HasSearch: siteDefinition.HasSearch,
                        HasAdminMenu: siteDefinition.HasAdminMenu,
                        SupportedLanguages: supportedLanguages.AsReadOnly()
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading site config");
                    throw;
                }
            });
    }

    /// <summary>
    /// Build a deep link URL using the current HTTP context.
    /// </summary>
    private string BuildDeepLink(string path)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            // Fallback if no HTTP context (should not happen in normal operation)
            return path;
        }

        var scheme = httpContext.Request.Scheme;
        var host = httpContext.Request.Host.ToString();

        return $"{scheme}://{host}{path}";
    }

    /// <summary>
    /// Convert LanguageCode enum value to string representation.
    /// </summary>
    private string LanguageCodeToString(LanguageCode code)
    {
        return code switch
        {
            LanguageCode.DE => "DE",
            LanguageCode.EN => "EN",
            LanguageCode.NL => "NL",
            LanguageCode.FR => "FR",
            LanguageCode.IT => "IT",
            LanguageCode.ES => "ES",
            _ => code.ToString()
        };
    }

    /// <summary>
    /// Deserialize LanguagesJson to extract supported language codes.
    /// Handles both array of strings and array of objects formats.
    /// </summary>
    private List<string> DeserializeLanguages(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new List<string> { "DE" }; // Default to German
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var languages = new List<string>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        // Format: ["DE", "EN", "FR"]
                        var langCode = element.GetString();
                        if (!string.IsNullOrEmpty(langCode))
                        {
                            languages.Add(langCode);
                        }
                    }
                    else if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("code", out var codeElement))
                    {
                        // Format: [{ "code": "DE", "name": "Deutsch" }, ...]
                        var langCode = codeElement.GetString();
                        if (!string.IsNullOrEmpty(langCode))
                        {
                            languages.Add(langCode);
                        }
                    }
                }
            }

            return languages.Count > 0 ? languages : new List<string> { "DE" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize LanguagesJson, defaulting to DE");
            return new List<string> { "DE" };
        }
    }
}
