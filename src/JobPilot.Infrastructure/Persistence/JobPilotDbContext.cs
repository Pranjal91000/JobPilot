using JobPilot.Application.Abstractions;
using JobPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Infrastructure.Persistence;

public class JobPilotDbContext : DbContext
{
    private readonly ICurrentUser? _currentUser;
    private readonly IClock? _clock;

    public JobPilotDbContext(DbContextOptions<JobPilotDbContext> options) : base(options)
    {
    }

    public JobPilotDbContext(
        DbContextOptions<JobPilotDbContext> options,
        ICurrentUser currentUser,
        IClock clock) : base(options)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<ProfileFieldVerification> ProfileFieldVerifications => Set<ProfileFieldVerification>();
    public DbSet<Education> Educations => Set<Education>();
    public DbSet<EmploymentHistory> EmploymentHistories => Set<EmploymentHistory>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Certification> Certifications => Set<Certification>();
    public DbSet<Achievement> Achievements => Set<Achievement>();
    public DbSet<ProfessionalLink> ProfessionalLinks => Set<ProfessionalLink>();
    public DbSet<WorkAuthorization> WorkAuthorizations => Set<WorkAuthorization>();
    public DbSet<LocationPreference> LocationPreferences => Set<LocationPreference>();
    public DbSet<SavedAnswer> SavedAnswers => Set<SavedAnswer>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<Resume> Resumes => Set<Resume>();

    public DbSet<AtsTenant> AtsTenants => Set<AtsTenant>();
    public DbSet<JobPost> JobPosts => Set<JobPost>();
    public DbSet<JobPostSnapshot> JobPostSnapshots => Set<JobPostSnapshot>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<JobApplicationStatusHistory> JobApplicationStatusHistories => Set<JobApplicationStatusHistory>();
    public DbSet<ApplicationSession> ApplicationSessions => Set<ApplicationSession>();
    public DbSet<ApplicationField> ApplicationFields => Set<ApplicationField>();
    public DbSet<FieldMapping> FieldMappings => Set<FieldMapping>();
    public DbSet<ApplicationAnswer> ApplicationAnswers => Set<ApplicationAnswer>();
    public DbSet<SubmissionApproval> SubmissionApprovals => Set<SubmissionApproval>();
    public DbSet<AutomationCommand> AutomationCommands => Set<AutomationCommand>();
    public DbSet<AutomationEvent> AutomationEvents => Set<AutomationEvent>();
    public DbSet<AiInteractionLog> AiInteractionLogs => Set<AiInteractionLog>();
    public DbSet<ProviderSetting> ProviderSettings => Set<ProviderSetting>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();
    public DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("jobpilot");
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobPilotDbContext).Assembly);

        // Apply global query filter for multi-user readiness if user is authenticated
        if (_currentUser != null)
        {
            modelBuilder.Entity<UserProfile>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<ProfileFieldVerification>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<Education>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<EmploymentHistory>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<Skill>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<Project>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<Certification>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<Achievement>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<ProfessionalLink>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<WorkAuthorization>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<LocationPreference>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<SavedAnswer>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<SourceDocument>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<Resume>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<JobApplication>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
            modelBuilder.Entity<JobApplicationStatusHistory>().HasQueryFilter(e => e.Application.UserId == _currentUser.UserId);
            modelBuilder.Entity<ApplicationSession>().HasQueryFilter(e => e.Application.UserId == _currentUser.UserId);
            modelBuilder.Entity<ApplicationField>().HasQueryFilter(e => e.Session.Application.UserId == _currentUser.UserId);
            modelBuilder.Entity<FieldMapping>().HasQueryFilter(e => e.Field.Session.Application.UserId == _currentUser.UserId);
            modelBuilder.Entity<ApplicationAnswer>().HasQueryFilter(e => e.Application.UserId == _currentUser.UserId);
            modelBuilder.Entity<SubmissionApproval>().HasQueryFilter(e => e.Session.Application.UserId == _currentUser.UserId);
            modelBuilder.Entity<AutomationCommand>().HasQueryFilter(e => e.Session.Application.UserId == _currentUser.UserId);
            modelBuilder.Entity<AutomationEvent>().HasQueryFilter(e => e.Session.Application.UserId == _currentUser.UserId);
            modelBuilder.Entity<UserSetting>().HasQueryFilter(e => e.UserId == _currentUser.UserId);
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Domain.Common.AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

