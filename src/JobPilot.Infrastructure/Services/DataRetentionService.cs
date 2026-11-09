using JobPilot.Application.Features.Retention;
using JobPilot.Domain.Enums;
using JobPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobPilot.Infrastructure.Services;

public sealed class DataRetentionService(
    JobPilotDbContext dbContext,
    ILogger<DataRetentionService> logger) : IDataRetentionService
{
    public async Task<RetentionCleanupResult> RunCleanupAsync(int retentionDays = 90, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        logger.LogInformation("Running retention cleanup for records older than {Cutoff} ({Days} days)", cutoff, retentionDays);

        // 1. Purge old automation events
        var oldEvents = await dbContext.AutomationEvents
            .Where(e => e.EventTimestamp < cutoff)
            .ToListAsync(ct);
        dbContext.AutomationEvents.RemoveRange(oldEvents);

        // 2. Purge old finished commands
        var oldCommands = await dbContext.AutomationCommands
            .Where(c => c.FinishedAt != null && c.FinishedAt < cutoff)
            .ToListAsync(ct);
        dbContext.AutomationCommands.RemoveRange(oldCommands);

        // 3. Purge old AI interaction logs
        var oldLogs = await dbContext.AiInteractionLogs
            .Where(l => l.CreatedAt < cutoff)
            .ToListAsync(ct);
        dbContext.AiInteractionLogs.RemoveRange(oldLogs);

        // 4. Clean up terminal sessions older than retention days
        var oldSessions = await dbContext.ApplicationSessions
            .Where(s => (s.SessionStatus == SessionStatus.Completed ||
                         s.SessionStatus == SessionStatus.Cancelled ||
                         s.SessionStatus == SessionStatus.Failed) &&
                        s.LastActivityAt < cutoff)
            .ToListAsync(ct);
        dbContext.ApplicationSessions.RemoveRange(oldSessions);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Retention cleanup completed: {Sessions} sessions, {Events} events, {Commands} commands, {Logs} AI logs purged.",
            oldSessions.Count, oldEvents.Count, oldCommands.Count, oldLogs.Count);

        return new RetentionCleanupResult(
            oldSessions.Count,
            oldEvents.Count,
            oldCommands.Count,
            oldLogs.Count);
    }
}
