using MB.ComTools.Apps.Data;
using MB.Core.Types;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace MB.ComTools.Apps.Setup.Mcp.Tools;

/// <summary>
/// MCP Tools for querying authenticated user context:
/// progress, bookmarks, and public collections.
/// NEVER cached - always returns fresh user-specific data.
/// </summary>
public class McpUserTools
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<McpUserTools> _logger;

    public McpUserTools(
        AppDbContext db,
        IHttpContextAccessor httpContextAccessor,
        ILogger<McpUserTools> logger)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Get current authenticated user's learning progress:
    /// completed courses, in-progress courses, and per-profile completion rates.
    /// </summary>
    [McpServerTool, Description("Returns the authenticated user's learning progress: completed courses, in-progress courses with completion percentage, and overall site completion rate. Requires a valid OIDC Bearer token in the Authorization header.")]
    public async Task<object> get_my_progress(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            _logger.LogWarning("get_my_progress called without authenticated user");
            return new { error = "Authentication required. Please provide a valid bearer token." };
        }

        _logger.LogInformation("Loading progress for authenticated user");

        // Load all completions for this user with course titles
        var completions = await _db.UserCompletions
            .Where(uc => uc.UserId == userId)
            .Join(
                _db.ContentBases
                    .Where(cb => cb.Discriminator == "LearnCourse" && !cb.IsDeleted),
                uc => uc.BaseEntryId,
                cb => cb.BaseEntryId,
                (uc, cb) => new
                {
                    uc.BaseEntryId,
                    cb.Title,
                    uc.ProgressPercentage,
                    uc.CompletedAt,
                    cb.SiteUrl
                })
            .ToListAsync(cancellationToken);

        // Split into completed vs in-progress
        var completed = completions
            .Where(c => c.ProgressPercentage >= 100)
            .Select(c => new
            {
                courseId = c.BaseEntryId,
                courseTitle = c.Title,
                completedAt = c.CompletedAt,
                deepLink = BuildDeepLink(c.BaseEntryId)
            })
            .OrderByDescending(c => c.completedAt)
            .ToList();

        var inProgress = completions
            .Where(c => c.ProgressPercentage < 100)
            .Select(c => new
            {
                courseId = c.BaseEntryId,
                courseTitle = c.Title,
                progressPercentage = c.ProgressPercentage,
                deepLink = BuildDeepLink(c.BaseEntryId)
            })
            .OrderByDescending(c => c.progressPercentage)
            .ToList();

        // Calculate per-profile completion rates
        // A profile is a LearnCourse with Discriminator "LearnProfile" (if it exists)
        // For now, we'll calculate completion rate for learn-skills site overall
        var totalCoursesInSite = await _db.ContentBases
            .Where(cb => cb.Discriminator == "LearnCourse"
                && cb.SiteUrl == "learn-skills"
                && !cb.IsDeleted)
            .CountAsync(cancellationToken);

        var completedInSite = completions
            .Count(c => c.ProgressPercentage >= 100 && c.SiteUrl == "learn-skills");

        var profileCompletionRates = new[]
        {
            new
            {
                siteUrl = "learn-skills",
                completedCount = completedInSite,
                totalCourses = totalCoursesInSite,
                completionRate = totalCoursesInSite > 0
                    ? Math.Round((double)completedInSite / totalCoursesInSite * 100, 2)
                    : 0.0
            }
        };

        return new
        {
            completedCourses = completed,
            inProgressCourses = inProgress,
            profileCompletionRates = profileCompletionRates
        };
    }

    /// <summary>
    /// Get current authenticated user's bookmarks (courses and profiles).
    /// </summary>
    [McpServerTool, Description("Returns the authenticated user's bookmarked courses and learning profiles with their deep links. Requires a valid OIDC Bearer token in the Authorization header.")]
    public async Task<object> get_my_bookmarks(CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            _logger.LogWarning("get_my_bookmarks called without authenticated user");
            return new { error = "Authentication required. Please provide a valid bearer token." };
        }

        _logger.LogInformation("Loading bookmarks for authenticated user");

        // Load all bookmarks for this user with content titles
        var bookmarks = await _db.UserBookmarks
            .Where(ub => ub.UserId == userId)
            .Join(
                _db.ContentBases.Where(cb => !cb.IsDeleted),
                ub => ub.BaseEntryId,
                cb => cb.BaseEntryId,
                (ub, cb) => new
                {
                    cb.BaseEntryId,
                    cb.Title,
                    cb.Discriminator,
                    ub.CreatedAt
                })
            .ToListAsync(cancellationToken);

        // Split by discriminator
        var courseBookmarks = bookmarks
            .Where(b => b.Discriminator == "LearnCourse")
            .Select(b => new
            {
                courseId = b.BaseEntryId,
                courseTitle = b.Title,
                deepLink = BuildDeepLink(b.BaseEntryId)
            })
            .OrderBy(b => b.courseTitle)
            .ToList();

        var profileBookmarks = bookmarks
            .Where(b => b.Discriminator == "LearnProfile")
            .Select(b => new
            {
                profileId = b.BaseEntryId,
                profileTitle = b.Title,
                deepLink = BuildProfileDeepLink(b.BaseEntryId)
            })
            .OrderBy(b => b.profileTitle)
            .ToList();

        return new
        {
            courses = courseBookmarks,
            profiles = profileBookmarks
        };
    }

    /// <summary>
    /// Get public collections from the learn-skills site.
    /// No authentication required.
    /// </summary>
    [McpServerTool, Description("Returns public curated content collections from the learn-skills platform. No authentication required. Collections are ordered by last update date.")]
    public async Task<object> search_collections(
        int limit = 20,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        // Clamp parameters
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(offset, 0);

        _logger.LogInformation(
            "Loading public collections (limit={Limit}, offset={Offset})",
            limit,
            offset);

        var query = _db.ContentCollections
            .Where(cc => cc.IsPublic
                && cc.SiteUrl == "learn-skills"
                && !cc.IsDeleted)
            .OrderByDescending(cc => cc.LastUpdated ?? cc.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var collections = await query
            .Skip(offset)
            .Take(limit)
            .Select(cc => new
            {
                id = cc.BaseEntryId,
                title = cc.Title,
                summary = string.IsNullOrWhiteSpace(cc.Description)
                    ? null
                    : cc.Description,
                itemCount = cc.ItemCount,
                collectionType = cc.CollectionType.ToString()
            })
            .ToListAsync(cancellationToken);

        var result = collections
            .Select(cc => new
            {
                cc.id,
                cc.title,
                cc.summary,
                cc.itemCount,
                cc.collectionType,
                deepLink = BuildCollectionDeepLink(cc.id)
            })
            .ToList();

        return new
        {
            collections = result,
            totalCount,
            limit,
            offset
        };
    }

    /// <summary>
    /// Extract current authenticated user ID from HttpContext claims.
    /// Returns null if user is not authenticated.
    /// </summary>
    private string? GetCurrentUserId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // Try "sub" claim first (OIDC standard), then NameIdentifier
        return user.FindFirst("sub")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    /// Build deep link for a course content item.
    /// </summary>
    private string BuildDeepLink(string contentId)
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null)
        {
            return $"/learn-skills/pages/content/{contentId}";
        }

        return $"{request.Scheme}://{request.Host}/learn-skills/pages/content/{contentId}";
    }

    /// <summary>
    /// Build deep link for a profile view.
    /// </summary>
    private string BuildProfileDeepLink(string profileId)
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null)
        {
            return $"/learn-skills/profile/view/{profileId}";
        }

        return $"{request.Scheme}://{request.Host}/learn-skills/profile/view/{profileId}";
    }

    /// <summary>
    /// Build deep link for a collection view.
    /// </summary>
    private string BuildCollectionDeepLink(string collectionId)
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null)
        {
            return $"/learn-skills/c/{collectionId}";
        }

        return $"{request.Scheme}://{request.Host}/learn-skills/c/{collectionId}";
    }
}
