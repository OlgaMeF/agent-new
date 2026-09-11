using System.Diagnostics;
using MB.ComTools.Apps.Content.Services;
using Microsoft.Extensions.Logging;

namespace MB.ComTools.Apps.Content.Services.Agent;

/// <summary>
/// Structured per-turn metrics. Prefer one instance per <c>ProcessAsync</c> call.
/// </summary>
public sealed class AgentTurnTelemetry
{
    private readonly ILogger _logger;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Dictionary<string, long> _phases = new(StringComparer.OrdinalIgnoreCase);

    public string TurnId { get; private set; } = string.Empty;

    public string ConversationId { get; private set; } = string.Empty;

    public string? Intent { get; set; }

    public List<string> Tools { get; } = [];

    public int CourseCount { get; set; }

    public int ProfileCount { get; set; }

    public List<string> CardIds { get; } = [];

    public IReadOnlyDictionary<string, long> ElapsedMs => _phases;

    public AgentTurnTelemetry(ILogger logger)
    {
        _logger = logger;
    }

    public AgentTurnTelemetry Start(string conversationId, string? turnId = null)
    {
        ConversationId = conversationId;
        TurnId = string.IsNullOrWhiteSpace(turnId) ? Guid.NewGuid().ToString("N") : turnId;
        _stopwatch.Restart();
        _phases.Clear();
        Tools.Clear();
        CardIds.Clear();
        CourseCount = 0;
        ProfileCount = 0;
        Intent = null;

        _logger.LogInformation(
            "AGENT_TURN_START turnId={TurnId}, conversationId={ConversationId}",
            TurnId,
            ConversationId);

        return this;
    }

    /// <summary>
    /// Records elapsed milliseconds from turn start for the named phase
    /// (e.g. perception, reasoning, planning, execution, response, state).
    /// </summary>
    public void Mark(string phase)
    {
        var elapsed = _stopwatch.ElapsedMilliseconds;
        _phases[phase] = elapsed;

        _logger.LogInformation(
            "AGENT_TURN_PHASE turnId={TurnId}, conversationId={ConversationId}, phase={Phase}, elapsedMs={ElapsedMs}",
            TurnId,
            ConversationId,
            phase,
            elapsed);
    }

    public void Complete(ChatTurnResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var totalMs = _stopwatch.ElapsedMilliseconds;
        _phases["complete"] = totalMs;

        // Enrich the result so API consumers / stream done-events carry the same summary.
        result.TurnId ??= TurnId;
        result.ConversationId = string.IsNullOrWhiteSpace(result.ConversationId)
            ? ConversationId
            : result.ConversationId;
        result.Intent = string.IsNullOrWhiteSpace(result.Intent)
            ? Intent ?? result.Intent
            : result.Intent;
        result.Tools = Tools.Count > 0 ? Tools.ToArray() : result.Tools;
        result.CourseCount = CourseCount > 0 ? CourseCount : result.CourseCount;
        result.ProfileCount = ProfileCount > 0 ? ProfileCount : result.ProfileCount;
        result.CardIds = CardIds.Count > 0
            ? CardIds.ToArray()
            : result.Cards
                .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                .Select(c => c.Id!)
                .ToArray();
        result.ElapsedMs = new Dictionary<string, long>(_phases, StringComparer.OrdinalIgnoreCase);
        result.TotalElapsedMs = totalMs;

        _logger.LogInformation(
            "AGENT_TURN_COMPLETE turnId={TurnId}, conversationId={ConversationId}, intent={Intent}, " +
            "tools={Tools}, courseCount={CourseCount}, profileCount={ProfileCount}, " +
            "cardIds={CardIds}, totalElapsedMs={TotalElapsedMs}, phases={Phases}",
            TurnId,
            ConversationId,
            result.Intent,
            string.Join(",", result.Tools),
            result.CourseCount,
            result.ProfileCount,
            string.Join(",", result.CardIds),
            totalMs,
            string.Join(";", _phases.Select(p => $"{p.Key}={p.Value}")));
    }
}
