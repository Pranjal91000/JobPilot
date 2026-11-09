using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Automation;
using JobPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class SessionsController(
    IWorkdayAutomationService automationService,
    IAutomationCommandService commandService,
    IWorkerHeartbeatService heartbeatService,
    JobPilotDbContext dbContext) : ControllerBase
{
    [HttpPost("applications/{applicationId:guid}/session/start")]
    public async Task<ActionResult<SessionDto>> StartSession(Guid applicationId, CancellationToken ct)
    {
        var session = await automationService.StartSessionAsync(applicationId, ct);
        return Ok(session);
    }

    [HttpGet("applications/{applicationId:guid}/session/active")]
    public async Task<ActionResult<SessionDto?>> GetActiveSession(Guid applicationId, CancellationToken ct)
    {
        var activeStatuses = new[]
        {
            Domain.Enums.SessionStatus.Pending,
            Domain.Enums.SessionStatus.Launching,
            Domain.Enums.SessionStatus.AwaitingAuthentication,
            Domain.Enums.SessionStatus.AwaitingUser,
            Domain.Enums.SessionStatus.Inspecting,
            Domain.Enums.SessionStatus.Mapping,
            Domain.Enums.SessionStatus.Filling,
            Domain.Enums.SessionStatus.Navigating,
            Domain.Enums.SessionStatus.WatchingForConfirmation,
            Domain.Enums.SessionStatus.Paused
        };

        var session = await dbContext.ApplicationSessions
            .Include(s => s.Application)
            .Where(s => s.ApplicationId == applicationId && activeStatuses.Contains(s.SessionStatus))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (session == null) return Ok(null);

        var isWorkerOnline = await heartbeatService.IsWorkerOnlineAsync(ct);

        return Ok(new SessionDto(
            session.SessionId,
            session.ApplicationId,
            session.SessionStatus,
            Domain.Enums.AuthState.Unknown,
            session.CurrentStepKey,
            session.Application.CompletionPercentage,
            session.LastActivityAt,
            isWorkerOnline));
    }

    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<ActionResult<SessionDto>> GetSession(Guid sessionId, CancellationToken ct)
    {
        var session = await dbContext.ApplicationSessions
            .Include(s => s.Application)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);

        if (session == null) return NotFound();

        var isWorkerOnline = await heartbeatService.IsWorkerOnlineAsync(ct);

        return Ok(new SessionDto(
            session.SessionId,
            session.ApplicationId,
            session.SessionStatus,
            Domain.Enums.AuthState.Unknown,
            session.CurrentStepKey,
            session.Application.CompletionPercentage,
            session.LastActivityAt,
            isWorkerOnline));
    }

    [HttpPost("sessions/{sessionId:guid}/inspect")]
    public async Task<ActionResult<CommandAccepted>> Inspect(Guid sessionId, CancellationToken ct)
    {
        var result = await automationService.InspectAsync(sessionId, ct);
        return Ok(result);
    }

    [HttpPost("sessions/{sessionId:guid}/map")]
    public async Task<ActionResult<CommandAccepted>> MapFields(Guid sessionId, CancellationToken ct)
    {
        var result = await automationService.MapFieldsAsync(sessionId, ct);
        return Ok(result);
    }

    [HttpPost("sessions/{sessionId:guid}/autofill")]
    public async Task<ActionResult<CommandAccepted>> Autofill(Guid sessionId, CancellationToken ct)
    {
        var result = await automationService.AutofillAsync(sessionId, ct);
        return Ok(result);
    }

    public sealed record NavigateRequest(string Direction);

    [HttpPost("sessions/{sessionId:guid}/navigate")]
    public async Task<ActionResult<CommandAccepted>> Navigate(Guid sessionId, [FromBody] NavigateRequest request, CancellationToken ct)
    {
        var result = await automationService.NavigateAsync(sessionId, request.Direction, ct);
        return Ok(result);
    }

    [HttpPost("sessions/{sessionId:guid}/validate")]
    public async Task<ActionResult<ValidationReport>> Validate(Guid sessionId, CancellationToken ct)
    {
        var result = await automationService.ValidateAsync(sessionId, ct);
        return Ok(result);
    }

    [HttpPost("sessions/{sessionId:guid}/review")]
    public async Task<ActionResult<ReviewResult>> ApplyReview(Guid sessionId, [FromBody] ReviewDecisions decisions, CancellationToken ct)
    {
        var result = await automationService.ApplyReviewAsync(sessionId, decisions, ct);
        return Ok(result);
    }

    public sealed record ApproveSubmissionRequest(Guid ReportId, List<Guid>? AcknowledgedWarnings);

    [HttpPost("sessions/{sessionId:guid}/approve-submission")]
    public async Task<ActionResult<SubmissionApprovalDto>> ApproveSubmission(
        Guid sessionId,
        [FromBody] ApproveSubmissionRequest request,
        CancellationToken ct)
    {
        var result = await automationService.ApproveSubmissionAsync(
            sessionId,
            request.ReportId,
            request.AcknowledgedWarnings ?? [],
            ct);

        return Ok(result);
    }

    [HttpPost("sessions/{sessionId:guid}/pause")]
    public async Task<ActionResult<CommandAccepted>> Pause(Guid sessionId, CancellationToken ct)
    {
        var result = await automationService.PauseAsync(sessionId, ct);
        return Ok(result);
    }

    [HttpPost("sessions/{sessionId:guid}/resume")]
    public async Task<ActionResult<CommandAccepted>> Resume(Guid sessionId, CancellationToken ct)
    {
        var result = await automationService.ResumeAsync(sessionId, ct);
        return Ok(result);
    }

    [HttpPost("sessions/{sessionId:guid}/cancel")]
    public async Task<ActionResult<CommandAccepted>> Cancel(Guid sessionId, CancellationToken ct)
    {
        var result = await automationService.CancelAsync(sessionId, ct);
        return Ok(result);
    }

    [HttpGet("sessions/{sessionId:guid}/commands")]
    public async Task<ActionResult<IReadOnlyList<AutomationCommandDto>>> GetCommands(Guid sessionId, CancellationToken ct)
    {
        var commands = await commandService.GetSessionCommandsAsync(sessionId, ct);
        return Ok(commands);
    }
}
