using System.Text.Json;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace JobPilot.Application.Features.Mapping;

public sealed record DraftQuestion(
    Guid FieldId,
    string Question,
    FieldType Type,
    bool Required,
    IReadOnlyList<string> Options);

public sealed record DraftContext(
    string? Company,
    string? JobTitle,
    string? JobDescription,
    IReadOnlyDictionary<string, string> ProfileFacts,
    IReadOnlyList<(string Question, string Answer)> PastAnswers);

public sealed record DraftedAnswer(Guid FieldId, string Value, decimal Confidence, string Reason);

public interface IAnswerDraftingService
{
    Task<IReadOnlyList<DraftedAnswer>> DraftAsync(
        IReadOnlyList<DraftQuestion> questions, DraftContext context, CancellationToken ct);
}

/// <summary>
/// Drafts answers for application questions that no profile fact or saved answer covers
/// ("Why do you want to work here?", "Are you willing to relocate?", tenant-specific dropdowns).
/// Every drafted answer is flagged for the user's review. Sensitive questions (EEO, disability,
/// veteran, sponsorship, legal attestations) must be filtered out by the caller and never sent here.
/// </summary>
public sealed class AnswerDraftingService(IAiProvider aiProvider, ILogger<AnswerDraftingService> logger) : IAnswerDraftingService
{
    private const string SystemPrompt = """
        You fill in job application questions on behalf of a candidate, using ONLY the facts provided.
        Rules:
        1. Never invent experience, employers, dates, degrees, certifications, numbers or skills.
           If the facts do not support an answer, return value null.
        2. For questions with options, value MUST be copied exactly from that question's options.
        3. Prefer the candidate's past answers to the same or very similar question.
        4. Free-text answers: first person, specific to the company and role when possible,
           professional, at most 120 words, no placeholders like [Company].
        5. Factual yes/no questions (e.g. "Have you worked for us before?") need explicit support
           in the facts or past answers; otherwise return null.
        6. confidence is 0..1: how sure you are this is what the candidate would submit.
        Output JSON only: { "answers": [ { "fieldRef": string, "value": string|null, "confidence": number, "reason": string } ] }
        """;

    public async Task<IReadOnlyList<DraftedAnswer>> DraftAsync(
        IReadOnlyList<DraftQuestion> questions, DraftContext context, CancellationToken ct)
    {
        if (questions.Count == 0) return [];

        var payload = new
        {
            job = new
            {
                company = context.Company,
                title = context.JobTitle,
                description = Truncate(context.JobDescription, 6000)
            },
            candidateFacts = context.ProfileFacts,
            pastAnswers = context.PastAnswers.Take(40).Select(p => new { question = p.Question, answer = p.Answer }),
            questions = questions.Select(q => new
            {
                fieldRef = q.FieldId.ToString(),
                question = q.Question,
                type = q.Type.ToString(),
                required = q.Required,
                options = q.Options.Count > 0 ? q.Options : null
            })
        };

        var request = new AiStructuredRequest(
            Purpose: "answer-drafting.v1",
            PromptVersion: "2026-11-10",
            SystemPrompt: SystemPrompt,
            UserContentJson: JsonSerializer.Serialize(payload),
            OutputSchema: JsonDocument.Parse("{}"),
            Options: new AiCallOptions(0.2m, 4096, TimeSpan.FromSeconds(90)));

        var response = await aiProvider.CompleteStructuredAsync(request, ct);
        if (!response.Success || response.Output == null)
        {
            logger.LogWarning("Answer drafting failed: {Error}", response.Error);
            return [];
        }

        var byId = questions.ToDictionary(q => q.FieldId);
        var results = new List<DraftedAnswer>();

        if (!response.Output.RootElement.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var a in answers.EnumerateArray())
        {
            if (!a.TryGetProperty("fieldRef", out var refProp) || !Guid.TryParse(refProp.GetString(), out var fieldId)) continue;
            if (!byId.TryGetValue(fieldId, out var question)) continue;
            if (!a.TryGetProperty("value", out var valueProp) || valueProp.ValueKind != JsonValueKind.String) continue;

            var value = valueProp.GetString()!.Trim();
            if (value.Length == 0) continue;

            // Enforce rule 2 locally: a choice answer must be one of the offered options.
            if (question.Options.Count > 0)
            {
                var exact = question.Options.FirstOrDefault(o => string.Equals(o.Trim(), value, StringComparison.OrdinalIgnoreCase));
                if (exact == null) continue;
                value = exact;
            }

            var confidence = a.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number
                ? Math.Clamp(c.GetDecimal(), 0m, 1m)
                : 0.6m;
            var reason = a.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";

            results.Add(new DraftedAnswer(fieldId, value, confidence, reason));
        }

        return results;
    }

    private static string? Truncate(string? s, int max) =>
        s == null ? null : s.Length <= max ? s : s[..max];
}
