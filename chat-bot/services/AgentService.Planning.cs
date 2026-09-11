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
    // PHASE 3 — PLANNING
    // ===============================================================

    private static ExecutionPlan CreatePlan(
        ReasoningResult reasoning,
        ConversationState conversation,
        string userMessage)
    {   
        var intent = reasoning.Intent;
        var clarification = reasoning.ClarificationQuestion;
        var toolCalls = reasoning.ToolCalls.ToList();
        var explicitProfile = ExtractExplicitProfileQuery(userMessage);
        var itemPosition = ExtractItemPosition(userMessage);
        var profileCourseRequest = IsProfileCourseRequest(userMessage, conversation);
        var profileSearchQuery = ExtractProfileSearchQuery(userMessage);
        var collectionTopic = ExtractCollectionTopic(userMessage);
        var division = reasoning.Slots.TryGetValue("division", out var divisionValue)
            ? divisionValue
            : null;
        var profileDivision = ExtractProfileDivisionQuery(userMessage) ?? division;
        
        if (itemPosition is not null)
        {
            var selected = SelectRenderedItem(conversation, itemPosition.Value, ExtractPositionKind(userMessage));

            if (selected is null)
            {
                // The previous answer did not contain that card. Guessing an item
                // here is what made the assistant answer about the wrong entry,
                // so it asks instead of sending a placeholder query to MCP.
                intent = AgentIntent.Clarify;
                toolCalls = [];
                clarification = Localize(
                    reasoning.Language,
                    "Diesen Eintrag gab es in meiner letzten Antwort nicht. Welchen der gezeigten Einträge meinst du?",
                    "That entry was not part of my last answer. Which of the shown entries do you mean?");
            }
            else if (selected.Kind == EntityKind.Profile)
            {
                intent = AgentIntent.ProfileDetails;

                if (!string.IsNullOrWhiteSpace(selected.Id))
                {
                    toolCalls =
                    [
                        new ToolCallRequest(
                            "get_profile",
                            selected.Id,
                            ReferenceType.None)
                    ];
                }
                else
                {
                    toolCalls =
                    [
                        new ToolCallRequest(
                            "search_profiles",
                            selected.DisplayName,
                            ReferenceType.None)
                    ];

                    reasoning.Slots["requestedIntent"] = "profile_details";
                    reasoning.Slots["topic"] = selected.DisplayName;
                }
            }
            else if (selected.Kind == EntityKind.Collection)
            {
                // MCP has no get_collection — re-search and keep the named match.
                intent = AgentIntent.Collections;
                toolCalls = [new ToolCallRequest(
                    "search_collections",
                    selected.DisplayName,
                    ReferenceType.None)];
            }
            else if (selected.Kind == EntityKind.Division)
            {
                intent = AgentIntent.DivisionOverview;
                toolCalls = [new ToolCallRequest("get_divisions", string.Empty, ReferenceType.None)];
                reasoning.Slots["division"] = selected.DisplayName;
            }
            else
            {
                intent = AgentIntent.CourseDetails;
                toolCalls = [new ToolCallRequest(
                    "get_course",
                    selected.Id ?? selected.DisplayName,
                    ReferenceType.None)];
            }
        }
        else if (profileCourseRequest && conversation.ActiveProfile is not null)
        {
            intent = AgentIntent.ProfileCourses;
            toolCalls = [new ToolCallRequest(
                "get_profile",
                conversation.ActiveProfile.Id ?? conversation.ActiveProfile.DisplayName,
                ReferenceType.None)];
        }
        else if (IsProgressRequest(userMessage))
        {
            intent = AgentIntent.Progress;
            toolCalls = [new ToolCallRequest("get_my_progress", string.Empty, ReferenceType.None)];
        }
        else if (IsBookmarkRequest(userMessage))
        {
            intent = AgentIntent.Bookmarks;
            toolCalls = [new ToolCallRequest("get_my_bookmarks", string.Empty, ReferenceType.None)];
        }
        else if (IsPersonalCollectionRequest(userMessage))
        {
            intent = AgentIntent.Clarify;
            toolCalls = [];
            clarification = Localize(
                reasoning.Language,
                "Persönliche Collections kann ich aktuell nicht abrufen. Ich kann dir aber öffentliche Sammlungen zeigen.",
                "I cannot retrieve personal collections right now, but I can show public collections.");
        }
        else if (IsCollectionRequest(userMessage))
        {
            intent = AgentIntent.Collections;
            toolCalls = [new ToolCallRequest(
                "search_collections",
                collectionTopic ?? string.Empty,
                ReferenceType.None)];
        }
        else if (profileSearchQuery is not null || profileDivision is not null)
        {
            intent = AgentIntent.ProfileSearch;

            if (!string.IsNullOrWhiteSpace(profileDivision))
            {
                reasoning.Slots["division"] = profileDivision;

                toolCalls =
                [
                    new ToolCallRequest(
                        "get_divisions",
                        string.Empty,
                        ReferenceType.None)
                ];
            }
            else if (!string.IsNullOrWhiteSpace(profileSearchQuery))
            {
                reasoning.Slots["topic"] = profileSearchQuery;

                toolCalls =
                [
                    new ToolCallRequest(
                        "search_profiles",
                        profileSearchQuery,
                        ReferenceType.None)
                ];
            }
            else
            {
                intent = AgentIntent.Clarify;
                toolCalls = [];

                clarification = Localize(
                    reasoning.Language,
                    "Nach welchem Profil möchtest du suchen?",
                    "Which profile would you like to search for?");
            }
        }
        else if (explicitProfile is not null)
        {
            intent = AgentIntent.ProfileDetails;

            toolCalls =
            [
                new ToolCallRequest(
                    "search_profiles",
                    explicitProfile,
                    ReferenceType.None)
            ];

            reasoning.Slots["requestedIntent"] = "profile_details";
            reasoning.Slots["topic"] = explicitProfile;
        }
        else if (intent == AgentIntent.ProfileSearch && !string.IsNullOrWhiteSpace(division))
        {
            toolCalls = [new ToolCallRequest("get_divisions", string.Empty, ReferenceType.None)];
        }
        else if (IsCourseFollowUpRequest(userMessage)
                 && (conversation.LastCourses.Count > 0
                     || !string.IsNullOrWhiteSpace(conversation.LastSearchTopic)
                     || conversation.ActiveCourse is not null))
        {
            intent = AgentIntent.CourseSearch;
            var followUpQuery = ResolveCourseFollowUpQuery(userMessage, conversation);
            toolCalls = [new ToolCallRequest("search_courses", followUpQuery, ReferenceType.None)];
            reasoning.Slots["topic"] = followUpQuery;
            if (conversation.ActiveCourse is not null)
            {
                reasoning.Slots["similarCourse"] = conversation.ActiveCourse.DisplayName;
            }
        }
        else if (IsNamedSkillCourseRequest(userMessage, conversation))
        {
            intent = AgentIntent.SkillCourses;
            var skill = conversation.LastSkills
                .Where(s => ContainsText(userMessage, s.DisplayName))
                .OrderByDescending(s => s.DisplayName.Length)
                .FirstOrDefault()
                ?? conversation.ActiveSkill;

            if (skill is not null)
            {
                toolCalls = [new ToolCallRequest(
                    "get_courses_by_tag",
                    skill.DisplayName,
                    ReferenceType.None)];
            }
        }
        else if (IsContextualCourseRequest(userMessage, conversation))
        {
            if (conversation.LastIntent is AgentIntent.SkillDetails or AgentIntent.SkillSearch
                && conversation.ActiveSkill is not null)
            {
                intent = AgentIntent.SkillCourses;
                toolCalls = [new ToolCallRequest(
                    "get_courses_by_tag",
                    conversation.ActiveSkill.DisplayName,
                    ReferenceType.None)];
            }
            else if (conversation.ActiveProfile is not null)
            {
                intent = AgentIntent.ProfileCourses;
                toolCalls = [new ToolCallRequest(
                    "get_profile",
                    conversation.ActiveProfile.Id ?? conversation.ActiveProfile.DisplayName,
                    ReferenceType.None)];
            }
            else if (!string.IsNullOrWhiteSpace(conversation.LastSearchTopic)
                     && !IsOrdinalPhrase(conversation.LastSearchTopic))
            {
                intent = AgentIntent.CourseSearch;
                toolCalls = [new ToolCallRequest(
                    "search_courses",
                    conversation.LastSearchTopic,
                    ReferenceType.None)];
                reasoning.Slots["topic"] = conversation.LastSearchTopic!;
            }
        }

        // Corrective rewrites — same helpers as TryResolveFollowUp (one source of truth).
        if (itemPosition is null
            && TryResolveDeterministicIntent(
                userMessage,
                conversation,
                reasoning.Language,
                out var deterministic))
        {
            intent = deterministic.Intent;
            clarification = deterministic.ClarificationQuestion;
            toolCalls = deterministic.ToolCalls.ToList();

            reasoning.Slots.Clear();

            foreach (var slot in deterministic.Slots)
            {
                reasoning.Slots[slot.Key] = slot.Value;
            }
        }

            var needsData = intent is not (
                AgentIntent.Capabilities or
                AgentIntent.SmallTalk or
                AgentIntent.OutOfScope or
                AgentIntent.Clarify);
            
            if (intent == AgentIntent.LearningRecommendation
                && toolCalls.Count == 0)
            {
                var currentProfile =
                    reasoning.Slots.TryGetValue(
                        "currentProfile",
                        out var currentValue)
                        ? currentValue?.Trim()
                        : null;

                var targetProfile =
                    reasoning.Slots.TryGetValue(
                        "targetProfile",
                        out var targetValue)
                        ? targetValue?.Trim()
                        : null;

                if (string.IsNullOrWhiteSpace(currentProfile))
                {
                    intent = AgentIntent.Clarify;
                    needsData = false;

                    clarification = Localize(
                        reasoning.Language,
                        "Welches aktuelle Profil hast du?",
                        "What is your current profile?");

                    reasoning.Slots["clarifyKind"] = "currentProfile";
                }
                else if (string.IsNullOrWhiteSpace(targetProfile))
                {
                    intent = AgentIntent.Clarify;
                    needsData = false;

                    clarification = Localize(
                        reasoning.Language,
                        "Welches Zielprofil möchtest du erreichen?",
                        "Which target profile would you like to reach?");

                    reasoning.Slots["clarifyKind"] = "targetProfile";
                }
                else
                {
                    toolCalls.Add(
                        new ToolCallRequest(
                            "get_profile_skills",
                            currentProfile,
                            ReferenceType.None));

                    toolCalls.Add(
                        new ToolCallRequest(
                            "get_profile_skills",
                            targetProfile,
                            ReferenceType.None));
                }
            }


        // The classifier may return an intent that needs data but no usable call,
        // for instance because a reference could not be resolved. Recover from the
        // remembered context before falling back to a clarification.
        if (needsData && toolCalls.Count == 0)
        {
            if (intent == AgentIntent.CourseSearch)
            {
                var searchTopic = reasoning.Slots.TryGetValue("topic", out var slottedSearch)
                    && !string.IsNullOrWhiteSpace(slottedSearch)
                    && !IsOrdinalPhrase(slottedSearch)
                        ? SanitizeTopicQuery(slottedSearch) ?? slottedSearch.Trim()
                        : null;

                searchTopic ??= !string.IsNullOrWhiteSpace(conversation.LastSearchTopic)
                    && !IsOrdinalPhrase(conversation.LastSearchTopic)
                        ? conversation.LastSearchTopic
                        : InferSearchQueryFromUserMessage(userMessage);

                if (!string.IsNullOrWhiteSpace(searchTopic))
                {
                    toolCalls.Add(new ToolCallRequest(
                        "search_courses",
                        searchTopic,
                        ReferenceType.None));
                    reasoning.Slots["topic"] = searchTopic;
                }
                else
                {
                    needsData = false;
                    intent = AgentIntent.Clarify;
                }
            }
            else if (intent == AgentIntent.SkillDetails || intent == AgentIntent.SkillSearch)
            {
                var skillTopic = reasoning.Slots.TryGetValue("topic", out var slottedSkill)
                    && !string.IsNullOrWhiteSpace(slottedSkill)
                        ? slottedSkill.Trim()
                        : InferSearchQueryFromUserMessage(userMessage);

                if (!string.IsNullOrWhiteSpace(skillTopic))
                {
                    toolCalls.Add(new ToolCallRequest(
                        intent == AgentIntent.SkillSearch ? "search_skills" : "get_skill",
                        intent == AgentIntent.SkillSearch ? string.Empty : skillTopic,
                        ReferenceType.None));
                    reasoning.Slots["topic"] = skillTopic;
                }
                else
                {
                    needsData = false;
                    intent = AgentIntent.Clarify;
                }
            }
            else if (intent == AgentIntent.LearningRecommendation
                && toolCalls.Count == 0)
            {
                var currentProfile =
                    reasoning.Slots.TryGetValue(
                        "currentProfile",
                        out var currentValue)
                        ? currentValue
                        : null;

                var targetProfile =
                    reasoning.Slots.TryGetValue(
                        "targetProfile",
                        out var targetValue)
                        ? targetValue
                        : null;

                if (!string.IsNullOrWhiteSpace(currentProfile))
                {
                    toolCalls.Add(
                        new ToolCallRequest(
                            "search_profiles",
                            currentProfile,
                            ReferenceType.None));
                }

                if (!string.IsNullOrWhiteSpace(targetProfile))
                {
                    toolCalls.Add(
                        new ToolCallRequest(
                            "search_profiles",
                            targetProfile,
                            ReferenceType.None));
                }
            }
            else if (intent == AgentIntent.CourseCompare)
            {
                var topic = reasoning.Slots.TryGetValue("topic", out var topicValue)
                    && !string.IsNullOrWhiteSpace(topicValue)
                    && userMessage.Contains(
                        topicValue,
                        StringComparison.OrdinalIgnoreCase)
                        ? topicValue.Trim()
                        : null;

                if (!string.IsNullOrWhiteSpace(topic))
                {
                    // "Welcher GenAI-Kurs ist besser?" ist zunächst eine Suche,
                    // solange keine zwei konkreten Kurse ausgewählt wurden.
                    intent = AgentIntent.CourseSearch;

                    toolCalls.Add(new ToolCallRequest(
                        "search_courses",
                        topic,
                        ReferenceType.None));
                }
                else
                {
                    var firstCourse = conversation.LastCourses
                        .ElementAtOrDefault(0);

                    var secondCourse = conversation.LastCourses
                        .ElementAtOrDefault(1);

                    if (firstCourse is not null
                        && secondCourse is not null
                        && userMessage.Contains("erste", StringComparison.OrdinalIgnoreCase)
                        && userMessage.Contains("zwei", StringComparison.OrdinalIgnoreCase))
                    {
                        toolCalls.Add(new ToolCallRequest(
                            "get_course",
                            firstCourse.Id ?? string.Empty,
                            ReferenceType.None));

                        toolCalls.Add(new ToolCallRequest(
                            "get_course",
                            secondCourse.Id ?? string.Empty,
                            ReferenceType.None));
                    }
                    else
                    {
                        intent = AgentIntent.Clarify;
                        needsData = false;

                        clarification = Localize(
                            reasoning.Language,
                            "Welche zwei Kurse möchtest du vergleichen?",
                            "Which two courses would you like to compare?");
                    }
                }
            }
            else
            {
                var recovered = RecoverToolCall(intent, conversation);

                if (recovered is not null)
                {
                    toolCalls.Add(recovered);
                }
                else
                {
                    needsData = false;
                    intent = AgentIntent.Clarify;
                }
            }
        }
        return new ExecutionPlan(
            Intent: intent,
            Language: reasoning.Language,
            ClarificationQuestion: clarification,
            Slots: reasoning.Slots,
            FirstRound: toolCalls,
            NeedsData: needsData)
        {
            UserMessage = userMessage
        };
    }

    private static ToolCallRequest? RecoverToolCall(
        AgentIntent intent,
        ConversationState conversation)
    {
        var activeProfile = conversation.ActiveProfile;
        var activeCourse = conversation.ActiveCourse;
        var activeSkill = conversation.ActiveSkill;

        switch (intent)
        {
            case AgentIntent.ProfileCourses or AgentIntent.ProfileDetails
                when activeProfile is not null:

                return !string.IsNullOrWhiteSpace(activeProfile.Id)

                ? new ToolCallRequest(
                    "get_profile",
                    activeProfile.Id,
                    ReferenceType.None)

                : new ToolCallRequest(
                    "search_profiles",
                    activeProfile.DisplayName,
                    ReferenceType.None);

            case AgentIntent.CourseDetails
                when activeCourse is not null:

                return !string.IsNullOrWhiteSpace(activeCourse.Id)

                    ? new ToolCallRequest(
                        "get_course",
                        activeCourse.Id,
                        ReferenceType.None)

                    : new ToolCallRequest(
                        "search_courses",
                        activeCourse.DisplayName,
                        ReferenceType.None);

            case AgentIntent.SkillDetails or AgentIntent.SkillCourses when activeSkill is not null:
                return new ToolCallRequest(
                    intent == AgentIntent.SkillCourses ? "get_courses_by_tag" : "get_skill",
                    activeSkill.DisplayName,
                    ReferenceType.None);

            case AgentIntent.SkillSearch:
                return new ToolCallRequest("search_skills", string.Empty, ReferenceType.None);

            case AgentIntent.DivisionOverview:
                return new ToolCallRequest("get_divisions", string.Empty, ReferenceType.None);

            case AgentIntent.Collections:
                return new ToolCallRequest("search_collections", string.Empty, ReferenceType.None);

            case AgentIntent.Bookmarks:
                return new ToolCallRequest("get_my_bookmarks", string.Empty, ReferenceType.None);

            case AgentIntent.Progress:
                return new ToolCallRequest("get_my_progress", string.Empty, ReferenceType.None);

            case AgentIntent.CourseCompare when conversation.LastCourses.Count >= 2:
                // Comparison recovery is handled in CreatePlan (needs multiple calls).
                return null;

            case AgentIntent.LearningPath when conversation.Slots.TryGetValue("targetProfile", out var target):
                return new ToolCallRequest("search_profiles", target, ReferenceType.None);

            case AgentIntent.LearningRecommendation
                when conversation.Slots.TryGetValue(
                    "targetProfile",
                    out var targetProfile)
                && !string.IsNullOrWhiteSpace(targetProfile):
                return new ToolCallRequest(
                    "get_profile_skills",
                    targetProfile,
                    ReferenceType.None);

            default:
                return null;
        }
    }

    private static string ResolveCourseFollowUpQuery(string message, ConversationState conversation)
    {
        var knownTopic = conversation.Slots.TryGetValue("topic", out var topicValue)
            && !string.IsNullOrWhiteSpace(topicValue)
            && !IsOrdinalPhrase(topicValue)
            ? SanitizeTopicQuery(topicValue)
            : null;

        if (!string.IsNullOrWhiteSpace(knownTopic))
        {
            return knownTopic;
        }

        if (conversation.ActiveCourse is not null)
        {
            var fromActive = PreferCourseKeyword(conversation.ActiveCourse.DisplayName);
            if (!string.IsNullOrWhiteSpace(fromActive) && !IsOrdinalPhrase(fromActive))
            {
                return fromActive;
            }
        }

        var lastTopic = SanitizeTopicQuery(conversation.LastSearchTopic);
        if (!string.IsNullOrWhiteSpace(lastTopic) && !IsOrdinalPhrase(lastTopic))
        {
            return lastTopic;
        }

        // Prefer an explicit subject over an arbitrary card from a multi-card list.
        if (conversation.ActiveSkill is not null && !string.IsNullOrWhiteSpace(conversation.ActiveSkill.DisplayName))
        {
            return conversation.ActiveSkill.DisplayName.Trim().ToLowerInvariant();
        }

        if (conversation.ActiveProfile is not null
            && !string.IsNullOrWhiteSpace(conversation.ActiveProfile.DisplayName))
        {
            return conversation.ActiveProfile.DisplayName.Trim();
        }

        if (conversation.LastCourses.Count >= 1)
        {
            var fromCard = SanitizeTopicQuery(
                ExtractCourseSearchTopic(conversation.LastCourses[0].DisplayName));
            if (!string.IsNullOrWhiteSpace(fromCard))
            {
                return fromCard;
            }
        }

        var cleanedMessage = StripFollowUpTerms(message);
        var inferred = InferSearchQueryFromUserMessage(cleanedMessage);
        if (!string.IsNullOrWhiteSpace(inferred))
        {
            return inferred;
        }

        // Never send the raw follow-up phrase ("ähnliche Kurse") as MCP query.
        return SanitizeTopicQuery(conversation.LastSearchTopic)
               ?? SanitizeTopicQuery(ExtractCourseSearchTopic(conversation.ActiveCourse?.DisplayName))
               ?? string.Empty;
    }

    private static string? ExtractCourseSearchTopic(string? courseTitle)
    {
        if (string.IsNullOrWhiteSpace(courseTitle))
        {
            return null;
        }

        var topic = Regex.Replace(courseTitle.Trim(), @"\s*\([^)]*\)", string.Empty, RegexOptions.IgnoreCase);

        foreach (var separator in new[]
                 {
                    " im ", " in ", " für ", " for ", " mit ", " von ", " by ", " and ", " & ", " - ", " – ", " — ", ": "
                 })
        {
            var index = topic.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                topic = topic.Substring(0, index).Trim();
                break;
            }
        }

        topic = Regex.Replace(topic, @"^(?:the|die|der|das|ein|eine|einen)\s+", string.Empty, RegexOptions.IgnoreCase);
        topic = Regex.Replace(topic, @"\s+[-–—:]+\s+.*$", string.Empty, RegexOptions.None);
        topic = topic.Trim();

        return string.IsNullOrWhiteSpace(topic) ? null : topic;
    }

    private static string StripFollowUpTerms(string message)
    {
        var cleaned = message.Trim();

        foreach (var phrase in new[]
                 {
                    "ähnliche kurse",
                    "ähnliche kurs",
                    "similar courses",
                    "similar course",
                    "weitere kurse",
                    "weitere kurs",
                    "more courses",
                    "more course",
                    "gibt es",
                    "can you",
                    "could you",
                    "please",
                    "kurs",
                    "course"
                 })
        {
            cleaned = Regex.Replace(
                cleaned,
                $@"(?i)\b{Regex.Escape(phrase)}\b",
                string.Empty,
                RegexOptions.CultureInvariant);
        }

        cleaned = Regex.Replace(cleaned, @"[?!.;,]+$", string.Empty, RegexOptions.None);
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ", RegexOptions.None).Trim();

        return cleaned;
    }

    private static bool IsContextualCourseRequest(
        string message,
        ConversationState conversation)
    {
        if (!(message.Contains("kurs", StringComparison.OrdinalIgnoreCase)
              || message.Contains("course", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // "Kurse dazu" must work after a single profile/skill was established even
        // when the previous intent was a search that left an Active* subject.
        var hasSubject = conversation.ActiveSkill is not null || conversation.ActiveProfile is not null;

        if (!IsCourseRelated(conversation.LastIntent) && !hasSubject)
        {
            return false;
        }

        return message.Contains("dazu", StringComparison.OrdinalIgnoreCase)
            || message.Contains("diesem skill", StringComparison.OrdinalIgnoreCase)
            || message.Contains("dieser skill", StringComparison.OrdinalIgnoreCase)
            || message.Contains("diesem profil", StringComparison.OrdinalIgnoreCase)
            || message.Contains("dieser profil", StringComparison.OrdinalIgnoreCase)
            || message.Contains("about this skill", StringComparison.OrdinalIgnoreCase)
            || message.Contains("for this skill", StringComparison.OrdinalIgnoreCase)
            || message.Contains("about this profile", StringComparison.OrdinalIgnoreCase)
            || message.Contains("for this profile", StringComparison.OrdinalIgnoreCase)
            || message.Contains("belong to this profile", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNamedSkillCourseRequest(
        string message,
        ConversationState conversation) =>
        (message.Contains("kurs", StringComparison.OrdinalIgnoreCase)
         || message.Contains("course", StringComparison.OrdinalIgnoreCase))
        && conversation.LastSkills.Any(skill => ContainsText(message, skill.DisplayName));

    private static string? ExtractExplicitProfileQuery(string message)
    {
        var selfProfileMatch = Regex.Match(
            message,
            @"^(?:ich\s+bin|i\s+am|i['’]m|as\s+(?:a|an))\s+(?<name>[^,?.!]+)",
            RegexOptions.IgnoreCase);

        if (selfProfileMatch.Success)
        {
            var selfProfile = selfProfileMatch.Groups["name"].Value.Trim();

            // "Ich bin interessiert an Python" states an interest, not a role, and
            // was previously searched as if "interessiert an Python" were a
            // profile name.
            var statesInterest = Regex.IsMatch(
                selfProfile,
                @"^(?:interessiert|interested|neu\b|new\b|auf der suche|looking|nicht\b|not\b)",
                RegexOptions.IgnoreCase);

            if (selfProfile.Length >= 3
                && !statesInterest
                && selfProfile.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5)
            {
                return selfProfile;
            }
        }

        var hasProfileWord = message.Contains("profil", StringComparison.OrdinalIgnoreCase)
            || message.Contains("profile", StringComparison.OrdinalIgnoreCase);

        if (!hasProfileWord
            || message.Contains("dieses profil", StringComparison.OrdinalIgnoreCase)
            || message.Contains("this profile", StringComparison.OrdinalIgnoreCase)
            || message.Contains("welche profile", StringComparison.OrdinalIgnoreCase)
            || message.Contains("alle profile", StringComparison.OrdinalIgnoreCase)
            || message.Contains("which profiles", StringComparison.OrdinalIgnoreCase)
            || message.Contains("all profiles", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = Regex.Match(
            message,
            @"(?:profil(?:e)?\s+(?:für\s+)?|profile\s+(?:for\s+)?)(?<name>[^?!.]+)",
            RegexOptions.IgnoreCase);

        var name = match.Success ? match.Groups["name"].Value.Trim() : string.Empty;

        return name.Length >= 3 ? name : null;
    }

    private static bool IsProgressRequest(string message) =>
        HasPersonalSignal(message)
        && Regex.IsMatch(
            message,
            @"\b(?:begonnen|gestartet|angefangen|laufend(?:e[nrms]?)?|abgeschlossen(?:e[nrms]?)?|fertig|completed|in\s*progress|started)\b",
            RegexOptions.IgnoreCase);

    private static bool IsBookmarkRequest(string message) =>
        HasPersonalSignal(message)
        && Regex.IsMatch(
            message,
            @"\b(?:bookmark|bookmarks|gemerkt(?:e[nrms]?)?|gespeichert(?:e[nrms]?)?|saved|favorit(?:en)?)\b",
            RegexOptions.IgnoreCase);

    private static bool IsPersonalCollectionRequest(string message) =>
        HasPersonalSignal(message)
        && Regex.IsMatch(
            message,
            @"\b(?:collections?|collctions?|sammlung(?:en)?)\b",
            RegexOptions.IgnoreCase);

    private static bool HasPersonalSignal(string message) =>
        Regex.IsMatch(message, @"\b(?:mein(?:e[nrms]?)?|meine|ich|habe\s+ich|my|mine)\b", RegexOptions.IgnoreCase);

    private static string? ExtractProfileSearchQuery(string message)
    {
        if (!LooksLikeProfileListRequest(message))
        {
            return null;
        }

        if (ExtractProfileDivisionQuery(message) is not null)
        {
            return null;
        }

        var terms = ExtractSearchTerms(message)
            .Where(term => term is not "profil" and not "profile" and not "profiles")
            .ToList();

        return terms.Count == 0 ? string.Empty : string.Join(" ", terms);
    }

    private static string? ExtractProfileDivisionQuery(string message)
    {
        if (!LooksLikeProfileListRequest(message))
        {
            return null;
        }

        var match = Regex.Match(
            message,
            @"(?:\b(?:in|im|für|fuer)\s+(?:den\s+|dem\s+|der\s+)?(?<division>[A-Za-z0-9]{2,12})\s+(?:bereich|area)\b|\b(?:bereich|area)\s+(?<division>[A-Za-z0-9]{2,12})\b)",
            RegexOptions.IgnoreCase);

        return match.Success ? match.Groups["division"].Value.Trim() : null;
    }

    private static bool LooksLikeProfileListRequest(string message) =>
        Regex.IsMatch(
            message,
            @"\b(?:welche|alle|finde|suche|zeige|which|all|find|show)\b.*\b(?:profile|profiles)\b|\b(?:profile|profiles)\b.*\b(?:gibt|für|fuer|in|im|bereich|area|for)\b",
            RegexOptions.IgnoreCase);

    /// <summary>
    /// Recognises "tell me more about the second one". The English ordinals used
    /// to be accepted by the gate but not by the index lookup, so every English
    /// positional follow-up silently fell through to a fresh search.
    /// </summary>
    private static int? ExtractItemPosition(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();
        var index = ExtractPositionIndex(normalized);

        if (index is null)
        {
            return null;
        }

        var namesAnItem = PositionNouns.Any(noun => normalized.Contains(noun, StringComparison.Ordinal));

        var asksForDetail = normalized.Contains("erzähl")
            || normalized.Contains("mehr")
            || normalized.Contains("detail")
            || normalized.Contains("lerne")
            || normalized.Contains("beschreibung")
            || normalized.Contains("zeige")
            || normalized.Contains("zeig")
            || normalized.Contains("show")
            || normalized.Contains("tell me")
            || normalized.Contains("what do i learn");

        var isBareSelection = Regex.IsMatch(
            normalized,
            @"^(?:kurs|course|profil|profile|sammlung|collection)?\s*(?:[1-5]|ersten?|zweiten?|dritten?|vierten?|fünften?|fuenften?|first|second|third|fourth|fifth)$");

        return (namesAnItem && asksForDetail) || isBareSelection ? index : null;
    }

    private static readonly string[] PositionNouns =
    {
        "kurs", "course", "profil", "profile", "sammlung", "collection",
        "eintrag", "entry", "bereich", "division", "abteilung", "area"
    };

    private static int? ExtractPositionIndex(string normalized)
    {
        if (Regex.IsMatch(normalized, @"\b(5|fünften?|fuenften?|fifth)\b"))
        {
            return 4;
        }

        if (Regex.IsMatch(normalized, @"\b(4|vierten?|fourth)\b"))
        {
            return 3;
        }

        if (Regex.IsMatch(normalized, @"\b(3|dritten?|third)\b"))
        {
            return 2;
        }

        if (Regex.IsMatch(normalized, @"\b(2|zweiten?|second)\b"))
        {
            return 1;
        }

        if (Regex.IsMatch(normalized, @"\b(1|ersten?|first)\b"))
        {
            return 0;
        }

        return null;
    }

    private static EntityKind? ExtractPositionKind(string message)
    {
        if (message.Contains("profil", StringComparison.OrdinalIgnoreCase)
            || message.Contains("profile", StringComparison.OrdinalIgnoreCase))
        {
            return EntityKind.Profile;
        }

        if (message.Contains("sammlung", StringComparison.OrdinalIgnoreCase)
            || message.Contains("collection", StringComparison.OrdinalIgnoreCase))
        {
            return EntityKind.Collection;
        }

        if (message.Contains("bereich", StringComparison.OrdinalIgnoreCase)
            || message.Contains("division", StringComparison.OrdinalIgnoreCase)
            || message.Contains("abteilung", StringComparison.OrdinalIgnoreCase)
            || message.Contains("area", StringComparison.OrdinalIgnoreCase))
        {
            return EntityKind.Division;
        }

        if (message.Contains("kurs", StringComparison.OrdinalIgnoreCase)
            || message.Contains("course", StringComparison.OrdinalIgnoreCase))
        {
            return EntityKind.Course;
        }

        return null;
    }

    /// <summary>
    /// Positions address the visual card order of the last answer. A preferred
    /// kind (e.g. "second profile") filters that order; a bare "the second one"
    /// uses the unified list so mixed bookmark answers stay consistent.
    /// </summary>
    private static EntityRef? SelectRenderedItem(
        ConversationState conversation,
        int index,
        EntityKind? preferredKind)
    {
        var addressable = conversation.LastAddressableItems;

        if (preferredKind is not null)
        {
            var filtered = addressable.Where(item => item.Kind == preferredKind).ToList();

            if (filtered.Count > index)
            {
                return filtered[index];
            }

            // Explicit kind that is not in the last answer must not fall through
            // to another type — that is how cards from mixed answers got swapped.
            return null;
        }

        return addressable.ElementAtOrDefault(index);
    }

    private static bool IsCourseRelated(AgentIntent intent) =>
        intent is AgentIntent.CourseSearch
            or AgentIntent.CourseDetails
            or AgentIntent.CourseCompare
            or AgentIntent.SkillSearch
            or AgentIntent.SkillDetails
            or AgentIntent.SkillCourses
            or AgentIntent.ProfileSearch
            or AgentIntent.ProfileDetails
            or AgentIntent.ProfileCourses;

    private static bool IsProfileCourseRequest(
        string message,
        ConversationState conversation)
    {
        if (conversation.ActiveProfile is null
            || !(message.Contains("kurs", StringComparison.OrdinalIgnoreCase)
                 || message.Contains("course", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return message.Contains("pflicht", StringComparison.OrdinalIgnoreCase)
            || message.Contains("mandatory", StringComparison.OrdinalIgnoreCase)
            || message.Contains("required", StringComparison.OrdinalIgnoreCase)
            || message.Contains("gehören", StringComparison.OrdinalIgnoreCase)
            || message.Contains("gehören dazu", StringComparison.OrdinalIgnoreCase)
            || message.Contains("diesem profil", StringComparison.OrdinalIgnoreCase)
            || message.Contains("dieses profil", StringComparison.OrdinalIgnoreCase)
            || message.Contains("this profile", StringComparison.OrdinalIgnoreCase)
            || message.Contains("belong to", StringComparison.OrdinalIgnoreCase)
            || message.Contains("belong to it", StringComparison.OrdinalIgnoreCase)
            || message.Contains("courses for this profile", StringComparison.OrdinalIgnoreCase)
            || message.Contains("does this profile have", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCollectionRequest(string message)
    {
        if (!(message.Contains("sammlung", StringComparison.OrdinalIgnoreCase)
              || message.Contains("collection", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Positional follow-ups on a collection card are handled earlier; do not
        // rewrite them into a fresh collection search.
        return ExtractItemPosition(message) is null;
    }

    private static string? ExtractCollectionTopic(string message)
    {
        var match = Regex.Match(
            message,
            @"\b(?:zu|über|ueber|about|on)\s+(?<topic>[^?!.]+)",
            RegexOptions.IgnoreCase);

        return match.Success ? match.Groups["topic"].Value.Trim() : null;
    }

    // ===============================================================
}
