namespace MB.ComTools.Apps.Content.Services;

public partial class AgentService
{
    private sealed record IntentRule(
        string[] AllowedTools,
        int MinToolCalls = 0,
        int MaxToolCalls = int.MaxValue,
        bool QueryRequired = false);

    private static readonly Dictionary<AgentIntent, IntentRule>
        IntentToolMatrix =
        new()
        {
            [AgentIntent.CourseSearch] =
                new(
                    ["search_courses"],
                    MinToolCalls: 1,
                    MaxToolCalls: 1,
                    // Empty query is valid for catalogue overviews ("alle Kurse").
                    QueryRequired: false),

            [AgentIntent.CourseDetails] =
                new(
                    [
                        "search_courses",
                        "get_course"
                    ],
                    MinToolCalls: 1,
                    MaxToolCalls: 1,
                    QueryRequired: true),

            [AgentIntent.CourseCompare] =
                new(
                    [
                        "search_courses",
                        "get_course"
                    ],
                    MinToolCalls: 2,
                    MaxToolCalls: 2,
                    QueryRequired: true),

            [AgentIntent.ProfileDetails] =
                new(
                    [
                        "search_profiles",
                        "get_profile",
                        "get_profile_skills"
                    ],
                    MinToolCalls: 1,
                    MaxToolCalls: 2,
                    QueryRequired: true),

            [AgentIntent.ProfileCourses] =
                new(
                    [
                        "search_profiles",
                        "get_profile"
                    ],
                    MinToolCalls: 1,
                    MaxToolCalls: 1,
                    QueryRequired: true),

            [AgentIntent.SkillSearch] =
                new(
                    ["search_skills"],
                    MinToolCalls: 1,
                    MaxToolCalls: 1,
                    QueryRequired: false),

            [AgentIntent.SkillDetails] =
                new(
                    ["get_skill"],
                    MinToolCalls: 1,
                    MaxToolCalls: 1,
                    QueryRequired: true),

            [AgentIntent.SkillCourses] =
                new(
                    ["get_courses_by_tag"],
                    MinToolCalls: 1,
                    MaxToolCalls: 1,
                    QueryRequired: true),

            [AgentIntent.ProfileSearch] =
                new(
                    ["search_profiles"],
                    MinToolCalls: 1,
                    MaxToolCalls: 1,
                    QueryRequired: false),

            [AgentIntent.LearningRecommendation] =
                new(
                    [
                        "get_profile_skills"
                    ],
                    MinToolCalls: 1,
                    MaxToolCalls: 2,
                    QueryRequired: true),

            [AgentIntent.LearningPath] =
                new(
                    [
                        "get_profile_skills"
                    ],
                    MinToolCalls: 2,
                    MaxToolCalls: 2,
                    QueryRequired: true),

            [AgentIntent.DivisionOverview] =
                new(
                    ["get_divisions"],
                    MinToolCalls: 1,
                    MaxToolCalls: 1),

            [AgentIntent.Collections] =
                new(
                    ["search_collections"],
                    MinToolCalls: 1,
                    MaxToolCalls: 1,
                    QueryRequired: false),

            [AgentIntent.Bookmarks] =
                new(
                    ["get_my_bookmarks"],
                    MinToolCalls: 1,
                    MaxToolCalls: 1),

            [AgentIntent.Progress] =
                new(
                    ["get_my_progress"],
                    MinToolCalls: 1,
                    MaxToolCalls: 1),

            [AgentIntent.Capabilities] =
                new([], 0, 0),

            [AgentIntent.SmallTalk] =
                new([], 0, 0),

            [AgentIntent.OutOfScope] =
                new([], 0, 0),

            [AgentIntent.Clarify] =
                new([], 0, 0)
        };

    private ReasoningResult ValidateIntentToolMatrix(
        ReasoningResult reasoning)
    {
        if (!IntentToolMatrix.TryGetValue(
                reasoning.Intent,
                out var rule))
        {
            return reasoning;
        }

        var filtered = new List<ToolCallRequest>();

        foreach (var tool in reasoning.ToolCalls)
        {
            if (!rule.AllowedTools.Contains(
                    tool.Tool,
                    StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "INTENT_TOOL_MISMATCH intent={Intent} tool={Tool}",
                    reasoning.Intent,
                    tool.Tool);

                continue;
            }

            if (rule.QueryRequired
                && string.IsNullOrWhiteSpace(tool.Query)
                && tool.Reference == ReferenceType.None)
            {
                _logger.LogWarning(
                    "INTENT_QUERY_MISSING intent={Intent} tool={Tool}",
                    reasoning.Intent,
                    tool.Tool);

                continue;
            }

            filtered.Add(tool);
        }

        // GenAI sometimes returns the right intent with zero/invalid tools.
        // Repair from slots before falling back to clarify.
        if (filtered.Count < rule.MinToolCalls
            && TryRepairMissingTools(reasoning, rule, out var repaired))
        {
            _logger.LogInformation(
                "INTENT_TOOL_REPAIR intent={Intent} calls={Calls}",
                repaired.Intent,
                DescribeToolCalls(repaired.ToolCalls));

            return repaired;
        }

        if (filtered.Count < rule.MinToolCalls)
        {
            _logger.LogWarning(
                "INTENT_TOO_FEW_TOOLS intent={Intent} expected={Expected} actual={Actual}",
                reasoning.Intent,
                rule.MinToolCalls,
                filtered.Count);

            return reasoning with
            {
                Intent = AgentIntent.Clarify,
                ToolCalls = [],
                ClarificationQuestion =
                Localize(
                reasoning.Language,
                "Bitte präzisiere deine Anfrage.",
                "Please clarify your request.")
            };
        }

        if (filtered.Count > rule.MaxToolCalls)
        {
            filtered = filtered
                .Take(rule.MaxToolCalls)
                .ToList();
        }

        return reasoning with
        {
            ToolCalls = filtered
        };
    }

    /// <summary>
    /// When GenAI classified intent correctly but omitted toolCalls/query, rebuild
    /// the minimum allowed tool calls from slots so MCP still runs.
    /// </summary>
    private static bool TryRepairMissingTools(
        ReasoningResult reasoning,
        IntentRule rule,
        out ReasoningResult repaired)
    {
        repaired = reasoning;

        if (rule.AllowedTools.Length == 0 || rule.MinToolCalls <= 0)
        {
            return false;
        }

        reasoning.Slots.TryGetValue("topic", out var topic);
        reasoning.Slots.TryGetValue("currentProfile", out var currentProfile);
        reasoning.Slots.TryGetValue("targetProfile", out var targetProfile);

        var calls = new List<ToolCallRequest>();

        switch (reasoning.Intent)
        {
            case AgentIntent.CourseSearch:
                calls.Add(new ToolCallRequest(
                    "search_courses",
                    topic ?? string.Empty,
                    ReferenceType.None));
                break;

            case AgentIntent.CourseDetails:
                if (string.IsNullOrWhiteSpace(topic))
                {
                    return false;
                }

                calls.Add(new ToolCallRequest(
                    "search_courses",
                    topic,
                    ReferenceType.None));
                break;

            case AgentIntent.SkillSearch:
                calls.Add(new ToolCallRequest(
                    "search_skills",
                    topic ?? string.Empty,
                    ReferenceType.None));
                break;

            case AgentIntent.SkillDetails:
                if (string.IsNullOrWhiteSpace(topic))
                {
                    return false;
                }

                calls.Add(new ToolCallRequest(
                    "get_skill",
                    topic,
                    ReferenceType.None));
                break;

            case AgentIntent.SkillCourses:
                if (string.IsNullOrWhiteSpace(topic))
                {
                    return false;
                }

                calls.Add(new ToolCallRequest(
                    "get_courses_by_tag",
                    topic,
                    ReferenceType.None));
                break;

            case AgentIntent.ProfileSearch:
                calls.Add(new ToolCallRequest(
                    "search_profiles",
                    topic ?? string.Empty,
                    ReferenceType.None));
                break;

            case AgentIntent.ProfileDetails:
            {
                var profile = FirstNonEmpty(currentProfile, topic);

                if (string.IsNullOrWhiteSpace(profile))
                {
                    return false;
                }

                calls.Add(new ToolCallRequest(
                    "get_profile_skills",
                    profile,
                    ReferenceType.None));
                break;
            }

            case AgentIntent.ProfileCourses:
            {
                var profile = FirstNonEmpty(currentProfile, topic);

                if (string.IsNullOrWhiteSpace(profile))
                {
                    return false;
                }

                calls.Add(new ToolCallRequest(
                    "search_profiles",
                    profile,
                    ReferenceType.None));
                break;
            }

            case AgentIntent.LearningPath:
            {
                if (string.IsNullOrWhiteSpace(currentProfile)
                    || string.IsNullOrWhiteSpace(targetProfile))
                {
                    return false;
                }

                calls.Add(new ToolCallRequest(
                    "get_profile_skills",
                    currentProfile,
                    ReferenceType.None));
                calls.Add(new ToolCallRequest(
                    "get_profile_skills",
                    targetProfile,
                    ReferenceType.None));
                break;
            }

            case AgentIntent.LearningRecommendation:
            {
                var profile = FirstNonEmpty(targetProfile, currentProfile, topic);

                if (string.IsNullOrWhiteSpace(profile))
                {
                    return false;
                }

                calls.Add(new ToolCallRequest(
                    "get_profile_skills",
                    profile,
                    ReferenceType.None));
                break;
            }

            case AgentIntent.DivisionOverview:
                calls.Add(new ToolCallRequest(
                    "get_divisions",
                    string.Empty,
                    ReferenceType.None));
                break;

            case AgentIntent.Collections:
                calls.Add(new ToolCallRequest(
                    "search_collections",
                    topic ?? string.Empty,
                    ReferenceType.None));
                break;

            case AgentIntent.Bookmarks:
                calls.Add(new ToolCallRequest(
                    "get_my_bookmarks",
                    string.Empty,
                    ReferenceType.None));
                break;

            case AgentIntent.Progress:
                calls.Add(new ToolCallRequest(
                    "get_my_progress",
                    string.Empty,
                    ReferenceType.None));
                break;

            default:
                return false;
        }

        if (calls.Count < rule.MinToolCalls)
        {
            return false;
        }

        if (rule.QueryRequired
            && calls.Any(call =>
                string.IsNullOrWhiteSpace(call.Query)
                && call.Reference == ReferenceType.None))
        {
            return false;
        }

        repaired = reasoning with
        {
            ToolCalls = calls.Take(rule.MaxToolCalls).ToList()
        };

        return true;
    }
}