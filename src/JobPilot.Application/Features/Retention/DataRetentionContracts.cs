namespace JobPilot.Application.Features.Retention;

public sealed record RetentionCleanupResult(
    int ExpiredSessionsCleaned,
    int OldEventsCleaned,
    int OldCommandsCleaned,
    int OldLogsCleaned);

public interface IDataRetentionService
{
    Task<RetentionCleanupResult> RunCleanupAsync(int retentionDays = 90, CancellationToken ct = default);
}
