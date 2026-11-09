using System.Text.Json;
using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Retention;
using JobPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class DataRetentionController(
    IDataRetentionService retentionService,
    IUserProfileService profileService,
    JobPilotDbContext dbContext) : ControllerBase
{
    public sealed record RetentionCleanupRequest(int RetentionDays = 90);

    [HttpPost("settings/retention/cleanup")]
    public async Task<ActionResult<RetentionCleanupResult>> Cleanup(
        [FromBody] RetentionCleanupRequest? request,
        CancellationToken ct)
    {
        var days = request?.RetentionDays ?? 90;
        var result = await retentionService.RunCleanupAsync(days, ct);
        return Ok(result);
    }

    [HttpGet("profile/export")]
    public async Task<IActionResult> ExportData(CancellationToken ct)
    {
        var profile = await profileService.GetAsync(ct);
        var applications = await dbContext.JobApplications
            .Include(a => a.JobPost)
            .Include(a => a.History)
            .Include(a => a.Answers)
            .ToListAsync(ct);

        var exportPayload = new
        {
            ExportedAt = DateTimeOffset.UtcNow,
            Profile = profile,
            Applications = applications.Select(a => new
            {
                a.ApplicationId,
                a.ApplicationUrl,
                a.Status,
                a.SubmissionReference,
                a.SubmissionConfirmedAt,
                JobTitle = a.JobPost?.JobTitle,
                CompanyName = a.JobPost?.CompanyName,
                History = a.History.Select(h => new
                {
                    h.PreviousStatus,
                    h.NewStatus,
                    h.ChangeSource,
                    h.ChangedAt,
                    h.Notes
                }),
                Answers = a.Answers.Select(ans => new
                {
                    ans.Question,
                    ans.AnswerValue,
                    ans.AnswerSource
                })
            })
        };

        var json = JsonSerializer.Serialize(exportPayload, new JsonSerializerOptions { WriteIndented = true });
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"jobpilot_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
    }
}
