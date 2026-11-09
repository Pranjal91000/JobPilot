using System.Text.Json;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Domain.Policies;

namespace JobPilot.Application.Features.Mapping;

public sealed record MappingContext(
    string AtsKey,
    string? TenantKey,
    string? StepKey,
    string? SectionTitle,
    bool AiEnabled);

public sealed record ConfidenceBreakdown(
    decimal MethodCap,
    decimal LabelMatch,
    decimal OptionMatch,
    decimal Grounding,
    decimal LlmSelfScore,
    decimal HistoryAccuracy,
    decimal FinalScore);

public sealed record ProfileProjection(
    IReadOnlyDictionary<string, object?> VerifiedFacts,
    IReadOnlyList<string> AllowedPaths);

public sealed record AiFieldSuggestion(
    string FieldRef,
    string? ProfilePath,
    string? Transform,
    string? OptionKey,
    decimal Confidence,
    string Reason);

public sealed record AiMappingResponse(
    IReadOnlyList<AiFieldSuggestion> Suggestions);

public interface IAdapterRuleMapper
{
    FieldMappingResult? TryMap(FieldDescriptor field, VerifiedProfileSnapshot profile, MappingContext context);
}

public interface IDictionaryMapper
{
    FieldMappingResult? TryMap(FieldDescriptor field, VerifiedProfileSnapshot profile, MappingContext context);
}

public interface ISavedAnswerMatcher
{
    Task<FieldMappingResult?> TryMapAsync(FieldDescriptor field, IReadOnlyList<SavedAnswer> savedAnswers, MappingContext context, CancellationToken ct);
}

public interface IGroundingValidator
{
    (bool IsGrounded, string? GroundedValue, string Reason) Validate(
        string profilePath,
        string? transform,
        string? optionKey,
        FieldDescriptor field,
        VerifiedProfileSnapshot profile);
}

public interface IConfidenceCalculator
{
    (decimal FinalScore, ConfidenceBreakdown Breakdown) Calculate(
        MappingMethod method,
        decimal labelMatch,
        decimal optionMatch,
        decimal grounding,
        decimal llmSelfScore,
        decimal historyAccuracy);
}

public interface IFieldMappingService
{
    Task<IReadOnlyList<FieldMappingResult>> MapFieldsAsync(
        IReadOnlyList<FieldDescriptor> fields,
        VerifiedProfileSnapshot profile,
        IReadOnlyList<SavedAnswer> savedAnswers,
        MappingContext context,
        CancellationToken ct);
}

public sealed record AiSettingsDto(
    string ProviderName,
    string DefaultModel,
    bool IsEnabled,
    bool HasApiKey,
    DateTimeOffset? LastTestedAt,
    string? LastTestStatus);

public sealed record UpdateAiSettingsRequest(
    string ProviderName,
    string DefaultModel,
    bool IsEnabled,
    string? ApiKey);

public sealed record TestAiConnectionResponse(
    bool Success,
    string Message,
    int? LatencyMs);

public interface IAiSettingsService
{
    Task<AiSettingsDto> GetSettingsAsync(CancellationToken ct);
    Task<AiSettingsDto> UpdateSettingsAsync(UpdateAiSettingsRequest request, CancellationToken ct);
    Task<TestAiConnectionResponse> TestConnectionAsync(CancellationToken ct);
    Task<string?> GetDecryptedApiKeyAsync(CancellationToken ct);
}

