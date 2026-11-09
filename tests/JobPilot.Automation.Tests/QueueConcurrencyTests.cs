using JobPilot.Application.Features.Automation;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Infrastructure.Persistence;
using JobPilot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobPilot.Automation.Tests;

public class QueueConcurrencyTests
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
    public async Task EnqueueAndClaimCommand_StrictFifoOrder_Succeeds()
    {
        await using var db1 = CreateDbContext();
        await using var db2 = CreateDbContext();

        var service1 = new AutomationCommandService(db1, NullLogger<AutomationCommandService>.Instance);
        var service2 = new AutomationCommandService(db2, NullLogger<AutomationCommandService>.Instance);

        // Ensure user and application exist
        var user = await db1.Users.FirstOrDefaultAsync() ?? new AppUser
        {
            UserId = Guid.CreateVersion7(),
            DisplayName = "Test User",
            EmailLogin = "test@example.com"
        };
        if (!await db1.Users.AnyAsync(u => u.UserId == user.UserId))
        {
            db1.Users.Add(user);
            await db1.SaveChangesAsync();
        }

        var post = new JobPost
        {
            JobPostId = Guid.CreateVersion7(),
            Source = "Workday",
            NormalizedUrl = $"https://test.wd5.myworkdayjobs.com/job/dev_{Guid.NewGuid():N}",
            CompanyName = "Queue Test Corp",
            JobTitle = "Automation Engineer",
            DiscoveredUrl = "https://test.wd5.myworkdayjobs.com/job/dev",
            DiscoveredTitle = "Automation Engineer",
            DiscoveredBy = "Test"
        };
        db1.JobPosts.Add(post);

        var app = new JobApplication
        {
            ApplicationId = Guid.CreateVersion7(),
            UserId = user.UserId,
            JobPostId = post.JobPostId,
            ApplicationUrl = post.NormalizedUrl,
            Status = ApplicationStatus.InProgress
        };
        db1.JobApplications.Add(app);

        // Ensure single active session constraint (ADR-012)
        var lingering = await db1.ApplicationSessions
            .Where(s => s.SessionStatus == SessionStatus.Launching ||
                        s.SessionStatus == SessionStatus.Pending ||
                        s.SessionStatus == SessionStatus.Inspecting ||
                        s.SessionStatus == SessionStatus.Mapping ||
                        s.SessionStatus == SessionStatus.Filling ||
                        s.SessionStatus == SessionStatus.AwaitingUser ||
                        s.SessionStatus == SessionStatus.AwaitingAuthentication ||
                        s.SessionStatus == SessionStatus.Navigating ||
                        s.SessionStatus == SessionStatus.WatchingForConfirmation ||
                        s.SessionStatus == SessionStatus.Paused)
            .ToListAsync();
        foreach (var s in lingering)
        {
            s.SessionStatus = SessionStatus.Completed;
        }
        await db1.SaveChangesAsync();

        var session = new ApplicationSession
        {
            SessionId = Guid.CreateVersion7(),
            ApplicationId = app.ApplicationId,
            SessionStatus = SessionStatus.Launching
        };
        db1.ApplicationSessions.Add(session);
        await db1.SaveChangesAsync();

        // Enqueue two sequential commands
        var cmd1 = await service1.EnqueueCommandAsync(session.SessionId, AutomationCommandType.Inspect);
        await Task.Delay(50); // Guarantee distinct EnqueuedAt timestamps
        var cmd2 = await service1.EnqueueCommandAsync(session.SessionId, AutomationCommandType.Autofill);

        // Worker 1 claims command
        var claimed1 = await service1.FetchNextPendingCommandAsync("worker-test-1");
        Assert.NotNull(claimed1);
        Assert.Equal(cmd1.CommandId, claimed1.CommandId);
        Assert.Equal(AutomationCommandStatus.Running, claimed1.Status);
        Assert.Equal(1, claimed1.Attempt);

        // Worker 2 claims next command (Worker 1's claimed command is skipped because it's no longer 'Queued')
        var claimed2 = await service2.FetchNextPendingCommandAsync("worker-test-2");
        Assert.NotNull(claimed2);
        Assert.Equal(cmd2.CommandId, claimed2.CommandId);
        Assert.Equal(AutomationCommandStatus.Running, claimed2.Status);

        // Complete cmd1 and Fail cmd2
        await service1.CompleteCommandAsync(claimed1.CommandId, "SuccessOutcome");
        await service2.FailCommandAsync(claimed2.CommandId, "Intentional failure error message");

        // Verify status in DB
        await using var verifyDb = CreateDbContext();
        var c1 = await verifyDb.AutomationCommands.FindAsync(claimed1.CommandId);
        Assert.NotNull(c1);
        Assert.Equal(AutomationCommandStatus.Succeeded, c1.Status);
        Assert.Equal("SuccessOutcome", c1.Outcome);
        Assert.NotNull(c1.FinishedAt);

        var c2 = await verifyDb.AutomationCommands.FindAsync(claimed2.CommandId);
        Assert.NotNull(c2);
        Assert.Equal(AutomationCommandStatus.Failed, c2.Status);
        Assert.Equal("Intentional failure error message", c2.Error);
        Assert.NotNull(c2.FinishedAt);
    }
}
