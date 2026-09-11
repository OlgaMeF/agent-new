using System.Text.RegularExpressions;
using MB.ComTools.Apps.Content.Services.Agent;

namespace MB.ComTools.Apps.Content.Services;

public partial class AgentService
{
    // ===============================================================
    // DETERMINISTIC FOLLOW-UPS (before router LLM)
    /// Priority (top wins):
    // 1. PendingSlot
    // 2. Positional reference
    // 3. Vague detail follow-up
    // 4. Similar courses
    // 5. Deterministic intent resolution
    // 6. Contextual course request
    // 7. Active course attribute
    //   8. Role → get_profile_skills
    //   9. Skill explain → get_skill
    //  10. Beginner / "eignet sich" recommendation
    //  11. Advisory (besser / erweitern / GenAI)
    //  12. "Kurse dazu" (ActiveSkill → ActiveProfile → LastSearchTopic)
    //  13. Active course attribute → get_course
    // ===============================================================

    private static bool IsSimilarCoursesRequest(string message)
    {
        var text = message.ToLowerInvariant();

        var mentionsCourse =
            text.Contains("kurs") ||
            text.Contains("course");

        var asksSimilar =
            text.Contains("ähnlich") ||
            text.Contains("vergleichbar") ||
            text.Contains("weitere") ||
            text.Contains("similar") ||
            text.Contains("more");

        return mentionsCourse && asksSimilar;
    }
    private static bool IsCourseFollowUpRequest(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.Trim().ToLowerInvariant();

        var asksForCourses =
            normalized.Contains("kurs")
            || normalized.Contains("course");

        var asksForMore =
            normalized.Contains("weitere")
            || normalized.Contains("ähnlich")
            || normalized.Contains("aehnlich")
            || normalized.Contains("vergleichbar")
            || normalized.Contains("fortgeschritten")
            || normalized.Contains("advanced")
            || normalized.Contains("noch mehr")
            || normalized.Contains("more")
            || normalized.Contains("similar")
            || normalized.Contains("further");

        return asksForCourses && asksForMore;
    }

    private static bool TryExtractTargetProfileOnly(
        string message,
        out string targetProfile)
    {
        targetProfile = string.Empty;

        var match = Regex.Match(
            message.Trim(),
            @"\bich\s+möchte\s+(?<target>.+?)\s+werden\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return false;
        }

        targetProfile =
            match.Groups["target"].Value.Trim();

        return targetProfile.Length >= 3;
    }

    private static bool TryResolveDeterministicIntent(
        string message,
        ConversationState conversation,
        string language,
        out ReasoningResult reasoning)
    {
        if (TryExtractRoleTransition(
                message,
                out var currentProfile,
                out var targetProfile))
        {
            reasoning = new ReasoningResult(
                Intent: AgentIntent.LearningRecommendation,
                Language: language,
                ClarificationQuestion: null,
                Slots: new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["currentProfile"] = currentProfile,
                    ["targetProfile"] = targetProfile
                },
                ToolCalls: []);

            return true;
        }

        if (TryExtractExplicitCompareTitles(message, out var compareTitles)
            && compareTitles.Count >= 2
            && compareTitles.All(LooksLikeCatalogueCourseTitle))
        {
            reasoning = new ReasoningResult(
                Intent: AgentIntent.CourseCompare,
                Language: language,
                ClarificationQuestion: null,
                Slots: new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["firstCourse"] = compareTitles[0],
                    ["secondCourse"] = compareTitles[1],
                    ["topic"] = string.Join(
                        " vs ",
                        compareTitles.Take(2))
                },
                ToolCalls:
                [
                    new ToolCallRequest(
                        "search_courses",
                        compareTitles[0],
                        ReferenceType.None),

                    new ToolCallRequest(
                        "search_courses",
                        compareTitles[1],
                        ReferenceType.None)
                ]);

            return true;
        }

        if (IsConceptualTopicCompare(message)
            && TryResolveAdvisoryTopic(
                message,
                conversation,
                out var conceptTopic))
        {
            reasoning = BuildCourseSearchReasoning(
                conceptTopic,
                language,
                allowGeneralKnowledge: true);

            return true;
        }

        if (TryExtractNamedCourseTitle(message, out var namedCourse)
            && !IsOrdinalPhrase(namedCourse))
        {
            reasoning = new ReasoningResult(
                Intent: AgentIntent.CourseDetails,
                Language: language,
                ClarificationQuestion: null,
                Slots: new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["topic"] = namedCourse
                },
                ToolCalls:
                [
                    new ToolCallRequest(
                        "search_courses",
                        namedCourse,
                        ReferenceType.None)
                ]);

            return true;
        }

        // Role detection must run before generic skill explanation.
        if (TryExtractRoleSkillQuery(message, out var roleName))
        {
            reasoning = new ReasoningResult(
                Intent: AgentIntent.ProfileDetails,
                Language: language,
                ClarificationQuestion: null,
                Slots: new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["topic"] = roleName,
                    ["currentProfile"] = roleName
                },
                ToolCalls:
                [
                    new ToolCallRequest(
                        "get_profile_skills",
                        roleName,
                        ReferenceType.None)
                ]);

            return true;
        }

        if (TryExtractSkillExplainQuery(message, out var skillTopic))
        {
            skillTopic = SanitizeTopicQuery(skillTopic) ?? skillTopic;
            
            reasoning = new ReasoningResult(
                Intent: AgentIntent.SkillDetails,
                Language: language,
                ClarificationQuestion: null,
                Slots: new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["topic"] = skillTopic
                },
                ToolCalls:
                [
                    new ToolCallRequest(
                        "get_skill",
                        skillTopic,
                        ReferenceType.None)
                ]);

            return true;
        }

        if (IsCourseRecommendationRequest(message)
            && TryResolveRecommendationTopic(
                message,
                conversation,
                out var recommendationTopic))
        {
            reasoning = BuildCourseSearchReasoning(
                recommendationTopic,
                language,
                allowGeneralKnowledge: true,
                audience: "beginner");

            return true;
        }

        if (IsAdvisoryLearningRequest(message)
            && TryResolveAdvisoryTopic(
                message,
                conversation,
                out var advisoryTopic))
        {
            reasoning = BuildCourseSearchReasoning(
                advisoryTopic,
                language,
                allowGeneralKnowledge: true);

            return true;
        }

        reasoning = default!;
        return false;
    }

    private static bool TryResolveFollowUp(
        string message,
        ConversationState conversation,
        out ReasoningResult reasoning)
    {
        if (!string.IsNullOrWhiteSpace(conversation.PendingSlot)
            && IsShortSlotAnswer(message))
        {
            reasoning = ResolvePendingSlot(message, conversation);
            return true;
        }

        var position = ExtractFollowUpPosition(message);

        if (position is not null)
        {
            reasoning = conversation.LastAddressableItems.Count > 0
                ? ResolvePositionalFollowUp(position.Value, conversation)
                : BuildMissingPositionReasoning(conversation.Language);

            return true;
        }

        if (IsVagueDetailFollowUp(message)
            && conversation.LastAddressableItems.Count > 1
            && conversation.ActiveCourse is null
            && conversation.ActiveProfile is null)
        {
            reasoning = new ReasoningResult(
                Intent: AgentIntent.Clarify,
                Language: conversation.Language,
                ClarificationQuestion: Localize(
                    conversation.Language,
                    "Welchen der gezeigten Einträge meinst du? Sag zum Beispiel „die zweite“.",
                    "Which of the shown entries do you mean? For example say \"the second one\"."),
                Slots: new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase),
                ToolCalls: []);

            return true;
        }

        if (IsSimilarCoursesRequest(message))
        {
            var topic = ResolveSimilarCoursesTopic(
                conversation,
                message);

            if (!string.IsNullOrWhiteSpace(topic))
            {
                reasoning = BuildSimilarCoursesReasoning(
                    topic,
                    conversation.ActiveCourse,
                    conversation.Language);

                return true;
            }
        }

        if (TryResolveDeterministicIntent(
            message,
            conversation,
            conversation.Language,
            out reasoning))
        {
            return true;
        }

        if (IsLooseContextualCourseRequest(message, conversation))
        {
            if (conversation.ActiveSkill is not null)
            {
                var skill = conversation.ActiveSkill;

                reasoning = new ReasoningResult(
                    Intent: AgentIntent.SkillCourses,
                    Language: conversation.Language,
                    ClarificationQuestion: null,
                    Slots: new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["topic"] = skill.DisplayName
                    },
                    ToolCalls:
                    [
                        new ToolCallRequest(
                            "get_courses_by_tag",
                            skill.DisplayName,
                            ReferenceType.None)
                    ]);

                return true;
            }

            if (conversation.ActiveProfile is not null)
            {
                var profile = conversation.ActiveProfile;

                if (!string.IsNullOrWhiteSpace(profile.Id))
                {
                    reasoning = new ReasoningResult(
                        Intent: AgentIntent.ProfileCourses,
                        Language: conversation.Language,
                        ClarificationQuestion: null,
                        Slots: new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            ["topic"] = profile.DisplayName
                        },
                        ToolCalls:
                        [
                            new ToolCallRequest(
                                "get_profile",
                                profile.Id,
                                ReferenceType.None)
                        ]);

                    return true;
                }

                // get_profile accepts only BaseEntryId.
                // If no ID is available, search the profile first.
                reasoning = new ReasoningResult(
                    Intent: AgentIntent.ProfileSearch,
                    Language: conversation.Language,
                    ClarificationQuestion: null,
                    Slots: new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["topic"] = profile.DisplayName
                    },
                    ToolCalls:
                    [
                        new ToolCallRequest(
                            "search_profiles",
                            profile.DisplayName,
                            ReferenceType.None)
                    ]);

                return true;
            }

            if (!string.IsNullOrWhiteSpace(conversation.LastSearchTopic)
                && !IsOrdinalPhrase(conversation.LastSearchTopic))
            {
                reasoning = BuildCourseSearchReasoning(
                    conversation.LastSearchTopic,
                    conversation.Language);

                return true;
            }
        }

        if (IsActiveCourseAttributeQuestion(message, conversation))
        {
            var course = conversation.ActiveCourse!;

            if (!string.IsNullOrWhiteSpace(course.Id))
            {
                reasoning = new ReasoningResult(
                    Intent: AgentIntent.CourseDetails,
                    Language: conversation.Language,
                    ClarificationQuestion: null,
                    Slots: new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["topic"] = course.DisplayName
                    },
                    ToolCalls:
                    [
                        new ToolCallRequest(
                            "get_course",
                            course.Id,
                            ReferenceType.None)
                    ]);

                return true;
            }

            reasoning = new ReasoningResult(
                Intent: AgentIntent.CourseSearch,
                Language: conversation.Language,
                ClarificationQuestion: null,
                Slots: new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["topic"] = course.DisplayName
                },
                ToolCalls:
                [
                    new ToolCallRequest(
                        "search_courses",
                        course.DisplayName,
                        ReferenceType.None)
                ]);

            return true;
        }

        reasoning = default!;
        return false;
    }

    private static ReasoningResult ResolvePositionalFollowUp(
        int index,
        ConversationState conversation)
    {
        var selected =
            conversation.LastAddressableItems.ElementAtOrDefault(index);

        if (selected is null)
        {
            return BuildMissingPositionReasoning(
                conversation.Language);
        }

        if (selected.Kind == EntityKind.Course)
        {
            if (!string.IsNullOrWhiteSpace(selected.Id))
            {
                return BuildEntityFollowUp(
                    AgentIntent.CourseDetails,
                    "get_course",
                    selected,
                    conversation.Language);
            }

            return new ReasoningResult(
                Intent: AgentIntent.CourseSearch,
                Language: conversation.Language,
                ClarificationQuestion: null,
                Slots: new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["topic"] = selected.DisplayName
                },
                ToolCalls:
                [
                    new ToolCallRequest(
                        "search_courses",
                        selected.DisplayName,
                        ReferenceType.None)
                ]);
        }

        if (selected.Kind == EntityKind.Profile)
        {
            if (!string.IsNullOrWhiteSpace(selected.Id))
            {
                return BuildEntityFollowUp(
                    AgentIntent.ProfileDetails,
                    "get_profile",
                    selected,
                    conversation.Language);
            }

            return new ReasoningResult(
                Intent: AgentIntent.ProfileSearch,
                Language: conversation.Language,
                ClarificationQuestion: null,
                Slots: new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["topic"] = selected.DisplayName
                },
                ToolCalls:
                [
                    new ToolCallRequest(
                        "search_profiles",
                        selected.DisplayName,
                        ReferenceType.None)
                ]);
        }

        if (selected.Kind == EntityKind.Collection)
        {
            return BuildEntityFollowUp(
                AgentIntent.Collections,
                "search_collections",
                selected,
                conversation.Language);
        }

        if (selected.Kind == EntityKind.Division)
        {
            return BuildEntityFollowUp(
                AgentIntent.DivisionOverview,
                "get_divisions",
                selected,
                conversation.Language,
                divisionSlot: selected.DisplayName);
        }

        return BuildUnsupportedReferenceReasoning(
            conversation.Language);
    }

    private static bool IsVagueDetailFollowUp(string message)
    {
        var text = message.Trim().ToLowerInvariant();

        if (ExtractFollowUpPosition(message) is not null)
        {
            return false;
        }

        return text is "mehr dazu" or "erzähl mir mehr dazu" or "erzaehl mir mehr dazu"
            or "tell me more" or "more about it" or "mehr darüber" or "mehr darueber"
            || text.Contains("erzähl mir mehr dazu", StringComparison.Ordinal)
            || text.Contains("erzaehl mir mehr dazu", StringComparison.Ordinal)
            || text.Contains("tell me more about", StringComparison.Ordinal)
            || (text.Contains("mehr dazu", StringComparison.Ordinal)
                && !text.Contains("kurs", StringComparison.Ordinal));
    }

    private static bool IsActiveCourseAttributeQuestion(
        string message,
        ConversationState conversation)
    {
        if (conversation.ActiveCourse is null)
        {
            return false;
        }

        var text = message.ToLowerInvariant();
        var asksSkills = text.Contains("skill")
            || text.Contains("kompetenz")
            || text.Contains("fähigkeit")
            || text.Contains("faehigkeit")
            || text.Contains("lerne ich")
            || text.Contains("lernt man")
            || text.Contains("inhalte")
            || text.Contains("deckt");

        var refersToCourse = text.Contains("kurs")
            || text.Contains("course")
            || text.Contains("dazu")
            || text.Contains("dieser")
            || text.Contains("dieses")
            || text.Contains("dem ");

        return asksSkills && refersToCourse;
    }

    private static ReasoningResult BuildCourseSearchReasoning(
    string topic,
    string language,
    bool allowGeneralKnowledge = false,
    string? audience = null)
{
    var slots = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["topic"] = topic
    };

    if (allowGeneralKnowledge)
    {
        slots["allowGeneralKnowledge"] = "true";
    }

    if (!string.IsNullOrWhiteSpace(audience))
    {
        slots["audience"] = audience;
    }

    return new ReasoningResult(
        Intent: AgentIntent.CourseSearch,
        Language: language,
        ClarificationQuestion: null,
        Slots: slots,
        ToolCalls:
        [
            new ToolCallRequest(
                "search_courses",
                topic,
                ReferenceType.None)
        ]);
}

    private static string? ResolveSimilarCoursesTopic(
        ConversationState conversation,
        string message)
    {
        var lastTopic = SanitizeTopicQuery(conversation.LastSearchTopic);
        var lastTopicOk = !string.IsNullOrWhiteSpace(lastTopic)
            && !IsOrdinalPhrase(lastTopic)
            && lastTopic.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4;

        // Still inside a search thread (e.g. Kommunikation → first card → ähnlich):
        // prefer the short search topic over a unique compound title that only
        // finds the same course again (Kapitalmarktskommunikation → 0 cards).
        // Exception: a short distinctive token in the title (empathie, python).
        if (lastTopicOk
            && conversation.ActiveCourse is not null
            && TopicOverlapsTitle(lastTopic!, conversation.ActiveCourse.DisplayName))
        {
            var fromActive = PreferCourseKeyword(conversation.ActiveCourse.DisplayName);
            if (IsDistinctiveSimilarKeyword(fromActive, lastTopic!))
            {
                return fromActive;
            }

            return BroadenSimilarTopic(lastTopic!);
        }

        if (conversation.ActiveCourse is not null)
        {
            var fromActive = PreferCourseKeyword(
                conversation.ActiveCourse.DisplayName,
                lastTopicOk ? lastTopic : null);
            if (!string.IsNullOrWhiteSpace(fromActive) && !IsOrdinalPhrase(fromActive))
            {
                // Long compounds only match the opened course; broaden when possible.
                if (IsLongCompoundTopic(fromActive) && lastTopicOk)
                {
                    return BroadenSimilarTopic(lastTopic!);
                }

                return BroadenSimilarTopic(fromActive);
            }
        }

        if (lastTopicOk)
        {
            return BroadenSimilarTopic(lastTopic!);
        }

        if (conversation.LastCourses.Count >= 1)
        {
            var fromCard = PreferCourseKeyword(conversation.LastCourses[0].DisplayName);
            if (!string.IsNullOrWhiteSpace(fromCard) && !IsOrdinalPhrase(fromCard))
            {
                return fromCard;
            }
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="keyword"/> is a short, distinctive subject
    /// (empathie / python) rather than a long compound or program label.
    /// </summary>
    private static bool IsDistinctiveSimilarKeyword(string? keyword, string lastTopic)
    {
        if (string.IsNullOrWhiteSpace(keyword) || IsOrdinalPhrase(keyword))
        {
            return false;
        }

        if (SameText(keyword, lastTopic) || TopicOverlapsTitle(lastTopic, keyword))
        {
            return false;
        }

        var parts = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2 && keyword.Length <= 14;
    }

    private static bool IsLongCompoundTopic(string? topic) =>
        !string.IsNullOrWhiteSpace(topic)
        && !topic.Contains(' ', StringComparison.Ordinal)
        && topic.Length > 14;

    /// <summary>
    /// "kapitalmarktskommunikation" → "kommunikation" so similar search finds siblings.
    /// </summary>
    private static string BroadenSimilarTopic(string topic)
    {
        if (!IsLongCompoundTopic(topic))
        {
            return topic;
        }

        foreach (var stem in new[]
                 {
                     "kommunikation", "communication", "management", "leadership",
                     "analytics", "python", "agile", "scrum", "genai"
                 })
        {
            if (topic.Contains(stem, StringComparison.OrdinalIgnoreCase)
                && topic.Length > stem.Length)
            {
                return stem;
            }
        }

        return topic;
    }

    private static bool TopicOverlapsTitle(string topic, string title)
    {
        var topicTerms = ExtractSearchTerms(topic);
        var titleText = NormalizeSearchText(title);
        return topicTerms.Count > 0
               && topicTerms.Any(term => titleText.Contains(term, StringComparison.Ordinal));
    }

    private static string? StripTitleDecorators(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var topic = Regex.Replace(title.Trim(), @"\s*\([^)]*\)", string.Empty);
        topic = Regex.Replace(
            topic,
            @"^(?:externe|interne|die|der|das|ein|eine|the|a|an)\s+",
            string.Empty,
            RegexOptions.IgnoreCase);
        return string.IsNullOrWhiteSpace(topic) ? null : topic.Trim();
    }

    private static bool TryExtractExplicitCompareTitles(
        string message,
        out List<string> titles)
    {
        titles = [];
        var text = message.Trim();

        var compareMatch = Regex.Match(
            text,
            @"^(?:vergleiche|vergleich|compare)\s*:?\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var body = compareMatch.Success
            ? compareMatch.Groups[1].Value.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(body)
            && Regex.IsMatch(text, @"\b(?:vs\.?|versus|oder)\b", RegexOptions.IgnoreCase)
            && (text.Contains("kurs", StringComparison.OrdinalIgnoreCase)
                || text.Contains("course", StringComparison.OrdinalIgnoreCase)
                || text.Contains("besser", StringComparison.OrdinalIgnoreCase)))
        {
            body = text;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        body = Regex.Replace(
            body,
            @"^(?:die\s+kurse|the\s+courses|kurse|courses)\s+",
            string.Empty,
            RegexOptions.IgnoreCase);

        var parts = Regex.Split(
                body,
                @"\s+(?:und|and|vs\.?|versus|oder|or)\s+",
                RegexOptions.IgnoreCase)
            .Select(part => Regex.Replace(part, @"^[""'\s]+|[""'\s]+$", string.Empty).Trim())
            .Select(part => Regex.Replace(
                part,
                @"^(?:den\s+kurs|die\s+kurse|kurs|course)\s+",
                string.Empty,
                RegexOptions.IgnoreCase).Trim())
            .Where(part => part.Length >= 3)
            .ToList();

        if (parts.Count >= 3)
        {
            titles =
            [
                string.Join(" und ", parts.Take(parts.Count - 1)),
                parts[^1]
            ];
            return titles.All(t => t.Length >= 3);
        }

        titles = parts
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(part => !IsAdvisoryFillerPhrase(part))
            .Take(3)
            .ToList();

        return titles.Count >= 2;
    }

    private static bool IsAdvisoryFillerPhrase(string part)
    {
        var normalized = part.Trim().ToLowerInvariant();
        return normalized is "ki" or "ai" or "prompt" or "kurs" or "course"
            || normalized.Length < 4;
    }

    private static bool IsOrdinalPhrase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim().ToLowerInvariant();
        return Regex.IsMatch(
            text,
            @"^(?:den\s+|die\s+|das\s+|der\s+|the\s+)?(?:erste[nrms]?|zweite[nrms]?|dritte[nrms]?|vierte[nrms]?|fünfte[nrms]?|fuenfte[nrms]?|first|second|third|fourth|fifth|\d+)\b");
    }

    private static bool LooksLikeCatalogueCourseTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || IsOrdinalPhrase(title) || IsAdvisoryFillerPhrase(title))
        {
            return false;
        }

        var lowered = title.Trim().ToLowerInvariant();
        if (lowered.StartsWith("was ist", StringComparison.Ordinal)
            || lowered.StartsWith("what is", StringComparison.Ordinal)
            || lowered.Contains('?'))
        {
            return false;
        }

        var content = SanitizeTopicQuery(title);
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1 && words[0] is "ki" or "ai" or "prompt")
        {
            return false;
        }

        return title.Length >= 8 || words.Length >= 2;
    }

    private static bool IsConceptualTopicCompare(string message)
    {
        var text = message.ToLowerInvariant();
        if (!(text.Contains("besser") || text.Contains("oder") || text.Contains("vs")))
        {
            return false;
        }

        return (Regex.IsMatch(text, @"\bki\b") || text.Contains("genai") || Regex.IsMatch(text, @"\bai\b"))
               && text.Contains("prompt");
    }

    private static bool IsCourseRecommendationRequest(string message)
    {
        var text = message.ToLowerInvariant();
        var asksCourse = text.Contains("kurs") || text.Contains("course");
        var asksFit = text.Contains("eignet")
            || text.Contains("beste")
            || text.Contains("besser")
            || text.Contains("empfehl")
            || text.Contains("fits")
            || text.Contains("suitable");
        var asksLevel = text.Contains("beginner")
            || text.Contains("anfänger")
            || text.Contains("anfaenger")
            || text.Contains("einsteiger")
            || text.Contains("fortgeschritten")
            || text.Contains("advanced");

        return asksCourse && (asksFit || asksLevel);
    }

    private static bool TryResolveRecommendationTopic(
        string message,
        ConversationState conversation,
        out string topic)
    {
        topic = string.Empty;

        if (TryResolveAdvisoryTopic(message, conversation, out var advisory)
            && !IsOrdinalPhrase(advisory))
        {
            topic = advisory;
            return true;
        }

        if (HasExplicitContextReference(message)
            && !string.IsNullOrWhiteSpace(conversation.LastSearchTopic)
            && !IsOrdinalPhrase(conversation.LastSearchTopic))
        {
            topic = conversation.LastSearchTopic!;
            return true;
        }

        if (conversation.ActiveCourse is not null)
        {
            var fromActive = PreferCourseKeyword(conversation.ActiveCourse.DisplayName);
            if (!string.IsNullOrWhiteSpace(fromActive))
            {
                topic = fromActive;
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractSkillExplainQuery(string message, out string skill)
    {
        skill = string.Empty;
        var text = message.Trim();

        var patterns = new[]
        {
            @"^was\s+ist\s+(?<skill>.+?)\s*[?.!]*$",
            @"^what\s+is\s+(?<skill>.+?)\s*[?.!]*$",
            @"^erkläre\s+(?<skill>.+?)\s*[?.!]*$",
            @"^erklaere\s+(?<skill>.+?)\s*[?.!]*$",
            @"^explain\s+(?<skill>.+?)\s*[?.!]*$",
            @"^was\s+bedeutet\s+(?<skill>.+?)\s*[?.!]*$",
            @"welche\s+(?:kompetenzen|skills|fähigkeiten|faehigkeiten)\s+umfasst\s+(?<skill>.+?)\s*[?.!]*$",
            @"welche\s+(?:skills|kompetenzen)\s+(?:gibt\s+es\s+)?unter\s+(?<skill>.+?)\s*[?.!]*$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            var raw = match.Groups["skill"].Value.Trim().Trim('"', '\'');
            raw = Regex.Replace(raw, @"^(?:der|die|das|ein|eine|the|a|an)\s+", string.Empty, RegexOptions.IgnoreCase);
            if (raw.Length < 2 || raw.Contains("kurs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            skill = raw;
            return true;
        }

        return false;
    }

    private static string? PreferCourseKeyword(string? courseTitle, string? preferTopic = null)
    {
        if (string.IsNullOrWhiteSpace(courseTitle))
        {
            return null;
        }

        // Use the full title — ExtractCourseSearchTopic would cut at "mit"/"für" and
        // drop the distinctive part ("Empathie" from "Kommunikation mit Empathie").
        var stripped = StripTitleDecorators(courseTitle) ?? courseTitle;
        var terms = ExtractSearchTerms(stripped);
        if (terms.Count == 0)
        {
            return SanitizeTopicQuery(stripped);
        }

        if (!string.IsNullOrWhiteSpace(preferTopic))
        {
            var preferTerms = ExtractSearchTerms(preferTopic);
            var overlap = terms
                .Where(t => preferTerms.Any(p =>
                    t.Equals(p, StringComparison.OrdinalIgnoreCase)
                    || t.Contains(p, StringComparison.OrdinalIgnoreCase)
                    || p.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (overlap.Count > 0)
            {
                return string.Join(" ", preferTerms.Take(3));
            }
        }

        var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "kommunikation", "communication", "grundlagen", "basics", "einführung", "introduction",
            "modul", "module", "teil", "part", "bootcamp", "training", "essential", "werden"
        };

        var specific = terms.Where(t => !generic.Contains(t)).ToList();
        if (specific.Count > 0)
        {
            // Prefer a short skill/topic token (python, empathie) over a program label.
            var distinctive = specific
                .Where(t => t.Length is >= 4 and <= 12)
                .OrderBy(t => t.Length)
                .ToList();
            if (distinctive.Count > 0)
            {
                return distinctive[0];
            }

            return string.Join(" ", specific.Take(3));
        }

        return string.Join(" ", terms.Take(3));
    }

    private static bool TryExtractNamedCourseTitle(string message, out string title)
    {
        title = string.Empty;

        var patterns = new[]
        {
            @"erzähl(?:e)?\s+mir\s+mehr\s+über\s+(?<title>.+?)\s+kurs",
            @"erzaehl(?:e)?\s+mir\s+mehr\s+über\s+(?<title>.+?)\s+kurs",
            @"tell\s+me\s+more\s+about\s+(?:the\s+)?(?:course\s+)?(?<title>.+?)(?:\s+course)?\s*[?.!]*$",
            @"details?\s+zum\s+kurs\s+(?<title>.+?)\s*[?.!]*$",
            @"details?\s+(?:about|for|on)\s+(?:the\s+)?(?:course\s+)?(?<title>.+?)\s*[?.!]*$",
            @"gebe\s+mir\s+details?\s+zum\s+kurs\s+(?<title>.+?)\s*[?.!]*$",
            @"mehr\s+über\s+(?:den\s+)?(?:kurs\s+)?(?<title>.+?)\s+kurs"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(
                message.Trim(),
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                continue;
            }

            var raw = match.Groups["title"].Value.Trim().Trim('"', '\'', '„', '“');
            raw = Regex.Replace(raw, @"\s+(?:kurs|course)\s*$", string.Empty, RegexOptions.IgnoreCase);
            if (raw.Length < 3)
            {
                continue;
            }

            title = raw;
            return true;
        }

        return false;
    }

    private static bool TryExtractRoleSkillQuery(string message, out string role)
    {
        role = string.Empty;
        var text = message.Trim();

        var patterns = new[]
        {
            @"(?:skills|kompetenzen|fähigkeiten|faehigkeiten)\s+(?:braucht|benötigt|benoetigt|hat|gehören\s+zu|gehoeren\s+zu|für|fuer|of)\s+(?:ein(?:e|em|er|es)?\s+|the\s+|a\s+|an\s+)?(?<role>.+?)\s*[?.!]*$",
            @"welche\s+(?:skills|kompetenzen|fähigkeiten|faehigkeiten)\s+(?:braucht|benötigt|benoetigt|hat)\s+(?:ein(?:e|em|er|es)?\s+)?(?<role>.+?)\s*[?.!]*$",
            @"what\s+skills\s+(?:does|do)\s+(?:a\s+|an\s+|the\s+)?(?<role>.+?)\s+need"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            var raw = match.Groups["role"].Value.Trim().Trim('"', '\'');
            // "ein Product Owner" — einem? does NOT match "ein"; strip explicitly.
            raw = Regex.Replace(
                raw,
                @"^(?:ein(?:e|em|er|es)?\s+|the\s+|a\s+|an\s+)",
                string.Empty,
                RegexOptions.IgnoreCase);
            raw = Regex.Replace(raw, @"\s+(?:profil|profile|rolle|role)\s*$", string.Empty, RegexOptions.IgnoreCase);
            if (raw.Length < 3 || raw.Contains("kurs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            role = raw;
            return true;
        }

        return false;
    }

    private static bool IsAdvisoryLearningRequest(string message)
    {
        var text = message.ToLowerInvariant();

        if (TryExtractExplicitCompareTitles(message, out var titles) && titles.Count >= 2
            && titles.All(LooksLikeCatalogueCourseTitle))
        {
            return false;
        }

        return text.Contains("besser")
            || text.Contains("beste")
            || text.Contains("eignet")
            || text.Contains("empfehl")
            || text.Contains("erweitern")
            || text.Contains("fortgeschritten")
            || text.Contains("advanced")
            || text.Contains("kenntnisse")
            || text.Contains("kentnisse")
            || text.Contains("anfänger")
            || text.Contains("anfaenger")
            || text.Contains("beginner")
            || text.Contains("welche thema")
            || text.Contains("was ist besser");
    }

    private static bool TryResolveAdvisoryTopic(
        string message,
        ConversationState conversation,
        out string topic)
    {
        topic = string.Empty;

        if (message.Contains("genai", StringComparison.OrdinalIgnoreCase)
            || message.Contains("gen ai", StringComparison.OrdinalIgnoreCase))
        {
            topic = "genai";
            return true;
        }

        if (Regex.IsMatch(message, @"\bki\b", RegexOptions.IgnoreCase)
            || message.Contains("künstliche intelligenz", StringComparison.OrdinalIgnoreCase)
            || message.Contains("kuenstliche intelligenz", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(message, @"\bai\b", RegexOptions.IgnoreCase))
        {
            topic = message.Contains("prompt", StringComparison.OrdinalIgnoreCase)
                ? "genai prompt"
                : "genai";
            return true;
        }

        if (message.Contains("prompt", StringComparison.OrdinalIgnoreCase))
        {
            topic = "prompt";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(conversation.LastSearchTopic)
            && !IsOrdinalPhrase(conversation.LastSearchTopic)
            && (message.Contains("dazu", StringComparison.OrdinalIgnoreCase)
                || message.Contains("erweitern", StringComparison.OrdinalIgnoreCase)
                || message.Contains("fortgeschritten", StringComparison.OrdinalIgnoreCase)
                || message.Contains("advanced", StringComparison.OrdinalIgnoreCase)
                || message.Contains("weitere", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ähnlich", StringComparison.OrdinalIgnoreCase)
                || message.Contains("eignet", StringComparison.OrdinalIgnoreCase)
                || message.Contains("beginner", StringComparison.OrdinalIgnoreCase)
                || message.Contains("anfänger", StringComparison.OrdinalIgnoreCase)
                || message.Contains("anfaenger", StringComparison.OrdinalIgnoreCase)
                || message.Contains("beste", StringComparison.OrdinalIgnoreCase)))
        {
            topic = conversation.LastSearchTopic!;
            return true;
        }

        return false;
    }

    private static bool IsLooseContextualCourseRequest(
        string message,
        ConversationState conversation)
    {
        if (!(message.Contains("kurs", StringComparison.OrdinalIgnoreCase)
              || message.Contains("course", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var refersBack = message.Contains("dazu", StringComparison.OrdinalIgnoreCase)
            || message.Contains("hierzu", StringComparison.OrdinalIgnoreCase)
            || message.Contains("dafür", StringComparison.OrdinalIgnoreCase)
            || message.Contains("dafuer", StringComparison.OrdinalIgnoreCase)
            || message.Contains("for that", StringComparison.OrdinalIgnoreCase)
            || message.Contains("about that", StringComparison.OrdinalIgnoreCase);

        if (!refersBack)
        {
            return false;
        }

        return conversation.ActiveSkill is not null
            || conversation.ActiveProfile is not null
            || (!string.IsNullOrWhiteSpace(conversation.LastSearchTopic)
                && !IsOrdinalPhrase(conversation.LastSearchTopic))
            || conversation.LastIntent is AgentIntent.SkillDetails
                or AgentIntent.SkillSearch
                or AgentIntent.ProfileDetails
                or AgentIntent.ProfileSearch;
    }

    private static bool HasStaleCourseCompareCalls(
        IReadOnlyList<ToolCallRequest> toolCalls,
        ConversationState conversation,
        string userMessage)
    {
        if (toolCalls.Count == 0)
        {
            return true;
        }

        var onlyEmptyOrRefs = toolCalls.All(call =>
            call.Tool.Equals("get_course", StringComparison.OrdinalIgnoreCase)
            && (call.Reference != ReferenceType.None || string.IsNullOrWhiteSpace(call.Query)));

        if (!onlyEmptyOrRefs)
        {
            return false;
        }

        if (IsAdvisoryLearningRequest(userMessage) || IsConceptualTopicCompare(userMessage))
        {
            return true;
        }

        if (TryExtractExplicitCompareTitles(userMessage, out _))
        {
            return true;
        }

        return conversation.LastCourses.Count < 2;
    }

    private static ReasoningResult BuildSimilarCoursesReasoning(
        string topic,
        EntityRef? activeCourse,
        string language)
    {
        var keyword = PreferCourseKeyword(topic)
                      ?? SanitizeTopicQuery(ExtractCourseSearchTopic(topic) ?? topic)
                      ?? topic.Trim().ToLowerInvariant();

        var slots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["topic"] = keyword
        };

        if (activeCourse is not null)
        {
            slots["similarCourse"] = activeCourse.DisplayName;
        }

        return new ReasoningResult(
            Intent: AgentIntent.CourseSearch,
            Language: language,
            ClarificationQuestion: null,
            Slots: slots,
            ToolCalls:
            [
                new ToolCallRequest("search_courses", keyword, ReferenceType.None)
            ]);
    }

    private static bool TryExtractRoleTransition(
        string message,
        out string currentProfile,
        out string targetProfile)
    {
        currentProfile = string.Empty;
        targetProfile = string.Empty;

        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var match = Regex.Match(
            message.Trim(),
            @"\bich\s+bin\s+(?<current>.+?)(?:,|\.|\s+und\s+)\s*ich\s+möchte\s+(?<target>.+?)\s+werden\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            match = Regex.Match(
                message.Trim(),
                @"\bi\s+am\s+(?<current>.+?)(?:,|\.|\s+and\s+)\s*i\s+want\s+to\s+become\s+(?<target>.+?)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (!match.Success)
        {
            return false;
        }

        currentProfile = match.Groups["current"].Value.Trim();
        targetProfile = match.Groups["target"].Value.Trim();

        return currentProfile.Length >= 2
            && targetProfile.Length >= 2;
    }

    private static ReasoningResult ResolvePendingSlot(
        string message,
        ConversationState conversation)
    {
        var value = message.Trim();

        var slots = new Dictionary<string, string>(
            conversation.Slots,
            StringComparer.OrdinalIgnoreCase)
        {
            [conversation.PendingSlot!] = value
        };

        return conversation.PendingSlot switch
        {
            "targetProfile" =>
                BuildLearningRecommendationFromPendingTarget(
                    value,
                    conversation,
                    slots),

            "currentProfile" =>
                new ReasoningResult(
                    Intent: AgentIntent.Clarify,
                    Language: conversation.Language,
                    ClarificationQuestion: Localize(
                        conversation.Language,
                        "Welches Zielprofil möchtest du erreichen?",
                        "Which target profile would you like to reach?"),
                    Slots: slots,
                    ToolCalls: []),

            "topic" =>
                new ReasoningResult(
                    Intent: AgentIntent.CourseSearch,
                    Language: conversation.Language,
                    ClarificationQuestion: null,
                    Slots: slots,
                    ToolCalls:
                    [
                        new ToolCallRequest(
                            "search_courses",
                            value,
                            ReferenceType.None)
                    ]),

            "selection" =>
                ResolveSelectionSlotAnswer(
                    message,
                    conversation,
                    slots),

            _ =>
                new ReasoningResult(
                    Intent: AgentIntent.Clarify,
                    Language: conversation.Language,
                    ClarificationQuestion: Localize(
                        conversation.Language,
                        "Kannst du deine Auswahl bitte kurz genauer beschreiben?",
                        "Could you briefly clarify your selection?"),
                    Slots: slots,
                    ToolCalls: [])
        };
    }

    private static ReasoningResult BuildLearningRecommendationFromPendingTarget(
        string targetProfile,
        ConversationState conversation,
        Dictionary<string, string> slots)
    {
        slots["targetProfile"] = targetProfile;

        if (!slots.TryGetValue("currentProfile", out var currentProfile)
            || string.IsNullOrWhiteSpace(currentProfile))
        {
            return new ReasoningResult(
                Intent: AgentIntent.Clarify,
                Language: conversation.Language,
                ClarificationQuestion: Localize(
                    conversation.Language,
                    "Welches aktuelle Profil hast du?",
                    "What is your current profile?"),
                Slots: slots,
                ToolCalls: []);
        }

        return new ReasoningResult(
            Intent: AgentIntent.LearningRecommendation,
            Language: conversation.Language,
            ClarificationQuestion: null,
            Slots: slots,
            ToolCalls:
            [
                new ToolCallRequest(
                    "get_profile_skills",
                    currentProfile,
                    ReferenceType.None),

                new ToolCallRequest(
                    "get_profile_skills",
                    targetProfile,
                    ReferenceType.None)
            ]);
    }

    private static ReasoningResult ResolveSelectionSlotAnswer(
        string message,
        ConversationState conversation,
        Dictionary<string, string> slots)
    {
        var position = ExtractFollowUpPosition(message) ?? ExtractItemPosition(message);

        if (position is not null && conversation.LastAddressableItems.Count > 0)
        {
            return ResolvePositionalFollowUp(position.Value, conversation);
        }

        return new ReasoningResult(
            Intent: AgentIntent.Clarify,
            Language: conversation.Language,
            ClarificationQuestion: Localize(
                conversation.Language,
                "Welchen der gezeigten Einträge meinst du? Sag zum Beispiel „die zweite“.",
                "Which of the shown entries do you mean? For example say \"the second one\"."),
            Slots: slots,
            ToolCalls: []);
    }

    private static bool IsShortSlotAnswer(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.Trim();

        return normalized.Length <= 100
            && normalized.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries).Length <= 8;
    }

    private static ReasoningResult BuildEntityFollowUp(
        AgentIntent intent,
        string tool,
        EntityRef selected,
        string language,
        string? divisionSlot = null)
    {
        var slots = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(divisionSlot))
        {
            slots["division"] = divisionSlot;
        }

        var query = tool.Equals(
            "get_divisions",
            StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : selected.Id ?? selected.DisplayName;

        return new ReasoningResult(
            Intent: intent,
            Language: language,
            ClarificationQuestion: null,
            Slots: slots,
            ToolCalls:
            [
                new ToolCallRequest(
                    tool,
                    query,
                    ReferenceType.None)
            ]);
    }

    private static bool HasExplicitContextReference(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var text = message.Trim().ToLowerInvariant();

        return ExtractFollowUpPosition(message) is not null
            || text.Contains("dazu")
            || text.Contains("hierzu")
            || text.Contains("dafür")
            || text.Contains("dafuer")
            || text.Contains("davon")
            || text.Contains("darüber")
            || text.Contains("darueber")
            || text.Contains("dieser")
            || text.Contains("diese")
            || text.Contains("dieses")
            || text.Contains("diesen")
            || text.Contains("diesem")
            || text.Contains("weitere")
            || text.Contains("ähnlich")
            || text.Contains("aehnlich")
            || text.Contains("more")
            || text.Contains("similar")
            || text.Contains("about that")
            || text.Contains("for that")
            || text.Contains("this course")
            || text.Contains("that course")
            || text.Contains("this profile")
            || text.Contains("that profile");
    }

    private static ReasoningResult BuildMissingPositionReasoning(
        string language)
    {
        return new ReasoningResult(
            Intent: AgentIntent.Clarify,
            Language: language,
            ClarificationQuestion: Localize(
                language,
                "Diesen Eintrag gab es in meiner letzten Antwort nicht. Welchen der gezeigten Einträge meinst du?",
                "That entry was not part of my last answer. Which shown item do you mean?"),
            Slots: new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase),
            ToolCalls: []);
    }

    private static ReasoningResult BuildUnsupportedReferenceReasoning(
        string language)
    {
        return new ReasoningResult(
            Intent: AgentIntent.Clarify,
            Language: language,
            ClarificationQuestion: Localize(
                language,
                "Auf diesen Eintrag kann ich noch nicht direkt zugreifen. Welchen Kurs oder welches Profil meinst du?",
                "I cannot access that item directly yet. Which course or profile do you mean?"),
            Slots: new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase),
            ToolCalls: []);
    }
}
