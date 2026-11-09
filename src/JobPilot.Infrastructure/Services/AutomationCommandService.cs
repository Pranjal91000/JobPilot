using System.Text.Json;
using JobPilot.Application.Features.Automation;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobPilot.Infrastructure.Services;

public sealed class AutomationCommandService(
    JobPilotDbContext dbContext,
    ILogger<AutomationCommandService> logger) : IAutomationCommandService
{
    public async Task<AutomationCommandDto> EnqueueCommandAsync(
        Guid sessionId,
        AutomationCommandType commandType,
        object? payload = null,
        CancellationToken ct = default)
    {
        var session = await dbContext.ApplicationSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found.");

        var command = new AutomationCommand
        {
            CommandId = Guid.CreateVersion7(),
            SessionId = sessionId,
            CommandType = commandType,
            Payload = payload != null
                ? JsonSerializer.SerializeToDocument(payload)
                : JsonDocument.Parse("{}"),
            Status = AutomationCommandStatus.Queued,
            IdempotencyKey = $"{sessionId:N}_{commandType}_{Guid.NewGuid():N}",
            EnqueuedAt = DateTimeOffset.UtcNow
        };

        dbContext.AutomationCommands.Add(command);

        // Also record an automation event for auditing
        var evt = new AutomationEvent
        {
            EventId = Guid.CreateVersion7(),
            SessionId = sessionId,
            CommandId = command.CommandId,
            EventType = $"CommandEnqueued_{commandType}",
            EventDescription = $"Automation command {commandType} enqueued for session {sessionId}.",
            EventStatus = AutomationEventStatus.Info,
            EventTimestamp = DateTimeOffset.UtcNow
        };
        dbContext.AutomationEvents.Add(evt);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Enqueued {CommandType} command {CommandId} for session {SessionId}",
            commandType, command.CommandId, sessionId);

        return ToDto(command);
    }

    public async Task<AutomationCommand?> FetchNextPendingCommandAsync(
        string workerInstanceId,
        CancellationToken ct = default)
    {
        // Use a database transaction to execute SELECT ... FOR UPDATE SKIP LOCKED
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        // Query raw SQL to safely lock the next queued command without blocking other workers
        var commandIds = await dbContext.Database
            .SqlQueryRaw<Guid>(
                """
                SELECT command_id AS "Value"
                FROM jobpilot.automation_command
                WHERE status = 'Queued'
                ORDER BY enqueued_at ASC
                LIMIT 1
                FOR UPDATE SKIP LOCKED;
                """)
            .ToListAsync(ct);

        if (commandIds.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        var commandId = commandIds[0];
        var command = await dbContext.AutomationCommands
            .FirstOrDefaultAsync(c => c.CommandId == commandId, ct);

        if (command != null)
        {
            command.Status = AutomationCommandStatus.Running;
            command.StartedAt = DateTimeOffset.UtcNow;
            command.Attempt += 1;

            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogInformation("Worker {WorkerId} claimed command {CommandId} ({CommandType})",
                workerInstanceId, command.CommandId, command.CommandType);

            return command;
        }

        await tx.RollbackAsync(ct);
        return null;
    }

    public async Task CompleteCommandAsync(
        Guid commandId,
        string outcome,
        string? resultJson = null,
        CancellationToken ct = default)
    {
        var command = await dbContext.AutomationCommands
            .FirstOrDefaultAsync(c => c.CommandId == commandId, ct);

        if (command == null) return;

        // Refresh entity in case xmin updated during prior transaction
        await dbContext.Entry(command).ReloadAsync(ct);

        command.Status = AutomationCommandStatus.Succeeded;
        command.Outcome = outcome;
        command.FinishedAt = DateTimeOffset.UtcNow;

        var evt = new AutomationEvent
        {
            EventId = Guid.Empty,
            SessionId = command.SessionId,
            CommandId = command.CommandId,
            EventType = $"CommandCompleted_{command.CommandType}",
            EventDescription = $"Command {command.CommandType} completed with outcome: {outcome}",
            EventStatus = AutomationEventStatus.Info,
            EventTimestamp = DateTimeOffset.UtcNow
        };
        dbContext.AutomationEvents.Add(evt);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Completed command {CommandId} with outcome {Outcome}", commandId, outcome);
    }

    public async Task FailCommandAsync(
        Guid commandId,
        string error,
        CancellationToken ct = default)
    {
        var command = await dbContext.AutomationCommands
            .FirstOrDefaultAsync(c => c.CommandId == commandId, ct);

        if (command == null) return;

        // Refresh entity in case xmin updated during prior transaction
        await dbContext.Entry(command).ReloadAsync(ct);

        command.Status = AutomationCommandStatus.Failed;
        command.Error = error;
        command.FinishedAt = DateTimeOffset.UtcNow;

        var evt = new AutomationEvent
        {
            EventId = Guid.Empty,
            SessionId = command.SessionId,
            CommandId = command.CommandId,
            EventType = $"CommandFailed_{command.CommandType}",
            EventDescription = $"Command {command.CommandType} failed: {error}",
            EventStatus = AutomationEventStatus.Error,
            EventTimestamp = DateTimeOffset.UtcNow
        };
        dbContext.AutomationEvents.Add(evt);

        await dbContext.SaveChangesAsync(ct);

        logger.LogWarning("Command {CommandId} failed: {Error}", commandId, error);
    }

    public async Task<IReadOnlyList<AutomationCommandDto>> GetSessionCommandsAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        var commands = await dbContext.AutomationCommands
            .AsNoTracking()
            .Where(c => c.SessionId == sessionId)
            .OrderByDescending(c => c.EnqueuedAt)
            .ToListAsync(ct);

        return commands.Select(ToDto).ToList();
    }

    private static AutomationCommandDto ToDto(AutomationCommand c) =>
        new(
            c.CommandId,
            c.SessionId,
            c.CommandType,
            c.Status,
            c.Outcome,
            c.Error,
            c.EnqueuedAt,
            c.StartedAt,
            c.FinishedAt);
}

public sealed class WorkerHeartbeatService(
    JobPilotDbContext dbContext) : IWorkerHeartbeatService
{
    public async Task RecordHeartbeatAsync(
        string workerInstanceId,
        string status,
        Guid? activeSessionId = null,
        CancellationToken ct = default)
    {
        var heartbeat = await dbContext.WorkerHeartbeats
            .FirstOrDefaultAsync(h => h.WorkerInstanceId == workerInstanceId, ct);

        var displayStatus = activeSessionId.HasValue ? $"{status}:{activeSessionId.Value:N}" : status;

        if (heartbeat == null)
        {
            heartbeat = new WorkerHeartbeat
            {
                WorkerInstanceId = workerInstanceId,
                LastHeartbeatAt = DateTimeOffset.UtcNow,
                Status = displayStatus
            };
            dbContext.WorkerHeartbeats.Add(heartbeat);
        }
        else
        {
            heartbeat.LastHeartbeatAt = DateTimeOffset.UtcNow;
            heartbeat.Status = displayStatus;
        }

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<bool> IsWorkerOnlineAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(30);
        return await dbContext.WorkerHeartbeats
            .AsNoTracking()
            .AnyAsync(h => h.Status != null && h.Status.StartsWith("Online") && h.LastHeartbeatAt >= cutoff, ct);
    }
}
