using MB.ComTools.Apps.Data;
using MB.ComTools.Apps.Setup.Mcp.Dtos;
using MB.Core.Types;
using MB.Core.Types.Learning;
using MB.Core.Types.Tags;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace MB.ComTools.Apps.Setup.Mcp.Tools;

/// <summary>
/// MCP Tools for querying and searching LearnCourse data.
/// Provides advanced search and filtering capabilities for courses by skill/tag.
/// </summary>
public class McpCourseTools
{
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<McpCourseTools> _logger;
    private readonly McpCacheService _cache;

    public McpCourseTools(
        AppDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<McpCourseTools> logger,
        McpCacheService cache)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _cache = cache;
    }

    /// <summary>
    /// Search published LearnCourses with flexible filters.
    /// Supports filtering by query text, platform, cluster, and pagination.
    /// </summary>
    [McpServerTool, Description("Search active published courses on the learn-skills platform. Filter by text query, platform (e.g. 'LinkedIn Learning'), or cluster name. Returns paginated course card summaries with title, instructor, duration, difficulty level when set, platform, and deep link.")]
    public async Task<McpCourseListResponse> search_courses(
        string? query = null,
        string? platform = null,
        string? clusterName = null,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (query is not null
            && (string.IsNullOrWhiteSpace(query) || query.Trim().Length > 200))
        {
            throw new ArgumentException(
                "query must be null for browse or contain 1-200 non-whitespace characters.",
                nameof(query));
        }

        // Validate and clamp pagination parameters
        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(offset, 0);

        var cacheKey = $"courses:search:{McpCacheService.HashParams(new { query, platform, clusterName, limit, offset })}";
        return await _cache.GetOrCreateAsync(
            cacheKey,
            "courses",
            async () =>
            {
                var baseQuery = _context.LearnCourses
                    .Where(c => c.Discriminator == "LearnCourse")
                    .Where(c => !c.IsDeleted)
                    .Where(c => c.IsVisible)
                    .Where(c => c.IsActive);

                // Apply text search filter
                if (!string.IsNullOrWhiteSpace(query))
                {
                    baseQuery = baseQuery.Where(c =>
                        EF.Functions.ILike(c.Title, $"%{query}%") ||
                        EF.Functions.ILike(c.Summary ?? "", $"%{query}%") ||
                        EF.Functions.ILike(c.Instructor ?? "", $"%{query}%") ||
                        EF.Functions.ILike(c.LmsTags ?? "", $"%{query}%") ||
                        EF.Functions.ILike(c.ClusterName ?? "", $"%{query}%") ||
                        EF.Functions.ILike(c.SubClusterName ?? "", $"%{query}%") ||
                        EF.Functions.ILike(c.ParentTag.Name ?? "", $"%{query}%") ||
                        EF.Functions.ILike(c.ChildTag.Name ?? "", $"%{query}%"));
                }

                // Apply platform filter (exact match)
                if (!string.IsNullOrWhiteSpace(platform))
                {
                    baseQuery = baseQuery.Where(c => c.SourcePlatform == platform);
                }

                // Apply cluster name filter (partial match)
                if (!string.IsNullOrWhiteSpace(clusterName))
                {
                    baseQuery = baseQuery.Where(c => EF.Functions.ILike(c.ClusterName ?? "", $"%{clusterName}%"));
                }

                var orderedQuery = !string.IsNullOrWhiteSpace(query)
                    ? baseQuery
                        .OrderByDescending(c => c.Title == query)
                        .ThenByDescending(c => c.Title.Contains(query))
                        .ThenByDescending(c => (c.LmsTags ?? "").Contains(query))
                        .ThenByDescending(c => (c.ClusterName ?? "").Contains(query))
                        .ThenByDescending(c => c.DisplayDate)
                        .ThenBy(c => c.BaseEntryId)
                    : baseQuery
                        .OrderByDescending(c => c.DisplayDate)
                        .ThenBy(c => c.BaseEntryId);

                var queryWithTags = orderedQuery
                    .Include(c => c.ParentTag)
                    .Include(c => c.ChildTag);

                var totalCount = await queryWithTags.CountAsync(cancellationToken);

                var courses = await queryWithTags
                    .Skip(offset)
                    .Take(limit)
                    .ToListAsync(cancellationToken);

                var httpContext = _httpContextAccessor.HttpContext;
                var cards = courses
                    .Select(course => new
                    {
                        Card = MapToCardDto(course, httpContext),
                        Score = CalculateRelevanceScore(course, query)
                    })
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.Card.Id, StringComparer.Ordinal)
                    .Select(item => item.Card)
                    .ToList();

                return new McpCourseListResponse(
                    cards,
                    totalCount,
                    DateTimeOffset.UtcNow
                );
            },
            isNegative: result => result.Courses.Count == 0);
    }

    /// <summary>
    /// Get full details for a single LearnCourse by ID.
    /// </summary>
    [McpServerTool, Description("Get complete details for a single published course by its ID, including full description, instructor, duration, difficulty level, platform, skill tags, and direct deep link to the course.")]
    public async Task<McpCourseDetailDto?> get_course(
        [Description("The course ID (BaseEntryId) to look up.")] string courseId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            throw new ArgumentException("courseId is required.", nameof(courseId));
        }

        return await _cache.GetOrCreateAsync(
            $"courses:detail:{courseId}",
            "courses",
            async () =>
            {
                var course = await _context.LearnCourses
                    .Where(c => c.BaseEntryId == courseId)
                    .Where(c => c.Discriminator == "LearnCourse")
                    .Where(c => !c.IsDeleted)
                    .Where(c => c.IsVisible)
                    .Where(c => c.IsActive)
                    .Include(c => c.ParentTag)
                    .Include(c => c.ChildTag)
                    .FirstOrDefaultAsync(cancellationToken);

                if (course == null)
                {
                    _logger.LogWarning("Course with ID {CourseId} not found or not published", courseId);
                    throw new McpToolException(
                        "not_found",
                        $"Course '{courseId}' was not found.");
                }

                var httpContext = _httpContextAccessor.HttpContext;
                return MapToDetailDto(course, httpContext);
            });
    }

    /// <summary>
    /// Get published LearnCourses associated with a skill/tag.
    /// Resolves the tag by ID or name (with optional language), then finds all courses
    /// linked via legacy FK pattern (ParentTagId/ChildTagId) or TagAssignments.
    /// </summary>
    [McpServerTool, Description("Find all published courses linked to a specific skill or tag. Resolve the tag by ID or by display name (optionally filtered by language). Use search_skills first to discover available tag IDs and names.")]
    public async Task<McpCourseListResponse> get_courses_by_tag(
        string? tagId = null,
        string? tagName = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tagId) && string.IsNullOrWhiteSpace(tagName))
        {
            throw new ArgumentException("Either tagId or tagName is required.");
        }

        var cacheKey = $"courses:skill:{McpCacheService.HashParams(new { tagId, tagName, language })}";
        return await _cache.GetOrCreateAsync(
            cacheKey,
            "courses",
            async () =>
            {
                // Resolve tagId if only tagName is provided
                if (string.IsNullOrWhiteSpace(tagId) && !string.IsNullOrWhiteSpace(tagName))
                {
                    tagId = await ResolveTagIdAsync(tagName, language, cancellationToken);
                }

                // If we still don't have a tagId, return empty
                if (string.IsNullOrWhiteSpace(tagId))
                {
                    _logger.LogWarning("Tag not found for name {TagName} and language {Language}", tagName, language);
                    throw new McpToolException(
                        "not_found",
                        $"Tag '{tagName}' was not found.");
                }

                // Find all course IDs linked to this tag via two patterns:
                // 1. Legacy FK pattern: ParentTagId or ChildTagId
                var legacyCourseIds = await _context.ContentBases
                    .Where(c => c.Discriminator == "LearnCourse")
                    .Where(c => !c.IsDeleted)
                    .Where(c => c.ParentTagId == tagId || c.ChildTagId == tagId)
                    .Select(c => c.BaseEntryId)
                    .ToListAsync(cancellationToken);

                // 2. TagAssignments pattern
                var assignmentCourseIds = await _context.Set<TagAssignment>()
                    .Where(ta => ta.TagId == tagId)
                    .Join(
                        _context.ContentBases
                            .Where(c => c.Discriminator == "LearnCourse")
                            .Where(c => !c.IsDeleted),
                        ta => ta.ContentId,
                        c => c.BaseEntryId,
                        (ta, c) => c.BaseEntryId)
                    .ToListAsync(cancellationToken);

                // Union both ID sets and deduplicate
                var allCourseIds = legacyCourseIds.Union(assignmentCourseIds).ToList();

                if (!allCourseIds.Any())
                {
                    _logger.LogInformation("No courses found for tag ID {TagId}", tagId);
                    return new McpCourseListResponse(
                        Array.Empty<McpCourseCardDto>(),
                        0,
                        DateTimeOffset.UtcNow
                    );
                }

                // Load full course entities
                var courses = await _context.LearnCourses
                    .Where(c => allCourseIds.Contains(c.BaseEntryId))
                    .Where(c => c.Discriminator == "LearnCourse")
                    .Where(c => !c.IsDeleted)
                    .Where(c => c.IsVisible)
                    .Where(c => c.IsActive)
                    .Include(c => c.ParentTag)
                    .Include(c => c.ChildTag)
                    .OrderByDescending(c => c.DisplayDate)
                    .ToListAsync(cancellationToken);

                var httpContext = _httpContextAccessor.HttpContext;
                var cards = courses.Select(c => MapToCardDto(c, httpContext)).ToList();

                return new McpCourseListResponse(
                    cards,
                    cards.Count,
                    DateTimeOffset.UtcNow
                );
            });
    }

    /// <summary>
    /// Resolve a tag name to a tag ID.
    /// First tries TagTranslations (with optional language filter), then falls back to Tags.Name.
    /// </summary>
    private async Task<string?> ResolveTagIdAsync(
        string tagName,
        string? language,
        CancellationToken cancellationToken)
    {
        // Try to resolve via TagTranslations
        var translationQuery = _context.TagTranslations
            .Where(tt => EF.Functions.ILike(tt.Name, tagName));

        // Apply language filter if provided
        if (!string.IsNullOrWhiteSpace(language) && Enum.TryParse<LanguageCode>(language.ToUpperInvariant(), out var languageCode))
        {
            translationQuery = translationQuery.Where(tt => tt.Language == languageCode);
        }

        var translation = await translationQuery
            .FirstOrDefaultAsync(cancellationToken);

        if (translation != null)
        {
            return translation.TagId;
        }

        // Fall back to direct Tag.Name lookup
        var tag = await _context.Tags
            .Where(t => EF.Functions.ILike(t.Name, tagName))
            .FirstOrDefaultAsync(cancellationToken);

        return tag?.BaseEntryId;
    }

    private static double CalculateRelevanceScore(LearnCourse course, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        var normalizedQuery = query.Trim();
        var score = 0.0;
        if (string.Equals(course.Title, normalizedQuery, StringComparison.OrdinalIgnoreCase)) score += 100;
        if (course.Title.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)) score += 50;
        if ((course.LmsTags ?? string.Empty).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)) score += 30;
        if ((course.ClusterName ?? string.Empty).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)) score += 20;
        if ((course.SubClusterName ?? string.Empty).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)) score += 15;
        if ((course.Summary ?? string.Empty).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)) score += 10;
        if ((course.Instructor ?? string.Empty).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)) score += 2;
        return score;
    }

    private McpCourseCardDto MapToCardDto(LearnCourse course, HttpContext? httpContext)
    {
        return new McpCourseCardDto(
            Id: course.BaseEntryId,
            Title: course.Title,
            Summary: string.IsNullOrWhiteSpace(course.Summary) ? null : course.Summary,
            SourcePlatform: string.IsNullOrWhiteSpace(course.SourcePlatform) ? null : course.SourcePlatform,
            ExternalCourseId: string.IsNullOrWhiteSpace(course.ExternalCourseId) ? null : course.ExternalCourseId,
            Instructor: string.IsNullOrWhiteSpace(course.Instructor) ? null : course.Instructor,
            DurationInHours: string.IsNullOrWhiteSpace(course.DurationInHours) ? null : course.DurationInHours,
            DifficultyLevel: MapDifficultyWireValue(course.DifficultyLevel),
            ClusterId: string.IsNullOrWhiteSpace(course.ClusterId) ? null : course.ClusterId,
            ClusterName: string.IsNullOrWhiteSpace(course.ClusterName) ? null : course.ClusterName,
            SubClusterId: string.IsNullOrWhiteSpace(course.SubClusterId) ? null : course.SubClusterId,
            SubClusterName: string.IsNullOrWhiteSpace(course.SubClusterName) ? null : course.SubClusterName,
            TrainingUrl: string.IsNullOrWhiteSpace(course.TrainingUrl) ? null : course.TrainingUrl,
            SsoTrainingUrl: string.IsNullOrWhiteSpace(course.SsoTrainingUrl) ? null : course.SsoTrainingUrl,
            Language: course.Language.ToString(),
            IsActive: course.IsActive,
            DisplayDate: SafeDisplayDate(course.DisplayDate),
            ParentTagId: course.ParentTagId,
            ParentTagName: course.ParentTag?.Name,
            ChildTagId: course.ChildTagId,
            ChildTagName: course.ChildTag?.Name,
            DeepLink: BuildDeepLink(course, httpContext)
        );
    }

    private McpCourseDetailDto MapToDetailDto(LearnCourse course, HttpContext? httpContext)
    {
        // Extract description from ContentJson if available
        string? description = null;
        if (!string.IsNullOrWhiteSpace(course.ContentJson) && course.ContentJson != "{}")
        {
            try
            {
                var contentData = System.Text.Json.JsonDocument.Parse(course.ContentJson);
                if (contentData.RootElement.TryGetProperty("description", out var descProp))
                {
                    description = descProp.GetString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse ContentJson for course {CourseId}", course.BaseEntryId);
            }
        }
        _logger.LogInformation(
           "Course={Title}, DisplayDate={DisplayDate}",
           course.Title,
           course.DisplayDate);

        return new McpCourseDetailDto(
            Id: course.BaseEntryId,
            Title: course.Title,
            Summary: string.IsNullOrWhiteSpace(course.Summary) ? null : course.Summary,
            Description: description,
            SourcePlatform: string.IsNullOrWhiteSpace(course.SourcePlatform) ? null : course.SourcePlatform,
            ExternalCourseId: string.IsNullOrWhiteSpace(course.ExternalCourseId) ? null : course.ExternalCourseId,
            Instructor: string.IsNullOrWhiteSpace(course.Instructor) ? null : course.Instructor,
            DurationInHours: string.IsNullOrWhiteSpace(course.DurationInHours) ? null : course.DurationInHours,
            DifficultyLevel: MapDifficultyWireValue(course.DifficultyLevel),
            ClusterId: string.IsNullOrWhiteSpace(course.ClusterId) ? null : course.ClusterId,
            ClusterName: string.IsNullOrWhiteSpace(course.ClusterName) ? null : course.ClusterName,
            SubClusterId: string.IsNullOrWhiteSpace(course.SubClusterId) ? null : course.SubClusterId,
            SubClusterName: string.IsNullOrWhiteSpace(course.SubClusterName) ? null : course.SubClusterName,
            TrainingUrl: string.IsNullOrWhiteSpace(course.TrainingUrl) ? null : course.TrainingUrl,
            SsoTrainingUrl: string.IsNullOrWhiteSpace(course.SsoTrainingUrl) ? null : course.SsoTrainingUrl,
            Language: course.Language.ToString(),
            IsActive: course.IsActive,
            IsUpdated: course.IsUpdated,
            LmsTags: string.IsNullOrWhiteSpace(course.LmsTags) ? null : course.LmsTags,
            DisplayDate: SafeDisplayDate(course.DisplayDate),
            ParentTagId: course.ParentTagId,
            ParentTagName: course.ParentTag?.Name,
            ChildTagId: course.ChildTagId,
            ChildTagName: course.ChildTag?.Name,
            DeepLink: BuildDeepLink(course, httpContext)
        );
    }

    private string BuildDeepLink(LearnCourse course, HttpContext? httpContext)
    {
        // External courses: prefer SsoTrainingUrl over TrainingUrl
        if (!string.IsNullOrWhiteSpace(course.SsoTrainingUrl))
        {
            return course.SsoTrainingUrl;
        }

        if (!string.IsNullOrWhiteSpace(course.TrainingUrl))
        {
            return course.TrainingUrl;
        }

        // Internal content: build deep link from current request context
        if (httpContext?.Request != null)
        {
            var request = httpContext.Request;
            return $"{request.Scheme}://{request.Host}/learn-skills/pages/content/{course.BaseEntryId}";
        }

        // Fallback: relative URL (shouldn't happen in normal operation)
        return $"/learn-skills/pages/content/{course.BaseEntryId}";
    }

    private static DateTimeOffset SafeDisplayDate(DateTime displayDate)
    {
        if (displayDate <= DateTime.MinValue.AddDays(1)
            || displayDate >= DateTime.MaxValue.AddDays(-1))
        {
            return DateTimeOffset.MinValue;
        }

        try
        {
            var utcDate = displayDate.Kind switch
            {
                DateTimeKind.Utc => displayDate,
                DateTimeKind.Local => displayDate.ToUniversalTime(),
                _ => DateTime.SpecifyKind(displayDate, DateTimeKind.Utc)
            };

            return new DateTimeOffset(utcDate, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// Maps LearnCourse.DifficultyLevel (Unset=0 … Expert=3) to the MCP wire string.
    /// </summary>
    private static string? MapDifficultyWireValue(object? level)
    {
        if (level is null)
        {
            return null;
        }

        var value = level is Enum
            ? Convert.ToInt32(level)
            : Convert.ToInt32(level);

        return value switch
        {
            1 => "Beginner",
            2 => "Intermediate",
            3 => "Expert",
            _ => null
        };
    }
}
