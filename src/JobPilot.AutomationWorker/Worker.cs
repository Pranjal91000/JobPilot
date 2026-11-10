using System.Text.Json;
using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Automation;
using JobPilot.AutomationWorker.Adapters;
using JobPilot.AutomationWorker.Services;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace JobPilot.AutomationWorker;

public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    IBrowserSessionManager browserSessionManager,
    IWorkdayAdapter workdayAdapter,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly string _workerInstanceId = $"worker-{Environment.MachineName}-{Guid.NewGuid():N}";
    private DateTimeOffset _lastHeartbeat = DateTimeOffset.MinValue;
    private Guid? _activeSessionId;

    public string WorkerInstanceId => _workerInstanceId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("JobPilot Automation Worker started. Instance ID: {InstanceId}", _workerInstanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<JobPilotDbContext>();
                var commandService = scope.ServiceProvider.GetRequiredService<IAutomationCommandService>();
                var heartbeatService = scope.ServiceProvider.GetRequiredService<IWorkerHeartbeatService>();

                // 1. Send periodic heartbeat (every 10 seconds)
                if (DateTimeOffset.UtcNow - _lastHeartbeat >= TimeSpan.FromSeconds(10))
                {
                    await heartbeatService.RecordHeartbeatAsync(_workerInstanceId, "Online", _activeSessionId, stoppingToken);
                    _lastHeartbeat = DateTimeOffset.UtcNow;
                }

                // 2. Poll for the next command from durable queue (PostgreSQL SKIP LOCKED)
                var command = await commandService.FetchNextPendingCommandAsync(_workerInstanceId, stoppingToken);
                if (command != null)
                {
                    logger.LogInformation("Processing command {CommandId} ({CommandType}) for session {SessionId}",
                        command.CommandId, command.CommandType, command.SessionId);

                    await ProcessCommandAsync(command, commandService, dbContext, stoppingToken);
                    continue;
                }

                // Delay between polling cycles
                await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in Automation Worker loop");
                await Task.Delay(2000, stoppingToken);
            }
        }

        logger.LogInformation("JobPilot Automation Worker stopping. Releasing browser resources.");
        await browserSessionManager.DisposeAsync();
    }

    private async Task ProcessCommandAsync(
        AutomationCommand command,
        IAutomationCommandService commandService,
        JobPilotDbContext dbContext,
        CancellationToken ct)
    {
        try
        {
            switch (command.CommandType)
            {
                case AutomationCommandType.StartSession:
                    await HandleStartSessionAsync(command, commandService, dbContext, ct);
                    break;

                case AutomationCommandType.Inspect:
                    await HandleInspectAsync(command, commandService, dbContext, ct);
                    break;

                case AutomationCommandType.Autofill:
                    await HandleAutofillAsync(command, commandService, dbContext, ct);
                    break;

                case AutomationCommandType.Navigate:
                    await HandleNavigateAsync(command, commandService, dbContext, ct);
                    break;

                case AutomationCommandType.WatchForConfirmation:
                    await HandleWatchForConfirmationAsync(command, commandService, dbContext, ct);
                    break;

                case AutomationCommandType.CloseBrowser:
                    await HandleCloseBrowserAsync(command, commandService, dbContext, ct);
                    break;

                case AutomationCommandType.RunToReview:
                    await HandleRunToReviewAsync(command, commandService, dbContext, ct);
                    break;

                default:
                    await commandService.CompleteCommandAsync(command.CommandId, "IgnoredUnsupportedCommand", null, ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing command {CommandId} ({CommandType})", command.CommandId, command.CommandType);
            await commandService.FailCommandAsync(command.CommandId, ex.Message, ct);
        }
    }

    private async Task HandleStartSessionAsync(
        AutomationCommand command,
        IAutomationCommandService commandService,
        JobPilotDbContext dbContext,
        CancellationToken ct)
    {
        var session = await dbContext.ApplicationSessions
            .Include(s => s.Application)
                .ThenInclude(a => a.JobPost)
                    .ThenInclude(p => p.Tenant)
            .FirstOrDefaultAsync(s => s.SessionId == command.SessionId, ct)
            ?? throw new KeyNotFoundException($"Session {command.SessionId} not found.");

        var payload = command.Payload.Deserialize<StartSessionPayload>()
            ?? new StartSessionPayload(session.Application.ApplicationUrl, session.Application.JobPost.Tenant?.TenantKey ?? "default");

        _activeSessionId = session.SessionId;
        session.SessionStatus = SessionStatus.Launching;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        var page = await browserSessionManager.LaunchOrAttachAsync(
            session.SessionId,
            payload.ApplicationUrl,
            payload.TenantKey,
            payload.Headless,
            ct);

        // Check authentication state
        var authState = await workdayAdapter.DetectAuthStateAsync(page, ct);
        if (authState == AuthState.SignInRequired)
        {
            session.SessionStatus = SessionStatus.AwaitingAuthentication;
            await dbContext.SaveChangesAsync(ct);
            await commandService.CompleteCommandAsync(command.CommandId, "AwaitingAuthentication", null, ct);
            logger.LogInformation("Session {SessionId} requires manual user authentication.", session.SessionId);
            return;
        }

        var stepKey = await workdayAdapter.DetectCurrentStepKeyAsync(page, ct);
        session.CurrentStepKey = stepKey;
        session.SessionStatus = SessionStatus.Inspecting;
        await dbContext.SaveChangesAsync(ct);

        await commandService.CompleteCommandAsync(command.CommandId, "Launched", JsonSerializer.Serialize(new { Step = stepKey }), ct);
    }

    private async Task<IPage> EnsureActivePageAsync(Guid sessionId, JobPilotDbContext dbContext, CancellationToken ct)
    {
        var page = browserSessionManager.GetActivePage(sessionId);
        if (page != null && !page.IsClosed)
        {
            return page;
        }

        var session = await dbContext.ApplicationSessions
            .Include(s => s.Application)
                .ThenInclude(a => a.JobPost)
                    .ThenInclude(p => p.Tenant)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found.");

        logger.LogInformation("No active browser attached for session {SessionId}. Auto-attaching persistent browser context...", sessionId);

        var url = session.Application?.ApplicationUrl ?? "";
        var tenantKey = session.Application?.JobPost?.Tenant?.TenantKey ?? "default";

        _activeSessionId = sessionId;

        return await browserSessionManager.LaunchOrAttachAsync(
            sessionId,
            url,
            tenantKey,
            headless: false,
            ct);
    }

    private async Task HandleInspectAsync(
        AutomationCommand command,
        IAutomationCommandService commandService,
        JobPilotDbContext dbContext,
        CancellationToken ct)
    {
        var page = await EnsureActivePageAsync(command.SessionId, dbContext, ct);
        logger.LogInformation("Inspecting session {SessionId}. Page URL: {Url}, Title: {Title}",
            command.SessionId, page.Url, await page.TitleAsync());

        var session = await dbContext.ApplicationSessions
            .FirstOrDefaultAsync(s => s.SessionId == command.SessionId, ct)
            ?? throw new KeyNotFoundException($"Session {command.SessionId} not found.");

        var authState = await workdayAdapter.DetectAuthStateAsync(page, ct);
        if (authState == AuthState.SignInRequired)
        {
            session.SessionStatus = SessionStatus.AwaitingAuthentication;
            await dbContext.SaveChangesAsync(ct);
            await commandService.CompleteCommandAsync(command.CommandId, "AwaitingAuthentication", null, ct);
            logger.LogInformation("Session {SessionId} requires manual user authentication.", session.SessionId);
            return;
        }

        var stepKey = await workdayAdapter.DetectCurrentStepKeyAsync(page, ct);
        session.CurrentStepKey = stepKey;
        session.SessionStatus = SessionStatus.Inspecting;

        var detectedFields = await workdayAdapter.InspectFieldsAsync(page, stepKey, ct);

        var (addedCount, updatedCount) = await StoreInspectionAsync(command.SessionId, stepKey, detectedFields, dbContext, ct);

        session.SessionStatus = SessionStatus.Mapping;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Inspection completed for session {SessionId}: {Added} added, {Updated} updated.",
            command.SessionId, addedCount, updatedCount);

        await commandService.CompleteCommandAsync(
            command.CommandId,
            "Inspected",
            JsonSerializer.Serialize(new { Step = stepKey, Added = addedCount, Updated = updatedCount, Total = detectedFields.Count }),
            ct);
    }

    private async Task HandleAutofillAsync(
        AutomationCommand command,
        IAutomationCommandService commandService,
        JobPilotDbContext dbContext,
        CancellationToken ct)
    {
        var page = await EnsureActivePageAsync(command.SessionId, dbContext, ct);

        var session = await dbContext.ApplicationSessions
            .FirstOrDefaultAsync(s => s.SessionId == command.SessionId, ct)
            ?? throw new KeyNotFoundException($"Session {command.SessionId} not found.");

        session.SessionStatus = SessionStatus.Filling;
        await dbContext.SaveChangesAsync(ct);

        var payload = command.Payload.Deserialize<AutofillPayload>()
            ?? new AutofillPayload([]);

        var outcomes = await FillFieldsAsync(page, command.SessionId, payload.Fields, dbContext, ct);
        var successCount = outcomes.Count(o => o.Result.Success);
        var failCount = outcomes.Count - successCount;

        session.SessionStatus = SessionStatus.AwaitingUser;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        await commandService.CompleteCommandAsync(
            command.CommandId,
            "Autofilled",
            JsonSerializer.Serialize(new { SuccessCount = successCount, FailCount = failCount }),
            ct);
    }

    private async Task HandleNavigateAsync(
        AutomationCommand command,
        IAutomationCommandService commandService,
        JobPilotDbContext dbContext,
        CancellationToken ct)
    {
        var page = await EnsureActivePageAsync(command.SessionId, dbContext, ct);

        var session = await dbContext.ApplicationSessions
            .FirstOrDefaultAsync(s => s.SessionId == command.SessionId, ct)
            ?? throw new KeyNotFoundException($"Session {command.SessionId} not found.");

        var payload = command.Payload.Deserialize<NavigatePayload>() ?? new NavigatePayload("Next");
        session.SessionStatus = SessionStatus.Navigating;
        await dbContext.SaveChangesAsync(ct);

        bool navigated;
        if (payload.Direction.Equals("Back", StringComparison.OrdinalIgnoreCase))
        {
            navigated = await workdayAdapter.NavigateBackAsync(page, ct);
        }
        else
        {
            navigated = await workdayAdapter.NavigateNextAsync(page, ct);
        }

        var newStep = await workdayAdapter.DetectCurrentStepKeyAsync(page, ct);
        session.CurrentStepKey = newStep;
        session.SessionStatus = SessionStatus.Inspecting;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        await commandService.CompleteCommandAsync(
            command.CommandId,
            navigated ? "Navigated" : "NavigationNotCompleted",
            JsonSerializer.Serialize(new { Step = newStep, Navigated = navigated }),
            ct);
    }

    private async Task HandleWatchForConfirmationAsync(
        AutomationCommand command,
        IAutomationCommandService commandService,
        JobPilotDbContext dbContext,
        CancellationToken ct)
    {
        var page = await EnsureActivePageAsync(command.SessionId, dbContext, ct);

        var session = await dbContext.ApplicationSessions
            .Include(s => s.Application)
            .FirstOrDefaultAsync(s => s.SessionId == command.SessionId, ct)
            ?? throw new KeyNotFoundException($"Session {command.SessionId} not found.");

        session.SessionStatus = SessionStatus.WatchingForConfirmation;
        await dbContext.SaveChangesAsync(ct);

        var isSubmitted = await workdayAdapter.DetectSubmissionConfirmationAsync(page, ct);
        if (isSubmitted)
        {
            var reference = await workdayAdapter.ExtractSubmissionReferenceAsync(page, ct);
            session.Application.Status = ApplicationStatus.Submitted;
            session.Application.SubmissionReference = reference;
            session.Application.SubmissionConfirmedAt = DateTimeOffset.UtcNow;
            session.Application.SubmissionConfirmationSource = SubmissionConfirmationSource.Detector;
            session.SessionStatus = SessionStatus.Completed;
            session.LastActivityAt = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync(ct);

            await commandService.CompleteCommandAsync(
                command.CommandId,
                "SubmissionConfirmed",
                JsonSerializer.Serialize(new { Reference = reference }),
                ct);
            return;
        }

        await commandService.CompleteCommandAsync(command.CommandId, "NoConfirmationDetected", null, ct);
    }

    private async Task HandleCloseBrowserAsync(
        AutomationCommand command,
        IAutomationCommandService commandService,
        JobPilotDbContext dbContext,
        CancellationToken ct)
    {
        await browserSessionManager.CloseSessionAsync(command.SessionId, ct);

        var session = await dbContext.ApplicationSessions
            .FirstOrDefaultAsync(s => s.SessionId == command.SessionId, ct);

        if (session != null)
        {
            session.SessionStatus = SessionStatus.Completed;
            session.LastActivityAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);
        }

        _activeSessionId = null;
        await commandService.CompleteCommandAsync(command.CommandId, "Closed", null, ct);
    }

    // =========================================================================================
    // Shared helpers
    // =========================================================================================

    private sealed record FillOutcome(ApplicationField Field, FillResult Result);

    // Column limits (see JobAndAutomationConfigurations). Workday question text can exceed them.
    private static string? Clip(string? s, int max) => s == null || s.Length <= max ? s : s[..max];

    private static FieldDescriptor ToDescriptor(ApplicationField f) => new(
        f.FieldId, f.FieldFingerprint, f.FieldIdentifier ?? "", f.FieldLabel, f.FieldType, f.IsRequired,
        f.PageSection, f.StepKey, f.PageValueText, f.AvailableOptions, f.LocatorRecipe);

    /// <summary>Upserts detected fields; hides fields of this step that are no longer on the page.</summary>
    private static async Task<(int Added, int Updated)> StoreInspectionAsync(
        Guid sessionId, string? stepKey, IReadOnlyList<FieldDescriptor> detectedFields, JobPilotDbContext dbContext, CancellationToken ct)
    {
        var existingFields = await dbContext.ApplicationFields
            .Where(f => f.SessionId == sessionId)
            .ToListAsync(ct);

        var existingMap = existingFields.ToDictionary(f => f.FieldFingerprint);
        var added = 0;
        var updated = 0;

        foreach (var detected in detectedFields)
        {
            if (existingMap.TryGetValue(detected.Fingerprint, out var existing))
            {
                existing.PageValueText = Clip(detected.PageValue, 4000);
                existing.LocatorRecipe = detected.LocatorRecipe;
                // Keep options probed earlier (dropdowns only reveal them when opened).
                if (detected.AvailableOptions != null) existing.AvailableOptions = detected.AvailableOptions;
                existing.StepKey = Clip(detected.StepKey, 100);
                existing.PageSection = Clip(detected.Section, 200);
                existing.FieldLabel = Clip(detected.Label, 500)!;
                existing.IsRequired = detected.Required;
                existing.IsPresent = true;
                existing.LastSeenAt = DateTimeOffset.UtcNow;
                updated++;
            }
            else
            {
                var newField = new ApplicationField
                {
                    FieldId = Guid.CreateVersion7(),
                    SessionId = sessionId,
                    FieldFingerprint = detected.Fingerprint,
                    FieldIdentifier = Clip(detected.FieldIdentifier, 255),
                    FieldLabel = Clip(detected.Label, 500)!,
                    FieldType = detected.Type,
                    IsRequired = detected.Required,
                    PageSection = Clip(detected.Section, 200),
                    StepKey = Clip(detected.StepKey, 100),
                    PageValueText = Clip(detected.PageValue, 4000),
                    AvailableOptions = detected.AvailableOptions,
                    LocatorRecipe = detected.LocatorRecipe,
                    FillStatus = "Discovered"
                };
                dbContext.ApplicationFields.Add(newField);
                existingMap[newField.FieldFingerprint] = newField;
                added++;
            }
        }

        // Fields recorded earlier for this same step that are no longer on the page (removed
        // sections, or controls the old inspector split up, such as one field per radio button)
        // are hidden. Skipped when nothing was detected, so a bad read can't hide every field.
        var detectedFingerprints = detectedFields.Select(d => d.Fingerprint).ToHashSet();
        foreach (var stale in existingFields.Where(f =>
                     detectedFingerprints.Count > 0 &&
                     f.IsPresent &&
                     string.Equals(f.StepKey, Clip(stepKey, 100), StringComparison.OrdinalIgnoreCase) &&
                     !detectedFingerprints.Contains(f.FieldFingerprint)))
        {
            stale.IsPresent = false;
        }

        await dbContext.SaveChangesAsync(ct);
        return (added, updated);
    }

    private async Task<List<FillOutcome>> FillFieldsAsync(
        IPage page, Guid sessionId, IEnumerable<FillFieldRequest> requests, JobPilotDbContext dbContext, CancellationToken ct)
    {
        var fieldMap = await dbContext.ApplicationFields
            .Where(f => f.SessionId == sessionId)
            .ToDictionaryAsync(f => f.FieldId, ct);

        var outcomes = new List<FillOutcome>();
        foreach (var req in requests)
        {
            if (!fieldMap.TryGetValue(req.FieldId, out var field)) continue;
            // SAFETY: never touch the final submit control.
            if (field.FieldIdentifier == "bottom-navigation-submit-button") continue;

            var result = await workdayAdapter.FillFieldAsync(page, ToDescriptor(field), req.Value, req.OptionKey, ct);
            if (result.Success)
            {
                // A success can carry a warning (e.g. some skills Workday doesn't list).
                field.FillStatus = result.Error == null ? "FilledVerified" : "FilledPartial";
                field.PageValueText = Clip(result.ReadBackValue, 4000);
                field.FillError = Clip(result.Error, 1000);
            }
            else
            {
                field.FillStatus = "FillFailed";
                field.FillError = Clip(result.Error, 1000);
            }
            outcomes.Add(new FillOutcome(field, result));
        }

        await dbContext.SaveChangesAsync(ct);
        return outcomes;
    }

    // =========================================================================================
    // Run to Review: works through the application unattended and stops at Workday's Review page.
    // Never clicks Submit. Pauses with a "needs you" message for logins it can't complete,
    // questions it can't answer, and anything Workday rejects.
    // =========================================================================================

    private const int MaxRunIterations = 25;

    private async Task HandleRunToReviewAsync(
        AutomationCommand command,
        IAutomationCommandService commandService,
        JobPilotDbContext dbContext,
        CancellationToken ct)
    {
        using var runScope = scopeFactory.CreateScope();
        var api = runScope.ServiceProvider.GetRequiredService<JobPilotApiClient>();
        var heartbeat = runScope.ServiceProvider.GetRequiredService<IWorkerHeartbeatService>();
        var sessionId = command.SessionId;

        var session = await dbContext.ApplicationSessions.AsNoTracking()
            .Include(s => s.Application).ThenInclude(a => a.JobPost).ThenInclude(p => p.Tenant)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found.");

        var payload = command.Payload.Deserialize<StartSessionPayload>()
                      ?? new StartSessionPayload(session.Application.ApplicationUrl, session.Application.JobPost?.Tenant?.TenantKey ?? "default");
        var tenantKey = payload.TenantKey;
        var jobPostId = session.Application.JobPostId;
        var applicationId = session.ApplicationId;

        async Task Status(string runState, string message, SessionStatus? sessionStatus = null, string? step = null)
        {
            // ExecuteUpdate: no row-version check, so a concurrent UI action can't fail the run.
            var text = message.Length > 2000 ? message[..2000] : message;
            var stepText = step is { Length: > 100 } ? step[..100] : step;
            await dbContext.ApplicationSessions
                .Where(s => s.SessionId == sessionId)
                .ExecuteUpdateAsync(u =>
                {
                    u.SetProperty(s => s.RunState, runState);
                    u.SetProperty(s => s.RunMessage, text);
                    u.SetProperty(s => s.LastActivityAt, DateTimeOffset.UtcNow);
                    if (sessionStatus.HasValue) u.SetProperty(s => s.SessionStatus, sessionStatus.Value);
                    if (stepText != null) u.SetProperty(s => s.CurrentStepKey, stepText);
                }, ct);
            await heartbeat.RecordHeartbeatAsync(_workerInstanceId, "Online", sessionId, ct);
            _lastHeartbeat = DateTimeOffset.UtcNow;
        }

        async Task NeedsUser(string message)
        {
            await Status(RunStates.NeedsUser, message, SessionStatus.AwaitingUser);
            await commandService.CompleteCommandAsync(command.CommandId, "NeedsUser", JsonSerializer.Serialize(new { message }), ct);
            logger.LogInformation("Run {SessionId} needs the user: {Message}", sessionId, message);
        }

        try
        {
            await Status(RunStates.Running, "Opening the job page.", SessionStatus.Launching);
            _activeSessionId = sessionId;
            var page = await browserSessionManager.LaunchOrAttachAsync(sessionId, payload.ApplicationUrl, tenantKey, false, ct);

            string? lastStep = null;
            var sameStepRepeats = 0;
            var applyAttempts = 0;
            var signInAttempts = 0;
            var entriesAddedOnStep = new HashSet<string>();

            for (var iteration = 0; iteration < MaxRunIterations; iteration++)
            {
                ct.ThrowIfCancellationRequested();
                try { await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 15000 }); }
                catch { /* SPA navigation */ }
                await page.WaitForTimeoutAsync(1500);

                if (await workdayAdapter.DetectSubmissionConfirmationAsync(page, ct))
                {
                    await Status(RunStates.ReadyForReview, "Workday shows this application as submitted.", SessionStatus.WatchingForConfirmation);
                    await commandService.CompleteCommandAsync(command.CommandId, "AlreadySubmitted", null, ct);
                    return;
                }

                var auth = await workdayAdapter.DetectAuthStateAsync(page, ct);

                // ---- Sign in / create account -------------------------------------------------
                if (auth == AuthState.SignInRequired)
                {
                    if (++signInAttempts > 2)
                    {
                        await NeedsUser($"Couldn't get past the {tenantKey} sign-in page. Sign in in the browser tab, then press Continue.");
                        return;
                    }

                    var login = await api.GetLoginAsync(tenantKey, ct);
                    if (login == null)
                    {
                        await NeedsUser($"Sign in or create your account on the {tenantKey} Workday site in the browser tab, or save your Workday login in JobPilot (Workday login button), then press Continue.");
                        return;
                    }

                    await Status(RunStates.Running, login.AccountExists ? "Signing in." : "Signing in (or creating your account).", SessionStatus.AwaitingAuthentication);
                    var outcome = await workdayAdapter.SignInOrCreateAccountAsync(page, login.Email, login.Password, login.AccountExists, ct);

                    switch (outcome.Kind)
                    {
                        case AuthOutcomeKind.SignedIn:
                        case AuthOutcomeKind.AccountCreated:
                            await api.MarkAccountExistsAsync(tenantKey, ct);
                            continue;
                        case AuthOutcomeKind.VerificationNeeded:
                            await api.MarkAccountExistsAsync(tenantKey, ct);
                            await NeedsUser($"Workday sent a verification email to {login.Email}. Click the link in it, sign in in the browser tab if asked, then press Continue.");
                            return;
                        default:
                            await NeedsUser($"Couldn't sign in automatically: {outcome.Message} Sign in in the browser tab, then press Continue.");
                            return;
                    }
                }

                // ---- Job posting page: read details, click Apply ------------------------------
                if (auth != AuthState.InApplyFlow)
                {
                    if (applyAttempts == 0) await SaveJobDetailsAsync(page, jobPostId, dbContext, ct);

                    if (++applyAttempts > 2)
                    {
                        await NeedsUser("Couldn't open the application form. Open it in the browser tab (Apply), then press Continue.");
                        return;
                    }

                    await Status(RunStates.Running, "Starting the application.");
                    var started = await workdayAdapter.StartApplicationAsync(page, ct);
                    if (started == null)
                    {
                        await NeedsUser("No Apply button found on this page. Open the application form in the browser tab, then press Continue.");
                        return;
                    }
                    logger.LogInformation("Run {SessionId}: started application via '{Option}'", sessionId, started);
                    continue;
                }

                // ---- A form step ------------------------------------------------------------
                var step = await workdayAdapter.DetectCurrentStepKeyAsync(page, ct);

                if (await workdayAdapter.IsReviewStepAsync(page, step))
                {
                    await Status(RunStates.ReadyForReview,
                        "Ready for your review: check the application on Workday's Review page in the browser tab and press Submit yourself.",
                        SessionStatus.AwaitingUser, step);
                    await dbContext.JobApplications
                        .Where(a => a.ApplicationId == applicationId)
                        .ExecuteUpdateAsync(u => u
                            .SetProperty(a => a.Status, ApplicationStatus.AwaitingUserReview)
                            .SetProperty(a => a.LastActivityAt, DateTimeOffset.UtcNow), ct);
                    await commandService.CompleteCommandAsync(command.CommandId, "ReadyForReview", JsonSerializer.Serialize(new { Step = step }), ct);
                    return;
                }

                sameStepRepeats = step == lastStep ? sameStepRepeats + 1 : 0;
                lastStep = step;
                if (sameStepRepeats >= 2)
                {
                    var errors = await workdayAdapter.DetectValidationErrorsAsync(page, ct);
                    await NeedsUser($"Stuck on \"{step}\". " + (errors.Count > 0
                        ? $"Workday says: {string.Join(" | ", errors.Take(5))}. Fix it in the browser tab or in JobPilot, then press Continue."
                        : "Check the browser tab, then press Continue."));
                    return;
                }

                // Inspect
                await Status(RunStates.Running, $"\"{step}\": reading the form.", SessionStatus.Inspecting, step);
                var detected = await workdayAdapter.InspectFieldsAsync(page, step, ct);
                detected = await ProbeDropdownOptionsAsync(page, detected);
                dbContext.ChangeTracker.Clear();
                await StoreInspectionAsync(sessionId, step, detected, dbContext, ct);

                // Decide (in the API: profile, past answers, standing answers, AI drafts)
                await Status(RunStates.Running, $"\"{step}\": choosing answers.", SessionStatus.Mapping);
                var plan = await api.PlanStepAsync(sessionId, ct);

                // Work experience / education entries in the profile that the page doesn't have yet:
                // click Add, then read the step again so the new entries get planned and filled.
                if (plan.AddEntries is { Count: > 0 } && entriesAddedOnStep.Add(step ?? ""))
                {
                    await Status(RunStates.Running, $"\"{step}\": adding work experience / education entries.");
                    var added = await workdayAdapter.AddRepeatEntriesAsync(page, plan.AddEntries.Select(t => (t.Section, t.Wanted)).ToList());
                    if (added > 0)
                    {
                        logger.LogInformation("Run {SessionId}: added {Count} entries on {Step}", sessionId, added, step);
                        lastStep = null; // re-reading the same step is expected here, not "stuck"
                        continue;
                    }
                }

                // Fill
                await Status(RunStates.Running, $"\"{step}\": filling {plan.Fills.Count} field(s).", SessionStatus.Filling);
                dbContext.ChangeTracker.Clear();
                var outcomes = await FillFieldsAsync(page, sessionId,
                    plan.Fills.Select(f => new FillFieldRequest(f.FieldId, f.Value, f.OptionKey)), dbContext, ct);

                var failedRequired = outcomes.Where(o => !o.Result.Success && o.Field.IsRequired).ToList();
                if (plan.NeedsUser.Count > 0 || failedRequired.Count > 0)
                {
                    var parts = new List<string>();
                    if (plan.NeedsUser.Count > 0)
                        parts.Add("Please answer: " + string.Join("; ", plan.NeedsUser));
                    if (failedRequired.Count > 0)
                        parts.Add("Couldn't fill: " + string.Join("; ", failedRequired.Select(o => $"{o.Field.FieldLabel} ({o.Result.Error})")));
                    await NeedsUser($"\"{step}\" needs you. {string.Join(" ", parts)} Then press Continue.");
                    return;
                }

                // Next step
                await Status(RunStates.Running, $"\"{step}\": saving and continuing.", SessionStatus.Navigating);
                if (!await workdayAdapter.NavigateNextAsync(page, ct))
                {
                    await NeedsUser($"Couldn't find \"Save and Continue\" on \"{step}\". Continue in the browser tab, then press Continue.");
                    return;
                }
            }

            await NeedsUser("Stopped after too many steps without reaching the Review page. Check the browser tab, then press Continue.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {SessionId} failed", sessionId);
            await Status(RunStates.Failed, $"The run stopped with an error: {ex.Message} Press Continue to retry.", SessionStatus.AwaitingUser);
            await commandService.FailCommandAsync(command.CommandId, ex.Message, ct);
        }
    }

    /// <summary>Opens dropdowns without known options so the planner can pick a real option.</summary>
    private async Task<IReadOnlyList<FieldDescriptor>> ProbeDropdownOptionsAsync(IPage page, IReadOnlyList<FieldDescriptor> fields)
    {
        var result = new List<FieldDescriptor>(fields.Count);
        foreach (var f in fields)
        {
            var kind = f.LocatorRecipe?.RootElement.TryGetProperty("kind", out var k) == true ? k.GetString() : null;
            if (kind == "dropdown" && f.AvailableOptions == null)
            {
                var options = await workdayAdapter.ReadDropdownOptionsAsync(page, f);
                if (options.Count > 0)
                {
                    var doc = JsonSerializer.SerializeToDocument(options.Select(o => new { Key = o, Label = o }));
                    result.Add(f with { AvailableOptions = doc });
                    continue;
                }
            }
            result.Add(f);
        }
        return result;
    }

    private async Task SaveJobDetailsAsync(IPage page, Guid jobPostId, JobPilotDbContext dbContext, CancellationToken ct)
    {
        try
        {
            var details = await workdayAdapter.ReadJobDetailsAsync(page);
            if (details.Title == null && details.Description == null) return;

            var title = details.Title is { Length: > 200 } ? details.Title[..200] : details.Title;
            var description = details.Description;
            await dbContext.JobPosts
                .Where(p => p.JobPostId == jobPostId)
                .ExecuteUpdateAsync(u =>
                {
                    if (title != null) u.SetProperty(p => p.JobTitle, title);
                    if (description != null) u.SetProperty(p => p.JobDescription, description);
                }, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Couldn't read job details");
        }
    }
}
