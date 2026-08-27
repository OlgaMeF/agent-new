namespace MB.ComTools.Apps.Content.Services.Agent;

/// <summary>
/// Logical layer contracts. Today <see cref="AgentService"/> implements all three
/// internally; these interfaces document the intended boundaries for extraction.
/// </summary>
public interface IAgentRouter
{
    /// <summary>Maps a user message + conversation snapshot into an execution plan intent and tool calls.</summary>
    Task<AgentRouteDecision> RouteAsync(
        string userMessage,
        ConversationSnapshot conversation,
        CancellationToken cancellationToken = default);
}

public interface IAgentExecutor
{
    /// <summary>Runs MCP tool calls and returns raw evidence counts for telemetry.</summary>
    Task<AgentExecutionSummary> ExecuteAsync(
        AgentRouteDecision route,
        CancellationToken cancellationToken = default);
}

public interface IAgentRenderer
{
    /// <summary>Builds structured cards, suggestions and prose from evidence.</summary>
    Task<ChatTurnResult> RenderAsync(
        AgentRouteDecision route,
        AgentExecutionSummary execution,
        CancellationToken cancellationToken = default);
}

/// <summary>Router output — intent + tool names/args (no MCP facts).</summary>
public sealed class AgentRouteDecision
{
    public string Intent { get; set; } = "course_search";

    public string Language { get; set; } = "de";

    public string UserMessage { get; set; } = string.Empty;

    public List<string> ToolNames { get; set; } = [];

    public bool NeedsData { get; set; }
}

/// <summary>Executor output summary (facts stay inside AgentService evidence today).</summary>
public sealed class AgentExecutionSummary
{
    public List<string> ExecutedTools { get; set; } = [];

    public int CourseCount { get; set; }

    public int ProfileCount { get; set; }

    public int SkillCount { get; set; }

    public bool RequiresSignIn { get; set; }
}
