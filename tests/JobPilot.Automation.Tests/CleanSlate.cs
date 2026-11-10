using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using JobPilot.Infrastructure.Persistence;
using Xunit;

namespace JobPilot.Automation.Tests;

public class CleanSlate
{
    [Fact]
    public async Task Wipe()
    {
        var options = new DbContextOptionsBuilder<JobPilotDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=jobpilot_dev;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var db = new JobPilotDbContext(options);
        var apps = await db.JobApplications.ToListAsync();
        db.JobApplications.RemoveRange(apps);
        await db.SaveChangesAsync();

        var posts = await db.JobPosts.ToListAsync();
        db.JobPosts.RemoveRange(posts);
        await db.SaveChangesAsync();

        var commands = await db.AutomationCommands.ToListAsync();
        db.AutomationCommands.RemoveRange(commands);
        await db.SaveChangesAsync();
    }
}

