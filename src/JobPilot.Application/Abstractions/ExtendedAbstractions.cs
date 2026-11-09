using System.Text.Json;
using JobPilot.Domain.Enums;
using JobPilot.Domain.ValueObjects;

namespace JobPilot.Application.Abstractions;

public sealed record UrlClassification(
    bool IsSupported,
    string Ats,
    string? TenantKey,
    string? Host,
    string? SiteKey,
    string? ExternalJobId,
    string? Locale,
    string NormalizedUrl);

public sealed record JobPostDto(
    Guid JobPostId,
    bool Created,
    string JobTitle,
    string CompanyName,
    string? Location,
    string? ExternalJobId,
    string NormalizedUrl);

public sealed record JobApplicationSummaryDto(
    Guid ApplicationId,
    Guid JobPostId,
    string JobTitle,
    string CompanyName,
    ApplicationStatus Status,
    ReviewStatus ReviewStatus,
    decimal CompletionPercentage,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? AppliedAt);

public sealed record JobApplicationDto(
    Guid ApplicationId,
    Guid JobPostId,
    string JobTitle,
    string CompanyName,
    string ApplicationUrl,
    ApplicationStatus Status,
    ReviewStatus ReviewStatus,
    decimal CompletionPercentage,
    string? SubmissionReference,
    DateTimeOffset? SubmissionConfirmedAt,
    DateTimeOffset LastActivityAt,
    IReadOnlyList<StatusHistoryDto> History);

public sealed record StatusHistoryDto(
    Guid HistoryId,
    ApplicationStatus? PreviousStatus,
    ApplicationStatus NewStatus,
    ChangeSource ChangeSource,
    DateTimeOffset ChangedAt,
    string? Notes);

public sealed record IntakeRequest(string Url, Guid? ResumeId);

public sealed record IntakeResult(
    UrlClassification Classification,
    JobPostDto JobPost,
    JobApplicationDto Application,
    IReadOnlyList<string> DuplicateWarnings);

public sealed record CommandAccepted(Guid CommandId, Guid SessionId);

public sealed record SessionDto(
    Guid SessionId,
    Guid ApplicationId,
    SessionStatus Status,
    AuthState AuthState,
    string? CurrentStepKey,
    decimal CompletionPercentage,
    DateTimeOffset LastActivityAt,
    bool WorkerOnline);

public sealed record FieldDescriptor(
    Guid FieldId,
    string Fingerprint,
    string? FieldIdentifier,
    string Label,
    FieldType Type,
    bool Required,
    string? Section,
    string? StepKey,
    string? PageValue,
    JsonDocument? AvailableOptions,
    JsonDocument? LocatorRecipe);

public sealed record FieldMappingResult(
    Guid FieldId,
    string? ProfilePath,
    string? SuggestedValue,
    string? SuggestedOptionKey,
    decimal ConfidenceScore,
    MappingMethod Method,
    MappingSource Source,
    MappingStatus Status,
    bool RequiresIntervention,
    bool IsSensitive,
    string Reason);

public sealed record ValidationIssue(string Code, string Message, Guid? FieldId, string? StepKey);

public sealed record ValidationReport(
    Guid ReportId,
    DateTimeOffset CreatedAt,
    bool IsSubmittable,
    IReadOnlyList<ValidationIssue> Blocking,
    IReadOnlyList<ValidationIssue> Warnings,
    string FieldValuesHash);

public sealed record SubmissionApprovalDto(
    Guid ApprovalId,
    string SubmissionControl,
    string Instructions,
    DateTimeOffset WatchingForConfirmationUntil);

public sealed record ReviewDecision(
    Guid FieldId,
    string Action, // "Approve", "Reject", "SetValue"
    string? Value,
    string? OptionKey,
    SavedAnswerOption? SaveAsAnswer);

public sealed record SavedAnswerOption(string CanonicalKey, SavedAnswerScope Scope);

public sealed record ReviewDecisions(IReadOnlyList<ReviewDecision> Decisions);

public sealed record ReviewResult(int Applied, bool InvalidatedApproval, IReadOnlyList<Guid> FieldsPendingFill);

public sealed record AiCapabilities(bool NativeJsonSchema, int MaxContextTokens, bool SupportsSystemPrompt);

public sealed record AiCallOptions(decimal Temperature, int MaxOutputTokens, TimeSpan Timeout);

public sealed record AiStructuredRequest(
    string Purpose,
    string PromptVersion,
    string SystemPrompt,
    string UserContentJson,
    JsonDocument OutputSchema,
    AiCallOptions Options);

public sealed record AiStructuredResponse(
    bool Success,
    JsonDocument? Output,
    bool SchemaValid,
    string? RawText,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency,
    string Model,
    string? Error);

public interface IAiProvider
{
    string Name { get; }
    AiCapabilities Capabilities { get; }
    Task<AiStructuredResponse> CompleteStructuredAsync(AiStructuredRequest request, CancellationToken ct);
}

public interface IJobPostService
{
    Task<UrlClassification> ClassifyUrlAsync(string url, CancellationToken ct);
    Task<(JobPostDto Post, bool Created)> FindOrCreateAsync(string url, string title, string company, CancellationToken ct);
}

public interface IJobApplicationService
{
    Task<IntakeResult> IntakeAsync(IntakeRequest request, CancellationToken ct);
    Task<JobApplicationDto> GetAsync(Guid applicationId, CancellationToken ct);
    Task<JobApplicationDto> ChangeStatusAsync(Guid applicationId, ApplicationStatus newStatus, ChangeSource source, string? notes, CancellationToken ct);
    Task<JobApplicationDto> ConfirmSubmissionAsync(Guid applicationId, string? reference, CancellationToken ct);
}

public interface IWorkdayAutomationService
{
    Task<SessionDto> StartSessionAsync(Guid applicationId, CancellationToken ct);
    Task<CommandAccepted> InspectAsync(Guid sessionId, CancellationToken ct);
    Task<CommandAccepted> MapFieldsAsync(Guid sessionId, CancellationToken ct);
    Task<CommandAccepted> AutofillAsync(Guid sessionId, CancellationToken ct);
    Task<CommandAccepted> NavigateAsync(Guid sessionId, string direction, CancellationToken ct);
    Task<ValidationReport> ValidateAsync(Guid sessionId, CancellationToken ct);
    Task<ReviewResult> ApplyReviewAsync(Guid sessionId, ReviewDecisions decisions, CancellationToken ct);
    Task<SubmissionApprovalDto> ApproveSubmissionAsync(Guid sessionId, Guid reportId, IReadOnlyList<Guid> acknowledgedWarnings, CancellationToken ct);
    Task<CommandAccepted> PauseAsync(Guid sessionId, CancellationToken ct);
    Task<CommandAccepted> ResumeAsync(Guid sessionId, CancellationToken ct);
    Task<CommandAccepted> CancelAsync(Guid sessionId, CancellationToken ct);
}

public interface IAtsAdapter
{
    string AtsKey { get; }
    bool CanHandle(Uri url);
}

public interface IBrowserDriver
{
    bool IsAlive(Guid sessionId);
    Task CloseAsync(Guid sessionId, CancellationToken ct);
}

