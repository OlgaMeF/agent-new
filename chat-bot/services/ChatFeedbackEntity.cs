using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MB.ComTools.Apps.Data;

/// <summary>
/// Persisted thumbs-up / thumbs-down for one assistant message.
/// Map this entity onto <c>AppDbContext</c> as <c>DbSet&lt;ChatFeedbackEntity&gt; ChatFeedbacks</c>.
/// </summary>
[Table("ChatFeedbacks")]
public sealed class ChatFeedbackEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Client / API message id shown in the chat UI.</summary>
    [Required]
    [MaxLength(64)]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>positive | negative</summary>
    [Required]
    [MaxLength(16)]
    public string Rating { get; set; } = string.Empty;

    /// <summary>Optional structured reason (wrong_result, wrong_context, …).</summary>
    [MaxLength(64)]
    public string? Reason { get; set; }

    [MaxLength(64)]
    public string? Intent { get; set; }

    [MaxLength(4000)]
    public string? UserMessage { get; set; }

    [MaxLength(8000)]
    public string? AssistantResponse { get; set; }
}
