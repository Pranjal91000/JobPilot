using System.Text.Json;
using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Mapping;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Api.Controllers;

[ApiController]
[Route("api/applications/{applicationId:guid}/fields")]
public class FieldMappingController : ControllerBase
{
    private readonly JobPilotDbContext _db;
    private readonly IUserProfileService _profileService;
    private readonly IFieldMappingService _mappingService;
    private readonly IAiSettingsService _aiSettingsService;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public FieldMappingController(
        JobPilotDbContext db,
        IUserProfileService profileService,
        IFieldMappingService mappingService,
        IAiSettingsService aiSettingsService,
        ICurrentUser currentUser,
        IClock clock)
    {
        _db = db;
        _profileService = profileService;
        _mappingService = mappingService;
        _aiSettingsService = aiSettingsService;
        _currentUser = currentUser;
        _clock = clock;
    }

    public sealed record FieldDetailDto(
        Guid FieldId,
        string FieldIdentifier,
        string Label,
        FieldType Type,
        bool Required,
        string? Section,
        string? PageValue,
        JsonDocument? AvailableOptions,
        FieldMappingDto? Mapping,
        AnswerDto? Answer);

    public sealed record FieldMappingDto(
        Guid MappingId,
        string? ProfilePath,
        string? SuggestedValue,
        string? SuggestedOptionKey,
        decimal ConfidenceScore,
        MappingStatus Status,
        MappingMethod Method,
        MappingSource Source,
        bool RequiresIntervention,
        bool IsSensitive,
        string? Reason);

    public sealed record AnswerDto(
        Guid AnswerId,
        string? AnswerValue,
        bool IsUserApproved);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FieldDetailDto>>> GetFields(Guid applicationId, CancellationToken ct)
    {
        var session = await EnsureSessionAsync(applicationId, ct);
        var fields = await LoadFieldDtosAsync(session.SessionId, ct);

        if (fields.Count == 0)
        {
            // Seed realistic Workday fields for testing and demonstration
            await SeedWorkdaySampleFieldsAsync(session.SessionId, ct);
            fields = await LoadFieldDtosAsync(session.SessionId, ct);
        }

        return Ok(fields);
    }

    [HttpPost("map")]
    public async Task<ActionResult<IReadOnlyList<FieldDetailDto>>> MapFields(Guid applicationId, CancellationToken ct)
    {
        var session = await EnsureSessionAsync(applicationId, ct);
        var appFields = await _db.ApplicationFields
            .Where(f => f.SessionId == session.SessionId)
            .OrderBy(f => f.FirstSeenAt)
            .ToListAsync(ct);

        if (appFields.Count == 0)
        {
            await SeedWorkdaySampleFieldsAsync(session.SessionId, ct);
            appFields = await _db.ApplicationFields
                .Where(f => f.SessionId == session.SessionId)
                .OrderBy(f => f.FirstSeenAt)
                .ToListAsync(ct);
        }

        var profile = await _profileService.GetVerifiedSnapshotAsync(ct);
        var savedAnswers = await _db.SavedAnswers
            .Where(a => a.UserId == _currentUser.UserId)
            .ToListAsync(ct);

        var aiSettings = await _aiSettingsService.GetSettingsAsync(ct);

        var descriptors = appFields.Select(f => new FieldDescriptor(
            f.FieldId,
            f.FieldFingerprint,
            f.FieldIdentifier,
            f.FieldLabel,
            f.FieldType,
            f.IsRequired,
            f.PageSection,
            f.StepKey,
            f.PageValueText,
            f.AvailableOptions,
            f.LocatorRecipe)).ToList();

        var context = new MappingContext(
            AtsKey: "workday",
            TenantKey: null,
            StepKey: null,
            SectionTitle: null,
            AiEnabled: aiSettings.IsEnabled && aiSettings.HasApiKey);

        var results = await _mappingService.MapFieldsAsync(descriptors, profile, savedAnswers, context, ct);

        // Persist mapping results
        foreach (var res in results)
        {
            var existingMapping = await _db.FieldMappings
                .FirstOrDefaultAsync(m => m.FieldId == res.FieldId && m.IsCurrent, ct);

            if (existingMapping == null)
            {
                var newMapping = new FieldMapping
                {
                    FieldId = res.FieldId,
                    ProfileFieldReference = res.ProfilePath,
                    SuggestedValue = res.SuggestedValue,
                    SuggestedOptionKey = res.SuggestedOptionKey,
                    ConfidenceScore = res.ConfidenceScore,
                    MappingStatus = res.Status,
                    MappingMethod = res.Method,
                    MappingSource = res.Source,
                    RequiresIntervention = res.RequiresIntervention,
                    IsSensitive = res.IsSensitive,
                    Reason = res.Reason,
                    IsCurrent = true
                };
                _db.FieldMappings.Add(newMapping);
            }
            else
            {
                existingMapping.ProfileFieldReference = res.ProfilePath;
                existingMapping.SuggestedValue = res.SuggestedValue;
                existingMapping.SuggestedOptionKey = res.SuggestedOptionKey;
                existingMapping.ConfidenceScore = res.ConfidenceScore;
                existingMapping.MappingStatus = res.Status;
                existingMapping.MappingMethod = res.Method;
                existingMapping.MappingSource = res.Source;
                existingMapping.RequiresIntervention = res.RequiresIntervention;
                existingMapping.IsSensitive = res.IsSensitive;
                existingMapping.Reason = res.Reason;
            }
        }

        await _db.SaveChangesAsync(ct);
        var updatedDtos = await LoadFieldDtosAsync(session.SessionId, ct);
        return Ok(updatedDtos);
    }

    public sealed record FieldDecisionRequest(string Action, string? Value, string? OptionKey);

    [HttpPost("{fieldId:guid}/decision")]
    public async Task<IActionResult> MakeDecision(
        Guid applicationId,
        Guid fieldId,
        [FromBody] FieldDecisionRequest req,
        CancellationToken ct)
    {
        var field = await _db.ApplicationFields
            .Include(f => f.Mappings)
            .Include(f => f.Answer)
            .FirstOrDefaultAsync(f => f.FieldId == fieldId, ct);

        if (field == null)
            return NotFound($"Field '{fieldId}' not found.");

        var currentMapping = field.Mappings.FirstOrDefault(m => m.IsCurrent);

        if (req.Action.Equals("Approve", StringComparison.OrdinalIgnoreCase))
        {
            if (currentMapping != null)
            {
                currentMapping.MappingStatus = MappingStatus.Approved;
                currentMapping.RequiresIntervention = false;
            }

            if (field.Answer == null)
            {
                field.Answer = new ApplicationAnswer
                {
                    ApplicationId = applicationId,
                    FieldId = fieldId,
                    Question = field.FieldLabel,
                    AnswerSource = AnswerSource.UserEntered,
                    AnswerValue = req.Value ?? currentMapping?.SuggestedValue ?? "",
                    IsUserApproved = true,
                    ApprovedAt = _clock.UtcNow
                };
                _db.ApplicationAnswers.Add(field.Answer);
            }
            else
            {
                field.Answer.AnswerValue = req.Value ?? currentMapping?.SuggestedValue ?? "";
                field.Answer.IsUserApproved = true;
                field.Answer.ApprovedAt = _clock.UtcNow;
            }
        }
        else if (req.Action.Equals("SetValue", StringComparison.OrdinalIgnoreCase))
        {
            if (currentMapping != null)
            {
                currentMapping.MappingStatus = MappingStatus.Approved;
                currentMapping.RequiresIntervention = false;
            }

            if (field.Answer == null)
            {
                field.Answer = new ApplicationAnswer
                {
                    ApplicationId = applicationId,
                    FieldId = fieldId,
                    Question = field.FieldLabel,
                    AnswerSource = AnswerSource.UserEntered,
                    AnswerValue = req.Value ?? "",
                    IsUserApproved = true,
                    ApprovedAt = _clock.UtcNow
                };
                _db.ApplicationAnswers.Add(field.Answer);
            }
            else
            {
                field.Answer.AnswerValue = req.Value ?? "";
                field.Answer.AnswerSource = AnswerSource.UserEntered;
                field.Answer.IsUserApproved = true;
                field.Answer.ApprovedAt = _clock.UtcNow;
            }
        }
        else if (req.Action.Equals("Reject", StringComparison.OrdinalIgnoreCase))
        {
            if (currentMapping != null)
            {
                currentMapping.MappingStatus = MappingStatus.Rejected;
                currentMapping.RequiresIntervention = true;
            }

            if (field.Answer != null)
            {
                field.Answer.IsUserApproved = false;
            }
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }

    [HttpPost("bulk-approve")]
    public async Task<IActionResult> BulkApprove(Guid applicationId, CancellationToken ct)
    {
        var session = await EnsureSessionAsync(applicationId, ct);
        var fields = await _db.ApplicationFields
            .Include(f => f.Mappings)
            .Include(f => f.Answer)
            .Where(f => f.SessionId == session.SessionId)
            .ToListAsync(ct);

        int approvedCount = 0;
        var now = _clock.UtcNow;

        foreach (var field in fields)
        {
            // Never overwrite values entered or customized by the user!
            if (field.Answer != null && field.Answer.AnswerSource == AnswerSource.UserEntered)
                continue;

            var mapping = field.Mappings.FirstOrDefault(m => m.IsCurrent);
            // Invariant: NEVER bulk approve sensitive questions or LLM suggestions!
            if (mapping != null &&
                !mapping.IsSensitive &&
                mapping.MappingMethod != MappingMethod.Llm &&
                mapping.ConfidenceScore >= 0.90m &&
                !string.IsNullOrWhiteSpace(mapping.SuggestedValue))
            {
                mapping.MappingStatus = MappingStatus.Approved;
                mapping.RequiresIntervention = false;

                if (field.Answer == null)
                {
                    field.Answer = new ApplicationAnswer
                    {
                        ApplicationId = applicationId,
                        FieldId = field.FieldId,
                        Question = field.FieldLabel,
                        AnswerSource = AnswerSource.UserEntered,
                        AnswerValue = mapping.SuggestedValue,
                        IsUserApproved = true,
                        ApprovedAt = now
                    };
                    _db.ApplicationAnswers.Add(field.Answer);
                }
                else
                {
                    field.Answer.AnswerValue = mapping.SuggestedValue;
                    field.Answer.IsUserApproved = true;
                    field.Answer.ApprovedAt = now;
                }
                approvedCount++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true, approvedCount });
    }

    private async Task<ApplicationSession> EnsureSessionAsync(Guid applicationId, CancellationToken ct)
    {
        var session = await _db.ApplicationSessions
            .FirstOrDefaultAsync(s => s.ApplicationId == applicationId, ct);

        if (session == null)
        {
            // Complete any lingering active sessions to enforce single active session invariant (ADR-012)
            var activeStatuses = new[]
            {
                SessionStatus.Pending, SessionStatus.Launching, SessionStatus.AwaitingAuthentication,
                SessionStatus.AwaitingUser, SessionStatus.Inspecting, SessionStatus.Mapping,
                SessionStatus.Filling, SessionStatus.Navigating, SessionStatus.WatchingForConfirmation, SessionStatus.Paused
            };

            var lingering = await _db.ApplicationSessions
                .Where(s => activeStatuses.Contains(s.SessionStatus))
                .ToListAsync(ct);

            foreach (var l in lingering)
            {
                l.SessionStatus = SessionStatus.Completed;
            }

            session = new ApplicationSession
            {
                ApplicationId = applicationId,
                SessionStatus = SessionStatus.Mapping,
                CurrentStepKey = "ApplicationReview"
            };
            _db.ApplicationSessions.Add(session);
            await _db.SaveChangesAsync(ct);
        }

        return session;
    }

    private async Task<IReadOnlyList<FieldDetailDto>> LoadFieldDtosAsync(Guid sessionId, CancellationToken ct)
    {
        var fields = await _db.ApplicationFields
            .Include(f => f.Mappings)
            .Include(f => f.Answer)
            .Where(f => f.SessionId == sessionId)
            .OrderBy(f => f.FirstSeenAt)
            .ToListAsync(ct);

        return fields.Select(f =>
        {
            var m = f.Mappings.FirstOrDefault(m => m.IsCurrent);
            FieldMappingDto? mappingDto = m == null ? null : new FieldMappingDto(
                m.MappingId,
                m.ProfileFieldReference,
                m.SuggestedValue,
                m.SuggestedOptionKey,
                m.ConfidenceScore,
                m.MappingStatus,
                m.MappingMethod,
                m.MappingSource,
                m.RequiresIntervention,
                m.IsSensitive,
                m.Reason);

            AnswerDto? answerDto = f.Answer == null ? null : new AnswerDto(
                f.Answer.AnswerId,
                f.Answer.AnswerValue,
                f.Answer.IsUserApproved);

            return new FieldDetailDto(
                f.FieldId,
                f.FieldIdentifier ?? "",
                f.FieldLabel,
                f.FieldType,
                f.IsRequired,
                f.PageSection,
                f.PageValueText,
                f.AvailableOptions,
                mappingDto,
                answerDto);
        }).ToList();
    }

    private async Task SeedWorkdaySampleFieldsAsync(Guid sessionId, CancellationToken ct)
    {
        var fields = new List<ApplicationField>
        {
            // Contact Section
            new() { SessionId = sessionId, FieldFingerprint = "fp_legalNameSection_firstName", FieldIdentifier = "legalNameSection_firstName", FieldLabel = "First Name", FieldType = FieldType.Text, IsRequired = true, PageSection = "Contact Information" },
            new() { SessionId = sessionId, FieldFingerprint = "fp_legalNameSection_lastName", FieldIdentifier = "legalNameSection_lastName", FieldLabel = "Last Name", FieldType = FieldType.Text, IsRequired = true, PageSection = "Contact Information" },
            new() { SessionId = sessionId, FieldFingerprint = "fp_contactSection_email", FieldIdentifier = "contactSection_email", FieldLabel = "Email Address", FieldType = FieldType.Text, IsRequired = true, PageSection = "Contact Information" },
            new() { SessionId = sessionId, FieldFingerprint = "fp_contactSection_phone", FieldIdentifier = "contactSection_phone", FieldLabel = "Phone Number", FieldType = FieldType.Text, IsRequired = true, PageSection = "Contact Information" },
            new() { SessionId = sessionId, FieldFingerprint = "fp_addressSection_addressLine1", FieldIdentifier = "addressSection_addressLine1", FieldLabel = "Address Line 1", FieldType = FieldType.Text, IsRequired = true, PageSection = "Contact Information" },
            new() { SessionId = sessionId, FieldFingerprint = "fp_addressSection_city", FieldIdentifier = "addressSection_city", FieldLabel = "City", FieldType = FieldType.Text, IsRequired = true, PageSection = "Contact Information" },
            new() { SessionId = sessionId, FieldFingerprint = "fp_addressSection_countryRegion", FieldIdentifier = "addressSection_countryRegion", FieldLabel = "Country", FieldType = FieldType.SingleSelect, IsRequired = true, PageSection = "Contact Information",
                AvailableOptions = JsonDocument.Parse(@"[{""key"":""US"",""label"":""United States""},{""key"":""IN"",""label"":""India""},{""key"":""GB"",""label"":""United Kingdom""},{""key"":""CA"",""label"":""Canada""}]") },

            // Experience Section
            new() { SessionId = sessionId, FieldFingerprint = "fp_experience_years", FieldIdentifier = "experience-years", FieldLabel = "Total Years of Experience", FieldType = FieldType.Text, IsRequired = true, PageSection = "Work Experience" },
            new() { SessionId = sessionId, FieldFingerprint = "fp_current_company", FieldIdentifier = "current-company", FieldLabel = "Current Employer", FieldType = FieldType.Text, IsRequired = false, PageSection = "Work Experience" },
            new() { SessionId = sessionId, FieldFingerprint = "fp_linkedin_url", FieldIdentifier = "linkedin-url", FieldLabel = "LinkedIn Profile", FieldType = FieldType.Text, IsRequired = false, PageSection = "Work Experience" },

            // Application Screening Questions
            new() { SessionId = sessionId, FieldFingerprint = "fp_notice_period", FieldIdentifier = "notice-period", FieldLabel = "Notice Period (Days)", FieldType = FieldType.Text, IsRequired = true, PageSection = "Application Questions" },
            new() { SessionId = sessionId, FieldFingerprint = "fp_relocation_willingness", FieldIdentifier = "relocation-willingness", FieldLabel = "Are you willing to relocate for this position?", FieldType = FieldType.SingleSelect, IsRequired = true, PageSection = "Application Questions",
                AvailableOptions = JsonDocument.Parse(@"[{""key"":""Yes"",""label"":""Yes""},{""key"":""No"",""label"":""No""}]") },

            // Sensitive Disclosures (Guardrail Testing)
            new() { SessionId = sessionId, FieldFingerprint = "fp_work_authorization", FieldIdentifier = "work-authorization", FieldLabel = "Are you legally authorized to work in the country where this job is located?", FieldType = FieldType.SingleSelect, IsRequired = true, PageSection = "Legal & Sponsorship",
                AvailableOptions = JsonDocument.Parse(@"[{""key"":""Yes"",""label"":""Yes""},{""key"":""No"",""label"":""No""}]") },
            new() { SessionId = sessionId, FieldFingerprint = "fp_visa_sponsorship", FieldIdentifier = "visa-sponsorship", FieldLabel = "Will you now or in the future require visa sponsorship for employment?", FieldType = FieldType.SingleSelect, IsRequired = true, PageSection = "Legal & Sponsorship",
                AvailableOptions = JsonDocument.Parse(@"[{""key"":""Yes"",""label"":""Yes""},{""key"":""No"",""label"":""No""}]") },
            new() { SessionId = sessionId, FieldFingerprint = "fp_voluntary_disability", FieldIdentifier = "voluntary-disability", FieldLabel = "Voluntary Self-Identification of Disability", FieldType = FieldType.SingleSelect, IsRequired = false, PageSection = "Voluntary Disclosures",
                AvailableOptions = JsonDocument.Parse(@"[{""key"":""Yes"",""label"":""Yes, I have a disability""},{""key"":""No"",""label"":""No, I do not have a disability""},{""key"":""Decline"",""label"":""I do not wish to answer""}]") },
            new() { SessionId = sessionId, FieldFingerprint = "fp_voluntary_veteran", FieldIdentifier = "voluntary-veteran", FieldLabel = "Veteran Status", FieldType = FieldType.SingleSelect, IsRequired = false, PageSection = "Voluntary Disclosures",
                AvailableOptions = JsonDocument.Parse(@"[{""key"":""Protected"",""label"":""I identify as one or more of the classifications of protected veteran""},{""key"":""NotVeteran"",""label"":""I am not a protected veteran""},{""key"":""Decline"",""label"":""I decline to self-identify""}]") },
            new() { SessionId = sessionId, FieldFingerprint = "fp_attestation_consent", FieldIdentifier = "attestation-consent", FieldLabel = "I certify that all information provided is true and accurate.", FieldType = FieldType.Checkbox, IsRequired = true, PageSection = "Review & Attestation" }
        };

        _db.ApplicationFields.AddRange(fields);
        await _db.SaveChangesAsync(ct);
    }
}
