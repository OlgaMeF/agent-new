using MB.ComTools.Apps.Data;
using MB.ComTools.Apps.Setup.Mcp;
using MB.ComTools.Apps.Setup.Mcp.Dtos;
using MB.Core.Types;
using MB.Core.Types.Learning;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace MB.ComTools.Apps.Setup.Mcp.Resources;

/// <summary>
/// MCP Tools for querying published LearnCourse data.
/// Implemented as Tools since ModelContextProtocol.AspNetCore does not expose a separate Resources API.
/// </summary>
public class McpCourseResource
{
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<McpCourseResource> _logger;
    private readonly McpCacheService _cache;

    public McpCourseResource(
        AppDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<McpCourseResource> logger,
        McpCacheService cache)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _cache = cache;
    }

    /// <summary>
    /// List all published LearnCourses with pagination.
    /// Equivalent to MCP Resource: learn-skills://courses
    /// </summary>
    [McpServerTool, Description("List all published courses on the learn-skills platform ordered by display date. Use search_courses for filtered queries; use this for a full paginated catalog.")]
    public async Task<McpCourseListResponse> list_courses(
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        // Validate and clamp pagination parameters
        limit = Math.Clamp(limit, 1, 500);
        offset = Math.Max(offset, 0);

        return await _cache.GetOrCreateAsync(
            $"courses:list:{limit}:{offset}",
            "courses",
            async () =>
            {
                var query = _context.LearnCourses
                    .Where(c => c.Discriminator == "LearnCourse")
                    .Where(c => !c.IsDeleted)
                    .Where(c => c.IsVisible)
                    .Where(c => c.IsActive)
                    .Include(c => c.ParentTag)
                    .Include(c => c.ChildTag)
                    .OrderByDescending(c => c.DisplayDate);

                var totalCount = await query.CountAsync(cancellationToken);

                var courses = await query
                    .Skip(offset)
                    .Take(limit)
                    .ToListAsync(cancellationToken);

                var httpContext = _httpContextAccessor.HttpContext;
                var cards = courses.Select(c => MapToCardDto(c, httpContext)).ToList();

                return new McpCourseListResponse(
                    cards,
                    totalCount,
                    DateTimeOffset.UtcNow
                );
            });
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
            DisplayDate: course.DisplayDate,
            ParentTagId: course.ParentTagId,
            ParentTagName: course.ParentTag?.Name,
            ChildTagId: course.ChildTagId,
            ChildTagName: course.ChildTag?.Name,
            DeepLink: BuildDeepLink(course, httpContext)
        );
    }

    private static string BuildDeepLink(LearnCourse course, HttpContext? httpContext)
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

    private static string? MapDifficultyWireValue(object? level)
    {
        if (level is null)
        {
            return null;
        }

        var value = Convert.ToInt32(level);

        return value switch
        {
            1 => "Beginner",
            2 => "Intermediate",
            3 => "Expert",
            _ => null
        };
    }
}

