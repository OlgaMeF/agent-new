namespace MB.ComTools.Apps.Content.Services;

/// <summary>
/// Public chat DTOs shared by the API and frontend. Marker-string answers remain
/// available via <see cref="ChatTurnResult.AnswerLegacy"/> during migration.
/// </summary>
public sealed class ChatCardDto
{
    /// <summary>One of: course | profile | collection | division.</summary>
    public string Kind { get; set; } = string.Empty;

    public string? Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Url { get; set; }

    public List<ChatFactDto> Facts { get; set; } = [];

    public List<string> Chips { get; set; } = [];
}

public sealed class ChatFactDto
{
    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Structured result of one agent turn. Prefer this over parsing marker blocks.
/// </summary>
public sealed class ChatTurnResult
{
    public string MessageId { get; set; } = string.Empty;

    public string Prose { get; set; } = string.Empty;

    public List<ChatCardDto> Cards { get; set; } = [];

    public List<string> Suggestions { get; set; } = [];

    /// <summary>
    /// Full marker-string answer ([COURSE_CARD], [SUGGESTIONS], …) for backward
    /// compatibility with the existing frontend parser.
    /// </summary>
    public string AnswerLegacy { get; set; } = string.Empty;

    public string Intent { get; set; } = string.Empty;

    public string Language { get; set; } = "de";

    public string ConversationId { get; set; } = string.Empty;

    // --- Telemetry summary (also logged by AgentTurnTelemetry) ---

    public string? TurnId { get; set; }

    public IReadOnlyList<string> Tools { get; set; } = [];

    public int CourseCount { get; set; }

    public int ProfileCount { get; set; }

    public IReadOnlyList<string> CardIds { get; set; } = [];

    /// <summary>Phase name → elapsed milliseconds from turn start.</summary>
    public IReadOnlyDictionary<string, long> ElapsedMs { get; set; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    public long TotalElapsedMs { get; set; }
}

/// <summary>
/// Server-sent / streamed event for progressive chat UI updates.
/// </summary>
public sealed class ChatStreamEvent
{
    /// <summary>One of: status | prose | card | suggestions | done | error.</summary>
    public string Type { get; set; } = string.Empty;

    public string? Message { get; set; }

    public ChatCardDto? Card { get; set; }

    public List<string>? Suggestions { get; set; }

    public string? Prose { get; set; }

    /// <summary>Present on <c>done</c> (and optionally <c>error</c>) events.</summary>
    public ChatTurnResult? Result { get; set; }
}
