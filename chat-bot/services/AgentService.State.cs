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
    // PHASE 6 — STATE
    // ===============================================================

    private static void UpdateConversationState(
        ConversationState conversation,
        ExecutionPlan plan,
        Evidence evidence,
        AnswerResult answer)
    {
        lock (conversation.Gate)
        {
            conversation.Language = plan.Language;
            conversation.LastIntent = plan.Intent;
            conversation.PendingSlot = answer.PendingSlot;

            ApplySlots(conversation, plan);

            // Remember the last topical search so "ähnliche / weitere Kurse" does not
            // fall back to a full course title or an empty query after multi-card answers.
            RememberSearchTopic(conversation, plan);

            // A clarification, a greeting or an out-of-scope reply shows nothing
            // and changes no subject, so the previous context survives. Every
            // other turn replaces it — that is what stops a card list from three
            // turns ago from being addressable.
            var keepsPreviousContext = plan.Intent is
                AgentIntent.Clarify or
                AgentIntent.LearningRecommendation or
                AgentIntent.Capabilities or
                AgentIntent.SmallTalk or
                AgentIntent.OutOfScope;

            if (!keepsPreviousContext)
            {
                ApplyRenderedContext(conversation, plan, evidence, answer.Rendered);
            }

            AppendHistory(conversation, plan, answer);

            conversation.LastUpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Slots live for one turn. A topic the user mentioned five questions ago must
    /// never narrow down the current search; only the role slots of a learning
    /// path survive, because that dialogue is explicitly built over several turns.
    /// </summary>
    private static void ApplySlots(ConversationState conversation, ExecutionPlan plan)
    {
        var carried = CarryOverSlots
            .Where(key => conversation.Slots.ContainsKey(key))
            .ToDictionary(key => key, key => conversation.Slots[key], StringComparer.OrdinalIgnoreCase);

        // Profile transition slots must not leak into unrelated skill/course turns
        // (e.g. "Was ist GenAI?" after a Product-Owner question).
        if (plan.Intent is not (
            AgentIntent.LearningRecommendation or
            AgentIntent.LearningPath or
            AgentIntent.ProfileDetails or
            AgentIntent.ProfileCourses or
            AgentIntent.ProfileSearch))
        {
            carried.Remove("currentProfile");
            carried.Remove("targetProfile");
        }

        conversation.Slots.Clear();

        foreach (var slot in carried)
        {
            conversation.Slots[slot.Key] = slot.Value;
        }

        foreach (var slot in plan.Slots)
        {
            conversation.Slots[slot.Key] = slot.Value;
        }
    }

    private static readonly string[] CarryOverSlots = { "currentProfile", "targetProfile" };

    private static void RememberSearchTopic(ConversationState conversation, ExecutionPlan plan)
    {
        if (plan.Intent is not (
            AgentIntent.CourseSearch or
            AgentIntent.CourseDetails or
            AgentIntent.SkillCourses or
            AgentIntent.SkillDetails or
            AgentIntent.SkillSearch or
            AgentIntent.Collections or
            AgentIntent.ProfileSearch or
            AgentIntent.ProfileDetails or
            AgentIntent.ProfileCourses))
        {
            return;
        }

        var query = plan.FirstRound
            .Select(call => call.Query)
            .FirstOrDefault(q => !string.IsNullOrWhiteSpace(q) && !IsOrdinalPhrase(q));

        if (string.IsNullOrWhiteSpace(query)
            && plan.Slots.TryGetValue("topic", out var topic)
            && !IsOrdinalPhrase(topic))
        {
            query = topic;
        }

        if (string.IsNullOrWhiteSpace(query)
            && plan.Slots.TryGetValue("currentProfile", out var profile)
            && !IsOrdinalPhrase(profile))
        {
            query = profile;
        }

        if (plan.Intent == AgentIntent.CourseDetails)
        {
            // Keep the search-thread topic ("kommunikation" / "python"). Replacing it
            // with PreferCourseKeyword(course title) pollutes similar-course follow-ups
            // (e.g. kapitalmarktskommunikation → only the same course → 0 cards).
            var existing = SanitizeTopicQuery(conversation.LastSearchTopic);
            if (!string.IsNullOrWhiteSpace(existing) && !IsOrdinalPhrase(existing))
            {
                return;
            }

            query = PreferCourseKeyword(query) ?? query;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            query = InferSearchQueryFromUserMessage(plan.UserMessage);
        }

        if (IsOrdinalPhrase(query))
        {
            return;
        }

        var sanitized = SanitizeTopicQuery(query);
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            conversation.LastSearchTopic = sanitized;
        }
    }

    private static void ApplyRenderedContext(
        ConversationState conversation,
        ExecutionPlan plan,
        Evidence evidence,
        RenderedCards rendered)
    {
        // Visual order first — positional follow-ups must match what the user saw.
        conversation.LastAddressableItems = rendered.Addressable.ToList();

        conversation.LastCourses = rendered.Courses.ToList();
        conversation.LastProfiles = rendered.Profiles.ToList();
        conversation.LastCollections = rendered.Collections.ToList();
        conversation.LastDivisions = rendered.Divisions.ToList();
        conversation.LastRenderedKind = rendered.PrimaryKind;

        conversation.ActiveCourse =
            rendered.Courses.Count == 1 || plan.Intent == AgentIntent.CourseDetails
                ? rendered.Courses.FirstOrDefault()
                : null;

        conversation.ActiveProfile = ResolveActiveProfile(conversation, plan, evidence, rendered);

        // Skills are explained in prose, never on a card, so they are remembered
        // for name matching ("which courses train Moderation?") but deliberately
        // not for positional selection.
        conversation.LastSkills = evidence.Skills
            .Take(MaxCardsForCourseLists)
            .Select(s => new EntityRef(s.Id, s.Name, EntityKind.Skill))
            .ToList();

        var namedSkill = FindNamedSkill(plan, evidence);

        conversation.ActiveSkill = namedSkill is not null
            ? new EntityRef(namedSkill.Id, namedSkill.Name, EntityKind.Skill)
            : conversation.LastSkills.Count == 1
                ? conversation.LastSkills[0]
                : null;
    }

    /// <summary>
    /// UC8 answers a profile question with course cards only, so the subject of
    /// the conversation has to outlive a turn that renders no profile card.
    /// </summary>
    private static EntityRef? ResolveActiveProfile(
        ConversationState conversation,
        ExecutionPlan plan,
        Evidence evidence,
        RenderedCards rendered)
    {
        if (rendered.Profiles.Count == 1)
        {
            return rendered.Profiles[0];
        }

        if (evidence.Profiles.Count == 1)
        {
            var profile = evidence.Profiles[0];

            return new EntityRef(profile.Id, profile.Title, EntityKind.Profile);
        }

        var profileScoped = plan.Intent is
            AgentIntent.ProfileCourses or
            AgentIntent.ProfileDetails or
            AgentIntent.LearningPath;

        return profileScoped ? conversation.ActiveProfile : null;
    }

    private static void AppendHistory(
        ConversationState conversation,
        ExecutionPlan plan,
        AnswerResult answer)
    {
        conversation.History.Add(new ConversationTurn(
            "user",
            Truncate(plan.UserMessage, HistoryEntryMaxLength)!));

        if (!string.IsNullOrWhiteSpace(answer.Prose))
        {
            conversation.History.Add(new ConversationTurn(
                "assistant",
                Truncate(answer.Prose, HistoryEntryMaxLength)!));
        }

        var overflow = conversation.History.Count - MaxHistoryTurns * 2;

        if (overflow > 0)
        {
            conversation.History.RemoveRange(0, overflow);
        }
    }
}

