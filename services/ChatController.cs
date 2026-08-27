using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MB.ComTools.Apps.Content.Services;
using MB.ComTools.Apps.Data;
using MB.ComTools.Apps.Exceptions;
using MB.ComTools.Apps.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MB.ComTools.Apps.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private static readonly JsonSerializerOptions StreamJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AgentService _agentService;
    private readonly AppDbContext _db;
    private readonly SiteDefinitionService _siteDefinitionService;
    private readonly ChatClientOptions _chatClientOptions;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        AgentService agentService,
        AppDbContext db,
        SiteDefinitionService siteDefinitionService,
        ChatClientOptions chatClientOptions,
        ILogger<ChatController> logger)
    {
        _agentService = agentService;
        _db = db;
        _siteDefinitionService = siteDefinitionService;
        _chatClientOptions = chatClientOptions;
        _logger = logger;
    }

    /// <summary>
    /// Structured chat turn. Returns prose, cards, suggestions and a legacy
    /// marker string for older clients.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Chat(ChatRequest request)
    {
        var gate = await EnsureChatbotEnabledAsync();
        if (gate is not null)
        {
            return gate;
        }

        var result = await _agentService.ProcessTurnAsync(
            request.Message,
            HttpContext.RequestAborted);

        return Ok(new
        {
            answer = result.AnswerLegacy,
            messageId = result.MessageId,
            prose = result.Prose,
            cards = result.Cards,
            suggestions = result.Suggestions,
            intent = result.Intent,
            language = result.Language,
            conversationId = result.ConversationId,
            turnId = result.TurnId,
            tools = result.Tools,
            cardIds = result.CardIds,
            totalElapsedMs = result.TotalElapsedMs
        });
    }

    /// <summary>
    /// Server-Sent Events stream: status, prose, cards, suggestions and completion.
    /// </summary>
    [HttpPost("stream")]
    public async Task Stream(ChatRequest request)
    {
        var gate = await EnsureChatbotEnabledAsync();
        if (gate is not null)
        {
            Response.StatusCode = gate is ObjectResult { StatusCode: { } code }
                ? code
                : StatusCodes.Status503ServiceUnavailable;

            return;
        }

        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");
        Response.ContentType = "text/event-stream";

        await foreach (var evt in _agentService.ProcessTurnStreamAsync(
                           request.Message,
                           HttpContext.RequestAborted))
        {
            var json = JsonSerializer.Serialize(evt, StreamJsonOptions);

            await Response.WriteAsync(
                $"event: {evt.Type}\n",
                HttpContext.RequestAborted);

            await Response.WriteAsync(
                $"data: {json}\n\n",
                HttpContext.RequestAborted);

            await Response.Body.FlushAsync(HttpContext.RequestAborted);
        }
    }

    /// <summary>
    /// Persists thumbs feedback.
    /// </summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback(ChatFeedbackRequest request)
    {
        if (request.Rating is not ("positive" or "negative"))
        {
            return BadRequest();
        }

        try
        {
            _logger.LogInformation(
                "FEEDBACK_SAVE user='{User}' assistant='{Assistant}'",
                request.UserMessage,
                request.AssistantResponse);

            _db.ChatFeedbacks.Add(new ChatFeedbackEntity
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                MessageId = request.MessageId,
                Rating = request.Rating,
                Reason = request.Reason,
                UserMessage = request.UserMessage,
                AssistantResponse = request.AssistantResponse
            });

            await _db.SaveChangesAsync(HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist chat feedback for messageId={MessageId}",
                request.MessageId);
        }

        return NoContent();
    }

    private async Task<IActionResult?> EnsureChatbotEnabledAsync()
    {
        // Deployment-level switch.
        if (!_chatClientOptions.Enabled)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message = "Chatbot is currently disabled."
                });
        }

        // Per-site switch.
        var siteUrl = ResolveSiteUrl();

        try
        {
            var site = await _siteDefinitionService.GetBySiteUrlAsync(siteUrl);

            if (!site.IsChatbotEnabled)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        message = "Chatbot is currently disabled."
                    });
            }
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }

        return null;
    }

    private string ResolveSiteUrl()
    {
        var siteUrlFromPathBase =
            HttpContext.Request.PathBase.Value?.Trim('/');

        return string.IsNullOrWhiteSpace(siteUrlFromPathBase)
            ? "learn-skills"
            : siteUrlFromPathBase;
    }
}

public class ChatRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;
}

public class ChatFeedbackRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string MessageId { get; set; } = string.Empty;

    [StringLength(20)]
    public string Rating { get; set; } = string.Empty;

    [StringLength(64)]
    public string? Reason { get; set; }

    public string? UserMessage { get; set; }

    public string? AssistantResponse { get; set; }
}
