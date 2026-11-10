using System.Net;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Enums;
using JobPilot.Infrastructure.Persistence;
using JobPilot.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Api.Controllers;

/// <summary>
/// Unattended "run to review": paste job links, JobPilot works through each Workday application
/// and stops at the Review page for the user to check and submit.
/// </summary>
[ApiController]
[Route("api")]
public class AutoRunController(
    JobPilotDbContext db,
    IWorkdayAutomationService automationService,
    IJobApplicationService applicationService,
    ILogger<AutoRunController> logger) : ControllerBase
{
    public sealed record BatchRunRequest(IReadOnlyList<string> Urls);
    public sealed record BatchRunItem(string Url, Guid? ApplicationId, string? Error);
    public sealed record RunStatusDto(
        Guid ApplicationId,
        Guid SessionId,
        string? RunState,
        string? RunMessage,
        string? CurrentStepKey,
        string SessionStatus,
        DateTimeOffset LastActivityAt);

    [HttpPost("applications/{applicationId:guid}/run")]
    public async Task<ActionResult<CommandAccepted>> StartRun(Guid applicationId, CancellationToken ct)
        => Ok(await automationService.StartRunAsync(applicationId, ct));

    [HttpPost("applications/batch-run")]
    public async Task<ActionResult<IReadOnlyList<BatchRunItem>>> BatchRun([FromBody] BatchRunRequest request, CancellationToken ct)
    {
        var results = new List<BatchRunItem>();
        var urls = request.Urls
            .Select(u => u.Trim())
            .Where(u => u.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50);

        foreach (var url in urls)
        {
            try
            {
                var intake = await applicationService.IntakeAsync(new IntakeRequest(url, null), ct);
                var appId = intake.Application.ApplicationId;
                await automationService.StartRunAsync(appId, ct);
                results.Add(new BatchRunItem(url, appId, null));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Batch run could not queue {Url}", url);
                results.Add(new BatchRunItem(url, null, ex.Message));
            }
        }

        return Ok(results);
    }

    /// <summary>Latest session per application with its run state, for the applications list.</summary>
    [HttpGet("applications/run-status")]
    public async Task<ActionResult<IReadOnlyList<RunStatusDto>>> RunStatus(CancellationToken ct)
    {
        var sessions = await db.ApplicationSessions.AsNoTracking()
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new { s.ApplicationId, s.SessionId, s.RunState, s.RunMessage, s.CurrentStepKey, s.SessionStatus, s.LastActivityAt })
            .ToListAsync(ct);

        return Ok(sessions
            .GroupBy(s => s.ApplicationId)
            .Select(g => g.First())
            .Select(s => new RunStatusDto(s.ApplicationId, s.SessionId, s.RunState, s.RunMessage, s.CurrentStepKey,
                s.SessionStatus.ToString(), s.LastActivityAt))
            .ToList());
    }

    // ---- Called by the automation worker (localhost only) -----------------------------------

    [HttpPost("internal/sessions/{sessionId:guid}/plan-step")]
    public async Task<ActionResult<StepPlan>> PlanStep(Guid sessionId, [FromServices] AutoRunPlanner planner, CancellationToken ct)
    {
        if (!IsLocalWorker()) return StatusCode(StatusCodes.Status403Forbidden);
        // The worker waits for this; a dropped connection must not leave half-saved decisions.
        return Ok(await planner.PlanStepAsync(sessionId, CancellationToken.None));
    }

    [HttpGet("internal/workday-login/{tenantKey}")]
    public async Task<ActionResult<WorkdayLoginSecret>> GetLogin(string tenantKey, [FromServices] WorkdayLoginService logins, CancellationToken ct)
    {
        if (!IsLocalWorker()) return StatusCode(StatusCodes.Status403Forbidden);
        var secret = await logins.GetSecretAsync(tenantKey, ct);
        return secret == null ? NotFound() : Ok(secret);
    }

    [HttpPost("internal/workday-login/{tenantKey}/account-exists")]
    public async Task<IActionResult> MarkAccount(string tenantKey, [FromServices] WorkdayLoginService logins, CancellationToken ct)
    {
        if (!IsLocalWorker()) return StatusCode(StatusCodes.Status403Forbidden);
        await logins.MarkAccountExistsAsync(tenantKey, ct);
        return NoContent();
    }

    // The worker runs on the same machine and sends this header. Browsers can't send a custom
    // header cross-origin without a CORS preflight, which the LocalDev policy only grants the UI.
    private bool IsLocalWorker() =>
        Request.Headers.TryGetValue("X-JobPilot-Worker", out var v) && v == "1" &&
        HttpContext.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
}

[ApiController]
[Route("api/settings/workday-login")]
public class WorkdayLoginController(WorkdayLoginService logins) : ControllerBase
{
    public sealed record SaveLoginRequest(string Email, string? Password);

    [HttpGet]
    public async Task<ActionResult<WorkdayLoginDto>> Get(CancellationToken ct) => Ok(await logins.GetAsync(ct));

    [HttpPut]
    public async Task<ActionResult<WorkdayLoginDto>> Save([FromBody] SaveLoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) return BadRequest("Email is required.");
        try
        {
            await logins.SaveDefaultAsync(request.Email, request.Password, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        return Ok(await logins.GetAsync(ct));
    }
}
