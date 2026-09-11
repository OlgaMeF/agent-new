namespace MB.ComTools.Apps.Setup.Mcp.Dtos;

/// <summary>Card-level schema returned by the courses list.</summary>
public record McpCourseCardDto(
    string Id,
    string Title,
    string? Summary,
    string? SourcePlatform,
    string? ExternalCourseId,
    string? Instructor,
    string? DurationInHours,
    string? ClusterId,
    string? ClusterName,
    string? SubClusterId,
    string? SubClusterName,
    string? TrainingUrl,
    string? SsoTrainingUrl,
    string? Language,
    bool IsActive,
    DateTimeOffset? DisplayDate,
    string? ParentTagId,
    string? ParentTagName,
    string? ChildTagId,
    string? ChildTagName,
    string DeepLink
);

/// <summary>Full-detail schema returned by get_course.</summary>
public record McpCourseDetailDto(
    string Id,
    string Title,
    string? Summary,
    string? Description,
    string? SourcePlatform,
    string? ExternalCourseId,
    string? Instructor,
    string? DurationInHours,
    string? ClusterId,
    string? ClusterName,
    string? SubClusterId,
    string? SubClusterName,
    string? TrainingUrl,
    string? SsoTrainingUrl,
    string? Language,
    bool IsActive,
    bool IsUpdated,
    string? LmsTags,
    DateTimeOffset? DisplayDate,
    string? ParentTagId,
    string? ParentTagName,
    string? ChildTagId,
    string? ChildTagName,
    string DeepLink
);

public record McpCourseListResponse(
    IReadOnlyList<McpCourseCardDto> Courses,
    int TotalCount,
    DateTimeOffset GeneratedAt
);
