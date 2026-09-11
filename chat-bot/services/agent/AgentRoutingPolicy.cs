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
                    QueryRequired: true),

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
                    QueryRequired: true),

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
                    QueryRequired: true),

            [AgentIntent.LearningRecommendation] =
                new(
                    [
                        "get_profile_skills"
                    ],
                    MinToolCalls: 1,
                    MaxToolCalls: 1,
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
                    QueryRequired: true),

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
}