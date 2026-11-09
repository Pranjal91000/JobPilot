using System.Security.Cryptography;
using System.Text;
using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Automation;
using JobPilot.Application.Features.Mapping;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobPilot.Infrastructure.Services;

public sealed class WorkdayAutomationService(
    JobPilotDbContext dbContext,
    IAutomationCommandService commandService,
    IWorkerHeartbeatService heartbeatService,
    IFieldMappingService mappingService,
    IUserProfileService profileService,
    ILogger<WorkdayAutomationService> logger) : IWorkdayAutomationService
{
    public async Task<SessionDto> StartSessionAsync(Guid applicationId, CancellationToken ct)
    {
        var app = await dbContext.JobApplications
            .Include(a => a.JobPost)
                .ThenInclude(p => p.Tenant)
            .FirstOrDefaultAsync(a => a.ApplicationId == applicationId, ct)
            ?? throw new KeyNotFoundException($"Application {applicationId} not found.");

        // ADR-012 Single active session: complete any prior active sessions
        var activeSessions = await dbContext.ApplicationSessions
            .Where(s => s.SessionStatus != SessionStatus.Completed &&
                        s.SessionStatus != SessionStatus.Cancelled &&
                        s.SessionStatus != SessionStatus.Failed &&
                        s.SessionStatus != SessionStatus.Interrupted)
            .ToListAsync(ct);

        foreach (var active in activeSessions)
        {
            active.SessionStatus = SessionStatus.Completed;
            active.CompletedAt = DateTimeOffset.UtcNow;
            active.LastActivityAt = DateTimeOffset.UtcNow;
        }

        var session = new ApplicationSession
        {
            SessionId = Guid.CreateVersion7(),
            ApplicationId = applicationId,
            SessionStatus = SessionStatus.Launching,
            StartedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow
        };
        dbContext.ApplicationSessions.Add(session);

        // Update application status to InProgress
        if (app.Status == ApplicationStatus.ReadyToApply || app.Status == ApplicationStatus.Discovered)
        {
            app.Status = ApplicationStatus.InProgress;
            app.LastActivityAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(ct);

        // Enqueue command for the worker
        var tenantKey = app.JobPost?.Tenant?.TenantKey ?? "default";
        var cmd = await commandService.EnqueueCommandAsync(
            session.SessionId,
            AutomationCommandType.StartSession,
            new StartSessionPayload(app.ApplicationUrl, tenantKey),
            ct);

        var isWorkerOnline = await heartbeatService.IsWorkerOnlineAsync(ct);

        logger.LogInformation("Started automation session {SessionId} for app {AppId}. Worker online: {WorkerOnline}",
            session.SessionId, applicationId, isWorkerOnline);

        return new SessionDto(
            session.SessionId,
            session.ApplicationId,
            session.SessionStatus,
            AuthState.Unknown,
            session.CurrentStepKey,
            app.CompletionPercentage,
            session.LastActivityAt,
            isWorkerOnline);
    }

    private async Task EnsureSingleActiveSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var otherActiveSessions = await dbContext.ApplicationSessions
            .Where(s => s.SessionId != sessionId &&
                        s.SessionStatus != SessionStatus.Completed &&
                        s.SessionStatus != SessionStatus.Cancelled &&
                        s.SessionStatus != SessionStatus.Failed &&
                        s.SessionStatus != SessionStatus.Interrupted)
            .ToListAsync(ct);

        foreach (var other in otherActiveSessions)
        {
            other.SessionStatus = SessionStatus.Completed;
            other.CompletedAt = DateTimeOffset.UtcNow;
            other.LastActivityAt = DateTimeOffset.UtcNow;
        }
    }

    public async Task<CommandAccepted> InspectAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await dbContext.ApplicationSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found.");

        await EnsureSingleActiveSessionAsync(sessionId, ct);

        session.SessionStatus = SessionStatus.Inspecting;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        var cmd = await commandService.EnqueueCommandAsync(sessionId, AutomationCommandType.Inspect, null, ct);
        return new CommandAccepted(cmd.CommandId, sessionId);
    }

    public async Task<CommandAccepted> MapFieldsAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await dbContext.ApplicationSessions
            .Include(s => s.Fields)
                .ThenInclude(f => f.Mappings)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found.");

        await EnsureSingleActiveSessionAsync(sessionId, ct);

        session.SessionStatus = SessionStatus.Mapping;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        var profile = await profileService.GetVerifiedSnapshotAsync(ct);
        var savedAnswers = await dbContext.SavedAnswers.ToListAsync(ct);

        var descriptors = session.Fields.Select(f => new FieldDescriptor(
            f.FieldId,
            f.FieldFingerprint,
            f.FieldIdentifier,
            f.FieldLabel,
            f.FieldType,
            f.IsRequired,
            f.PageSection,
            f.StepKey,
            f.PageValueText,
            f.AvailableOptions,
            f.LocatorRecipe)).ToList();

        var context = new MappingContext(
            AtsKey: "workday",
            TenantKey: null,
            StepKey: session.CurrentStepKey,
            SectionTitle: null,
            AiEnabled: false); // Deterministic first

        var results = await mappingService.MapFieldsAsync(descriptors, profile, savedAnswers, context, ct);

        foreach (var res in results)
        {
            var cur = await dbContext.FieldMappings
                .FirstOrDefaultAsync(m => m.FieldId == res.FieldId && m.IsCurrent, ct);

            if (cur == null)
            {
                var newMapping = new FieldMapping
                {
                    MappingId = Guid.CreateVersion7(),
                    FieldId = res.FieldId,
                    ProfileFieldReference = res.ProfilePath,
                    SuggestedValue = res.SuggestedValue,
                    SuggestedOptionKey = res.SuggestedOptionKey,
                    ConfidenceScore = res.ConfidenceScore,
                    MappingStatus = res.Status,
                    MappingMethod = res.Method,
                    MappingSource = res.Source,
                    RequiresIntervention = res.RequiresIntervention,
                    Reason = res.Reason,
                    IsCurrent = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                dbContext.FieldMappings.Add(newMapping);
            }
            else if (cur.MappingStatus != MappingStatus.Approved)
            {
                cur.SuggestedValue = res.SuggestedValue;
                cur.SuggestedOptionKey = res.SuggestedOptionKey;
                cur.ConfidenceScore = res.ConfidenceScore;
                cur.MappingStatus = res.Status;
                cur.MappingMethod = res.Method;
                cur.MappingSource = res.Source;
                cur.RequiresIntervention = res.RequiresIntervention;
                cur.Reason = res.Reason;
            }
        }

        session.SessionStatus = SessionStatus.AwaitingUser;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        return new CommandAccepted(Guid.Empty, sessionId);
    }

    public async Task<CommandAccepted> AutofillAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await dbContext.ApplicationSessions
            .Include(s => s.Fields)
                .ThenInclude(f => f.Mappings)
            .Include(s => s.Fields)
                .ThenInclude(f => f.Answer)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found.");

        await EnsureSingleActiveSessionAsync(sessionId, ct);

        // Collect fields approved for filling
        var fillRequests = new List<FillFieldRequest>();

        foreach (var field in session.Fields)
        {
            // SAFETY: Never autofill sensitive fields
            if (field.FieldIdentifier == "bottom-navigation-submit-button") continue;

            string? targetValue = null;
            string? optionKey = null;

            if (field.Answer != null && !string.IsNullOrWhiteSpace(field.Answer.AnswerValue))
            {
                targetValue = field.Answer.AnswerValue;
            }
            else
            {
                var approvedMapping = field.Mappings.FirstOrDefault(m => m.IsCurrent && m.MappingStatus == MappingStatus.Approved);
                if (approvedMapping != null && !string.IsNullOrWhiteSpace(approvedMapping.SuggestedValue))
                {
                    targetValue = approvedMapping.SuggestedValue;
                    optionKey = approvedMapping.SuggestedOptionKey;
                }
            }

            if (!string.IsNullOrWhiteSpace(targetValue))
            {
                fillRequests.Add(new FillFieldRequest(field.FieldId, targetValue, optionKey));
            }
        }

        var cmd = await commandService.EnqueueCommandAsync(
            sessionId,
            AutomationCommandType.Autofill,
            new AutofillPayload(fillRequests),
            ct);

        return new CommandAccepted(cmd.CommandId, sessionId);
    }

    public async Task<CommandAccepted> NavigateAsync(Guid sessionId, string direction, CancellationToken ct)
    {
        var cmd = await commandService.EnqueueCommandAsync(
            sessionId,
            AutomationCommandType.Navigate,
            new NavigatePayload(direction),
            ct);

        return new CommandAccepted(cmd.CommandId, sessionId);
    }

    public async Task<ValidationReport> ValidateAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await dbContext.ApplicationSessions
            .Include(s => s.Fields)
                .ThenInclude(f => f.Mappings)
            .Include(s => s.Fields)
                .ThenInclude(f => f.Answer)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found.");

        await EnsureSingleActiveSessionAsync(sessionId, ct);

        var blocking = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

        var valueConcat = new StringBuilder();

        foreach (var field in session.Fields)
        {
            var hasValue = !string.IsNullOrWhiteSpace(field.PageValueText) ||
                           (field.Answer != null && !string.IsNullOrWhiteSpace(field.Answer.AnswerValue)) ||
                           field.Mappings.Any(m => m.IsCurrent && m.MappingStatus == MappingStatus.Approved && !string.IsNullOrWhiteSpace(m.SuggestedValue));

            valueConcat.Append($"{field.FieldFingerprint}:{field.PageValueText};");

            if (field.IsRequired && !hasValue)
            {
                blocking.Add(new ValidationIssue(
                    "REQUIRED_FIELD_EMPTY",
                    $"Required field '{field.FieldLabel}' has no value provided.",
                    field.FieldId,
                    field.StepKey));
            }

            var currentMapping = field.Mappings.FirstOrDefault(m => m.IsCurrent);
            if (currentMapping != null && currentMapping.MappingStatus == MappingStatus.NeedsReview)
            {
                warnings.Add(new ValidationIssue(
                    "MAPPING_NEEDS_REVIEW",
                    $"Field '{field.FieldLabel}' mapping has not been reviewed.",
                    field.FieldId,
                    field.StepKey));
            }
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valueConcat.ToString()))).ToLowerInvariant();
        var reportId = Guid.CreateVersion7();

        return new ValidationReport(
            reportId,
            DateTimeOffset.UtcNow,
            blocking.Count == 0,
            blocking,
            warnings,
            hash);
    }

    public async Task<ReviewResult> ApplyReviewAsync(Guid sessionId, ReviewDecisions decisions, CancellationToken ct)
    {
        var session = await dbContext.ApplicationSessions
            .Include(s => s.Fields)
                .ThenInclude(f => f.Mappings)
            .Include(s => s.Fields)
                .ThenInclude(f => f.Answer)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found.");

        await EnsureSingleActiveSessionAsync(sessionId, ct);

        var fieldMap = session.Fields.ToDictionary(f => f.FieldId);
        var appliedCount = 0;
        var pendingFill = new List<Guid>();

        foreach (var dec in decisions.Decisions)
        {
            if (!fieldMap.TryGetValue(dec.FieldId, out var field)) continue;

            if (dec.Action == "Approve")
            {
                var cur = field.Mappings.FirstOrDefault(m => m.IsCurrent);
                if (cur != null)
                {
                    cur.MappingStatus = MappingStatus.Approved;
                    appliedCount++;
                    pendingFill.Add(field.FieldId);
                }
            }
            else if (dec.Action == "Reject")
            {
                var cur = field.Mappings.FirstOrDefault(m => m.IsCurrent);
                if (cur != null)
                {
                    cur.MappingStatus = MappingStatus.Rejected;
                    appliedCount++;
                }
            }
            else if (dec.Action == "SetValue" && !string.IsNullOrWhiteSpace(dec.Value))
            {
                if (field.Answer == null)
                {
                    field.Answer = new ApplicationAnswer
                    {
                        AnswerId = Guid.CreateVersion7(),
                        ApplicationId = session.ApplicationId,
                        FieldId = field.FieldId,
                        Question = field.FieldLabel,
                        AnswerValue = dec.Value,
                        AnswerSource = AnswerSource.UserEntered,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    dbContext.ApplicationAnswers.Add(field.Answer);
                }
                else
                {
                    field.Answer.AnswerValue = dec.Value;
                    field.Answer.AnswerSource = AnswerSource.UserEntered;
                }

                var cur = field.Mappings.FirstOrDefault(m => m.IsCurrent);
                if (cur != null)
                {
                    cur.MappingStatus = MappingStatus.Approved;
                }

                appliedCount++;
                pendingFill.Add(field.FieldId);
            }
        }

        await dbContext.SaveChangesAsync(ct);
        return new ReviewResult(appliedCount, false, pendingFill);
    }

    public async Task<SubmissionApprovalDto> ApproveSubmissionAsync(
        Guid sessionId,
        Guid reportId,
        IReadOnlyList<Guid> acknowledgedWarnings,
        CancellationToken ct)
    {
        var session = await dbContext.ApplicationSessions
            .Include(s => s.Application)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found.");

        var approval = new SubmissionApproval
        {
            ApprovalId = Guid.CreateVersion7(),
            SessionId = sessionId,
            ApprovedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            ValidationReportId = reportId.ToString(),
            FieldValuesHash = reportId.ToString()[..16]
        };
        dbContext.SubmissionApprovals.Add(approval);

        session.Application.ReviewStatus = ReviewStatus.Approved;
        session.Application.Status = ApplicationStatus.ConfirmationPending;
        session.LastActivityAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(ct);

        // Enqueue WatchForConfirmation command
        await commandService.EnqueueCommandAsync(sessionId, AutomationCommandType.WatchForConfirmation, null, ct);

        logger.LogInformation("Submission approved for session {SessionId}. Ready for user manual submission in browser.", sessionId);

        return new SubmissionApprovalDto(
            approval.ApprovalId,
            "Workday Submit Button (data-automation-id='bottom-navigation-submit-button')",
            "Your application fields have been verified and approved. Per JobPilot safety policy, you must personally review and click the final Submit button in the visible browser window.",
            approval.ExpiresAt);
    }

    public async Task<CommandAccepted> PauseAsync(Guid sessionId, CancellationToken ct)
    {
        var cmd = await commandService.EnqueueCommandAsync(sessionId, AutomationCommandType.Pause, null, ct);
        return new CommandAccepted(cmd.CommandId, sessionId);
    }

    public async Task<CommandAccepted> ResumeAsync(Guid sessionId, CancellationToken ct)
    {
        var cmd = await commandService.EnqueueCommandAsync(sessionId, AutomationCommandType.Resume, null, ct);
        return new CommandAccepted(cmd.CommandId, sessionId);
    }

    public async Task<CommandAccepted> CancelAsync(Guid sessionId, CancellationToken ct)
    {
        var cmd = await commandService.EnqueueCommandAsync(sessionId, AutomationCommandType.Cancel, null, ct);
        return new CommandAccepted(cmd.CommandId, sessionId);
    }
}
