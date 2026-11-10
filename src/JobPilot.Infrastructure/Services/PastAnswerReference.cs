using System.Text.RegularExpressions;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Domain.Policies;
using JobPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Infrastructure.Services;

/// <summary>
/// Builds the reference answers used by the saved-answer matcher: explicitly saved answers plus
/// every answer the user approved in past applications (including earlier steps/sessions of the
/// current one). Past-application answers are returned as in-memory, unpersisted SavedAnswer
/// instances with no ApprovedAt, so the matcher proposes them for review rather than treating
/// them as pre-approved — a Yes/No answered for one company is not automatically right for another.
/// </summary>
public static class PastAnswerReference
{
    public static async Task<List<SavedAnswer>> LoadAsync(JobPilotDbContext db, Guid? userId, CancellationToken ct)
    {
        var saved = await db.SavedAnswers
            .Where(a => userId == null || a.UserId == userId)
            .ToListAsync(ct);

        var past = await db.ApplicationAnswers
            .Where(a => a.IsUserApproved && a.AnswerValue != null && a.AnswerValue != "" && a.Field != null)
            .OrderByDescending(a => a.ApprovedAt)
            .Select(a => new
            {
                a.Question,
                a.AnswerValue,
                a.ApprovedAt,
                Identifier = a.Field!.FieldIdentifier,
                Label = a.Field.FieldLabel,
                Company = a.Application.JobPost.CompanyName
            })
            .Take(500)
            .ToListAsync(ct);

        var seen = new HashSet<string>(saved.Select(s => Key(s.QuestionText, s.CanonicalKey)));
        var result = new List<SavedAnswer>(saved);

        foreach (var p in past)
        {
            var question = string.IsNullOrWhiteSpace(p.Label) ? p.Question : p.Label;
            var canonical = StripDisambiguator(p.Identifier ?? "");
            // Newest approval wins: ordered by ApprovedAt desc, so skip anything already seen.
            if (!seen.Add(Key(question, canonical))) continue;

            result.Add(new SavedAnswer
            {
                SavedAnswerId = Guid.Empty,
                CanonicalKey = canonical,
                QuestionText = question,
                QuestionNormalized = question,
                AnswerValue = p.AnswerValue,
                Scope = SavedAnswerScope.Company,
                ScopeRef = string.IsNullOrWhiteSpace(p.Company) ? "a past application" : p.Company,
                IsSensitive = SensitiveQuestionPolicy.Evaluate(question).IsSensitive,
                ApprovedAt = default
            });
        }

        return result;
    }

    private static string Key(string question, string canonical) =>
        $"{Regex.Replace(question ?? "", @"\W+", " ").Trim().ToLowerInvariant()}|{canonical.ToLowerInvariant()}";

    // Identifiers get a "#2" suffix when a page has duplicates; strip it so keys match across pages.
    private static string StripDisambiguator(string identifier) =>
        Regex.Replace(identifier, @"#\d+$", "");
}
