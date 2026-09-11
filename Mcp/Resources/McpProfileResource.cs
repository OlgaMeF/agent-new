using MB.ComTools.Apps.Data;
using MB.ComTools.Apps.Setup.Mcp.Dtos;
using MB.Core.Types;
using MB.Core.Types.Learning;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace MB.ComTools.Apps.Setup.Mcp.Resources;

/// <summary>
/// MCP Tools for querying published LearnProfile data.
/// Implemented as Tools since ModelContextProtocol.AspNetCore does not expose a separate Resources API.
/// </summary>
public class McpProfileResource
{
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<McpProfileResource> _logger;
    private readonly McpCacheService _cache;

    public McpProfileResource(
        AppDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<McpProfileResource> logger,
        McpCacheService cache)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _cache = cache;
    }

    /// <summary>
    /// List all published LearnProfiles with pagination.
    /// Equivalent to MCP Resource: learn-skills://profiles
    /// </summary>
    [McpServerTool, Description("List all published learning profiles (role-based course collections) ordered by display date. Use search_profiles for filtered queries; use this for a full paginated catalog.")]
    public async Task<McpProfileListResponse> list_profiles(
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        // Validate and clamp pagination parameters
        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(offset, 0);

        return await _cache.GetOrCreateAsync(
            $"profiles:list:{limit}:{offset}",
            "profiles",
            async () =>
            {
                var query = _context.LearnProfiles
                    .Where(p => !p.IsDeleted)
                    .Where(p => p.IsVisible)
                    .OrderByDescending(p => p.DisplayDate);

                var totalCount = await query.CountAsync(cancellationToken);

                var profiles = await query
                    .Skip(offset)
                    .Take(limit)
                    .Select(p => new
                    {
                        Profile = p,
                        RequiredCount = _context.LearnProfileCourseAssignments
                            .Count(ca => ca.ProfileId == p.BaseEntryId && ca.RequirementType == CourseRequirementType.Mandatory),
                        OptionalCount = _context.LearnProfileCourseAssignments
                            .Count(ca => ca.ProfileId == p.BaseEntryId && ca.RequirementType == CourseRequirementType.Optional)
                    })
                    .ToListAsync(cancellationToken);

                // Load department/division names separately
                var departmentIds = profiles
                    .Where(p => !string.IsNullOrEmpty(p.Profile.DepartmentId))
                    .Select(p => p.Profile.DepartmentId!)
                    .Distinct()
                    .ToList();

                var departments = await _context.LearnDepartments
                    .Where(d => departmentIds.Contains(d.BaseEntryId))
                    .Select(d => new { d.BaseEntryId, d.Title, d.DivisionId })
                    .ToListAsync(cancellationToken);

                var divisionIds = departments.Select(d => d.DivisionId).Distinct().ToList();
                var divisions = await _context.LearnDivisions
                    .Where(d => divisionIds.Contains(d.BaseEntryId))
                    .ToDictionaryAsync(d => d.BaseEntryId, d => d.Title ?? string.Empty, cancellationToken);

                var departmentLookup = departments.ToDictionary(
                    d => d.BaseEntryId,
                    d => new { d.Title, DivisionName = divisions.GetValueOrDefault(d.DivisionId, string.Empty) }
                );

                var httpContext = _httpContextAccessor.HttpContext;
                var cards = profiles.Select(p =>
                {
                    var dept = !string.IsNullOrEmpty(p.Profile.DepartmentId)
                        ? departmentLookup.GetValueOrDefault(p.Profile.DepartmentId)
                        : null;

                    return new McpProfileCardDto(
                        Id: p.Profile.BaseEntryId,
                        Title: p.Profile.Title,
                        Summary: p.Profile.Summary,
                        DivisionId: dept != null
                            ? departments.First(d => d.BaseEntryId == p.Profile.DepartmentId).DivisionId
                            : null,
                        DivisionName: dept?.DivisionName,
                        DepartmentId: p.Profile.DepartmentId,
                        DepartmentName: dept?.Title ?? p.Profile.DeptName,
                        TotalCourses: p.Profile.TotalCourses,
                        RequiredCoursesCount: p.RequiredCount,
                        OptionalCoursesCount: p.OptionalCount,
                        TeaserImageUrl: GetImageUrl(p.Profile.TeaserImageName, httpContext),
                        DeepLink: BuildDeepLink($"/learn-skills/profile/view/{p.Profile.BaseEntryId}", httpContext)
                    );
                }).ToList();

                return new McpProfileListResponse(
                    cards,
                    totalCount,
                    DateTimeOffset.UtcNow
                );
            });
    }

    private static string BuildDeepLink(string path, HttpContext? httpContext)
    {
        // Internal content: build deep link from current request context
        if (httpContext?.Request != null)
        {
            var request = httpContext.Request;
            return $"{request.Scheme}://{request.Host}{path}";
        }

        // Fallback: relative URL (shouldn't happen in normal operation)
        return path;
    }

    private static string? GetImageUrl(string? imageName, HttpContext? httpContext)
    {
        if (string.IsNullOrEmpty(imageName))
            return null;

        if (httpContext?.Request != null)
        {
            var request = httpContext.Request;
            return $"{request.Scheme}://{request.Host}/images/{imageName}";
        }

        // Fallback: relative URL
        return $"/images/{imageName}";
    }
}
