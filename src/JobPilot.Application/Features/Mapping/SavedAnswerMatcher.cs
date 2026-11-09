using System.Text.RegularExpressions;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;

namespace JobPilot.Application.Features.Mapping;

public sealed class SavedAnswerMatcher : ISavedAnswerMatcher
{
    public Task<FieldMappingResult?> TryMapAsync(
        FieldDescriptor field,
        IReadOnlyList<SavedAnswer> savedAnswers,
        MappingContext context,
        CancellationToken ct)
    {
        if (savedAnswers.Count == 0)
            return Task.FromResult<FieldMappingResult?>(null);

        var normalizedLabel = Normalize(field.Label);

        // 1. Try match by normalized question text
        var match = savedAnswers.FirstOrDefault(a =>
            Normalize(a.QuestionNormalized).Equals(normalizedLabel, StringComparison.OrdinalIgnoreCase) ||
            Normalize(a.QuestionText).Equals(normalizedLabel, StringComparison.OrdinalIgnoreCase));

        // 2. Try match by canonical key
        if (match == null && !string.IsNullOrWhiteSpace(field.FieldIdentifier))
        {
            var cleanId = field.FieldIdentifier.Replace("data-automation-id=", "").Trim('"', '\'', ' ');
            match = savedAnswers.FirstOrDefault(a => a.CanonicalKey.Equals(cleanId, StringComparison.OrdinalIgnoreCase));
        }

        if (match == null)
            return Task.FromResult<FieldMappingResult?>(null);

        // Sensitivity check
        var isSensitive = match.IsSensitive;
        var isApproved = match.ApprovedAt != default;
        var requiresIntervention = isSensitive || !isApproved;

        var result = new FieldMappingResult(
            field.FieldId,
            ProfilePath: null,
            SuggestedValue: match.AnswerValue,
            SuggestedOptionKey: match.AnswerOptionText,
            ConfidenceScore: isApproved ? 0.97m : 0.80m,
            Method: MappingMethod.SavedAnswer,
            Source: MappingSource.SavedAnswer,
            Status: requiresIntervention ? MappingStatus.NeedsReview : MappingStatus.Proposed,
            RequiresIntervention: requiresIntervention,
            IsSensitive: isSensitive,
            Reason: isSensitive
                ? $"Matched sensitive saved answer '{match.CanonicalKey}'. Requires explicit per-application user confirmation."
                : $"Matched user-approved saved answer '{match.CanonicalKey}'.");

        return Task.FromResult<FieldMappingResult?>(result);
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleaned = Regex.Replace(text, @"[^\w\s]", "");
        return Regex.Replace(cleaned, @"\s+", " ").Trim().ToLowerInvariant();
    }
}
