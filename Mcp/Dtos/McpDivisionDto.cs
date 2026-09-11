namespace MB.ComTools.Apps.Setup.Mcp.Dtos;

public record McpDepartmentDto(string Id, string Title);

public record McpDivisionDto(
    string Id,
    string Title,
    IReadOnlyList<McpDepartmentDto> Departments,
    string DeepLink
);

public record McpDivisionListResponse(
    IReadOnlyList<McpDivisionDto> Divisions,
    DateTimeOffset GeneratedAt
);
