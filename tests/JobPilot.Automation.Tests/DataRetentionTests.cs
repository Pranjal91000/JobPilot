using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Infrastructure.Persistence;
using JobPilot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobPilot.Automation.Tests;

public class DataRetentionTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=jobpilot_dev;Username=postgres;Password=postgres";

    private JobPilotDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JobPilotDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new JobPilotDbContext(options);
    }

    [Fact]
    public async Task RunCleanupAsync_PurgesOnlyRecordsOlderThanCutoff()
    {
        await using var db = CreateDbContext();
        var service = new DataRetentionService(db, NullLogger<DataRetentionService>.Instance);

        var oldTimestamp = DateTimeOffset.UtcNow.AddDays(-100);
        var recentTimestamp = DateTimeOffset.UtcNow.AddDays(-10);

        var user = await db.Users.FirstOrDefaultAsync();
        if (user == null)
        {
            user = new AppUser
            {
                UserId = Guid.CreateVersion7(),
                DisplayName = "Retention Test User",
                EmailLogin = "retention@example.com"
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var post = new JobPost
        {
            JobPostId = Guid.CreateVersion7(),
            Source = "Workday",
            NormalizedUrl = $"https://retention.wd5.myworkdayjobs.com/job_{Guid.NewGuid():N}",
            CompanyName = "Retention Corp",
            JobTitle = "Security Engineer",
            DiscoveredUrl = "https://retention.wd5.myworkdayjobs.com/job",
            DiscoveredTitle = "Security Engineer",
            DiscoveredBy = "RetentionTest"
        };
        db.JobPosts.Add(post);

        var app = new JobApplication
        {
            ApplicationId = Guid.CreateVersion7(),
            UserId = user.UserId,
            JobPostId = post.JobPostId,
            ApplicationUrl = post.NormalizedUrl,
            Status = ApplicationStatus.InProgress
        };
        db.JobApplications.Add(app);

        // Terminal session: old vs recent
        var oldSession = new ApplicationSession
        {
            SessionId = Guid.CreateVersion7(),
            ApplicationId = app.ApplicationId,
            SessionStatus = SessionStatus.Completed,
            LastActivityAt = oldTimestamp,
            CompletedAt = oldTimestamp
        };
        var recentSession = new ApplicationSession
        {
            SessionId = Guid.CreateVersion7(),
            ApplicationId = app.ApplicationId,
            SessionStatus = SessionStatus.Completed,
            LastActivityAt = recentTimestamp,
            CompletedAt = recentTimestamp
        };
        db.ApplicationSessions.AddRange(oldSession, recentSession);

        // Old event vs recent event
        var oldEvent = new AutomationEvent
        {
            EventId = Guid.CreateVersion7(),
            SessionId = oldSession.SessionId,
            EventType = "OldAudit",
            EventDescription = "Ancient log",
            EventTimestamp = oldTimestamp
        };
        var recentEvent = new AutomationEvent
        {
            EventId = Guid.CreateVersion7(),
            SessionId = recentSession.SessionId,
            EventType = "RecentAudit",
            EventDescription = "Fresh log",
            EventTimestamp = recentTimestamp
        };
        db.AutomationEvents.AddRange(oldEvent, recentEvent);

        await db.SaveChangesAsync();

        // Run cleanup with 90-day retention
        var result = await service.RunCleanupAsync(retentionDays: 90);

        Assert.True(result.OldEventsCleaned >= 1, "Expected at least 1 old event to be cleaned");
        Assert.True(result.ExpiredSessionsCleaned >= 1, "Expected at least 1 old session to be cleaned");

        // Verify old record was purged
        var verifyEvent = await db.AutomationEvents.FindAsync(oldEvent.EventId);
        Assert.Null(verifyEvent);

        // Verify recent record was preserved
        var verifyRecentEvent = await db.AutomationEvents.FindAsync(recentEvent.EventId);
        Assert.NotNull(verifyRecentEvent);
    }
}
