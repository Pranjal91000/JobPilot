using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPilot.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("app_user");
        b.HasKey(x => x.UserId);
        b.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
        b.Property(x => x.EmailLogin).HasMaxLength(256).IsRequired();
        b.HasIndex(x => x.EmailLogin).IsUnique();
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> b)
    {
        b.ToTable("user_profile");
        b.HasKey(x => x.UserId);
        b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        b.Property(x => x.MiddleName).HasMaxLength(100);
        b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        b.Property(x => x.PreferredName).HasMaxLength(100);
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.PhoneE164).HasMaxLength(32);
        b.Property(x => x.PhoneCountryCode).HasMaxLength(8);
        b.Property(x => x.AddressLine1).HasMaxLength(200);
        b.Property(x => x.AddressLine2).HasMaxLength(200);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.StateRegion).HasMaxLength(100);
        b.Property(x => x.PostalCode).HasMaxLength(32);
        b.Property(x => x.CountryCode).HasMaxLength(8);
        b.Property(x => x.ProfessionalSummary).HasMaxLength(4000);
        b.Property(x => x.CurrentLocation).HasMaxLength(150);
        b.Property(x => x.ExpectedSalaryCurrency).HasMaxLength(8);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.User)
            .WithOne(u => u.Profile)
            .HasForeignKey<UserProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProfileFieldVerificationConfiguration : IEntityTypeConfiguration<ProfileFieldVerification>
{
    public void Configure(EntityTypeBuilder<ProfileFieldVerification> b)
    {
        b.ToTable("profile_field_verification");
        b.HasKey(x => new { x.UserId, x.ProfilePath });
        b.Property(x => x.ProfilePath).HasMaxLength(128).IsRequired();
        b.Property(x => x.VerificationStatus).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.User)
            .WithMany(u => u.FieldVerifications)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.SourceDocument)
            .WithMany()
            .HasForeignKey(x => x.SourceDocumentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class EducationConfiguration : IEntityTypeConfiguration<Education>
{
    public void Configure(EntityTypeBuilder<Education> b)
    {
        b.ToTable("education");
        b.HasKey(x => x.EducationId);
        b.Property(x => x.Institution).HasMaxLength(200).IsRequired();
        b.Property(x => x.Degree).HasMaxLength(150).IsRequired();
        b.Property(x => x.Specialization).HasMaxLength(150);
        b.Property(x => x.Grade).HasMaxLength(50);
        b.Property(x => x.CountryCode).HasMaxLength(8);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.OwnsOne(x => x.Provenance, p =>
        {
            p.Property(pr => pr.Status).HasColumnName("verification_status").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.SourceDocumentId).HasColumnName("source_document_id");
            p.Property(pr => pr.VerifiedAt).HasColumnName("verified_at");
        });

        b.HasOne(x => x.User)
            .WithMany(u => u.Educations)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EmploymentHistoryConfiguration : IEntityTypeConfiguration<EmploymentHistory>
{
    public void Configure(EntityTypeBuilder<EmploymentHistory> b)
    {
        b.ToTable("employment_history", t =>
        {
            t.HasCheckConstraint("ck_employment_current_or_end_date", "is_current = true OR end_date IS NOT NULL");
        });

        b.HasKey(x => x.EmploymentId);
        b.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
        b.Property(x => x.JobTitle).HasMaxLength(150).IsRequired();
        b.Property(x => x.EmploymentType).HasMaxLength(50);
        b.Property(x => x.Location).HasMaxLength(150);
        b.Property(x => x.Responsibilities).HasMaxLength(4000);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.OwnsOne(x => x.Provenance, p =>
        {
            p.Property(pr => pr.Status).HasColumnName("verification_status").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.SourceDocumentId).HasColumnName("source_document_id");
            p.Property(pr => pr.VerifiedAt).HasColumnName("verified_at");
        });

        b.HasOne(x => x.User)
            .WithMany(u => u.EmploymentHistories)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> b)
    {
        b.ToTable("skill");
        b.HasKey(x => x.SkillId);
        b.Property(x => x.SkillName).HasMaxLength(100).IsRequired();
        b.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
        b.Property(x => x.ProficiencyLevel).HasMaxLength(50);
        b.Property(x => x.YearsOfExperience).HasPrecision(4, 1);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.UserId, x.NormalizedName }).IsUnique();

        b.OwnsOne(x => x.Provenance, p =>
        {
            p.Property(pr => pr.Status).HasColumnName("verification_status").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.SourceDocumentId).HasColumnName("source_document_id");
            p.Property(pr => pr.VerifiedAt).HasColumnName("verified_at");
        });

        b.HasOne(x => x.User)
            .WithMany(u => u.Skills)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.ToTable("project");
        b.HasKey(x => x.ProjectId);
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Role).HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Url).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.OwnsOne(x => x.Provenance, p =>
        {
            p.Property(pr => pr.Status).HasColumnName("verification_status").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.SourceDocumentId).HasColumnName("source_document_id");
            p.Property(pr => pr.VerifiedAt).HasColumnName("verified_at");
        });

        b.HasOne(x => x.User)
            .WithMany(u => u.Projects)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CertificationConfiguration : IEntityTypeConfiguration<Certification>
{
    public void Configure(EntityTypeBuilder<Certification> b)
    {
        b.ToTable("certification");
        b.HasKey(x => x.CertificationId);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Issuer).HasMaxLength(150).IsRequired();
        b.Property(x => x.CredentialId).HasMaxLength(150);
        b.Property(x => x.CredentialUrl).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.OwnsOne(x => x.Provenance, p =>
        {
            p.Property(pr => pr.Status).HasColumnName("verification_status").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.SourceDocumentId).HasColumnName("source_document_id");
            p.Property(pr => pr.VerifiedAt).HasColumnName("verified_at");
        });

        b.HasOne(x => x.User)
            .WithMany(u => u.Certifications)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AchievementConfiguration : IEntityTypeConfiguration<Achievement>
{
    public void Configure(EntityTypeBuilder<Achievement> b)
    {
        b.ToTable("achievement");
        b.HasKey(x => x.AchievementId);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.OwnsOne(x => x.Provenance, p =>
        {
            p.Property(pr => pr.Status).HasColumnName("verification_status").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.SourceDocumentId).HasColumnName("source_document_id");
            p.Property(pr => pr.VerifiedAt).HasColumnName("verified_at");
        });

        b.HasOne(x => x.User)
            .WithMany(u => u.Achievements)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProfessionalLinkConfiguration : IEntityTypeConfiguration<ProfessionalLink>
{
    public void Configure(EntityTypeBuilder<ProfessionalLink> b)
    {
        b.ToTable("professional_link");
        b.HasKey(x => x.LinkId);
        b.Property(x => x.LinkType).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Url).HasMaxLength(500).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.UserId, x.LinkType })
            .HasFilter("link_type != 'Other'")
            .IsUnique();

        b.OwnsOne(x => x.Provenance, p =>
        {
            p.Property(pr => pr.Status).HasColumnName("verification_status").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.SourceDocumentId).HasColumnName("source_document_id");
            p.Property(pr => pr.VerifiedAt).HasColumnName("verified_at");
        });

        b.HasOne(x => x.User)
            .WithMany(u => u.ProfessionalLinks)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class WorkAuthorizationConfiguration : IEntityTypeConfiguration<WorkAuthorization>
{
    public void Configure(EntityTypeBuilder<WorkAuthorization> b)
    {
        b.ToTable("work_authorization");
        b.HasKey(x => x.AuthorizationId);
        b.Property(x => x.CountryCode).HasMaxLength(8).IsRequired();
        b.Property(x => x.StatusDescription).HasMaxLength(150).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.UserId, x.CountryCode }).IsUnique();

        b.OwnsOne(x => x.Provenance, p =>
        {
            p.Property(pr => pr.Status).HasColumnName("verification_status").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.SourceDocumentId).HasColumnName("source_document_id");
            p.Property(pr => pr.VerifiedAt).HasColumnName("verified_at");
        });

        b.HasOne(x => x.User)
            .WithMany(u => u.WorkAuthorizations)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class LocationPreferenceConfiguration : IEntityTypeConfiguration<LocationPreference>
{
    public void Configure(EntityTypeBuilder<LocationPreference> b)
    {
        b.ToTable("location_preference");
        b.HasKey(x => x.PreferenceId);
        b.Property(x => x.LocationText).HasMaxLength(150).IsRequired();
        b.Property(x => x.CountryCode).HasMaxLength(8);
        b.Property(x => x.RemotePreference).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.OwnsOne(x => x.Provenance, p =>
        {
            p.Property(pr => pr.Status).HasColumnName("verification_status").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(32);
            p.Property(pr => pr.SourceDocumentId).HasColumnName("source_document_id");
            p.Property(pr => pr.VerifiedAt).HasColumnName("verified_at");
        });

        b.HasOne(x => x.User)
            .WithMany(u => u.LocationPreferences)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SavedAnswerConfiguration : IEntityTypeConfiguration<SavedAnswer>
{
    public void Configure(EntityTypeBuilder<SavedAnswer> b)
    {
        b.ToTable("saved_answer");
        b.HasKey(x => x.SavedAnswerId);
        b.Property(x => x.CanonicalKey).HasMaxLength(128).IsRequired();
        b.Property(x => x.QuestionText).HasMaxLength(1000).IsRequired();
        b.Property(x => x.QuestionNormalized).HasMaxLength(1000).IsRequired();
        b.Property(x => x.AnswerValue).HasMaxLength(2000);
        b.Property(x => x.AnswerOptionText).HasMaxLength(500);
        b.Property(x => x.Scope).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.ScopeRef).HasMaxLength(150);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.QuestionNormalized);

        b.HasOne(x => x.User)
            .WithMany(u => u.SavedAnswers)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SourceDocumentConfiguration : IEntityTypeConfiguration<SourceDocument>
{
    public void Configure(EntityTypeBuilder<SourceDocument> b)
    {
        b.ToTable("source_document");
        b.HasKey(x => x.SourceDocumentId);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.ParserVersion).HasMaxLength(32);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Resume)
            .WithOne()
            .HasForeignKey<SourceDocument>(x => x.ResumeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class ResumeConfiguration : IEntityTypeConfiguration<Resume>
{
    public void Configure(EntityTypeBuilder<Resume> b)
    {
        b.ToTable("resume");
        b.HasKey(x => x.ResumeId);
        b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.StorageReference).HasMaxLength(500).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.ResumeType).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Label).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.UserId)
            .HasFilter("is_default = true AND deleted_at IS NULL")
            .IsUnique();

        b.HasOne(x => x.User)
            .WithMany(u => u.Resumes)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

