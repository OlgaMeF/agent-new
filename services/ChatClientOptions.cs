namespace MB.ComTools.Apps.Services;

/// <summary>
/// Deployment-level switch for the learn-skills chatbot, independent of the per-site
/// "chatbotEnabled" flag stored in SiteDefinition.ContentJson. Staging and Production
/// currently share one database, so the per-site flag alone cannot differ between stages;
/// this option lets each stage's appsettings enable/disable the chat client at deploy time.
/// </summary>
public class ChatClientOptions
{
    public const string SectionName = "ChatClient";

    /// <summary>Whether the chat client is enabled for this deployment. Defaults to false.</summary>
    public bool Enabled { get; set; } = false;
}
