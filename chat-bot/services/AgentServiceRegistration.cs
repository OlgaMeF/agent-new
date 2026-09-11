using MB.ComTools.Apps.Content.Services.Agent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MB.ComTools.Apps.Content.Services;

/// <summary>
/// DI registration for the learn-skills chat agent and feedback store.
/// </summary>
/// <remarks>
/// <para>
/// Conversation store: <c>Chat:ConversationStore=distributed</c> → Redis via
/// <see cref="IDistributedCache"/>; otherwise in-memory.
/// </para>
/// <para>
/// Feedback store: <c>Chat:FeedbackStore=sql</c> → <see cref="EfChatFeedbackStore"/>
/// (requires host <see cref="IChatFeedbackDbContext"/>); otherwise
/// <see cref="InMemoryChatFeedbackStore"/> for local smoke tests.
/// </para>
/// </remarks>
public static class AgentServiceRegistration
{
    public const string ConversationStoreConfigKey = "Chat:ConversationStore";
    public const string DistributedStoreValue = "distributed";
    /// <summary>
    /// Registers conversation store, feedback store/service, and <see cref="AgentService"/>.
    /// </summary>
    public static IServiceCollection AddLearnSkillsAgent(
        this IServiceCollection services)
    {
        services.AddSingleton<IConversationStore>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var mode = config[ConversationStoreConfigKey];

            if (string.Equals(
                mode,
                DistributedStoreValue,
                StringComparison.OrdinalIgnoreCase))
            {
                return ActivatorUtilities.CreateInstance<DistributedConversationStore>(sp);
            }

            return ActivatorUtilities.CreateInstance<InMemoryConversationStore>(sp);
        });

        services.AddScoped<AgentService>();

        return services;
    }
}
