using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MB.ComTools.Apps.Data;
using MXC.ComTools.Apps.LearnSkills.ChatBot.Feedback;
using Microsoft.EntityFrameworkCore;

namespace MB.ComTools.Apps.Controllers;

/// <summary>
/// Admin read API for chat feedback KPIs and negative-turn review.
/// Protect with Admin / MasterAdmin in the host (policy name may differ).
/// </summary>
[ApiController]
[Route("api/admin/chat-feedback")]
[Authorize(Roles = "Admin,MasterAdmin")]
public class ChatFeedbackAdminController : ControllerBase
{
    private readonly AppDbContext _db;

    public ChatFeedbackAdminController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Aggregates: up/down rate, reasons, breakdown by intent for a time range.
    /// Query: from, to (ISO-8601). Defaults to last 30 days.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ChatFeedbackSummaryDto>> Summary(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var (rangeFrom, rangeTo) = ResolveRange(from, to);
        var rows = await _db.ChatFeedbacks
            .Where(x => x.CreatedAt >= rangeFrom &&
                        x.CreatedAt <= rangeTo)
            .ToListAsync(cancellationToken);

        var positive = rows.Count(x => x.Rating == "positive");
        var negative = rows.Count(x => x.Rating == "negative");

        var summary = new ChatFeedbackSummaryDto
        {
            From = rangeFrom,
            To = rangeTo,
            Total = rows.Count,
            Positive = positive,
            Negative = negative,
            PositiveRate = rows.Count == 0
                ? null
                : (double)positive / rows.Count
        };
        return Ok(summary);
    }

    /// <summary>
    /// Newest negative ratings for the review queue.
    /// </summary>
    [HttpGet("negative")]
    public async Task<ActionResult<IReadOnlyList<ChatFeedbackListItemDto>>> Negative(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var (rangeFrom, rangeTo) = ResolveRange(from, to);
        var list = await _db.ChatFeedbacks
            .Where(x =>
                x.Rating == "negative" &&
                x.CreatedAt >= rangeFrom &&
                x.CreatedAt <= rangeTo)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new ChatFeedbackListItemDto
            {
                Id = x.Id,
                CreatedAt = x.CreatedAt,
                MessageId = x.MessageId,
                Rating = x.Rating,
                Reason = x.Reason,
                Intent = x.Intent,
                UserMessage = x.UserMessage,
                AssistantResponse = x.AssistantResponse
            })
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    /// <summary>
    /// Single feedback row detail (turnId, intent, cardIds, …).
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChatFeedbackEntity>> Detail(
        Guid id,
        CancellationToken cancellationToken)
    {
        var row = await _db.ChatFeedbacks
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        return Ok(row);
    }

    private static (DateTimeOffset From, DateTimeOffset To) ResolveRange(
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        var rangeTo = to ?? DateTimeOffset.UtcNow;
        var rangeFrom = from ?? rangeTo.AddDays(-30);

        if (rangeFrom > rangeTo)
        {
            rangeFrom = rangeTo.AddDays(-30);
        }

        return (rangeFrom, rangeTo);
    }
}
