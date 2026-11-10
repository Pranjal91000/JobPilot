using System.Text.Json;
using JobPilot.Domain.Common;
using JobPilot.Domain.Enums;
using JobPilot.Domain.Policies;

namespace JobPilot.Domain.Entities;

public sealed class AtsTenant : AuditableEntity
{
    public Guid TenantId { get; set; } = Guid.CreateVersion7();
    public string AtsKey { get; set; } = "workday";
    public string TenantKey { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string? DisplayCompanyName { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<JobPost> JobPosts { get; set; } = new List<JobPost>();
}

public sealed class JobPost : AuditableEntity
{
    public Guid JobPostId { get; set; } = Guid.CreateVersion7();
    public Guid? TenantId { get; set; }
    public string Source { get; set; } = "workday";
    public string? ExternalJobId { get; set; }
    public string? SiteKey { get; set; }
    public string? Locale { get; set; }
    public string NormalizedUrl { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string? JobDescription { get; set; }
    public string? Location { get; set; }
    public string? EmploymentType { get; set; }
    public string? ExperienceRequirement { get; set; }
    public string? SalaryInformation { get; set; }
    public DateTimeOffset? PostedAt { get; set; }

    // Immutable discovery columns
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
    public string DiscoveredUrl { get; set; } = string.Empty;
    public string DiscoveredTitle { get; set; } = string.Empty;
    public string DiscoveredBy { get; set; } = "UserUrl";

    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsClosed { get; set; }

    // Navigation
    public AtsTenant? Tenant { get; set; }
    public ICollection<JobPostSnapshot> Snapshots { get; set; } = new List<JobPostSnapshot>();
    public ICollection<JobApplication> Applications { get; set; } = new List<JobApplication>();
}

public sealed class JobPostSnapshot : AuditableEntity
{
    public Guid SnapshotId { get; set; } = Guid.CreateVersion7();
    public Guid JobPostId { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ContentHash { get; set; } = string.Empty;
    public JsonDocument Payload { get; set; } = null!;

    public JobPost JobPost { get; set; } = null!;
}

public sealed class JobApplication : AuditableEntity
{
    private readonly List<JobApplicationStatusHistory> _history = new();

    public Guid ApplicationId { get; set; } = Guid.CreateVersion7();
    public Guid JobPostId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ResumeId { get; set; }
    public ApplicationStatus Status { get; set; } = ApplicationStatus.ReadyToApply;
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.NotStarted;
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public string ApplicationUrl { get; set; } = string.Empty;
    public decimal CompletionPercentage { get; set; }
    public string? SubmissionReference { get; set; }
    public DateTimeOffset? SubmissionConfirmedAt { get; set; }
    public SubmissionConfirmationSource? SubmissionConfirmationSource { get; set; }

    public JobPost JobPost { get; set; } = null!;
    public AppUser User { get; set; } = null!;
    public Resume? Resume { get; set; }
    public IReadOnlyCollection<JobApplicationStatusHistory> History => _history;
    public ICollection<ApplicationSession> Sessions { get; set; } = new List<ApplicationSession>();
    public ICollection<ApplicationAnswer> Answers { get; set; } = new List<ApplicationAnswer>();

    public void ChangeStatus(ApplicationStatus to, ChangeSource source, DateTimeOffset now, string? notes = null)
    {
        if (!ApplicationStatusTransitions.CanTransition(Status, to, source))
            throw new InvalidStatusTransitionException(Status, to, source);

        var prev = Status;
        Status = to;
        LastActivityAt = now;

        _history.Add(new JobApplicationStatusHistory
        {
            ApplicationId = ApplicationId,
            PreviousStatus = prev,
            NewStatus = to,
            ChangeSource = source,
            ChangedAt = now,
            Notes = notes
        });
    }

    public void ConfirmSubmission(SubmissionConfirmationSource source, string? reference, DateTimeOffset now)
    {
        SubmissionConfirmedAt = now;
        SubmissionConfirmationSource = source;
        SubmissionReference = reference;
        AppliedAt ??= now;

        ChangeStatus(ApplicationStatus.Submitted,
            source == Enums.SubmissionConfirmationSource.Detector ? ChangeSource.Detector : ChangeSource.User,
            now, "Submission confirmed.");
    }
}

public sealed class JobApplicationStatusHistory
{
    public Guid HistoryId { get; set; }
    public Guid ApplicationId { get; set; }
    public ApplicationStatus? PreviousStatus { get; set; }
    public ApplicationStatus NewStatus { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public ChangeSource ChangeSource { get; set; }
    public string? Notes { get; set; }
    public Guid? SessionId { get; set; }

    public JobApplication Application { get; set; } = null!;
}

public sealed class ApplicationSession : AuditableEntity
{
    public Guid SessionId { get; set; } = Guid.CreateVersion7();
    public Guid ApplicationId { get; set; }
    public SessionStatus SessionStatus { get; set; } = SessionStatus.Pending;
    public string? BrowserSessionReference { get; set; }
    public string? CurrentStepKey { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public string? FailureReason { get; set; }
    public string? WorkerInstanceId { get; set; }

    /// <summary>Unattended "run to review" state; see <see cref="RunStates"/>. Null when never run.</summary>
    public string? RunState { get; set; }
    /// <summary>Human-readable progress or the reason the run needs the user.</summary>
    public string? RunMessage { get; set; }

    public JobApplication Application { get; set; } = null!;
    public ICollection<ApplicationField> Fields { get; set; } = new List<ApplicationField>();
    public ICollection<AutomationEvent> Events { get; set; } = new List<AutomationEvent>();
    public ICollection<AutomationCommand> Commands { get; set; } = new List<AutomationCommand>();
    public ICollection<SubmissionApproval> Approvals { get; set; } = new List<SubmissionApproval>();
}

public sealed class ApplicationField : AuditableEntity
{
    public Guid FieldId { get; set; } = Guid.CreateVersion7();
    public Guid SessionId { get; set; }
    public string FieldFingerprint { get; set; } = string.Empty;
    public string? FieldIdentifier { get; set; }
    public string FieldLabel { get; set; } = string.Empty;
    public FieldType FieldType { get; set; } = FieldType.Text;
    public bool IsRequired { get; set; }
    public JsonDocument? AvailableOptions { get; set; }
    public JsonDocument? DetectedContext { get; set; }
    public string? PageSection { get; set; }
    public string? StepKey { get; set; }
    public string? RepeatGroupKey { get; set; }
    public int? RepeatIndex { get; set; }
    public JsonDocument? LocatorRecipe { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsPresent { get; set; } = true;
    public string? PageValueText { get; set; }
    public string? FillStatus { get; set; }
    public string? FillError { get; set; }

    public ApplicationSession Session { get; set; } = null!;
    public ICollection<FieldMapping> Mappings { get; set; } = new List<FieldMapping>();
    public ApplicationAnswer? Answer { get; set; }
}

public sealed class FieldMapping : AuditableEntity
{
    public Guid MappingId { get; set; } = Guid.CreateVersion7();
    public Guid FieldId { get; set; }
    public string? ProfileFieldReference { get; set; }
    public Guid? SavedAnswerId { get; set; }
    public string? SuggestedValue { get; set; }
    public string? SuggestedOptionKey { get; set; }
    public decimal ConfidenceScore { get; set; }
    public JsonDocument? ConfidenceBreakdown { get; set; }
    public MappingStatus MappingStatus { get; set; } = MappingStatus.Proposed;
    public MappingMethod MappingMethod { get; set; } = MappingMethod.AdapterRule;
    public MappingSource MappingSource { get; set; } = MappingSource.None;
    public string? Reason { get; set; }
    public bool RequiresIntervention { get; set; }
    public bool IsSensitive { get; set; }
    public bool IsUserVerified { get; set; }
    public bool IsCurrent { get; set; } = true;
    public Guid? AiInteractionId { get; set; }

    public ApplicationField Field { get; set; } = null!;
    public SavedAnswer? SavedAnswer { get; set; }
    public AiInteractionLog? AiInteraction { get; set; }
}

public sealed class ApplicationAnswer : AuditableEntity
{
    public Guid AnswerId { get; set; } = Guid.CreateVersion7();
    public Guid ApplicationId { get; set; }
    public Guid? FieldId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string? AnswerValue { get; set; }
    public byte[]? AnswerValueEnc { get; set; }
    public AnswerSource AnswerSource { get; set; } = AnswerSource.UserEntered;
    public bool IsUserApproved { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public JobApplication Application { get; set; } = null!;
    public ApplicationField? Field { get; set; }
}

public sealed class SubmissionApproval : AuditableEntity
{
    public Guid ApprovalId { get; set; } = Guid.CreateVersion7();
    public Guid SessionId { get; set; }
    public DateTimeOffset ApprovedAt { get; set; } = DateTimeOffset.UtcNow;
    public string FieldValuesHash { get; set; } = string.Empty;
    public string? ValidationReportId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? InvalidatedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }

    public ApplicationSession Session { get; set; } = null!;
}

public sealed class AutomationCommand : AuditableEntity
{
    public Guid CommandId { get; set; } = Guid.CreateVersion7();
    public Guid SessionId { get; set; }
    public AutomationCommandType CommandType { get; set; }
    public JsonDocument Payload { get; set; } = null!;
    public AutomationCommandStatus Status { get; set; } = AutomationCommandStatus.Queued;
    public string? Outcome { get; set; }
    public int Attempt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset EnqueuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Error { get; set; }

    public ApplicationSession Session { get; set; } = null!;
}

public sealed class AutomationEvent
{
    public Guid EventId { get; set; } = Guid.CreateVersion7();
    public Guid SessionId { get; set; }
    public Guid? CommandId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EventDescription { get; set; } = string.Empty;
    public AutomationEventStatus EventStatus { get; set; } = AutomationEventStatus.Info;
    public DateTimeOffset EventTimestamp { get; set; } = DateTimeOffset.UtcNow;
    public JsonDocument? ErrorDetails { get; set; }
    public Guid? FieldId { get; set; }

    public ApplicationSession Session { get; set; } = null!;
}

public sealed class AiInteractionLog
{
    public Guid InteractionId { get; set; } = Guid.CreateVersion7();
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public JsonDocument RequestRedacted { get; set; } = null!;
    public JsonDocument ResponseRaw { get; set; } = null!;
    public bool SchemaValid { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int LatencyMs { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProviderSetting : AuditableEntity
{
    public Guid SettingId { get; set; } = Guid.CreateVersion7();
    public string ProviderName { get; set; } = string.Empty;
    public byte[]? ApiKeyEnc { get; set; }
    public string? DefaultModel { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class UserSetting : AuditableEntity
{
    public Guid SettingId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;

    public AppUser User { get; set; } = null!;
}

public sealed class WorkerHeartbeat
{
    public string WorkerInstanceId { get; set; } = string.Empty;
    public DateTimeOffset LastHeartbeatAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Status { get; set; }
}

/// <summary>
/// Login used to sign in to (or create) a candidate account on a Workday career site.
/// TenantKey "*" is the default login used for any site without its own entry.
/// The password is encrypted with the API's data-protection keys.
/// </summary>
public sealed class WorkdayCredential : AuditableEntity
{
    public Guid CredentialId { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string TenantKey { get; set; } = "*";
    public string Email { get; set; } = string.Empty;
    public string PasswordEnc { get; set; } = string.Empty;
    /// <summary>True once JobPilot (or the user) created/used an account on this tenant.</summary>
    public bool AccountExists { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
