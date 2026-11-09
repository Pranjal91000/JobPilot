using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Domain.ValueObjects;

namespace JobPilot.Application.Abstractions;

public sealed record FactRef(string EntityType, Guid? EntityId, string? ProfilePath);

public sealed record CompletenessIssue(string FieldKey, string Description, string Category, bool IsRequired);

public sealed record CompletenessReport(
    int TotalChecked,
    int VerifiedCount,
    int MissingCount,
    int UnconfirmedCount,
    decimal CompletenessPercentage,
    IReadOnlyList<CompletenessIssue> MissingOrUnconfirmed);

public sealed record ProfileDto(
    Guid UserId,
    string FirstName,
    string? MiddleName,
    string LastName,
    string? PreferredName,
    string Email,
    string? PhoneE164,
    string? PhoneCountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? CountryCode,
    string? ProfessionalSummary,
    string? CurrentLocation,
    int? NoticePeriodDays,
    string? ExpectedSalary,
    string? ExpectedSalaryCurrency,
    int? TotalExperienceMonths,
    IReadOnlyList<EducationDto> Educations,
    IReadOnlyList<EmploymentDto> EmploymentHistories,
    IReadOnlyList<SkillDto> Skills,
    IReadOnlyList<ProjectDto> Projects,
    IReadOnlyList<CertificationDto> Certifications,
    IReadOnlyList<AchievementDto> Achievements,
    IReadOnlyList<ProfessionalLinkDto> Links,
    IReadOnlyList<WorkAuthorizationDto> WorkAuthorizations,
    IReadOnlyList<LocationPreferenceDto> LocationPreferences,
    IReadOnlyDictionary<string, VerificationStatus> FieldVerifications);

public sealed record UpdateProfileRequest(
    string FirstName,
    string? MiddleName,
    string LastName,
    string? PreferredName,
    string Email,
    string? PhoneE164,
    string? PhoneCountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? CountryCode,
    string? ProfessionalSummary,
    string? CurrentLocation,
    int? NoticePeriodDays,
    string? ExpectedSalary,
    string? ExpectedSalaryCurrency);

public sealed record EducationDto(
    Guid EducationId,
    string Institution,
    string Degree,
    string? Specialization,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Grade,
    string? Description,
    string? CountryCode,
    int SortOrder,
    VerificationStatus VerificationStatus);

public sealed record EmploymentDto(
    Guid EmploymentId,
    string CompanyName,
    string JobTitle,
    string? EmploymentType,
    string? Location,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsCurrent,
    string? Responsibilities,
    string[] Technologies,
    int SortOrder,
    VerificationStatus VerificationStatus);

public sealed record SkillDto(
    Guid SkillId,
    string SkillName,
    string NormalizedName,
    string? ProficiencyLevel,
    decimal? YearsOfExperience,
    VerificationStatus VerificationStatus);

public sealed record ProjectDto(
    Guid ProjectId,
    string Name,
    string? Role,
    string? Description,
    string[] Technologies,
    string? Url,
    DateOnly? StartDate,
    DateOnly? EndDate,
    VerificationStatus VerificationStatus);

public sealed record CertificationDto(
    Guid CertificationId,
    string Name,
    string Issuer,
    string? CredentialId,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    string? CredentialUrl,
    VerificationStatus VerificationStatus);

public sealed record AchievementDto(
    Guid AchievementId,
    string Title,
    string? Description,
    DateOnly? AchievedOn,
    VerificationStatus VerificationStatus);

public sealed record ProfessionalLinkDto(
    Guid LinkId,
    LinkType LinkType,
    string Url,
    VerificationStatus VerificationStatus);

public sealed record WorkAuthorizationDto(
    Guid AuthorizationId,
    string CountryCode,
    string StatusDescription,
    bool? RequiresSponsorship,
    DateOnly? ValidUntil,
    VerificationStatus VerificationStatus);

public sealed record LocationPreferenceDto(
    Guid PreferenceId,
    string LocationText,
    string? CountryCode,
    bool IsRelocationOk,
    RemotePreference RemotePreference,
    int Priority,
    VerificationStatus VerificationStatus);

public sealed record SavedAnswerDto(
    Guid SavedAnswerId,
    string CanonicalKey,
    string QuestionText,
    string QuestionNormalized,
    string? AnswerValue,
    string? AnswerOptionText,
    SavedAnswerScope Scope,
    string? ScopeRef,
    bool IsSensitive,
    DateTimeOffset ApprovedAt);

public sealed record ResumeDto(
    Guid ResumeId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    int Version,
    ResumeType ResumeType,
    string? Label,
    bool IsActive,
    bool IsDefault,
    DateTimeOffset CreatedAt);

public sealed record ResumeMetadata(
    ResumeType ResumeType,
    string? Label,
    bool IsDefault,
    bool IsActive);

public sealed record FileHandle(string FilePath, Stream Stream, Action Cleanup) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        Stream.Dispose();
        Cleanup();
        return ValueTask.CompletedTask;
    }
}

public sealed record VerifiedProfileSnapshot(
    Guid UserId,
    string? FullName,
    string? Email,
    string? Phone,
    IReadOnlyDictionary<string, object?> VerifiedFacts);

public interface IUserProfileService
{
    Task<ProfileDto> GetAsync(CancellationToken ct);
    Task<ProfileDto> UpdateAsync(UpdateProfileRequest request, CancellationToken ct);
    Task ConfirmFactsAsync(IReadOnlyList<FactRef> facts, CancellationToken ct);
    Task<VerifiedProfileSnapshot> GetVerifiedSnapshotAsync(CancellationToken ct);
    Task<CompletenessReport> GetCompletenessAsync(CancellationToken ct);
}

public interface IResumeService
{
    Task<IReadOnlyList<ResumeDto>> ListAsync(CancellationToken ct);
    Task<ResumeDto> UploadAsync(Stream content, string fileName, string contentType, ResumeMetadata meta, CancellationToken ct);
    Task<ResumeDto> UpdateAsync(Guid resumeId, ResumeMetadata meta, CancellationToken ct);
    Task DeleteAsync(Guid resumeId, CancellationToken ct);
    Task<FileHandle> OpenForUploadAsync(Guid resumeId, CancellationToken ct);
    Task<Stream> OpenForReadAsync(Guid resumeId, CancellationToken ct);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ICurrentUser
{
    Guid UserId { get; }
    string Email { get; }
    bool IsAuthenticated { get; }
}

public interface IFileStore
{
    Task<string> SaveEncryptedAsync(Guid userId, string category, Guid fileId, Stream content, CancellationToken ct);
    Task<Stream> OpenReadEncryptedAsync(string storageReference, CancellationToken ct);
    Task DeleteAsync(string storageReference, CancellationToken ct);
}

public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedData);
    string ProtectString(string plaintext);
    string UnprotectString(string protectedData);
}

