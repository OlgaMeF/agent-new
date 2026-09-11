using MB.ComTools.Apps.Data;
using MB.ComTools.Apps.Setup.Mcp.Dtos;
using MB.Core.Types;
using MB.Core.Types.Learning;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace MB.ComTools.Apps.Setup.Mcp.Tools;

/// <summary>
/// MCP Tools for querying LearnProfile data and skill hierarchies.
/// Provides search, detail retrieval, and skill tree operations.
/// </summary>
public class McpProfileTools
{
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<McpProfileTools> _logger;
    private readonly McpCacheService _cache;

    public McpProfileTools(
        AppDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<McpProfileTools> logger,
        McpCacheService cache)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _cache = cache;
    }

    /// <summary>
    /// Search for LearnProfiles with optional filters for division, department, and query text.
    /// </summary>
    [McpServerTool, Description("Search learning profiles (curated role-based course collections). Optionally filter by division ID, department ID, or a free-text query on title and summary. Use get_divisions to discover valid division and department IDs.")]
    public async Task<McpProfileListResponse> search_profiles(
        string? divisionId = null,
        string? departmentId = null,
        string? query = null,
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

        var cacheKey = $"profiles:search:{McpCacheService.HashParams(new { divisionId, departmentId, query, limit, offset })}";
        return await _cache.GetOrCreateAsync(
            cacheKey,
            "profiles",
            async () =>
            {
                // Base query with standard filters
                var profileQuery = _context.LearnProfiles
                    .Where(p => !p.IsDeleted)
                    .Where(p => p.IsVisible);

                // Apply divisionId filter through Department → Division relationship
                if (!string.IsNullOrWhiteSpace(divisionId))
                {
                    profileQuery = profileQuery.Where(p =>
                        p.DepartmentId != null &&
                        _context.LearnDepartments.Any(d =>
                            d.BaseEntryId == p.DepartmentId &&
                            d.DivisionId == divisionId));
                }

                // Apply departmentId filter directly
                if (!string.IsNullOrWhiteSpace(departmentId))
                {
                    profileQuery = profileQuery.Where(p => p.DepartmentId == departmentId);
                }

                // Apply text search on Title and Summary
                if (!string.IsNullOrWhiteSpace(query))
                {
                    profileQuery = profileQuery.Where(p =>
                        EF.Functions.ILike(p.Title, $"%{query}%") ||
                        EF.Functions.ILike(p.Summary ?? "", $"%{query}%"));
                }

                // Order by DisplayDate descending
                var orderedQuery = profileQuery.OrderByDescending(p => p.DisplayDate);

                // Get total count before pagination
                var totalCount = await orderedQuery.CountAsync(cancellationToken);

                // Apply pagination
                var profiles = await orderedQuery
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
                        TotalCourses: p.RequiredCount + p.OptionalCount,
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
            },
            isNegative: result => result.Profiles.Count == 0);
    }

    /// <summary>
    /// Get skills and grouped course assignments for a learning profile.
    /// </summary>
    [McpServerTool, Description("Get course assignments for a learning profile, grouped by course cluster. The current data model does not provide a verified profile-skill relation, so clusters are not reported as skills.")]
    public async Task<McpProfileSkillsResponse?> get_profile_skills(
        [Description("Profile name or search term to find the profile.")] string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var cacheKey = $"profiles:skills:{McpCacheService.HashParams(new { query })}";
        return await _cache.GetOrCreateAsync(
            cacheKey,
            "profiles",
            async () =>
            {
                var profile = await _context.LearnProfiles
                    .Where(p => !p.IsDeleted
                        && p.IsVisible
                        && (EF.Functions.ILike(p.Title, $"%{query}%")
                            || p.BaseEntryId == query))
                    .OrderByDescending(p => p.DisplayDate)
                    .FirstOrDefaultAsync(cancellationToken);

                if (profile is null)
                {
                    _logger.LogInformation("get_profile_skills: no profile found for query={Query}", query);
                    throw new McpToolException(
                        "not_found",
                        $"Profile '{query}' was not found.");
                }

                var assignments = await _context.LearnProfileCourseAssignments
                    .Where(ca => ca.ProfileId == profile.BaseEntryId)
                    .Join(
                        _context.LearnCourses,
                        ca => ca.CourseId,
                        c => c.BaseEntryId,
                        (ca, c) => new { ca, c })
                    .Where(x => !x.c.IsDeleted)
                    .OrderBy(x => x.ca.SortOrder)
                    .Select(x => new
                    {
                        x.ca.CourseId,
                        CourseTitle = x.c.Title,
                        x.ca.RequirementType,
                        x.ca.SortOrder,
                        CourseClusterName = x.c.ClusterName ?? "Allgemein",
                        x.c.DurationInHours,
                        x.c.IsActive
                    })
                    .ToListAsync(cancellationToken);

                var httpContext = _httpContextAccessor.HttpContext;

                var courseClusters = assignments
                    .GroupBy(a => a.CourseClusterName, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key)
                    .Select(g => new McpProfileCourseClusterGroupDto(
                        CourseClusterName: g.Key,
                        RequiredCount: g.Count(a => a.RequirementType == CourseRequirementType.Mandatory),
                        OptionalCount: g.Count(a => a.RequirementType == CourseRequirementType.Optional),
                        Courses: g.Select(a => new McpProfileCourseDto(
                            CourseId: a.CourseId,
                            CourseTitle: a.CourseTitle,
                            RequirementType: MapRequirementType(a.RequirementType),
                            SortOrder: a.SortOrder,
                            DeepLink: BuildDeepLink($"/learn-skills/pages/content/{a.CourseId}", httpContext),
                            CourseClusterName: a.CourseClusterName,
                            EvidenceSource: "course.cluster"
                        )).ToList()))
                    .ToList();

                var requiredTotal = assignments.Count(a => a.RequirementType == CourseRequirementType.Mandatory);
                var optionalTotal = assignments.Count(a => a.RequirementType == CourseRequirementType.Optional);

                return new McpProfileSkillsResponse(
                    ProfileId: profile.BaseEntryId,
                    Title: profile.Title,
                    Summary: profile.Summary,
                    TotalCourses: assignments.Count,
                    RequiredCoursesCount: requiredTotal,
                    OptionalCoursesCount: optionalTotal,
                    CourseClusters: courseClusters,
                    DeepLink: BuildDeepLink($"/learn-skills/profile/view/{profile.BaseEntryId}", httpContext)
                );
            });
    }
        /// <summary>
    /// Get details for one skill/category tag by ID, German name, English name or translation.
    /// </summary>
    [McpServerTool, Description("Get details for one learning skill or skill category by ID, name, or translated name.")]
    public async Task<McpSkillDto?> get_skill(
        [Description("Skill or category ID/name, e.g. GenAI, Moderation, Kommunikation.")] string skillName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skillName))
        {
            throw new ArgumentException("skillName is required.", nameof(skillName));
        }

        var normalized = skillName.Trim();

        var cacheKey = $"tags:skill:{McpCacheService.HashParams(new { normalized })}";

        return await _cache.GetOrCreateAsync(
            cacheKey,
            "tags",
            async () =>
            {
                var tags = await _context.Tags
                    .AsNoTracking()
                    .Where(t => (t.SiteUrl == "learn-skills" || t.SiteUrl == null) && !t.IsDeleted)
                    .Include(t => t.ParentTag)
                    .Include(t => t.Translations)
                    .ToListAsync(cancellationToken);

                var exactMatches = tags
                    .Where(t => string.Equals(t.BaseEntryId, normalized, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(t.Name, normalized, StringComparison.OrdinalIgnoreCase)
                        || t.Translations.Any(tr =>
                            string.Equals(tr.Name, normalized, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var tag = exactMatches.Count switch
                {
                    0 => null,
                    1 => exactMatches[0],
                    _ => throw new McpToolException(
                        "ambiguous",
                        $"Skill '{skillName}' matches multiple tags: "
                        + string.Join(", ", exactMatches.Select(t => t.BaseEntryId)))
                };

                if (tag is null)
                {
                    var partialMatches = tags
                        .Where(t => t.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                            || t.Translations.Any(tr =>
                                tr.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    tag = partialMatches.Count switch
                    {
                        0 => null,
                        1 => partialMatches[0],
                        _ => throw new McpToolException(
                            "ambiguous",
                            $"Skill '{skillName}' matches multiple tags: "
                            + string.Join(", ", partialMatches.Select(t => t.BaseEntryId)))
                    };
                }

                if (tag is null)
                {
                    _logger.LogInformation("get_skill: no skill/category found for query={Query}", skillName);
                    throw new McpToolException(
                        "not_found",
                        $"Skill '{skillName}' was not found.");
                }

                var translations = tag.Translations
                    .Select(tr => new McpTagTranslationDto(
                        Language: LanguageCodeToString((LanguageCode)tr.Language),
                        Name: tr.Name))
                    .ToList()
                    .AsReadOnly();

                return new McpSkillDto(
                    Id: tag.BaseEntryId,
                    Name: tag.Name,
                    Description: tag.Description,
                    Type: tag.Type.ToString(),
                    Group: tag.Group,
                    ParentTagId: tag.ParentTagId,
                    ParentTagName: tag.ParentTag?.Name,
                    Translations: translations
                );
            });
    }
    /// <summary>
    /// Get full profile details including course assignments.
    /// </summary>
    [McpServerTool, Description("Get full details of a learning profile including all assigned courses with their requirement type (required / optional / recommended), platform, duration, and deep links.")]
    public async Task<McpProfileDetailDto?> get_profile(
        [Description("The profile ID (BaseEntryId) to look up.")] string profileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("profileId is required.", nameof(profileId));
        }

        return await _cache.GetOrCreateAsync(
            $"profiles:detail:{profileId}",
            "profiles",
            async () =>
            {
                var profile = await _context.LearnProfiles
                    .Where(p => p.BaseEntryId == profileId)
                    .Where(p => !p.IsDeleted)
                    .Where(p => p.IsVisible)
                    .FirstOrDefaultAsync(cancellationToken);

                if (profile == null)
                {
                    _logger.LogWarning("Profile with ID {ProfileId} not found or not published", profileId);
                    throw new McpToolException(
                        "not_found",
                        $"Profile '{profileId}' was not found.");
                }

                var httpContext = _httpContextAccessor.HttpContext;
                return await MapToDetailDto(profile, httpContext, cancellationToken);
            });
    }

    /// <summary>
    /// Build a hierarchical skill tree from tags for learn-skills.
    /// </summary>
    [McpServerTool, Description("Returns the skill taxonomy as a hierarchical tree. Without query this is an explicit full-hierarchy request; with query only matching skill branches are returned, limited by topK.")]
    public async Task<McpSkillHierarchyResponse> search_skills(
        string? query = null,
        int topK = 100,
        CancellationToken cancellationToken = default)
    {
        if (query is not null
            && (string.IsNullOrWhiteSpace(query) || query.Trim().Length > 200))
        {
            throw new ArgumentException(
                "query must be null for the full hierarchy or contain 1-200 non-whitespace characters.",
                nameof(query));
        }

        topK = Math.Clamp(topK, 1, 500);
        var normalizedQuery = query?.Trim();
        return await _cache.GetOrCreateAsync(
            $"tags:hierarchy:{McpCacheService.HashParams(new { query = normalizedQuery, topK })}",
            "tags",
            async () =>
            {
                _logger.LogInformation("Building skill hierarchy for learn-skills");

                // Load all tags for learn-skills in a single query
                var allTags = await _context.Tags
                    .AsNoTracking()
                    .Where(t => (t.SiteUrl == "learn-skills" || t.SiteUrl == null) && !t.IsDeleted)
                    .Include(t => t.Translations)
                    .OrderBy(t => t.Name)
                    .ToListAsync(cancellationToken);

                // Build tree in memory
                var nodeDictionary = new Dictionary<string, McpTagNodeDto>();
                var childrenLists = new Dictionary<string, List<McpTagNodeDto>>();

                // First pass: create all nodes without children
                foreach (var tag in allTags)
                {
                    var translations = tag.Translations
                        .Select(tr => new McpTagTranslationDto(
                            Language: LanguageCodeToString((LanguageCode)tr.Language),
                            Name: tr.Name))
                        .ToList()
                        .AsReadOnly();

                    var node = new McpTagNodeDto(
                        Id: tag.BaseEntryId,
                        Name: tag.Name,
                        Type: (int)tag.Type,
                        Group: tag.Group,
                        Translations: translations,
                        Children: Array.Empty<McpTagNodeDto>()  // Temporary empty list
                    );

                    nodeDictionary[tag.BaseEntryId] = node;
                    childrenLists[tag.BaseEntryId] = new List<McpTagNodeDto>();
                }

                // Second pass: build parent-child relationships
                var rootNodes = new List<McpTagNodeDto>();

                foreach (var tag in allTags)
                {
                    var node = nodeDictionary[tag.BaseEntryId];

                    if (string.IsNullOrEmpty(tag.ParentTagId))
                    {
                        // Root node
                        rootNodes.Add(node);
                    }
                    else if (nodeDictionary.ContainsKey(tag.ParentTagId))
                    {
                        // Add to parent's children list
                        childrenLists[tag.ParentTagId].Add(node);
                    }
                    else
                    {
                        // Parent not found, treat as root
                        _logger.LogWarning("Tag {TagId} references non-existent parent {ParentId}, treating as root",
                            tag.BaseEntryId, tag.ParentTagId);
                        rootNodes.Add(node);
                    }
                }

                // Third pass: replace empty children collections with actual children
                var finalNodes = new Dictionary<string, McpTagNodeDto>();
                foreach (var kvp in nodeDictionary)
                {
                    var tagId = kvp.Key;
                    var node = kvp.Value;
                    var children = childrenLists[tagId];

                    if (children.Count > 0)
                    {
                        // Recreate node with actual children
                        var updatedNode = node with { Children = children.AsReadOnly() };
                        finalNodes[tagId] = updatedNode;

                        // Update in root list if it's a root
                        var rootIndex = rootNodes.FindIndex(n => n.Id == tagId);
                        if (rootIndex >= 0)
                        {
                            rootNodes[rootIndex] = updatedNode;
                        }

                        // Update in parent's children list
                        foreach (var parentId in childrenLists.Keys)
                        {
                            var parentChildren = childrenLists[parentId];
                            var childIndex = parentChildren.FindIndex(n => n.Id == tagId);
                            if (childIndex >= 0)
                            {
                                parentChildren[childIndex] = updatedNode;
                            }
                        }
                    }
                    else
                    {
                        finalNodes[tagId] = node;
                    }
                }

                // Rebuild root nodes with proper children
                var finalRoots = new List<McpTagNodeDto>();
                foreach (var rootNode in rootNodes)
                {
                    finalRoots.Add(BuildNodeWithChildren(rootNode, childrenLists, finalNodes));
                }

                var roots = string.IsNullOrWhiteSpace(normalizedQuery)
                    ? finalRoots
                    : FilterSkillHierarchy(finalRoots, normalizedQuery, topK);

                return new McpSkillHierarchyResponse(
                    Roots: roots.AsReadOnly(),
                    TotalTags: string.IsNullOrWhiteSpace(normalizedQuery)
                        ? allTags.Count
                        : CountSkillNodes(roots),
                    GeneratedAt: DateTimeOffset.UtcNow
                );
            });
    }

    #region Private Helper Methods

    private static List<McpTagNodeDto> FilterSkillHierarchy(
        IReadOnlyList<McpTagNodeDto> roots,
        string query,
        int topK)
    {
        var matchCount = 0;
        var filteredRoots = new List<McpTagNodeDto>();

        foreach (var root in roots)
        {
            var filtered = FilterSkillNode(root, query, ref matchCount, topK);
            if (filtered is not null)
            {
                filteredRoots.Add(filtered);
            }

            if (matchCount >= topK)
            {
                break;
            }
        }

        return filteredRoots;
    }

    private static McpTagNodeDto? FilterSkillNode(
        McpTagNodeDto node,
        string query,
        ref int matchCount,
        int topK)
    {
        var directMatch = node.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || node.Translations.Any(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        var children = new List<McpTagNodeDto>();

        foreach (var child in node.Children)
        {
            if (matchCount >= topK)
            {
                break;
            }

            var filtered = FilterSkillNode(child, query, ref matchCount, topK);
            if (filtered is not null)
            {
                children.Add(filtered);
            }
        }

        if (!directMatch && children.Count == 0)
        {
            return null;
        }

        if (directMatch && matchCount < topK)
        {
            matchCount++;
        }

        return node with { Children = children.AsReadOnly() };
    }

    private static int CountSkillNodes(IEnumerable<McpTagNodeDto> nodes) =>
        nodes.Sum(node => 1 + CountSkillNodes(node.Children));

    private McpTagNodeDto BuildNodeWithChildren(
        McpTagNodeDto node,
        Dictionary<string, List<McpTagNodeDto>> childrenLists,
        Dictionary<string, McpTagNodeDto> nodeDictionary)
    {
        var children = childrenLists[node.Id];
        if (children.Count == 0)
        {
            return node;
        }

        var builtChildren = children
            .Select(child => BuildNodeWithChildren(child, childrenLists, nodeDictionary))
            .ToList()
            .AsReadOnly();

        return node with { Children = builtChildren };
    }

    private async Task<McpProfileDetailDto> MapToDetailDto(
        LearnProfile profile,
        HttpContext? httpContext,
        CancellationToken cancellationToken)
    {
        // Load course assignments ordered by SortOrder
        var courseAssignments = await _context.LearnProfileCourseAssignments
            .Where(ca => ca.ProfileId == profile.BaseEntryId)
            .Join(
                _context.LearnCourses,
                ca => ca.CourseId,
                c => c.BaseEntryId,
                (ca, c) => new { ca, c }
            )
            .Where(x => !x.c.IsDeleted)
            .OrderBy(x => x.ca.SortOrder)
            .Select(x => new McpProfileCourseDto(
                CourseId: x.ca.CourseId,
                CourseTitle: x.c.Title,
                RequirementType: MapRequirementType(x.ca.RequirementType),
                SortOrder: x.ca.SortOrder,
                DeepLink: BuildDeepLink($"/learn-skills/pages/content/{x.ca.CourseId}", httpContext)
            ))
            .ToListAsync(cancellationToken);

        var requiredCount = courseAssignments.Count(ca => ca.RequirementType == "Required");
        var optionalCount = courseAssignments.Count(ca => ca.RequirementType == "Optional");

        // Load department/division info
        string? divisionId = null;
        string? divisionName = null;
        string? departmentName = profile.DeptName;

        if (!string.IsNullOrEmpty(profile.DepartmentId))
        {
            var dept = await _context.LearnDepartments
                .Where(d => d.BaseEntryId == profile.DepartmentId)
                .Select(d => new { d.Title, d.DivisionId })
                .FirstOrDefaultAsync(cancellationToken);

            if (dept != null)
            {
                departmentName = dept.Title;
                divisionId = dept.DivisionId;

                if (!string.IsNullOrEmpty(dept.DivisionId))
                {
                    divisionName = await _context.LearnDivisions
                        .Where(dv => dv.BaseEntryId == dept.DivisionId)
                        .Select(dv => dv.Title)
                        .FirstOrDefaultAsync(cancellationToken);
                }
            }
        }

        return new McpProfileDetailDto(
            Id: profile.BaseEntryId,
            Title: profile.Title,
            Summary: profile.Summary,
            DivisionId: divisionId,
            DivisionName: divisionName,
            DepartmentId: profile.DepartmentId,
            DepartmentName: departmentName,
            TotalCourses: profile.TotalCourses,
            RequiredCoursesCount: requiredCount,
            OptionalCoursesCount: optionalCount,
            CourseAssignments: courseAssignments,
            DeepLink: BuildDeepLink($"/learn-skills/profile/view/{profile.BaseEntryId}", httpContext)
        );
    }

    private static string MapRequirementType(CourseRequirementType requirementType)
    {
        return requirementType switch
        {
            CourseRequirementType.Mandatory => "Required",
            CourseRequirementType.Optional => "Optional",
            CourseRequirementType.Recommended => "Recommended",
            _ => "Assigned"
        };
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

    private static string LanguageCodeToString(LanguageCode code)
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

    #endregion
}
