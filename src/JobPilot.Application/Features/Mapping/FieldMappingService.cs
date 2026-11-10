using System.Text.Json;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Domain.Policies;
using Microsoft.Extensions.Logging;

namespace JobPilot.Application.Features.Mapping;

public sealed class FieldMappingService : IFieldMappingService
{
    private readonly IAdapterRuleMapper _adapterRuleMapper;
    private readonly IDictionaryMapper _dictionaryMapper;
    private readonly ISavedAnswerMatcher _savedAnswerMatcher;
    private readonly IGroundingValidator _groundingValidator;
    private readonly IConfidenceCalculator _confidenceCalculator;
    private readonly IAiProvider _aiProvider;
    private readonly ILogger<FieldMappingService> _logger;

    public FieldMappingService(
        IAdapterRuleMapper adapterRuleMapper,
        IDictionaryMapper dictionaryMapper,
        ISavedAnswerMatcher savedAnswerMatcher,
        IGroundingValidator groundingValidator,
        IConfidenceCalculator confidenceCalculator,
        IAiProvider aiProvider,
        ILogger<FieldMappingService> logger)
    {
        _adapterRuleMapper = adapterRuleMapper;
        _dictionaryMapper = dictionaryMapper;
        _savedAnswerMatcher = savedAnswerMatcher;
        _groundingValidator = groundingValidator;
        _confidenceCalculator = confidenceCalculator;
        _aiProvider = aiProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FieldMappingResult>> MapFieldsAsync(
        IReadOnlyList<FieldDescriptor> fields,
        VerifiedProfileSnapshot profile,
        IReadOnlyList<SavedAnswer> savedAnswers,
        MappingContext context,
        CancellationToken ct)
    {
        var results = new List<FieldMappingResult>();
        var unresolvedFields = new List<FieldDescriptor>();

        foreach (var field in fields)
        {
            // 1. Sensitive question pre-check (Domain Policy)
            var sensitivity = SensitiveQuestionPolicy.Evaluate(field.Label, null, field.Section ?? context.SectionTitle);
            if (sensitivity.IsSensitive)
            {
                // Sensitive questions are NEVER passed to LLM!
                var savedMatch = await _savedAnswerMatcher.TryMapAsync(field, savedAnswers, context, ct);
                if (savedMatch != null)
                {
                    results.Add(savedMatch with
                    {
                        IsSensitive = true,
                        RequiresIntervention = true,
                        Status = MappingStatus.NeedsReview,
                        Reason = $"{sensitivity.Reason} (Pre-filled from saved answer, but requires explicit confirmation)."
                    });
                }
                else
                {
                    results.Add(new FieldMappingResult(
                        FieldId: field.FieldId,
                        ProfilePath: null,
                        SuggestedValue: null,
                        SuggestedOptionKey: null,
                        ConfidenceScore: 0m,
                        Method: MappingMethod.AdapterRule,
                        Source: MappingSource.None,
                        Status: MappingStatus.NeedsReview,
                        RequiresIntervention: true,
                        IsSensitive: true,
                        Reason: $"{sensitivity.Reason}. Safety policy blocks automatic fill. Manual decision required."));
                }
                continue;
            }

            // 2. Workday Adapter Rule Mapper
            var ruleResult = _adapterRuleMapper.TryMap(field, profile, context);
            if (ruleResult != null && ruleResult.SuggestedValue != null)
            {
                results.Add(ruleResult);
                continue;
            }

            // 3. Dictionary & Synonym Mapper
            var dictResult = _dictionaryMapper.TryMap(field, profile, context);
            if (dictResult != null && dictResult.SuggestedValue != null)
            {
                results.Add(dictResult);
                continue;
            }

            // 4. Saved Answer Matcher (Exact & Canonical)
            var savedResult = await _savedAnswerMatcher.TryMapAsync(field, savedAnswers, context, ct);
            if (savedResult != null)
            {
                results.Add(savedResult);
                continue;
            }

            // Field is unresolved by deterministic methods
            unresolvedFields.Add(field);
        }

        // 5. LLM Semantic Mapping for Unresolved Fields
        if (unresolvedFields.Count > 0 && context.AiEnabled)
        {
            await ProcessLlmBatchAsync(unresolvedFields, profile, results, context, ct);
        }
        else
        {
            // AI disabled or no unresolved fields
            foreach (var f in unresolvedFields)
            {
                results.Add(new FieldMappingResult(
                    FieldId: f.FieldId,
                    ProfilePath: null,
                    SuggestedValue: null,
                    SuggestedOptionKey: null,
                    ConfidenceScore: 0m,
                    Method: MappingMethod.User,
                    Source: MappingSource.None,
                    Status: MappingStatus.NeedsReview,
                    RequiresIntervention: true,
                    IsSensitive: false,
                    Reason: "No deterministic mapping found. Manual entry required."));
            }
        }

        return results;
    }

    private async Task ProcessLlmBatchAsync(
        List<FieldDescriptor> unresolvedFields,
        VerifiedProfileSnapshot profile,
        List<FieldMappingResult> results,
        MappingContext context,
        CancellationToken ct)
    {
        try
        {
            // Build minimal token projection of verified facts
            var allowedPaths = new List<string>
            {
                "Personal.FirstName", "Personal.LastName", "Contact.Email", "Contact.PhoneNational",
                "Address.Line1", "Address.City", "Address.Country", "Address.PostalCode",
                "Employment.TotalExperienceYears", "Employment.Current.CompanyName",
                "Preferences.NoticePeriodDays", "Compensation.ExpectedSalary",
                "Links.LinkedIn", "Links.GitHub"
            };

            var verifiedFactsProjection = new Dictionary<string, object?>();
            foreach (var path in allowedPaths)
            {
                if (profile.VerifiedFacts.TryGetValue(path, out var val))
                {
                    verifiedFactsProjection[path] = val;
                }
            }

            var fieldsPayload = unresolvedFields.Select(f => new
            {
                fieldRef = f.FieldId.ToString(),
                label = f.Label,
                type = f.Type.ToString(),
                section = f.Section,
                options = f.AvailableOptions != null ? f.AvailableOptions.RootElement : (object?)null
            }).ToList();

            var userContent = new
            {
                task = "map_fields",
                allowedProfilePaths = allowedPaths,
                allowedTransforms = new[] { "None", "YearsBetween", "MonthsToYearsRounded", "NumericRangeBucket", "PhoneNational" },
                profileProjection = verifiedFactsProjection,
                fields = fieldsPayload
            };

            var userJson = JsonSerializer.Serialize(userContent);
            var systemPrompt = @"You are the JobPilot form mapping assistant. Your task is to map ATS form fields to user profile paths.
Rules:
1. Only map to paths listed in allowedProfilePaths.
2. Never invent or hallucinate data. If no fact matches, set profilePath to null.
3. For choice fields, set optionKey to the exact key/label from available options.
4. Output JSON: { ""suggestions"": [ { ""fieldRef"": string, ""profilePath"": string|null, ""transform"": string|null, ""optionKey"": string|null, ""confidence"": number, ""reason"": string } ] }";

            var req = new AiStructuredRequest(
                Purpose: "field-mapping.v1",
                PromptVersion: "2026-11-01",
                SystemPrompt: systemPrompt,
                UserContentJson: userJson,
                OutputSchema: JsonDocument.Parse("{}"),
                Options: new AiCallOptions(0m, 4096, TimeSpan.FromSeconds(90)));

            var resp = await _aiProvider.CompleteStructuredAsync(req, ct);

            if (resp.Success && resp.Output != null)
            {
                var handledFieldIds = new HashSet<Guid>();
                if (resp.Output.RootElement.TryGetProperty("suggestions", out var suggestionsProp) &&
                    suggestionsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in suggestionsProp.EnumerateArray())
                    {
                        var fieldRefStr = elem.TryGetProperty("fieldRef", out var fRef) ? fRef.GetString() : null;
                        if (!Guid.TryParse(fieldRefStr, out var fieldId))
                            continue;

                        var field = unresolvedFields.FirstOrDefault(f => f.FieldId == fieldId);
                        if (field == null)
                            continue;

                        handledFieldIds.Add(fieldId);

                        var profilePath = elem.TryGetProperty("profilePath", out var pp) ? pp.GetString() : null;
                        var transform = elem.TryGetProperty("transform", out var tr) ? tr.GetString() : "None";
                        var optionKey = elem.TryGetProperty("optionKey", out var ok) ? ok.GetString() : null;
                        var selfScore = elem.TryGetProperty("confidence", out var cProp) ? cProp.GetDecimal() : 0.8m;
                        var reason = elem.TryGetProperty("reason", out var rProp) ? rProp.GetString() ?? "" : "";

                        if (string.IsNullOrWhiteSpace(profilePath))
                        {
                            results.Add(new FieldMappingResult(
                                FieldId: fieldId,
                                ProfilePath: null,
                                SuggestedValue: null,
                                SuggestedOptionKey: null,
                                ConfidenceScore: 0m,
                                Method: MappingMethod.Llm,
                                Source: MappingSource.None,
                                Status: MappingStatus.NeedsReview,
                                RequiresIntervention: true,
                                IsSensitive: false,
                                Reason: "AI determined no verified profile fact matches this field."));
                            continue;
                        }

                        // Grounding check
                        var (isGrounded, groundedValue, groundReason) = _groundingValidator.Validate(
                            profilePath, transform, optionKey, field, profile);

                        if (!isGrounded || groundedValue == null)
                        {
                            results.Add(new FieldMappingResult(
                                FieldId: fieldId,
                                ProfilePath: profilePath,
                                SuggestedValue: null,
                                SuggestedOptionKey: null,
                                ConfidenceScore: 0.30m,
                                Method: MappingMethod.Llm,
                                Source: MappingSource.VerifiedProfile,
                                Status: MappingStatus.NeedsReview,
                                RequiresIntervention: true,
                                IsSensitive: false,
                                Reason: $"AI proposed '{profilePath}', but grounding check failed: {groundReason}"));
                            continue;
                        }

                        var (finalConfidence, _) = _confidenceCalculator.Calculate(
                            MappingMethod.Llm, 0.9m, 1.0m, 1.0m, selfScore, 0.9m);

                        // Mandatory MVP Invariant: Every LLM-derived mapping requires explicit user approval!
                        results.Add(new FieldMappingResult(
                            FieldId: fieldId,
                            ProfilePath: profilePath,
                            SuggestedValue: groundedValue,
                            SuggestedOptionKey: optionKey,
                            ConfidenceScore: finalConfidence,
                            Method: MappingMethod.Llm,
                            Source: MappingSource.VerifiedProfile,
                            Status: MappingStatus.NeedsReview,
                            RequiresIntervention: true,
                            IsSensitive: false,
                            Reason: $"AI suggested mapping to '{profilePath}'. Explicit user approval required. ({reason})"));
                    }
                }

                // Add any fields that AI omitted
                foreach (var f in unresolvedFields.Where(f => !handledFieldIds.Contains(f.FieldId)))
                {
                    results.Add(new FieldMappingResult(
                        FieldId: f.FieldId,
                        ProfilePath: null,
                        SuggestedValue: null,
                        SuggestedOptionKey: null,
                        ConfidenceScore: 0m,
                        Method: MappingMethod.User,
                        Source: MappingSource.None,
                        Status: MappingStatus.NeedsReview,
                        RequiresIntervention: true,
                        IsSensitive: false,
                        Reason: "Field was not resolved by AI. Manual review required."));
                }
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to complete AI semantic mapping batch");
        }

        // Fallback for unresolved fields if AI fails or throws
        foreach (var f in unresolvedFields)
        {
            results.Add(new FieldMappingResult(
                FieldId: f.FieldId,
                ProfilePath: null,
                SuggestedValue: null,
                SuggestedOptionKey: null,
                ConfidenceScore: 0m,
                Method: MappingMethod.User,
                Source: MappingSource.None,
                Status: MappingStatus.NeedsReview,
                RequiresIntervention: true,
                IsSensitive: false,
                Reason: "AI unavailable or returned error. Manual input required."));
        }
    }
}
