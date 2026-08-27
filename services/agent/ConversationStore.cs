using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;

namespace MB.ComTools.Apps.Content.Services.Agent;

/// <summary>
/// Persists conversation snapshots between turns. Implementations may be
/// process-local or distributed (Redis via <see cref="IDistributedCache"/>).
/// </summary>
public interface IConversationStore
{
    Task<ConversationSnapshot?> GetAsync(string conversationId, CancellationToken cancellationToken = default);

    Task SaveAsync(string conversationId, ConversationSnapshot snapshot, CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON-serializable conversation state. Mirrors the fields of AgentService's
/// internal ConversationState without coupling to private types.
/// </summary>
public sealed class ConversationSnapshot
{
    public List<HistoryTurnSnapshot> History { get; set; } = [];

    public List<EntityRefSnapshot> LastCourses { get; set; } = [];

    public List<EntityRefSnapshot> LastProfiles { get; set; } = [];

    public List<EntityRefSnapshot> LastSkills { get; set; } = [];

    public List<EntityRefSnapshot> LastCollections { get; set; } = [];

    public List<EntityRefSnapshot> LastDivisions { get; set; } = [];

    /// <summary>
    /// Visual card order of the previous answer — the only list a bare
    /// positional follow-up may address.
    /// </summary>
    public List<EntityRefSnapshot> LastAddressableItems { get; set; } = [];

    public EntityRefSnapshot? ActiveCourse { get; set; }

    public EntityRefSnapshot? ActiveProfile { get; set; }

    public EntityRefSnapshot? ActiveSkill { get; set; }

    public Dictionary<string, string> Slots { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string? PendingSlot { get; set; }

    public string Language { get; set; } = "de";

    public string LastIntent { get; set; } = "course_search";

    /// <summary>Dominant card kind of the previous answer (e.g. course, profile).</summary>
    public string LastRenderedKind { get; set; } = "none";

    public DateTimeOffset LastUpdatedAt { get; set; }
}

public sealed class HistoryTurnSnapshot
{
    public string Role { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class EntityRefSnapshot
{
    public string? Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Entity kind as string: course | profile | skill | collection | division | none.</summary>
    public string Kind { get; set; } = "none";
}

/// <summary>
/// Process-local store with the same capacity / TTL eviction policy as the
/// static ConcurrentDictionary inside AgentService (max 500 entries, 3h age,
/// overflow by oldest LastUpdatedAt).
/// </summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private const int MaxTrackedConversations = 500;
    private static readonly TimeSpan ConversationEvictionAge = TimeSpan.FromHours(3);

    private readonly ConcurrentDictionary<string, ConversationSnapshot> _states =
        new(StringComparer.Ordinal);

    public Task<ConversationSnapshot?> GetAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        EvictStaleConversations();

        if (!_states.TryGetValue(conversationId, out var snapshot))
        {
            return Task.FromResult<ConversationSnapshot?>(null);
        }

        return Task.FromResult<ConversationSnapshot?>(Clone(snapshot));
    }

    public Task SaveAsync(
        string conversationId,
        ConversationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        EvictStaleConversations();

        snapshot.LastUpdatedAt = DateTimeOffset.UtcNow;
        _states[conversationId] = Clone(snapshot);

        return Task.CompletedTask;
    }

    private void EvictStaleConversations()
    {
        if (_states.Count <= MaxTrackedConversations)
        {
            return;
        }

        var threshold = DateTimeOffset.UtcNow - ConversationEvictionAge;

        foreach (var entry in _states)
        {
            if (entry.Value.LastUpdatedAt < threshold)
            {
                _states.TryRemove(entry.Key, out _);
            }
        }

        var overflow = _states.Count - MaxTrackedConversations;

        if (overflow <= 0)
        {
            return;
        }

        foreach (var entry in _states
                     .OrderBy(e => e.Value.LastUpdatedAt)
                     .Take(overflow))
        {
            _states.TryRemove(entry.Key, out _);
        }
    }

    private static ConversationSnapshot Clone(ConversationSnapshot source) =>
        JsonSerializer.Deserialize<ConversationSnapshot>(
            JsonSerializer.Serialize(source, ConversationStoreJson.Options),
            ConversationStoreJson.Options)
        ?? new ConversationSnapshot();
}

/// <summary>
/// Distributed store backed by <see cref="IDistributedCache"/> (typically Redis
/// via <c>AddStackExchangeRedisCache</c> in the host Program.cs). Keys use the
/// prefix <c>chat:conv:</c> with a 3-hour sliding expiration.
/// </summary>
public sealed class DistributedConversationStore : IConversationStore
{
    public const string KeyPrefix = "chat:conv:";
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromHours(3);

    private readonly IDistributedCache _cache;

    public DistributedConversationStore(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<ConversationSnapshot?> GetAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var bytes = await _cache.GetAsync(KeyPrefix + conversationId, cancellationToken);

        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ConversationSnapshot>(bytes, ConversationStoreJson.Options);
    }

    public async Task SaveAsync(
        string conversationId,
        ConversationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        snapshot.LastUpdatedAt = DateTimeOffset.UtcNow;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, ConversationStoreJson.Options);

        await _cache.SetAsync(
            KeyPrefix + conversationId,
            bytes,
            new DistributedCacheEntryOptions
            {
                SlidingExpiration = SlidingExpiration
            },
            cancellationToken);
    }
}

internal static class ConversationStoreJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
