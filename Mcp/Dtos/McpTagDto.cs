namespace MB.ComTools.Apps.Setup.Mcp.Dtos;

public record McpTagTranslationDto(string Language, string Name);

public record McpTagDto(
    string Id,
    string Name,
    int Type,
    string? Group,
    string? ParentTagId,
    IReadOnlyList<McpTagTranslationDto> Translations
);

public record McpTagListResponse(
    IReadOnlyList<McpTagDto> Tags,
    DateTimeOffset GeneratedAt
);

/// <summary>
/// Hierarchical tag node with nested children for skill tree representation.
/// </summary>
public record McpTagNodeDto(
    string Id,
    string Name,
    int Type,
    string? Group,
    IReadOnlyList<McpTagTranslationDto> Translations,
    IReadOnlyList<McpTagNodeDto> Children
);

/// <summary>
/// Response containing the full skill hierarchy tree.
/// </summary>
public record McpSkillHierarchyResponse(
    IReadOnlyList<McpTagNodeDto> Roots,
    int TotalTags,
    DateTimeOffset GeneratedAt
);
