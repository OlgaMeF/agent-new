using Microsoft.AspNetCore.Mvc;
using MB.ComTools.Apps.Content.Services;
using MB.ComTools.Apps.Services;
using Microsoft.AspNetCore.Authorization;
using MB.ComTools.Apps.Exceptions;
using System.ComponentModel.DataAnnotations;

namespace MB.ComTools.Apps.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly AgentService _agentService;
    private readonly SiteDefinitionService _siteDefinitionService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        AgentService agentService,
        SiteDefinitionService siteDefinitionService,
        ILogger<ChatController> logger)
    {
        _agentService = agentService;
        _siteDefinitionService = siteDefinitionService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Chat(ChatRequest request)
    {
        var siteUrl = ResolveSiteUrl();

        try
        {
            var site = await _siteDefinitionService.GetBySiteUrlAsync(siteUrl);
            if (!site.IsChatbotEnabled)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Chatbot is currently disabled."
                });
            }
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }

        var answer = await _agentService.ProcessAsync(
            request.Message,
            HttpContext.RequestAborted);

        return Ok(new
        {
            answer
        });
    }

    [HttpPost("feedback")]
    [AllowAnonymous]
    public IActionResult Feedback(ChatFeedbackRequest request)
    {
        if (request.Rating is not ("positive" or "negative"))
        {
            return BadRequest();
        }

        _logger.LogInformation(
            "CHAT_FEEDBACK messageId={MessageId}, rating={Rating}, hasReason={HasReason}",
            request.MessageId,
            request.Rating,
            !string.IsNullOrWhiteSpace(request.Reason));

        return NoContent();
    }

    private string ResolveSiteUrl()
    {
        var siteUrlFromPathBase = HttpContext.Request.PathBase.Value?.Trim('/');
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
    [StringLength(500)]
    public string? Reason { get; set; }
}