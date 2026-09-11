using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Runtime.CompilerServices;
using MB.ComTools.Apps.Content.Services.Agent;

namespace MB.ComTools.Apps.Content.Services;

public partial class AgentService
{
    // ===============================================================
    // PHASE 4b — MCP PAYLOAD EXTRACTION
    // ===============================================================

    private static List<CourseInfo> ExtractCourses(string? rawMcpResponse)
    {
        return TryParseMcpPayload(rawMcpResponse, out var root)
            ? ExtractCourses(root)
            : new List<CourseInfo>();
    }

    private static List<CourseInfo> ExtractCourses(JsonElement root)
    {
        var result = new List<CourseInfo>();

        foreach (var element in CollectObjects(root, LooksLikeCourse))
        {
            if (!TryGetString(element, out var title, CourseTitleKeys))
            {
                continue;
            }

            var deepLink = TryGetString(element, out var link, "deepLink", "DeepLink", "trainingUrl", "TrainingUrl")
                ? link
                : null;

            var id = TryGetString(element, out var rawId,
                        "id", "Id", "courseId", "CourseId", "baseEntryId", "BaseEntryId")
                ? rawId
                : ExtractFromDeepLink(deepLink, ContentIdRegex);

            result.Add(new CourseInfo(
                Id: id,
                Title: title!,
                Summary: TryGetString(element, out var summary, "summary", "Summary", "description", "Description")
                    ? summary
                    : null,
                Duration: TryGetString(element, out var duration, "durationInHours", "DurationInHours")
                    ? duration
                    : null,
                Platform: TryGetString(element, out var platform, "sourcePlatform", "SourcePlatform")
                    ? platform
                    : null,
                Category: TryGetString(element, out var category,
                              "childTagName", "ChildTagName", "clusterName", "ClusterName",
                              "parentTagName", "ParentTagName")
                    ? category
                    : null,
                Instructor: TryGetString(element, out var instructor, "instructor", "Instructor")
                    ? instructor
                    : null,
                // get_course exposes the long course text as description, parsed
                // from ContentJson; the card only carries the short summary.
                Objectives: TryGetString(element, out var objectives,
                                "learningObjectives", "LearningObjectives", "objectives", "Objectives",
                                "description", "Description")
                    ? objectives
                    : null,
                DeepLink: deepLink,
                Requirement: ExtractRequirement(element),
                IsActive: TryGetBool(element, "isActive", "IsActive") ?? true,
                HasDetail: false));
        }

        return Distinct(result, c => c.Id ?? c.Title);
    }

    private static List<ProfileInfo> ExtractProfiles(string? rawMcpResponse)
    {
        var result = new List<ProfileInfo>();

        if (!TryParseMcpPayload(rawMcpResponse, out var root))
        {
            return result;
        }

        foreach (var element in CollectObjects(root, LooksLikeProfile))
        {
            if (!TryGetString(element, out var title, ProfileTitleKeys))
            {
                continue;
            }

            var deepLink = TryGetString(element, out var link, "deepLink", "DeepLink") ? link : null;

            var courses = new List<CourseInfo>();

            foreach (var courseElement in CollectObjects(element, LooksLikeCourse))
            {
                if (!TryGetString(courseElement, out var courseTitle, CourseTitleKeys))
                {
                    continue;
                }

                var courseLink = TryGetString(courseElement, out var cLink,
                        "deepLink", "DeepLink", "trainingUrl", "TrainingUrl")
                    ? cLink
                    : null;

                courses.Add(new CourseInfo(
                    Id: TryGetString(courseElement, out var cId, "id", "Id", "courseId", "CourseId", "baseEntryId", "BaseEntryId")
                        ? cId
                        : ExtractFromDeepLink(courseLink, ContentIdRegex),
                    Title: courseTitle!,
                    Summary: TryGetString(courseElement, out var cSummary, "summary", "Summary") ? cSummary : null,
                    Duration: TryGetString(courseElement, out var cDuration, "durationInHours", "DurationInHours") ? cDuration : null,
                    Platform: TryGetString(courseElement, out var cPlatform, "sourcePlatform", "SourcePlatform") ? cPlatform : null,
                    Category: TryGetString(courseElement, out var cCategory, "childTagName", "ChildTagName", "clusterName", "ClusterName") ? cCategory : null,
                    Instructor: null,
                    Objectives: null,
                    DeepLink: courseLink,
                    Requirement: ExtractRequirement(courseElement),
                    IsActive: TryGetBool(courseElement, "isActive", "IsActive") ?? true,
                    HasDetail: false));
            }

            courses = Distinct(courses, c => c.Id ?? c.Title);

            var required = TryGetInt(element,
                               "requiredCoursesCount", "RequiredCoursesCount",
                               "requiredCourseCount", "RequiredCourseCount",
                               "requiredCount", "RequiredCount")
                           ?? courses.Count(c => IsRequired(c.Requirement));

            var optional = TryGetInt(element,
                               "optionalCoursesCount", "OptionalCoursesCount",
                               "optionalCourseCount", "OptionalCourseCount",
                               "optionalCount", "OptionalCount")
                           ?? courses.Count(c => c.Requirement is not null && !IsRequired(c.Requirement));

            result.Add(new ProfileInfo(
                Id: TryGetString(element, out var id, "id", "Id", "profileId", "ProfileId", "baseEntryId", "BaseEntryId")
                    ? id
                    : ExtractFromDeepLink(deepLink, ProfileIdRegex),
                Title: title!,
                Summary: TryGetString(element, out var summary, "summary", "Summary", "description", "Description")
                    ? summary
                    : null,
                Division: TryGetString(element, out var division, "divisionName", "DivisionName", "division", "Division")
                    ? division
                    : null,
                Department: TryGetString(element, out var department, "departmentName", "DepartmentName", "department", "Department")
                    ? department
                    : null,
                RequiredCount: required,
                OptionalCount: optional,
                DeepLink: deepLink,
                Courses: courses));
        }

        return Distinct(result, p => p.Id ?? p.Title);
    }

    private static List<SkillInfo> ExtractSkills(string? rawMcpResponse)
    {
        var result = new List<SkillInfo>();

        if (!TryParseMcpPayload(rawMcpResponse, out var root))
        {
            return result;
        }

        // search_skills returns the whole taxonomy in one payload, so the default
        // collection caps would silently cut the tree off.
        foreach (var element in CollectObjects(root, LooksLikeSkill, maxResults: 300, maxDepth: 12))
        {
            if (!TryGetString(element, out var name, SkillNameKeys))
            {
                continue;
            }

            var children = new List<string>();

            if (TryGetProperty(element, out var childrenElement, "children", "Children", "subSkills", "SubSkills")
                && childrenElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in childrenElement.EnumerateArray())
                {
                    if (TryGetString(child, out var childName, SkillNameKeys))
                    {
                        children.Add(childName!);
                    }
                }
            }

            // Skill groups of a profile carry their courses inline instead of a count.
            var courseCount = TryGetInt(element, "courseCount", "CourseCount", "contentCount", "ContentCount");

            if (courseCount is null
                && TryGetProperty(element, out var coursesElement, "courses", "Courses")
                && coursesElement.ValueKind == JsonValueKind.Array)
            {
                courseCount = coursesElement.GetArrayLength();
            }

            result.Add(new SkillInfo(
                Id: TryGetString(element, out var id, "id", "Id", "tagId", "TagId", "baseEntryId", "BaseEntryId")
                    ? id
                    : null,
                Name: name!,
                Description: TryGetString(element, out var description, "description", "Description", "summary", "Summary")
                    ? description
                    : null,
                Parent: TryGetString(element, out var parent, "parentName", "ParentName", "parentTagName", "ParentTagName")
                    ? parent
                    : null,
                Children: children,
                CourseCount: courseCount ?? 0)
            {
                RequiredCount = TryGetInt(element, "requiredCount", "RequiredCount") ?? 0,
                OptionalCount = TryGetInt(element, "optionalCount", "OptionalCount") ?? 0
            });
        }

        return Distinct(result, s => s.Id ?? s.Name);
    }

    private static List<CollectionInfo> ExtractCollections(string? rawMcpResponse)
    {
        var result = new List<CollectionInfo>();

        if (!TryParseMcpPayload(rawMcpResponse, out var root))
        {
            return result;
        }

        foreach (var element in CollectObjects(root, LooksLikeCollection))
        {
            if (!TryGetString(element, out var title, "title", "Title", "name", "Name"))
            {
                continue;
            }

            result.Add(new CollectionInfo(
                Id: TryGetString(element, out var id, "id", "Id", "collectionId", "CollectionId", "baseEntryId", "BaseEntryId")
                    ? id
                    : null,
                Title: title!,
                Summary: TryGetString(element, out var summary, "summary", "Summary", "description", "Description")
                    ? summary
                    : null,
                ItemCount: TryGetInt(element, "itemCount", "ItemCount", "contentCount", "ContentCount", "count", "Count") ?? 0,
                DeepLink: TryGetString(element, out var link, "deepLink", "DeepLink") ? link : null));
        }

        return Distinct(result, c => c.Id ?? c.Title);
    }

    private static List<DivisionInfo> ExtractDivisions(string? rawMcpResponse)
    {
        var result = new List<DivisionInfo>();

        if (!TryParseMcpPayload(rawMcpResponse, out var root))
        {
            return result;
        }

        foreach (var element in CollectObjects(root, LooksLikeDivision))
        {
            if (!TryGetString(element, out var name, "name", "Name", "title", "Title", "divisionName", "DivisionName"))
            {
                continue;
            }

            var departments = new List<string>();

            if (TryGetProperty(element, out var departmentsElement, "departments", "Departments")
                && departmentsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var department in departmentsElement.EnumerateArray())
                {
                    if (TryGetString(department, out var departmentName, "name", "Name", "title", "Title"))
                    {
                        departments.Add(departmentName!);
                    }
                }
            }

            result.Add(new DivisionInfo(
                Id: TryGetString(element, out var id, "id", "Id", "divisionId", "DivisionId", "baseEntryId", "BaseEntryId")
                    ? id
                    : null,
                Name: name!,
                Departments: departments,
                DeepLink: TryGetString(element, out var link, "deepLink", "DeepLink") ? link : null));
        }

        return Distinct(result, d => d.Id ?? d.Name);
    }

    private static bool ContainsAuthError(string rawMcpResponse)
    {
        if (!TryParseMcpPayload(rawMcpResponse, out var root))
        {
            return false;
        }

        return root.ValueKind == JsonValueKind.Object
               && TryGetString(root, out var error, "error", "Error")
               && error!.Contains("auth", StringComparison.OrdinalIgnoreCase);
    }

    // A course assignment carries its requirement as Required, Optional,
    // Recommended or Assigned, so it cannot be reduced to a boolean.
    private static string? ExtractRequirement(JsonElement element)
    {
        if (TryGetString(element, out var requirement, "requirementType", "RequirementType"))
        {
            return requirement;
        }

        return TryGetBool(element, "isMandatory", "IsMandatory", "isRequired", "IsRequired") switch
        {
            true => "Required",
            false => "Optional",
            _ => null
        };
    }

    private static bool IsRequired(string? requirement) =>
        string.Equals(requirement, "Required", StringComparison.OrdinalIgnoreCase)
        || string.Equals(requirement, "Mandatory", StringComparison.OrdinalIgnoreCase);

    private static string? RequirementBadge(string? requirement, string? language)
    {
        if (string.IsNullOrWhiteSpace(requirement))
        {
            return null;
        }

        return requirement.ToLowerInvariant() switch
        {
            "required" or "mandatory" => Localize(language, "Pflichtkurs", "Mandatory"),
            "optional" => Localize(language, "Optional", "Optional"),
            "recommended" => Localize(language, "Empfohlen", "Recommended"),
            _ => null
        };
    }

    // Course assignments inside a profile and bookmarked courses use courseTitle
    // instead of title, so both spellings have to be accepted here.
    private static readonly string[] CourseTitleKeys =
        { "title", "Title", "courseTitle", "CourseTitle" };

    private static readonly string[] ProfileTitleKeys =
        { "title", "Title", "name", "Name", "profileTitle", "ProfileTitle", "profileName", "ProfileName" };

    private static readonly string[] SkillNameKeys =
        { "name", "Name", "skillName", "SkillName", "tagName", "TagName" };

    private static bool LooksLikeCourse(JsonElement element) =>
        HasAnyProperty(element, CourseTitleKeys)
        && HasAnyProperty(element,
            "durationInHours", "DurationInHours",
            "sourcePlatform", "SourcePlatform",
            "trainingUrl", "TrainingUrl",
            "ssoTrainingUrl", "SsoTrainingUrl",
            "clusterName", "ClusterName",
            "childTagName", "ChildTagName",
            "requirementType", "RequirementType",
            "progressPercentage", "ProgressPercentage",
            "courseId", "CourseId");

    private static bool LooksLikeProfile(JsonElement element) =>
        HasAnyProperty(element, ProfileTitleKeys)
        && HasAnyProperty(element,
            "departmentName", "DepartmentName",
            "divisionName", "DivisionName",
            "departmentId", "DepartmentId",
            "divisionId", "DivisionId",
            "requiredCoursesCount", "RequiredCoursesCount",
            "requiredCourseCount", "RequiredCourseCount",
            "optionalCoursesCount", "OptionalCoursesCount",
            "optionalCourseCount", "OptionalCourseCount",
            "skillGroups", "SkillGroups",
            "profileId", "ProfileId",
            "courses", "Courses",
            "courseAssignments", "CourseAssignments");

    // A TagTranslation also carries name and description; only real tag nodes have
    // an id, children or a parent, so translations are filtered out here.
    private static bool LooksLikeSkill(JsonElement element) =>
        HasAnyProperty(element, SkillNameKeys)
        && !HasAnyProperty(element, "durationInHours", "DurationInHours", "sourcePlatform", "SourcePlatform")
        && (HasAnyProperty(element,
                "id", "Id", "tagId", "TagId", "baseEntryId", "BaseEntryId",
                "children", "Children", "parentTagId", "ParentTagId", "group", "Group")
            || !HasAnyProperty(element, "language", "Language"));

    private static bool LooksLikeCollection(JsonElement element) =>
        HasAnyProperty(element, "title", "Title", "name", "Name")
        && HasAnyProperty(element,
            "collectionId", "CollectionId",
            "collectionType", "CollectionType",
            "itemCount", "ItemCount",
            "isPublic", "IsPublic",
            "items", "Items");

    private static bool LooksLikeDivision(JsonElement element) =>
        HasAnyProperty(element, "name", "Name", "title", "Title", "divisionName", "DivisionName")
        && HasAnyProperty(element, "departments", "Departments", "divisionId", "DivisionId");

    /// <summary>
    /// Walks the payload and collects every object matching the predicate. The MCP
    /// DTO shapes differ per tool and nest differently, so a shape-agnostic walk is
    /// more robust than hard-coded property paths.
    /// </summary>
    private static List<JsonElement> CollectObjects(
        JsonElement root,
        Func<JsonElement, bool> predicate,
        int maxResults = 40,
        int maxDepth = 7)
    {
        var results = new List<JsonElement>();
        var queue = new Queue<(JsonElement Element, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0 && results.Count < maxResults)
        {
            var (element, depth) = queue.Dequeue();

            if (depth > maxDepth)
            {
                continue;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (predicate(element))
                    {
                        results.Add(element);
                    }

                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            queue.Enqueue((property.Value, depth + 1));
                        }
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            queue.Enqueue((item, depth + 1));
                        }
                    }

                    break;
            }
        }

        return results;
    }

    private static bool TryParseMcpPayload(string? rawResponse, out JsonElement root)
    {
        root = default;

        if (!TryExtractMcpTextPayload(rawResponse, out var textPayload, out _))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(textPayload);
            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractMcpTextPayload(string? rawResponse, out string textPayload, out string reason)
    {
        textPayload = string.Empty;
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            reason = "MCP response body is empty.";
            return false;
        }

        var trimmed = rawResponse.Trim();

        if (trimmed.StartsWith('{'))
        {
            return TryExtractMcpTextPayloadFromJsonRpc(trimmed, out textPayload, out reason);
        }

        foreach (var block in SplitSseBlocks(rawResponse))
        {
            string? eventName = null;
            var dataLines = new List<string>();

            foreach (var line in block)
            {
                if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                {
                    eventName = line[6..].Trim();
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    dataLines.Add(line[5..].TrimStart());
                }
            }

            if (dataLines.Count == 0)
            {
                continue;
            }

            var dataPayload = string.Join("\n", dataLines).Trim();

            if (string.IsNullOrWhiteSpace(dataPayload) || dataPayload == "[DONE]")
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(eventName)
                && !eventName.Equals("message", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryExtractMcpTextPayloadFromJsonRpc(dataPayload, out textPayload, out reason))
            {
                return true;
            }
        }

        reason = "No parseable SSE message/data payload found in MCP response.";
        return false;
    }

    private static IEnumerable<List<string>> SplitSseBlocks(string input)
    {
        var currentBlock = new List<string>();

        foreach (var rawLine in input.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentBlock.Count > 0)
                {
                    yield return currentBlock;
                    currentBlock = new List<string>();
                }

                continue;
            }

            currentBlock.Add(line);
        }

        if (currentBlock.Count > 0)
        {
            yield return currentBlock;
        }
    }

    private static bool TryExtractMcpTextPayloadFromJsonRpc(
        string jsonRpcPayload,
        out string textPayload,
        out string reason)
    {
        textPayload = string.Empty;
        reason = string.Empty;

        try
        {
            using var rpcDoc = JsonDocument.Parse(jsonRpcPayload);
            var root = rpcDoc.RootElement;

            if (root.TryGetProperty("error", out _))
            {
                reason = "MCP JSON-RPC response contains error.";
                return false;
            }

            if (!root.TryGetProperty("result", out var result)
                || !result.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                reason = "MCP JSON-RPC result.content is missing or invalid.";
                return false;
            }

            foreach (var item in content.EnumerateArray())
            {
                if (!TryGetString(item, out var itemType, "type", "Type")
                    || !string.Equals(itemType, "text", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryGetString(item, out var itemText, "text", "Text")
                    && !string.IsNullOrWhiteSpace(itemText))
                {
                    textPayload = itemText!;
                    reason = "ok";
                    return true;
                }
            }

            reason = "MCP JSON-RPC content does not contain text payload.";
            return false;
        }
        catch (JsonException)
        {
            reason = "MCP JSON-RPC payload is not valid JSON.";
            return false;
        }
    }
}
