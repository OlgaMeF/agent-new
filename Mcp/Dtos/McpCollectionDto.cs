namespace MB.ComTools.Apps.Setup.Mcp.Dtos;

public record McpCollectionDto(
    string Id,
    string? Title,
    string? Summary,
    int ItemCount,
    string CollectionType,
    string DeepLink
);

public record McpCollectionListResponse(
    IReadOnlyList<McpCollectionDto> Collections,
    int TotalCount,
    int Limit,
    int Offset
);

public record McpBookmarkCourseDto(
    string CourseId,
    string? CourseTitle,
    string DeepLink
);

public record McpBookmarkProfileDto(
    string ProfileId,
    string? ProfileTitle,
    string DeepLink
);

public record McpBookmarksResponse(
    IReadOnlyList<McpBookmarkCourseDto> Courses,
    IReadOnlyList<McpBookmarkProfileDto> Profiles,
    string? Error = null
);

public record McpProgressCourseDto(
    string CourseId,
    string? CourseTitle,
    object? ProgressPercentage,
    object? CompletedAt,
    string DeepLink
);

public record McpProgressRateDto(
    string SiteUrl,
    int CompletedCount,
    int TotalCourses,
    double CompletionRate
);

public record McpProgressResponse(
    IReadOnlyList<McpProgressCourseDto> CompletedCourses,
    IReadOnlyList<McpProgressCourseDto> InProgressCourses,
    IReadOnlyList<McpProgressRateDto> ProfileCompletionRates,
    string? Error = null
);