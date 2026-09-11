using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Runtime.CompilerServices;
using MB.ComTools.Apps.Content.Services.Agent;

namespace MB.ComTools.Apps.Content.Services;

/// <summary>
/// Conversational agent for the learn-skills platform (orchestrator).
/// Split across partial files by phase — see README and <c>agent/AgentLayers.cs</c>.
/// Logical layers: Router (native tool calling) → Executor (MCP) → Renderer (prose/cards).
/// Conversation state is persisted via <see cref="IConversationStore"/> for scale-out.
/// </summary>
public partial class AgentService
{
    private const int DefaultSearchLimit = 8;
    private const int MaxToolCallsPerRound = 4;
    private const int MaxHistoryTurns = 2;
    private const int MaxCardsDefault = 5;
    private const int MaxCardsForCourseLists = 5;
    private const int MaxTrackedConversations = 500;
    private const int HistoryEntryMaxLength = 320;
    private const int MaxMessageLength = 2000;

    private static readonly TimeSpan FollowUpContextTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ConversationEvictionAge = TimeSpan.FromHours(3);

    private static readonly Regex JsonObjectRegex =
        new(@"\{[\s\S]*\}", RegexOptions.Compiled);

    private static readonly Regex ContentIdRegex =
        new(@"/content/([^/?#\s]+)", RegexOptions.Compiled);

    private static readonly Regex ProfileIdRegex =
        new(@"/profile/view/([^/?#\s]+)", RegexOptions.Compiled);

    private static readonly Regex IdentifierLikeRegex =
        new(@"^[A-Za-z0-9][A-Za-z0-9_\-.:]{7,}$", RegexOptions.Compiled);

    private static readonly Regex BlockMarkerRegex =
        new(@"\[(/?)(COURSE_CARD|PROFILE_CARD|COLLECTION_CARD|DIVISION_CARD|COMPARE_TABLE|SUGGESTIONS)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Comparisons are answered in prose, so a table the model produced anyway is
    /// stripped — both the markdown pipe form and leftover COLUMNS/ROW lines.
    /// </summary>
    private static readonly Regex TableLineRegex =
        new(@"^[ \t]*(\|.*|(COLUMNS|ROW)[ \t]*:.*)$",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Tool catalogue. Personal user-context tools (bookmarks, progress) are not
    /// available — the agent has no access to personal information.
    /// </summary>
    private static readonly Dictionary<string, ToolDescriptor> Tools = new(StringComparer.OrdinalIgnoreCase)
    {
        ["search_courses"] = new("search_courses", EntityKind.Course, ArgumentNeed.Topic,
            "Find courses by topic or keyword. Results include difficulty level when set."),
        ["get_course"] = new("get_course", EntityKind.Course, ArgumentNeed.Identifier,
            "Get course details including difficulty level."),
        ["get_courses_by_tag"] = new("get_courses_by_tag", EntityKind.Course, ArgumentNeed.Topic,
            "Get courses for a skill."),

        ["search_skills"] = new("search_skills", EntityKind.Skill, ArgumentNeed.Optional,
            "Use for skill search, categories and hierarchy."),
        ["get_skill"] = new("get_skill", EntityKind.Skill, ArgumentNeed.Topic,
            "Get skill details."),

        ["search_profiles"] = new("search_profiles", EntityKind.Profile, ArgumentNeed.Optional,
            "Search profiles."),
        ["get_profile"] = new("get_profile", EntityKind.Profile, ArgumentNeed.Identifier,
            "Get profile details."),
        ["get_profile_skills"] = new("get_profile_skills", EntityKind.Profile, ArgumentNeed.Topic,
            "Skills of a profile. Accepts profile name or id."),

        ["get_divisions"] = new("get_divisions", EntityKind.Division, ArgumentNeed.Optional,
            "Get divisions. Use for 'which areas exist'."),
        ["search_collections"] = new("search_collections", EntityKind.Collection, ArgumentNeed.Optional,
            "Curated public learning collections. A topic may be given and is matched locally."),
    };

    private static readonly ConcurrentDictionary<string, ConversationState> ConversationStates =
        new(StringComparer.Ordinal);

    private readonly McpClientService _mcpClientService;
    private readonly GenAiService _genAiService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConversationStore _conversationStore;
    private readonly ILogger<AgentService> _logger;

    public AgentService(
        McpClientService mcpClientService,
        GenAiService genAiService,
        IHttpContextAccessor httpContextAccessor,
        IConversationStore conversationStore,
        ILogger<AgentService> logger)
    {
        _mcpClientService = mcpClientService;
        _genAiService = genAiService;
        _httpContextAccessor = httpContextAccessor;
        _conversationStore = conversationStore;
        _logger = logger;
    }

    /// <summary>
    /// Backward-compatible entry: returns the legacy marker string only.
    /// Prefer <see cref="ProcessTurnAsync"/> for structured UI payloads.
    /// </summary>
    public async Task<string> ProcessAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        var turn = await ProcessTurnAsync(message, cancellationToken);
        return turn.AnswerLegacy;
    }

    /// <summary>
    /// Full turn: structured cards/suggestions/prose + legacy marker string + telemetry.
    /// </summary>
    public async Task<ChatTurnResult> ProcessTurnAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        await foreach (var evt in ProcessTurnStreamAsync(message, cancellationToken))
        {
            if (evt.Type == "done" && evt.Result is not null)
            {
                return evt.Result;
            }

            if (evt.Type == "error" && evt.Result is not null)
            {
                return evt.Result;
            }
        }

        return BuildErrorTurnResult(
            conversationId: "unknown",
            language: "de",
            Localize("de",
                "Ich konnte die Anfrage gerade nicht verarbeiten. Bitte versuche es in wenigen Sekunden erneut.",
                "I could not process that request right now. Please try again in a few seconds."));
    }

    /// <summary>
    /// Streams status → prose → cards → suggestions → done for progressive UI.
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> ProcessTurnStreamAsync(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var events = await RunTurnEventsAsync(message, cancellationToken);

        foreach (var evt in events)
        {
            yield return evt;
        }
    }

    private async Task<List<ChatStreamEvent>> RunTurnEventsAsync(
        string message,
        CancellationToken cancellationToken)
    {
        var events = new List<ChatStreamEvent>();

        if (string.IsNullOrWhiteSpace(message))
        {
            var empty = BuildErrorTurnResult(
                "anon::default",
                "de",
                "Bitte stell mir eine Frage – zum Beispiel zu Kursen, Skills, Profilen oder Lernpfaden.");
            events.Add(new ChatStreamEvent { Type = "done", Result = empty, Prose = empty.Prose });
            return events;
        }

        if (message.Length > MaxMessageLength)
        {
            var tooLong = BuildErrorTurnResult(
                "anon::default",
                "de",
                $"Bitte kürze deine Frage auf höchstens {MaxMessageLength} Zeichen.");
            events.Add(new ChatStreamEvent { Type = "done", Result = tooLong, Prose = tooLong.Prose });
            return events;
        }

        ConversationState? conversation = null;
        var conversationGateHeld = false;
        var telemetry = new AgentTurnTelemetry(_logger);
        var conversationId = "anon::default";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            events.Add(new ChatStreamEvent { Type = "status", Message = "routing" });

            var perception = Perceive(message);
            conversationId = perception.ConversationId;
            conversation = await GetConversationStateAsync(perception.ConversationId, cancellationToken);
            await conversation.ProcessingGate.WaitAsync(cancellationToken);
            conversationGateHeld = true;

            telemetry.Start(perception.ConversationId);
            telemetry.Mark("perception");

            ReasoningResult reasoning;

            _logger.LogInformation(
                "USER_MESSAGE={Message}",
                perception.OriginalMessage);

            if (TryResolveFollowUp(
                    perception.OriginalMessage,
                    conversation,
                    out var followUpReasoning))
            {
                reasoning = followUpReasoning;

                _logger.LogInformation(
                    "FOLLOW_UP_RESOLVED intent={Intent} calls={Calls}",
                    reasoning.Intent,
                    DescribeToolCalls(reasoning.ToolCalls));
            }
            else
            {
                reasoning = await AnalyzeMessageAsync(
                    perception,
                    conversation,
                    cancellationToken);

                    _logger.LogInformation(
    "SLOTS={Slots}",
    JsonSerializer.Serialize(reasoning.Slots));

            }

            reasoning = ResolveToolCalls(
                reasoning,
                conversation,
                perception.OriginalMessage);

            // Validate the reasoning against the intent-tool matrix before proceeding
            reasoning = ValidateIntentToolMatrix(reasoning);

            reasoning = ValidateReasoning(reasoning, conversation);

            _logger.LogInformation(
                "ROUTER intent={Intent} toolCalls={Calls}",
                reasoning.Intent,
                DescribeToolCalls(reasoning.ToolCalls));

            // Nur einmal erstellen
            var plan = CreatePlan(
                reasoning,
                conversation,
                perception.OriginalMessage);

            _logger.LogInformation(
                "PLAN_DEBUG intent={Intent}",
                plan.Intent);

            telemetry.Intent = IntentToWireName(plan.Intent);
            telemetry.Mark("routing");

            events.Add(new ChatStreamEvent
            {
                Type = "status",
                Message = plan.NeedsData ? "fetching" : "writing"
            });
            _logger.LogInformation(
    "EXECUTE_DEBUG intent={Intent}",
    plan.Intent);
            var evidence = await ExecutePlanAsync(plan, conversation, cancellationToken);
            telemetry.Tools.AddRange(evidence.ExecutedTools);
            telemetry.CourseCount = evidence.Courses.Count;
            telemetry.ProfileCount = evidence.Profiles.Count;
            telemetry.Mark("execution");

            events.Add(new ChatStreamEvent { Type = "status", Message = "writing" });

            var answer = await ComposeAnswerAsync(plan, evidence, conversation, cancellationToken);
            telemetry.Mark("response");

            UpdateConversationState(conversation, plan, evidence, answer);
            await PersistConversationAsync(perception.ConversationId, conversation, cancellationToken);
            telemetry.Mark("state");

            var finalResult = BuildTurnResult(perception.ConversationId, plan, evidence, answer);
            telemetry.CardIds.AddRange(finalResult.CardIds);
            telemetry.Complete(finalResult);

            events.Add(new ChatStreamEvent { Type = "prose", Prose = finalResult.Prose });

            foreach (var card in finalResult.Cards)
            {
                events.Add(new ChatStreamEvent { Type = "card", Card = card });
            }

            if (finalResult.Suggestions.Count > 0)
            {
                events.Add(new ChatStreamEvent
                {
                    Type = "suggestions",
                    Suggestions = finalResult.Suggestions
                });
            }

            events.Add(new ChatStreamEvent { Type = "done", Result = finalResult });
            return events;
        }
        catch (OperationCanceledException)
        {
            var cancelled = BuildErrorTurnResult(
                conversationId,
                conversation?.Language ?? "de",
                Localize(conversation?.Language,
                    "Die Anfrage wurde abgebrochen. Bitte versuche es erneut.",
                    "The request was cancelled. Please try again."));
            events.Add(new ChatStreamEvent { Type = "error", Message = cancelled.Prose, Result = cancelled });
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing chat message.");
            var failed = BuildErrorTurnResult(
                conversationId,
                conversation?.Language ?? "de",
                Localize(conversation?.Language,
                    "Ich konnte die Anfrage gerade nicht verarbeiten. Bitte versuche es in wenigen Sekunden erneut.",
                    "I could not process that request right now. Please try again in a few seconds."));
            telemetry.Complete(failed);
            events.Add(new ChatStreamEvent { Type = "error", Message = failed.Prose, Result = failed });
            return events;
        }
        finally
        {
            if (conversationGateHeld)
            {
                conversation!.ProcessingGate.Release();
            }
        }
    }

    // ===============================================================
    // PHASE 1 — PERCEPTION
    // ===============================================================

    private UserPerception Perceive(string message)
    {
        return new UserPerception(
            OriginalMessage: message.Trim(),
            ConversationId: ResolveConversationId(),
            Timestamp: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The conversation key combines the authenticated user with the browser tab
    /// session. Without the user part, everyone behind a shared proxy would
    /// share one context; without the session part, two tabs would collide.
    /// The key has to stay stable across the requests of one conversation — a
    /// per-request GUID would silently disable every follow-up.
    /// </summary>
    private string ResolveConversationId()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext is null)
        {
            return "anon::default";
        }

        // The OIDC handler maps "sub" onto ClaimTypes.NameIdentifier by default,
        // so probing "sub" alone usually finds nothing.
        var userKey =
            FirstNonEmpty(
                httpContext.User?.FindFirst("sub")?.Value,
                httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                httpContext.User?.Identity?.Name)
            ?? "anon";

        var sessionKey =
            FirstNonEmpty(
                httpContext.Request.Headers["x-chat-session-id"].ToString(),
                httpContext.Request.Cookies.TryGetValue("chat-session-id", out var cookie) ? cookie : null)
            ?? "default";

        return $"{userKey}::{sessionKey}";
    }

    private async Task<ConversationState> GetConversationStateAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        EvictStaleConversations();

        var state = ConversationStates.GetOrAdd(
            conversationId,
            _ => new ConversationState { LastUpdatedAt = DateTimeOffset.UtcNow });

        var hydratedFromStore = false;

        lock (state.Gate)
        {
            if (state.LastUpdatedAt < DateTimeOffset.UtcNow - FollowUpContextTtl)
            {
                state.Reset();
            }
        }

        // Cold instance / scale-out: restore durable snapshot when memory is empty.
        if (state.History.Count == 0
            && state.LastAddressableItems.Count == 0
            && state.ActiveProfile is null
            && state.ActiveCourse is null)
        {
            var snapshot = await _conversationStore.GetAsync(conversationId, cancellationToken);

            if (snapshot is not null
                && snapshot.LastUpdatedAt >= DateTimeOffset.UtcNow - FollowUpContextTtl)
            {
                lock (state.Gate)
                {
                    ApplySnapshot(state, snapshot);
                    hydratedFromStore = true;
                }
            }
        }

        lock (state.Gate)
        {
            state.LastUpdatedAt = DateTimeOffset.UtcNow;
        }

        if (hydratedFromStore)
        {
            _logger.LogInformation(
                "CONVERSATION_HYDRATED conversation={ConversationId}, addressable={Addressable}",
                conversationId,
                state.LastAddressableItems.Count);
        }

        return state;
    }

    private async Task PersistConversationAsync(
        string conversationId,
        ConversationState state,
        CancellationToken cancellationToken)
    {
        ConversationSnapshot snapshot;

        lock (state.Gate)
        {
            snapshot = ToSnapshot(state);
        }

        await _conversationStore.SaveAsync(conversationId, snapshot, cancellationToken);
    }

    private static ConversationSnapshot ToSnapshot(ConversationState state) =>
        new()
        {
            History = state.History
                .Select(t => new HistoryTurnSnapshot { Role = t.Role, Text = t.Text })
                .ToList(),
            LastCourses = state.LastCourses.Select(ToEntitySnapshot).ToList(),
            LastProfiles = state.LastProfiles.Select(ToEntitySnapshot).ToList(),
            LastSkills = state.LastSkills.Select(ToEntitySnapshot).ToList(),
            LastCollections = state.LastCollections.Select(ToEntitySnapshot).ToList(),
            LastDivisions = state.LastDivisions.Select(ToEntitySnapshot).ToList(),
            LastAddressableItems = state.LastAddressableItems.Select(ToEntitySnapshot).ToList(),
            ActiveCourse = state.ActiveCourse is null ? null : ToEntitySnapshot(state.ActiveCourse),
            ActiveProfile = state.ActiveProfile is null ? null : ToEntitySnapshot(state.ActiveProfile),
            ActiveSkill = state.ActiveSkill is null ? null : ToEntitySnapshot(state.ActiveSkill),
            Slots = new Dictionary<string, string>(state.Slots, StringComparer.OrdinalIgnoreCase),
            PendingSlot = state.PendingSlot,
            Language = state.Language,
            LastIntent = IntentToWireName(state.LastIntent),
            LastRenderedKind = state.LastRenderedKind.ToString().ToLowerInvariant(),
            LastSearchTopic = state.LastSearchTopic,
            LastUpdatedAt = state.LastUpdatedAt
        };

    private static void ApplySnapshot(ConversationState state, ConversationSnapshot snapshot)
    {
        state.Reset();
        state.History.AddRange(snapshot.History.Select(t => new ConversationTurn(t.Role, t.Text)));
        state.LastCourses = snapshot.LastCourses.Select(FromEntitySnapshot).ToList();
        state.LastProfiles = snapshot.LastProfiles.Select(FromEntitySnapshot).ToList();
        state.LastSkills = snapshot.LastSkills.Select(FromEntitySnapshot).ToList();
        state.LastCollections = snapshot.LastCollections.Select(FromEntitySnapshot).ToList();
        state.LastDivisions = snapshot.LastDivisions.Select(FromEntitySnapshot).ToList();
        state.LastAddressableItems = snapshot.LastAddressableItems.Select(FromEntitySnapshot).ToList();
        state.ActiveCourse = snapshot.ActiveCourse is null ? null : FromEntitySnapshot(snapshot.ActiveCourse);
        state.ActiveProfile = snapshot.ActiveProfile is null ? null : FromEntitySnapshot(snapshot.ActiveProfile);
        state.ActiveSkill = snapshot.ActiveSkill is null ? null : FromEntitySnapshot(snapshot.ActiveSkill);
        foreach (var slot in snapshot.Slots)
        {
            state.Slots[slot.Key] = slot.Value;
        }

        state.PendingSlot = snapshot.PendingSlot;
        state.Language = snapshot.Language;
        if (TryParseIntent(
                snapshot.LastIntent,
                out var intent))
        {
            state.LastIntent = intent;
        }
        else
        {
            state.LastIntent = default; // oder euer Default
        }
        state.LastRenderedKind = ParseEntityKind(snapshot.LastRenderedKind);
        state.LastSearchTopic = snapshot.LastSearchTopic;
        state.LastUpdatedAt = snapshot.LastUpdatedAt;
    }

    private static EntityRefSnapshot ToEntitySnapshot(EntityRef entity) =>
        new()
        {
            Id = entity.Id,
            DisplayName = entity.DisplayName,
            Kind = entity.Kind.ToString().ToLowerInvariant()
        };

    private static EntityRef FromEntitySnapshot(EntityRefSnapshot snapshot) =>
        new(snapshot.Id, snapshot.DisplayName, ParseEntityKind(snapshot.Kind));

    private static EntityKind ParseEntityKind(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "course" => EntityKind.Course,
        "profile" => EntityKind.Profile,
        "skill" => EntityKind.Skill,
        "collection" => EntityKind.Collection,
        "division" => EntityKind.Division,
        _ => EntityKind.None
    };

    private static string IntentToWireName(AgentIntent intent) => intent switch
    {
        AgentIntent.CourseSearch => "course_search",
        AgentIntent.CourseDetails => "course_details",
        AgentIntent.CourseCompare => "course_compare",
        AgentIntent.SkillSearch => "skill_search",
        AgentIntent.SkillDetails => "skill_details",
        AgentIntent.SkillCourses => "skill_courses",
        AgentIntent.ProfileSearch => "profile_search",
        AgentIntent.ProfileDetails => "profile_details",
        AgentIntent.ProfileCourses => "profile_courses",
        AgentIntent.LearningPath => "learning_path",
        AgentIntent.LearningRecommendation => "learning_recommendation",
        AgentIntent.DivisionOverview => "division_overview",
        AgentIntent.Collections => "collections",
        AgentIntent.Bookmarks => "bookmarks",
        AgentIntent.Progress => "progress",
        AgentIntent.Capabilities => "capabilities",
        AgentIntent.SmallTalk => "smalltalk",
        AgentIntent.OutOfScope => "out_of_scope",
        AgentIntent.Clarify => "clarify",
        _ => "course_search"
    };

    private static ChatTurnResult BuildErrorTurnResult(string conversationId, string language, string message) =>
        new()
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ConversationId = conversationId,
            Language = language,
            Prose = message,
            AnswerLegacy = message,
            Intent = "error"
        };

    private static ChatTurnResult BuildTurnResult(
        string conversationId,
        ExecutionPlan plan,
        Evidence evidence,
        AnswerResult answer) =>
        new()
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ConversationId = conversationId,
            Language = plan.Language,
            Intent = IntentToWireName(plan.Intent),
            Prose = answer.Prose,
            AnswerLegacy = answer.Message,
            Cards = answer.StructuredCards.ToList(),
            Suggestions = answer.StructuredSuggestions.ToList(),
            Tools = evidence.ExecutedTools.ToArray(),
            CourseCount = evidence.Courses.Count,
            ProfileCount = evidence.Profiles.Count,
            CardIds = answer.StructuredCards
                .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                .Select(c => c.Id!)
                .ToArray()
        };

    private static ConversationState GetConversationState(string conversationId)
    {
        // Sync helper kept for any leftover call sites; prefer GetConversationStateAsync.
        EvictStaleConversations();

        var state = ConversationStates.GetOrAdd(
            conversationId,
            _ => new ConversationState { LastUpdatedAt = DateTimeOffset.UtcNow });

        lock (state.Gate)
        {
            if (state.LastUpdatedAt < DateTimeOffset.UtcNow - FollowUpContextTtl)
            {
                state.Reset();
            }

            state.LastUpdatedAt = DateTimeOffset.UtcNow;
        }

        return state;
    }

    private static void EvictStaleConversations()
    {
        if (ConversationStates.Count <= MaxTrackedConversations)
        {
            return;
        }

        var threshold = DateTimeOffset.UtcNow - ConversationEvictionAge;

        foreach (var entry in ConversationStates)
        {
            if (entry.Value.LastUpdatedAt < threshold)
            {
                ConversationStates.TryRemove(entry.Key, out _);
            }
        }

        // Age alone does not bound the dictionary: a burst of short-lived tab
        // sessions stays below the age threshold and would grow without limit.
        var overflow = ConversationStates.Count - MaxTrackedConversations;

        if (overflow <= 0)
        {
            return;
        }

        foreach (var entry in ConversationStates
                     .OrderBy(entry => entry.Value.LastUpdatedAt)
                     .Take(overflow))
        {
            ConversationStates.TryRemove(entry.Key, out _);
        }
    }
}
