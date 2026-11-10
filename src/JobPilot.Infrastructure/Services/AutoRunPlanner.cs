using System.Text.Json;
using System.Text.RegularExpressions;
using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Mapping;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Domain.Policies;
using JobPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobPilot.Infrastructure.Services;

public sealed record FillInstruction(Guid FieldId, string Label, string Value, string? OptionKey);

/// <summary>Ask the worker to click Add until a repeatable section has this many entries.</summary>
public sealed record RepeatTarget(string Section, int Wanted);

public sealed record StepPlan(
    IReadOnlyList<FillInstruction> Fills,
    IReadOnlyList<string> NeedsUser,
    int AutoDecided,
    int AiDrafted,
    IReadOnlyList<RepeatTarget>? AddEntries = null);

/// <summary>
/// Decides what to fill on the current step of an unattended run.
///
/// Decision rules (the "auto-approval policy"):
///   - Anything the user approved, edited or undid is respected as-is.
///   - Values from the verified profile (Workday rules / dictionary) are filled automatically.
///   - Answers the user approved before (saved or from past applications) are reused automatically.
///   - Sensitive questions (EEO, disability, veteran, sponsorship, legal) are filled only from a
///     standing answer the user saved "for all applications"; otherwise the run asks the user.
///   - Required questions nothing covers are drafted by AI (if enabled) and flagged for review.
///   - Required questions still unanswered stop the run with a "needs you" list.
/// Automatic decisions are stored as MappingStatus.AutoApproved with an unapproved answer, so the
/// review screen can show them as "filled automatically, please review" rather than user-approved.
/// </summary>
public sealed class AutoRunPlanner(
    JobPilotDbContext db,
    IUserProfileService profileService,
    IFieldMappingService mappingService,
    IAnswerDraftingService draftingService,
    IAiSettingsService aiSettingsService,
    ISavedAnswerMatcher savedAnswerMatcher,
    ILogger<AutoRunPlanner> logger)
{
    public async Task<StepPlan> PlanStepAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.ApplicationSessions
            .Include(s => s.Application).ThenInclude(a => a.JobPost).ThenInclude(p => p.Tenant)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct)
            ?? throw new KeyNotFoundException($"Session {sessionId} not found.");

        var step = session.CurrentStepKey;
        var fields = await db.ApplicationFields
            .Include(f => f.Mappings)
            .Include(f => f.Answer)
            .Where(f => f.SessionId == sessionId && f.IsPresent && (step == null || f.StepKey == step))
            .OrderBy(f => f.FirstSeenAt)
            .ToListAsync(ct);

        var settings = await aiSettingsService.GetSettingsAsync(ct);
        var aiEnabled = settings.IsEnabled && settings.HasApiKey;
        var profile = await profileService.GetVerifiedSnapshotAsync(ct);
        var reference = await PastAnswerReference.LoadAsync(db, session.Application.UserId, ct);
        var standing = reference.Where(r => r.SavedAnswerId != Guid.Empty && r.ApprovedAt != default).ToList();

        var context = new MappingContext(
            AtsKey: session.Application.JobPost?.Tenant?.AtsKey ?? "workday",
            TenantKey: session.Application.JobPost?.Tenant?.TenantKey,
            StepKey: step,
            SectionTitle: null,
            AiEnabled: aiEnabled);

        var autoDecided = 0;
        var aiDrafted = 0;

        // 0. Repeatable sections (work experience, education): entry k is filled from the k-th
        //    profile entry. Done before general mapping so "Job Title" in every entry isn't mapped
        //    to the current job.
        var employments = FactList<EmploymentHistory>(profile, "EmploymentHistories");
        var educations = FactList<Education>(profile, "Educations");
        autoDecided += DecideRepeatEntries(session.ApplicationId, fields, employments, educations);
        var addEntries = RepeatTargets(fields, employments.Count, educations.Count);

        // 1. Map every field without a decision yet.
        var toMap = fields.Where(f => !HasDecision(f) && RepeatKey(f) == null).ToList();
        if (toMap.Count > 0)
        {
            var results = await mappingService.MapFieldsAsync(toMap.Select(ToDescriptor).ToList(), profile, reference, context, ct);

            foreach (var res in results)
            {
                var field = toMap.FirstOrDefault(f => f.FieldId == res.FieldId);
                if (field == null) continue;

                var decided = res;
                var auto = false;

                if (res.IsSensitive)
                {
                    var standingMatch = await savedAnswerMatcher.TryMapAsync(ToDescriptor(field), standing, context, ct);
                    if (standingMatch?.SuggestedValue != null)
                    {
                        decided = standingMatch with
                        {
                            IsSensitive = true,
                            RequiresIntervention = false,
                            Reason = "Your saved answer for all applications."
                        };
                        auto = true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(res.SuggestedValue))
                {
                    auto = res.Method switch
                    {
                        MappingMethod.AdapterRule or MappingMethod.Dictionary => res.ConfidenceScore >= 0.80m,
                        MappingMethod.SavedAnswer => true,
                        MappingMethod.Llm => res.ConfidenceScore >= 0.70m,
                        _ => false
                    };
                }

                UpsertMapping(field, decided, auto ? MappingStatus.AutoApproved : decided.Status);
                if (auto)
                {
                    UpsertAutoAnswer(session.ApplicationId, field, decided.SuggestedValue!, SourceFor(decided.Method));
                    autoDecided++;
                }
            }
        }

        // 2. AI drafts for required, non-sensitive questions nothing else could answer.
        if (aiEnabled)
        {
            var undecided = fields
                .Where(f => f.IsRequired && DecisionValue(f) == null && !HasPageValue(f) && !IsRejected(f) && !IsSensitive(f) &&
                            f.FieldType is not FieldType.FileUpload and not FieldType.Checkbox)
                .ToList();

            if (undecided.Count > 0)
            {
                // Drafting is a bonus: if it fails, the run continues and asks the user instead.
                IReadOnlyList<DraftedAnswer> drafts;
                try
                {
                    drafts = await draftingService.DraftAsync(
                        undecided.Select(f => new DraftQuestion(f.FieldId, f.FieldLabel, f.FieldType, f.IsRequired, OptionLabels(f))).ToList(),
                        BuildDraftContext(session, profile, reference),
                        ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "AI drafting failed for session {SessionId}; continuing without drafts", sessionId);
                    drafts = [];
                }

                foreach (var d in drafts)
                {
                    var field = undecided.First(f => f.FieldId == d.FieldId);
                    UpsertMapping(field, new FieldMappingResult(
                        field.FieldId, null, d.Value, null, d.Confidence,
                        MappingMethod.Llm, MappingSource.None, MappingStatus.AutoApproved,
                        RequiresIntervention: false, IsSensitive: false,
                        Reason: $"AI draft, please review. {d.Reason}".Trim()), MappingStatus.AutoApproved);
                    UpsertAutoAnswer(session.ApplicationId, field, d.Value, AnswerSource.AiDraft);
                    aiDrafted++;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        // 3. Build the fill list and the questions that need the user.
        var resumePath = profile.VerifiedFacts.TryGetValue("Documents.Resume", out var rp) ? rp?.ToString() : null;
        var fills = new List<FillInstruction>();
        var needsUser = new List<string>();

        foreach (var f in fields)
        {
            var value = DecisionValue(f);

            if (f.FieldType == FieldType.FileUpload)
            {
                if (!string.IsNullOrWhiteSpace(f.PageValueText)) continue; // already attached; don't upload twice
                // Stored paths point at old temp copies; always use the freshly prepared resume.
                if (value != null && !string.IsNullOrWhiteSpace(resumePath)) value = resumePath;
            }

            // Keep what's already on the page (Workday's resume parsing, an earlier save, or the
            // user's own typing) unless the user explicitly approved a different value here.
            var userApproved = f.Answer is { IsUserApproved: true };
            if (!userApproved && HasPageValue(f) && f.FieldType != FieldType.FileUpload) continue;

            if (string.IsNullOrWhiteSpace(value))
            {
                if (f.IsRequired && !HasPageValue(f))
                {
                    needsUser.Add(IsSensitive(f)
                        ? $"{f.FieldLabel} (sensitive: answer once and tick \"Use for all applications\")"
                        : f.FieldLabel);
                }
                continue;
            }

            var mapping = f.Mappings.FirstOrDefault(m => m.IsCurrent);
            fills.Add(new FillInstruction(f.FieldId, f.FieldLabel, value, mapping?.SuggestedOptionKey));
        }

        logger.LogInformation(
            "Planned step {Step} for session {SessionId}: {Fills} fills, {Auto} auto-decided, {Ai} AI drafts, {Needs} need the user",
            step, sessionId, fills.Count, autoDecided, aiDrafted, needsUser.Count);

        return new StepPlan(fills, needsUser, autoDecided, aiDrafted, addEntries);
    }

    // ---- repeatable sections ------------------------------------------------------------------

    private const int MaxRepeatEntries = 5;

    // Workday ids look like "workExperience-29--jobTitle" or "education-3--schoolName"
    // (date parts: "workExperience-29--startDate-dateSectionMonth-input").
    private static readonly Regex RepeatId = new(@"^(?<section>workExperience|education)-(?<index>\d+)--(?<part>[A-Za-z]+)", RegexOptions.Compiled);

    private static (string Section, int Index, string Part)? RepeatKey(ApplicationField f)
    {
        var m = RepeatId.Match(f.FieldIdentifier ?? "");
        return m.Success ? (m.Groups["section"].Value, int.Parse(m.Groups["index"].Value), m.Groups["part"].Value) : null;
    }

    private static List<T> FactList<T>(VerifiedProfileSnapshot profile, string key) =>
        profile.VerifiedFacts.TryGetValue(key, out var v) && v is IEnumerable<T> items ? items.ToList() : [];

    private static IReadOnlyList<RepeatTarget> RepeatTargets(List<ApplicationField> fields, int employmentCount, int educationCount)
    {
        int Existing(string section) => fields
            .Select(RepeatKey).Where(k => k?.Section == section).Select(k => k!.Value.Index).Distinct().Count();

        var targets = new List<RepeatTarget>();
        var wantWork = Math.Min(employmentCount, MaxRepeatEntries);
        var wantEdu = Math.Min(educationCount, MaxRepeatEntries);
        if (wantWork > Existing("workExperience")) targets.Add(new RepeatTarget("workExperience", wantWork));
        if (wantEdu > Existing("education")) targets.Add(new RepeatTarget("education", wantEdu));
        return targets;
    }

    private int DecideRepeatEntries(Guid applicationId, List<ApplicationField> fields, List<EmploymentHistory> employments, List<Education> educations)
    {
        var decided = 0;
        foreach (var section in new[] { "workExperience", "education" })
        {
            // Entries in page order (fields are stored in DOM order as they are first seen).
            var entryIndexes = fields
                .Select(f => (Field: f, Key: RepeatKey(f)))
                .Where(x => x.Key?.Section == section)
                .GroupBy(x => x.Key!.Value.Index)
                .OrderBy(g => g.Min(x => x.Field.FirstSeenAt))
                .Select(g => g.Key)
                .ToList();

            for (var ordinal = 0; ordinal < entryIndexes.Count; ordinal++)
            {
                var entryFields = fields.Where(f => RepeatKey(f) is { } k && k.Section == section && k.Index == entryIndexes[ordinal]);
                foreach (var field in entryFields)
                {
                    if (HasDecision(field) || HasPageValue(field)) continue;
                    var part = RepeatKey(field)!.Value.Part;
                    var (value, source) = section == "workExperience"
                        ? (ordinal < employments.Count ? EmploymentValue(employments[ordinal], part) : null, ordinal < employments.Count ? employments[ordinal].CompanyName : null)
                        : (ordinal < educations.Count ? EducationValue(educations[ordinal], part) : null, ordinal < educations.Count ? educations[ordinal].Institution : null);
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    // A choice whose options don't include the profile wording (degree "B.Tech" vs
                    // "Bachelor's Degree") is left to the AI drafting step, which sees the options.
                    var options = OptionLabels(field);
                    if (options.Count > 0 && !options.Any(o => o.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                                                                value.Contains(o, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    UpsertMapping(field, new FieldMappingResult(
                        field.FieldId, $"{section}[{ordinal}].{part}", value, null, 0.95m,
                        MappingMethod.AdapterRule, MappingSource.VerifiedProfile, MappingStatus.AutoApproved,
                        RequiresIntervention: false, IsSensitive: false,
                        Reason: $"From your profile: {source} (entry {ordinal + 1})."), MappingStatus.AutoApproved);
                    UpsertAutoAnswer(applicationId, field, value, AnswerSource.Profile);
                    decided++;
                }
            }
        }
        return decided;
    }

    private static string? EmploymentValue(EmploymentHistory e, string part) => part switch
    {
        "jobTitle" => e.JobTitle,
        "companyName" => e.CompanyName,
        "location" => e.Location,
        "currentlyWorkHere" => e.IsCurrent ? "true" : "false",
        "startDate" => e.StartDate.ToString("MM'/'yyyy", System.Globalization.CultureInfo.InvariantCulture),
        "endDate" => e.IsCurrent ? null : e.EndDate?.ToString("MM'/'yyyy", System.Globalization.CultureInfo.InvariantCulture),
        "roleDescription" => e.Responsibilities,
        _ => null
    };

    private static string? EducationValue(Education e, string part) => part switch
    {
        "schoolName" or "school" => e.Institution,
        "degree" => e.Degree,
        "fieldOfStudy" => e.Specialization,
        "gradeAverage" or "grade" => e.Grade,
        "firstYearAttended" => e.StartDate?.Year.ToString(),
        "lastYearAttended" => e.EndDate?.Year.ToString(),
        "startDate" => e.StartDate?.ToString("MM'/'yyyy", System.Globalization.CultureInfo.InvariantCulture),
        "endDate" => e.EndDate?.ToString("MM'/'yyyy", System.Globalization.CultureInfo.InvariantCulture),
        _ => null
    };

    // ---- decision helpers -------------------------------------------------------------------

    private static bool IsRejected(ApplicationField f) =>
        f.Mappings.Any(m => m.IsCurrent && m.MappingStatus == MappingStatus.Rejected);

    /// <summary>True when the field already has a user or automatic decision (including "don't fill").</summary>
    private static bool HasDecision(ApplicationField f) =>
        (f.Answer?.IsUserApproved ?? false) ||
        IsRejected(f) ||
        f.Mappings.Any(m => m.IsCurrent && m.MappingStatus is MappingStatus.Approved or MappingStatus.AutoApproved);

    /// <summary>The value to fill: a user-approved answer, else an automatically approved one.</summary>
    internal static string? DecisionValue(ApplicationField f)
    {
        if (IsRejected(f)) return null;
        if (f.Answer is { IsUserApproved: true } && !string.IsNullOrWhiteSpace(f.Answer.AnswerValue))
            return f.Answer.AnswerValue;

        var mapping = f.Mappings.FirstOrDefault(m => m.IsCurrent);
        if (mapping?.MappingStatus is MappingStatus.Approved or MappingStatus.AutoApproved)
        {
            if (!string.IsNullOrWhiteSpace(f.Answer?.AnswerValue)) return f.Answer!.AnswerValue;
            if (!string.IsNullOrWhiteSpace(mapping.SuggestedValue)) return mapping.SuggestedValue;
        }
        return null;
    }

    private static bool HasPageValue(ApplicationField f) =>
        !string.IsNullOrWhiteSpace(f.PageValueText) &&
        !(f.FieldType == FieldType.Checkbox && f.PageValueText.Equals("false", StringComparison.OrdinalIgnoreCase));

    private static bool IsSensitive(ApplicationField f) =>
        f.Mappings.Any(m => m.IsCurrent && m.IsSensitive) ||
        SensitiveQuestionPolicy.Evaluate(f.FieldLabel, null, f.PageSection).IsSensitive;

    private static AnswerSource SourceFor(MappingMethod method) => method switch
    {
        MappingMethod.SavedAnswer => AnswerSource.SavedAnswer,
        MappingMethod.Llm => AnswerSource.AiDraft,
        MappingMethod.User => AnswerSource.UserEntered,
        _ => AnswerSource.Profile
    };

    private void UpsertMapping(ApplicationField field, FieldMappingResult res, MappingStatus status)
    {
        var current = field.Mappings.FirstOrDefault(m => m.IsCurrent);
        if (current == null)
        {
            current = new FieldMapping { FieldId = field.FieldId, IsCurrent = true };
            db.FieldMappings.Add(current);
            field.Mappings.Add(current);
        }

        current.ProfileFieldReference = Clip(res.ProfilePath, 150);
        current.SuggestedValue = Clip(res.SuggestedValue, 4000);
        current.SuggestedOptionKey = Clip(res.SuggestedOptionKey, 200);
        current.ConfidenceScore = res.ConfidenceScore;
        current.MappingStatus = status;
        current.MappingMethod = res.Method;
        current.MappingSource = res.Source;
        current.RequiresIntervention = status != MappingStatus.AutoApproved && res.RequiresIntervention;
        current.IsSensitive = res.IsSensitive;
        current.Reason = Clip(res.Reason, 500);
    }

    // Column limits (see JobAndAutomationConfigurations); Workday question text can be very long.
    private static string? Clip(string? s, int max) => s == null || s.Length <= max ? s : s[..max];

    private void UpsertAutoAnswer(Guid applicationId, ApplicationField field, string value, AnswerSource source)
    {
        if (field.Answer is { IsUserApproved: true }) return; // never overwrite the user's own answer

        if (field.Answer == null)
        {
            field.Answer = new ApplicationAnswer
            {
                ApplicationId = applicationId,
                FieldId = field.FieldId,
                Question = Clip(field.FieldLabel, 1000)!
            };
            db.ApplicationAnswers.Add(field.Answer);
        }

        field.Answer.AnswerValue = Clip(value, 4000);
        field.Answer.AnswerSource = source;
        field.Answer.IsUserApproved = false;
    }

    private static FieldDescriptor ToDescriptor(ApplicationField f) => new(
        f.FieldId, f.FieldFingerprint, f.FieldIdentifier ?? "", f.FieldLabel, f.FieldType, f.IsRequired,
        f.PageSection, f.StepKey, f.PageValueText, f.AvailableOptions, f.LocatorRecipe);

    private static IReadOnlyList<string> OptionLabels(ApplicationField f)
    {
        if (f.AvailableOptions == null || f.AvailableOptions.RootElement.ValueKind != JsonValueKind.Array) return [];
        var labels = new List<string>();
        foreach (var o in f.AvailableOptions.RootElement.EnumerateArray())
        {
            string? label = null;
            foreach (var key in new[] { "Label", "label", "Key", "key" })
            {
                if (o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                {
                    label = v.GetString();
                    break;
                }
            }
            if (label != null) labels.Add(label.Trim());
        }
        return labels.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Some facts are lists of EF entities (employment history, education, skills) whose navigation
    // properties point back at each other (User -> Profile -> User ...). Serialising them directly
    // loops forever, so keep only simple values and turn each entity into one readable line.
    private static string FactToText(object value)
    {
        if (value is string s) return s;
        if (IsSimple(value.GetType())) return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        if (value is System.Collections.IEnumerable items)
        {
            var lines = new List<string>();
            foreach (var item in items)
            {
                if (item == null) continue;
                lines.Add(IsSimple(item.GetType()) ? Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture) ?? "" : EntityToLine(item));
                if (lines.Count >= 30) break;
            }
            return string.Join("\n", lines.Where(l => l.Length > 0));
        }
        return EntityToLine(value);
    }

    private static string EntityToLine(object entity)
    {
        var parts = new List<string>();
        foreach (var prop in entity.GetType().GetProperties())
        {
            var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (!IsSimple(type) || type == typeof(Guid)) continue;          // skip navigations and ids
            if (prop.Name is "RowVersion" or "CreatedAt" or "UpdatedAt" || prop.Name.EndsWith("Enc", StringComparison.Ordinal)) continue;
            var v = prop.GetValue(entity);
            if (v == null || (v is string str && string.IsNullOrWhiteSpace(str))) continue;
            var text = v switch
            {
                DateTimeOffset d => d.ToString("yyyy-MM-dd"),
                DateTime d => d.ToString("yyyy-MM-dd"),
                DateOnly d => d.ToString("yyyy-MM-dd"),
                _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)
            };
            if (!string.IsNullOrWhiteSpace(text)) parts.Add($"{prop.Name}: {text}");
        }
        return string.Join("; ", parts);
    }

    private static bool IsSimple(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) ||
        t == typeof(DateTimeOffset) || t == typeof(DateOnly) || t == typeof(Guid) || t == typeof(TimeSpan);

    private static DraftContext BuildDraftContext(ApplicationSession session, VerifiedProfileSnapshot profile, IReadOnlyList<SavedAnswer> reference)
    {
        var facts = new Dictionary<string, string>();
        foreach (var (key, value) in profile.VerifiedFacts)
        {
            if (value == null || key.StartsWith("Documents.", StringComparison.Ordinal)) continue;
            var text = FactToText(value);
            if (!string.IsNullOrWhiteSpace(text)) facts[key] = text;
        }

        var past = reference
            .Where(r => !r.IsSensitive && !string.IsNullOrWhiteSpace(r.AnswerValue) && !string.IsNullOrWhiteSpace(r.QuestionText))
            .Select(r => (r.QuestionText, r.AnswerValue!))
            .ToList();

        var post = session.Application.JobPost;
        return new DraftContext(post?.DisplayCompanyNameOrCompany(), post?.JobTitle, post?.JobDescription, facts, past);
    }
}

internal static class JobPostExtensions
{
    public static string? DisplayCompanyNameOrCompany(this JobPost post) =>
        string.IsNullOrWhiteSpace(post.Tenant?.DisplayCompanyName) ? post.CompanyName : post.Tenant!.DisplayCompanyName;
}
