namespace MB.ComTools.Apps.Setup.Mcp.Dtos;

public record McpProfileCardDto(
    string Id,
    string Title,
    string? Summary,
    string? DivisionId,
    string? DivisionName,
    string? DepartmentId,
    string? DepartmentName,
    int TotalCourses,
    int RequiredCoursesCount,
    int OptionalCoursesCount,
    string? TeaserImageUrl,
    string DeepLink
);

public record McpProfileCourseDto(
    string CourseId,
    string CourseTitle,
    string RequirementType,
    int SortOrder,
    string DeepLink,
    string? CourseClusterName = null,
    string? SkillId = null,
    string? SkillName = null,
    string? CoverageLevel = null,
    string? TargetLevel = null,
    double? Weight = null,
    string? EvidenceSource = null
);

public record McpProfileDetailDto(
    string Id,
    string Title,
    string? Summary,
    string? DivisionId,
    string? DivisionName,
    string? DepartmentId,
    string? DepartmentName,
    int TotalCourses,
    int RequiredCoursesCount,
    int OptionalCoursesCount,
    IReadOnlyList<McpProfileCourseDto> CourseAssignments,
    string DeepLink
);

public record McpProfileListResponse(
    IReadOnlyList<McpProfileCardDto> Profiles,
    int TotalCount,
    DateTimeOffset GeneratedAt
);

public record McpProfileCourseClusterGroupDto(
    string CourseClusterName,
    int RequiredCount,
    int OptionalCount,
    IReadOnlyList<McpProfileCourseDto> Courses
);

public record McpProfileSkillsResponse(
    string ProfileId,
    string Title,
    string? Summary,
    int TotalCourses,
    int RequiredCoursesCount,
    int OptionalCoursesCount,
    IReadOnlyList<McpProfileCourseClusterGroupDto> CourseClusters,
    string DeepLink
);

public record McpSkillDto(
    string Id,
    string Name,
    string? Description,
    string Type,
    string Group,
    string? ParentTagId,
    string? ParentTagName,
    IReadOnlyList<McpTagTranslationDto> Translations
);
