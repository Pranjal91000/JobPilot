using JobPilot.Domain.Common;
using JobPilot.Domain.Enums;
using JobPilot.Domain.ValueObjects;

namespace JobPilot.Domain.Entities;

public sealed class AppUser : AuditableEntity
{
    public Guid UserId { get; set; } = Guid.CreateVersion7();
    public string DisplayName { get; set; } = string.Empty;
    public string EmailLogin { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Navigation
    public UserProfile? Profile { get; set; }
    public ICollection<Education> Educations { get; set; } = new List<Education>();
    public ICollection<EmploymentHistory> EmploymentHistories { get; set; } = new List<EmploymentHistory>();
    public ICollection<Skill> Skills { get; set; } = new List<Skill>();
    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<Certification> Certifications { get; set; } = new List<Certification>();
    public ICollection<Achievement> Achievements { get; set; } = new List<Achievement>();
    public ICollection<ProfessionalLink> ProfessionalLinks { get; set; } = new List<ProfessionalLink>();
    public ICollection<WorkAuthorization> WorkAuthorizations { get; set; } = new List<WorkAuthorization>();
    public ICollection<LocationPreference> LocationPreferences { get; set; } = new List<LocationPreference>();
    public ICollection<SavedAnswer> SavedAnswers { get; set; } = new List<SavedAnswer>();
    public ICollection<Resume> Resumes { get; set; } = new List<Resume>();
    public ICollection<ProfileFieldVerification> FieldVerifications { get; set; } = new List<ProfileFieldVerification>();
}

public sealed class UserProfile : AuditableEntity
{
    public Guid UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? PreferredName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PhoneE164 { get; set; }
    public string? PhoneCountryCode { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? StateRegion { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
    public string? ProfessionalSummary { get; set; }
    public string? CurrentLocation { get; set; }
    public int? NoticePeriodDays { get; set; }
    public byte[]? ExpectedSalaryEnc { get; set; }
    public string? ExpectedSalaryCurrency { get; set; }
    public int? TotalExperienceMonths { get; set; }

    // Navigation
    public AppUser User { get; set; } = null!;
}

public sealed class ProfileFieldVerification : AuditableEntity
{
    public Guid UserId { get; set; }
    public string ProfilePath { get; set; } = string.Empty;
    public VerificationStatus VerificationStatus { get; set; }
    public FactSource Source { get; set; }
    public Guid? SourceDocumentId { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }

    public AppUser User { get; set; } = null!;
    public SourceDocument? SourceDocument { get; set; }
}

public sealed class Education : AuditableEntity
{
    public Guid EducationId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Institution { get; set; } = string.Empty;
    public string Degree { get; set; } = string.Empty;
    public string? Specialization { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Grade { get; set; }
    public string? Description { get; set; }
    public string? CountryCode { get; set; }
    public int SortOrder { get; set; }
    public Provenance Provenance { get; set; } = new();

    public AppUser User { get; set; } = null!;
    public SourceDocument? SourceDocument { get; set; }
}

public sealed class EmploymentHistory : AuditableEntity
{
    public Guid EmploymentId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string? EmploymentType { get; set; }
    public string? Location { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public string? Responsibilities { get; set; }
    public string[] Technologies { get; set; } = Array.Empty<string>();
    public int SortOrder { get; set; }
    public Provenance Provenance { get; set; } = new();

    public AppUser User { get; set; } = null!;
    public SourceDocument? SourceDocument { get; set; }
}

public sealed class Skill : AuditableEntity
{
    public Guid SkillId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string SkillName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? ProficiencyLevel { get; set; }
    public decimal? YearsOfExperience { get; set; }
    public Provenance Provenance { get; set; } = new();

    public AppUser User { get; set; } = null!;
}

public sealed class Project : AuditableEntity
{
    public Guid ProjectId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string? Description { get; set; }
    public string[] Technologies { get; set; } = Array.Empty<string>();
    public string? Url { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public Provenance Provenance { get; set; } = new();

    public AppUser User { get; set; } = null!;
}

public sealed class Certification : AuditableEntity
{
    public Guid CertificationId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string? CredentialId { get; set; }
    public DateOnly? IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? CredentialUrl { get; set; }
    public Provenance Provenance { get; set; } = new();

    public AppUser User { get; set; } = null!;
}

public sealed class Achievement : AuditableEntity
{
    public Guid AchievementId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly? AchievedOn { get; set; }
    public Provenance Provenance { get; set; } = new();

    public AppUser User { get; set; } = null!;
}

public sealed class ProfessionalLink : AuditableEntity
{
    public Guid LinkId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public LinkType LinkType { get; set; }
    public string Url { get; set; } = string.Empty;
    public Provenance Provenance { get; set; } = new();

    public AppUser User { get; set; } = null!;
}

public sealed class WorkAuthorization : AuditableEntity
{
    public Guid AuthorizationId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string StatusDescription { get; set; } = string.Empty;
    public bool? RequiresSponsorship { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public Provenance Provenance { get; set; } = new();

    public AppUser User { get; set; } = null!;
}

public sealed class LocationPreference : AuditableEntity
{
    public Guid PreferenceId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string LocationText { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public bool IsRelocationOk { get; set; }
    public RemotePreference RemotePreference { get; set; } = RemotePreference.Any;
    public int Priority { get; set; }
    public Provenance Provenance { get; set; } = new();

    public AppUser User { get; set; } = null!;
}

public sealed class SavedAnswer : AuditableEntity
{
    public Guid SavedAnswerId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string CanonicalKey { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string QuestionNormalized { get; set; } = string.Empty;
    public string? AnswerValue { get; set; }
    public byte[]? AnswerValueEnc { get; set; }
    public string? AnswerOptionText { get; set; }
    public SavedAnswerScope Scope { get; set; } = SavedAnswerScope.Global;
    public string? ScopeRef { get; set; }
    public bool IsSensitive { get; set; }
    public DateTimeOffset ApprovedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public int UseCount { get; set; }

    public AppUser User { get; set; } = null!;
}

public sealed class SourceDocument : AuditableEntity
{
    public Guid SourceDocumentId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public SourceDocumentKind Kind { get; set; }
    public Guid? ResumeId { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset ParsedAt { get; set; }
    public string? ParserVersion { get; set; }

    public AppUser User { get; set; } = null!;
    public Resume? Resume { get; set; }
}

public sealed class Resume : AuditableEntity
{
    public Guid ResumeId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StorageReference { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public ResumeType ResumeType { get; set; } = ResumeType.General;
    public string? Label { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public AppUser User { get; set; } = null!;
}

