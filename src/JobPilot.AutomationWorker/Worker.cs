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

    private async Task HandleInspectAsync(
        AutomationCommand command,
        IAutomationCommandService commandService,
        JobPilotDbContext dbContext,
        CancellationToken ct)
    {
        var page = browserSessionManager.GetActivePage(command.SessionId)
            ?? throw new InvalidOperationException($"No active browser page found for session {command.SessionId}.");

        var session = await dbContext.ApplicationSessions
            .FirstOrDefaultAsync(s => s.SessionId == command.SessionId, ct)
            ?? throw new KeyNotFoundException($"Session {command.SessionId} not found.");

        var stepKey = await workdayAdapter.DetectCurrentStepKeyAsync(page, ct);
        session.CurrentStepKey = stepKey;
        session.SessionStatus = SessionStatus.Inspecting;

        var detectedFields = await workdayAdapter.InspectFieldsAsync(page, stepKey, ct);

        // Upsert fields into database
        var existingFields = await dbContext.ApplicationFields
            .Where(f => f.SessionId == command.SessionId)
            .ToListAsync(ct);

        var existingMap = existingFields.ToDictionary(f => f.FieldFingerprint);

        var addedCount = 0;
        var updatedCount = 0;

        foreach (var detected in detectedFields)
        {
            if (existingMap.TryGetValue(detected.Fingerprint, out var existing))
            {
                existing.PageValueText = detected.PageValue;
                existing.LocatorRecipe = detected.LocatorRecipe;
                existing.AvailableOptions = detected.AvailableOptions;
                existing.StepKey = detected.StepKey;
                existing.PageSection = detected.Section;
                updatedCount++;
            }
            else
            {
                var newField = new ApplicationField
                {
                    FieldId = Guid.CreateVersion7(),
                    SessionId = command.SessionId,
                    FieldFingerprint = detected.Fingerprint,
                    FieldIdentifier = detected.FieldIdentifier,
                    FieldLabel = detected.Label,
                    FieldType = detected.Type,
                    IsRequired = detected.Required,
                    PageSection = detected.Section,
                    StepKey = detected.StepKey,
                    PageValueText = detected.PageValue,
                    AvailableOptions = detected.AvailableOptions,
                    LocatorRecipe = detected.LocatorRecipe,
                    FillStatus = "Discovered"
                };
                dbContext.ApplicationFields.Add(newField);
                addedCount++;
            }
        }

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
        var page = browserSessionManager.GetActivePage(command.SessionId)
            ?? throw new InvalidOperationException($"No active browser page found for session {command.SessionId}.");

        var session = await dbContext.ApplicationSessions
            .FirstOrDefaultAsync(s => s.SessionId == command.SessionId, ct)
            ?? throw new KeyNotFoundException($"Session {command.SessionId} not found.");

        session.SessionStatus = SessionStatus.Filling;
        await dbContext.SaveChangesAsync(ct);

        var payload = command.Payload.Deserialize<AutofillPayload>()
            ?? new AutofillPayload([]);

        var fieldsToFill = await dbContext.ApplicationFields
            .Where(f => f.SessionId == command.SessionId)
            .ToListAsync(ct);

        var fieldMap = fieldsToFill.ToDictionary(f => f.FieldId);
        var successCount = 0;
        var failCount = 0;

        foreach (var req in payload.Fields)
        {
            if (!fieldMap.TryGetValue(req.FieldId, out var field)) continue;

            // SAFETY: Never autofill sensitive fields or submit buttons
            if (field.FieldIdentifier == "bottom-navigation-submit-button") continue;

            var descriptor = new FieldDescriptor(
                field.FieldId,
                field.FieldFingerprint,
                field.FieldIdentifier,
                field.FieldLabel,
                field.FieldType,
                field.IsRequired,
                field.PageSection,
                field.StepKey,
                field.PageValueText,
                field.AvailableOptions,
                field.LocatorRecipe);

            var result = await workdayAdapter.FillFieldAsync(page, descriptor, req.Value, req.OptionKey, ct);
            if (result.Success)
            {
                field.FillStatus = "FilledVerified";
                field.PageValueText = result.ReadBackValue;
                field.FillError = null;
                successCount++;
            }
            else
            {
                field.FillStatus = "FillFailed";
                field.FillError = result.Error;
                failCount++;
            }
        }

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
        var page = browserSessionManager.GetActivePage(command.SessionId)
            ?? throw new InvalidOperationException($"No active browser page found for session {command.SessionId}.");

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
        var page = browserSessionManager.GetActivePage(command.SessionId)
            ?? throw new InvalidOperationException($"No active browser page found for session {command.SessionId}.");

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
}
