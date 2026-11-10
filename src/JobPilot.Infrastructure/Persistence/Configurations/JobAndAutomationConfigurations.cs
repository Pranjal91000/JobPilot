using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPilot.Infrastructure.Persistence.Configurations;

public class AtsTenantConfiguration : IEntityTypeConfiguration<AtsTenant>
{
    public void Configure(EntityTypeBuilder<AtsTenant> b)
    {
        b.ToTable("ats_tenant");
        b.HasKey(x => x.TenantId);
        b.Property(x => x.AtsKey).HasMaxLength(32).IsRequired();
        b.Property(x => x.TenantKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.Host).HasMaxLength(255).IsRequired();
        b.Property(x => x.DisplayCompanyName).HasMaxLength(150);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.AtsKey, x.TenantKey }).IsUnique();
    }
}

public class JobPostConfiguration : IEntityTypeConfiguration<JobPost>
{
    public void Configure(EntityTypeBuilder<JobPost> b)
    {
        b.ToTable("job_post");
        b.HasKey(x => x.JobPostId);
        b.Property(x => x.Source).HasMaxLength(32).IsRequired();
        b.Property(x => x.ExternalJobId).HasMaxLength(100);
        b.Property(x => x.SiteKey).HasMaxLength(100);
        b.Property(x => x.Locale).HasMaxLength(16);
        b.Property(x => x.NormalizedUrl).HasMaxLength(1000).IsRequired();
        b.Property(x => x.CompanyName).HasMaxLength(150).IsRequired();
        b.Property(x => x.JobTitle).HasMaxLength(200).IsRequired();
        b.Property(x => x.Location).HasMaxLength(150);
        b.Property(x => x.EmploymentType).HasMaxLength(50);
        b.Property(x => x.ExperienceRequirement).HasMaxLength(100);
        b.Property(x => x.SalaryInformation).HasMaxLength(200);

        b.Property(x => x.DiscoveredUrl).HasMaxLength(1000).IsRequired();
        b.Property(x => x.DiscoveredTitle).HasMaxLength(200).IsRequired();
        b.Property(x => x.DiscoveredBy).HasMaxLength(50).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.TenantId, x.ExternalJobId })
            .HasFilter("external_job_id IS NOT NULL")
            .IsUnique();

        b.HasIndex(x => x.NormalizedUrl).IsUnique();

        b.HasOne(x => x.Tenant)
            .WithMany(t => t.JobPosts)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class JobPostSnapshotConfiguration : IEntityTypeConfiguration<JobPostSnapshot>
{
    public void Configure(EntityTypeBuilder<JobPostSnapshot> b)
    {
        b.ToTable("job_post_snapshot");
        b.HasKey(x => x.SnapshotId);
        b.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.JobPost)
            .WithMany(p => p.Snapshots)
            .HasForeignKey(x => x.JobPostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> b)
    {
        b.ToTable("job_application", t =>
        {
            t.HasCheckConstraint("ck_completion", "completion_percentage BETWEEN 0 AND 100");
        });

        b.HasKey(x => x.ApplicationId);
        b.Property(x => x.Status).HasColumnName("application_status").HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.ReviewStatus).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.CompletionPercentage).HasPrecision(5, 2);
        b.Property(x => x.ApplicationUrl).HasMaxLength(1000).IsRequired();
        b.Property(x => x.SubmissionReference).HasMaxLength(100);
        b.Property(x => x.SubmissionConfirmationSource).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.UserId, x.JobPostId }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.Status });
        b.HasIndex(x => new { x.UserId, x.LastActivityAt });

        b.HasOne(x => x.JobPost)
            .WithMany(p => p.Applications)
            .HasForeignKey(x => x.JobPostId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Resume)
            .WithMany()
            .HasForeignKey(x => x.ResumeId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class JobApplicationStatusHistoryConfiguration : IEntityTypeConfiguration<JobApplicationStatusHistory>
{
    public void Configure(EntityTypeBuilder<JobApplicationStatusHistory> b)
    {
        b.ToTable("job_application_status_history");
        b.HasKey(x => x.HistoryId);
        b.Property(x => x.PreviousStatus).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.NewStatus).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.ChangeSource).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.HasIndex(x => new { x.ApplicationId, x.ChangedAt });

        b.HasOne(x => x.Application)
            .WithMany(a => a.History)
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ApplicationSessionConfiguration : IEntityTypeConfiguration<ApplicationSession>
{
    public void Configure(EntityTypeBuilder<ApplicationSession> b)
    {
        b.ToTable("application_session");
        b.HasKey(x => x.SessionId);
        b.Property(x => x.SessionStatus).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.BrowserSessionReference).HasMaxLength(255);
        b.Property(x => x.CurrentStepKey).HasMaxLength(100);
        b.Property(x => x.FailureReason).HasMaxLength(1000);
        b.Property(x => x.WorkerInstanceId).HasMaxLength(100);
        b.Property(x => x.RunState).HasMaxLength(32);
        b.Property(x => x.RunMessage).HasMaxLength(2000);
        b.Property(x => x.RowVersion).IsRowVersion();

        // ADR-012 originally allowed one active session in the whole app (unique index on status).
        // Batch runs keep several applications open at Workday's Review page at once, so the rule
        // is now "one active session per application", enforced in WorkdayAutomationService.
        b.HasIndex(x => new { x.ApplicationId, x.SessionStatus });

        b.HasOne(x => x.Application)
            .WithMany(a => a.Sessions)
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ApplicationFieldConfiguration : IEntityTypeConfiguration<ApplicationField>
{
    public void Configure(EntityTypeBuilder<ApplicationField> b)
    {
        b.ToTable("application_field");
        b.HasKey(x => x.FieldId);
        b.Property(x => x.FieldFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.FieldIdentifier).HasMaxLength(255);
        b.Property(x => x.FieldLabel).HasMaxLength(500).IsRequired();
        b.Property(x => x.FieldType).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.PageSection).HasMaxLength(200);
        b.Property(x => x.StepKey).HasMaxLength(100);
        b.Property(x => x.RepeatGroupKey).HasMaxLength(100);
        b.Property(x => x.PageValueText).HasMaxLength(4000);
        b.Property(x => x.FillStatus).HasMaxLength(50);
        b.Property(x => x.FillError).HasMaxLength(1000);
        b.Property(x => x.AvailableOptions).HasColumnType("jsonb");
        b.Property(x => x.DetectedContext).HasColumnType("jsonb");
        b.Property(x => x.LocatorRecipe).HasColumnType("jsonb");
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.SessionId, x.FieldFingerprint }).IsUnique();

        b.HasOne(x => x.Session)
            .WithMany(s => s.Fields)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class FieldMappingConfiguration : IEntityTypeConfiguration<FieldMapping>
{
    public void Configure(EntityTypeBuilder<FieldMapping> b)
    {
        b.ToTable("field_mapping");
        b.HasKey(x => x.MappingId);
        b.Property(x => x.ProfileFieldReference).HasMaxLength(150);
        b.Property(x => x.SuggestedValue).HasMaxLength(4000);
        b.Property(x => x.SuggestedOptionKey).HasMaxLength(200);
        b.Property(x => x.ConfidenceScore).HasPrecision(4, 3);
        b.Property(x => x.MappingStatus).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.MappingMethod).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.MappingSource).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.ConfidenceBreakdown).HasColumnType("jsonb");
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.FieldId)
            .HasFilter("is_current = true")
            .IsUnique();

        b.HasOne(x => x.Field)
            .WithMany(f => f.Mappings)
            .HasForeignKey(x => x.FieldId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.SavedAnswer)
            .WithMany()
            .HasForeignKey(x => x.SavedAnswerId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.AiInteraction)
            .WithMany()
            .HasForeignKey(x => x.AiInteractionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class ApplicationAnswerConfiguration : IEntityTypeConfiguration<ApplicationAnswer>
{
    public void Configure(EntityTypeBuilder<ApplicationAnswer> b)
    {
        b.ToTable("application_answer");
        b.HasKey(x => x.AnswerId);
        b.Property(x => x.Question).HasMaxLength(1000).IsRequired();
        b.Property(x => x.AnswerValue).HasMaxLength(4000);
        b.Property(x => x.AnswerSource).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.Application)
            .WithMany(a => a.Answers)
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Field)
            .WithOne(f => f.Answer)
            .HasForeignKey<ApplicationAnswer>(x => x.FieldId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class SubmissionApprovalConfiguration : IEntityTypeConfiguration<SubmissionApproval>
{
    public void Configure(EntityTypeBuilder<SubmissionApproval> b)
    {
        b.ToTable("submission_approval");
        b.HasKey(x => x.ApprovalId);
        b.Property(x => x.FieldValuesHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ValidationReportId).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.Session)
            .WithMany(s => s.Approvals)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AutomationCommandConfiguration : IEntityTypeConfiguration<AutomationCommand>
{
    public void Configure(EntityTypeBuilder<AutomationCommand> b)
    {
        b.ToTable("automation_command");
        b.HasKey(x => x.CommandId);
        b.Property(x => x.CommandType).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Outcome).HasMaxLength(100);
        b.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.Error).HasMaxLength(2000);
        b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.HasIndex(x => new { x.Status, x.EnqueuedAt });

        b.HasOne(x => x.Session)
            .WithMany(s => s.Commands)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AutomationEventConfiguration : IEntityTypeConfiguration<AutomationEvent>
{
    public void Configure(EntityTypeBuilder<AutomationEvent> b)
    {
        b.ToTable("automation_event");
        b.HasKey(x => x.EventId);
        b.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        b.Property(x => x.EventDescription).HasMaxLength(1000).IsRequired();
        b.Property(x => x.EventStatus).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.ErrorDetails).HasColumnType("jsonb");

        b.HasIndex(x => new { x.SessionId, x.EventTimestamp });

        b.HasOne(x => x.Session)
            .WithMany(s => s.Events)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AiInteractionLogConfiguration : IEntityTypeConfiguration<AiInteractionLog>
{
    public void Configure(EntityTypeBuilder<AiInteractionLog> b)
    {
        b.ToTable("ai_interaction_log");
        b.HasKey(x => x.InteractionId);
        b.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        b.Property(x => x.Model).HasMaxLength(100).IsRequired();
        b.Property(x => x.PromptVersion).HasMaxLength(50).IsRequired();
        b.Property(x => x.Purpose).HasMaxLength(100).IsRequired();
        b.Property(x => x.RequestRedacted).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.ResponseRaw).HasColumnType("jsonb").IsRequired();

        b.HasIndex(x => x.CreatedAt);
    }
}

public class ProviderSettingConfiguration : IEntityTypeConfiguration<ProviderSetting>
{
    public void Configure(EntityTypeBuilder<ProviderSetting> b)
    {
        b.ToTable("provider_setting");
        b.HasKey(x => x.SettingId);
        b.Property(x => x.ProviderName).HasMaxLength(50).IsRequired();
        b.Property(x => x.DefaultModel).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.ProviderName).IsUnique();
    }
}

public class UserSettingConfiguration : IEntityTypeConfiguration<UserSetting>
{
    public void Configure(EntityTypeBuilder<UserSetting> b)
    {
        b.ToTable("user_setting");
        b.HasKey(x => x.SettingId);
        b.Property(x => x.SettingKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.SettingValue).HasMaxLength(2000).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.UserId, x.SettingKey }).IsUnique();

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class WorkerHeartbeatConfiguration : IEntityTypeConfiguration<WorkerHeartbeat>
{
    public void Configure(EntityTypeBuilder<WorkerHeartbeat> b)
    {
        b.ToTable("worker_heartbeat");
        b.HasKey(x => x.WorkerInstanceId);
        b.Property(x => x.WorkerInstanceId).HasMaxLength(100);
        b.Property(x => x.Status).HasMaxLength(50);
    }
}

public class WorkdayCredentialConfiguration : IEntityTypeConfiguration<WorkdayCredential>
{
    public void Configure(EntityTypeBuilder<WorkdayCredential> b)
    {
        b.ToTable("workday_credential");
        b.HasKey(x => x.CredentialId);
        b.Property(x => x.TenantKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.Email).HasMaxLength(320).IsRequired();
        b.Property(x => x.PasswordEnc).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.UserId, x.TenantKey }).IsUnique();
    }
}
