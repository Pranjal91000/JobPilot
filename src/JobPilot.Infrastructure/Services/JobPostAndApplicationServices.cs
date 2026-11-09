using JobPilot.Application.Abstractions;
using JobPilot.Application.Common;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Domain.Policies;
using JobPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Infrastructure.Services;

public class JobPostService : IJobPostService
{
    private readonly JobPilotDbContext _db;
    private readonly IClock _clock;

    public JobPostService(JobPilotDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<UrlClassification> ClassifyUrlAsync(string url, CancellationToken ct)
    {
        var classification = WorkdayUrlParser.Classify(url);
        return Task.FromResult(classification);
    }

    public async Task<(JobPostDto Post, bool Created)> FindOrCreateAsync(string url, string title, string company, CancellationToken ct)
    {
        var classification = WorkdayUrlParser.Classify(url);
        if (!classification.IsSupported)
            throw new InvalidOperationException($"Unsupported or invalid portal URL: {url}");

        var now = _clock.UtcNow;

        // 1. Find or create ATS Tenant
        var tenant = await _db.AtsTenants
            .FirstOrDefaultAsync(t => t.AtsKey == classification.Ats && t.TenantKey == classification.TenantKey, ct);

        if (tenant == null)
        {
            tenant = new AtsTenant
            {
                TenantId = Guid.CreateVersion7(),
                AtsKey = classification.Ats,
                TenantKey = classification.TenantKey ?? "unknown",
                Host = classification.Host ?? string.Empty,
                DisplayCompanyName = company,
                FirstSeenAt = now
            };
            _db.AtsTenants.Add(tenant);
            await _db.SaveChangesAsync(ct);
        }

        // 2. Duplicate Detection Cascade (Spec §12.4)
        // Step 1: Tenant + ExternalJobId
        JobPost? existingPost = null;
        if (!string.IsNullOrWhiteSpace(classification.ExternalJobId))
        {
            existingPost = await _db.JobPosts
                .FirstOrDefaultAsync(p => p.TenantId == tenant.TenantId && p.ExternalJobId == classification.ExternalJobId, ct);
        }

        // Step 2: Normalized URL
        if (existingPost == null)
        {
            existingPost = await _db.JobPosts
                .FirstOrDefaultAsync(p => p.NormalizedUrl == classification.NormalizedUrl, ct);
        }

        if (existingPost != null)
        {
            existingPost.LastSeenAt = now;
            await _db.SaveChangesAsync(ct);

            return (new JobPostDto(
                existingPost.JobPostId,
                false,
                existingPost.JobTitle,
                existingPost.CompanyName,
                existingPost.Location,
                existingPost.ExternalJobId,
                existingPost.NormalizedUrl), false);
        }

        // Step 3: Create new JobPost with immutable discovery fields
        var newPost = new JobPost
        {
            JobPostId = Guid.CreateVersion7(),
            TenantId = tenant.TenantId,
            Source = classification.Ats,
            ExternalJobId = classification.ExternalJobId,
            SiteKey = classification.SiteKey,
            Locale = classification.Locale,
            NormalizedUrl = classification.NormalizedUrl,
            CompanyName = string.IsNullOrWhiteSpace(company) ? tenant.TenantKey : company,
            JobTitle = string.IsNullOrWhiteSpace(title) ? (classification.ExternalJobId ?? "Workday Position") : title,
            DiscoveredAt = now,
            DiscoveredUrl = url,
            DiscoveredTitle = title,
            DiscoveredBy = "UserUrl",
            LastSeenAt = now,
            IsClosed = false
        };

        _db.JobPosts.Add(newPost);
        await _db.SaveChangesAsync(ct);

        return (new JobPostDto(
            newPost.JobPostId,
            true,
            newPost.JobTitle,
            newPost.CompanyName,
            newPost.Location,
            newPost.ExternalJobId,
            newPost.NormalizedUrl), true);
    }
}

public class JobApplicationService : IJobApplicationService
{
    private readonly JobPilotDbContext _db;
    private readonly IJobPostService _jobPostService;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public JobApplicationService(
        JobPilotDbContext db,
        IJobPostService jobPostService,
        ICurrentUser currentUser,
        IClock clock)
    {
        _db = db;
        _jobPostService = jobPostService;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<IntakeResult> IntakeAsync(IntakeRequest request, CancellationToken ct)
    {
        var classification = await _jobPostService.ClassifyUrlAsync(request.Url, ct);
        if (!classification.IsSupported)
        {
            throw new InvalidOperationException(
                "Only Workday career sites (*.myworkdayjobs.com) are supported in this version.");
        }

        var (jobPostDto, postCreated) = await _jobPostService.FindOrCreateAsync(
            request.Url,
            classification.ExternalJobId ?? "Workday Position",
            classification.TenantKey ?? "Company",
            ct);

        var now = _clock.UtcNow;
        var warnings = new List<string>();

        // Ensure current user exists in AppUser table before FK insertion
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == _currentUser.UserId, ct);
        if (user == null)
        {
            user = new AppUser
            {
                UserId = _currentUser.UserId,
                DisplayName = "Pranjal",
                EmailLogin = _currentUser.Email,
                IsActive = true
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }

        // Check if user already applied or has existing application
        var existingApp = await _db.JobApplications
            .Include(a => a.JobPost)
            .Include(a => a.History)
            .FirstOrDefaultAsync(a => a.UserId == _currentUser.UserId && a.JobPostId == jobPostDto.JobPostId, ct);

        if (existingApp != null)
        {
            warnings.Add($"Existing application detected (Status: {existingApp.Status}).");
            var existingDto = MapToDto(existingApp);
            return new IntakeResult(classification, jobPostDto, existingDto, warnings);
        }

        // Create new application with initial status history in same transaction
        var newApp = new JobApplication
        {
            ApplicationId = Guid.CreateVersion7(),
            JobPostId = jobPostDto.JobPostId,
            UserId = _currentUser.UserId,
            ResumeId = request.ResumeId,
            Status = ApplicationStatus.ReadyToApply,
            ReviewStatus = ReviewStatus.NotStarted,
            ApplicationUrl = request.Url,
            CompletionPercentage = 0,
            LastActivityAt = now
        };

        var history = new JobApplicationStatusHistory
        {
            HistoryId = Guid.CreateVersion7(),
            ApplicationId = newApp.ApplicationId,
            PreviousStatus = null,
            NewStatus = ApplicationStatus.ReadyToApply,
            ChangeSource = ChangeSource.User,
            ChangedAt = now,
            Notes = "Initial intake via URL"
        };

        _db.JobApplications.Add(newApp);
        _db.JobApplicationStatusHistories.Add(history);
        await _db.SaveChangesAsync(ct);

        // Load job post for DTO mapping
        newApp.JobPost = (await _db.JobPosts.FindAsync(new object[] { jobPostDto.JobPostId }, ct))!;

        return new IntakeResult(classification, jobPostDto, MapToDto(newApp), warnings);
    }

    public async Task<JobApplicationDto> GetAsync(Guid applicationId, CancellationToken ct)
    {
        var app = await _db.JobApplications
            .Include(a => a.JobPost)
            .Include(a => a.History)
            .FirstOrDefaultAsync(a => a.ApplicationId == applicationId && a.UserId == _currentUser.UserId, ct)
            ?? throw new KeyNotFoundException($"Application '{applicationId}' not found.");

        return MapToDto(app);
    }

    public async Task<IReadOnlyList<JobApplicationSummaryDto>> ListAsync(CancellationToken ct)
    {
        return await _db.JobApplications
            .Where(a => a.UserId == _currentUser.UserId)
            .OrderByDescending(a => a.LastActivityAt)
            .Select(a => new JobApplicationSummaryDto(
                a.ApplicationId,
                a.JobPostId,
                a.JobPost.JobTitle,
                a.JobPost.CompanyName,
                a.Status,
                a.ReviewStatus,
                a.CompletionPercentage,
                a.LastActivityAt,
                a.AppliedAt))
            .ToListAsync(ct);
    }

    public async Task<JobApplicationDto> ChangeStatusAsync(Guid applicationId, ApplicationStatus newStatus, ChangeSource source, string? notes, CancellationToken ct)
    {
        var app = await _db.JobApplications
            .Include(a => a.JobPost)
            .Include(a => a.History)
            .FirstOrDefaultAsync(a => a.ApplicationId == applicationId && a.UserId == _currentUser.UserId, ct)
            ?? throw new KeyNotFoundException($"Application '{applicationId}' not found.");

        var now = _clock.UtcNow;
        app.ChangeStatus(newStatus, source, now, notes);

        await _db.SaveChangesAsync(ct);
        return MapToDto(app);
    }

    public async Task<JobApplicationDto> ConfirmSubmissionAsync(Guid applicationId, string? reference, CancellationToken ct)
    {
        var app = await _db.JobApplications
            .Include(a => a.JobPost)
            .Include(a => a.History)
            .FirstOrDefaultAsync(a => a.ApplicationId == applicationId && a.UserId == _currentUser.UserId, ct)
            ?? throw new KeyNotFoundException($"Application '{applicationId}' not found.");

        var now = _clock.UtcNow;
        app.ConfirmSubmission(SubmissionConfirmationSource.User, reference, now);

        await _db.SaveChangesAsync(ct);
        return MapToDto(app);
    }

    private static JobApplicationDto MapToDto(JobApplication app)
    {
        var historyDtos = app.History
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new StatusHistoryDto(
                h.HistoryId,
                h.PreviousStatus,
                h.NewStatus,
                h.ChangeSource,
                h.ChangedAt,
                h.Notes))
            .ToList();

        return new JobApplicationDto(
            app.ApplicationId,
            app.JobPostId,
            app.JobPost?.JobTitle ?? "Workday Position",
            app.JobPost?.CompanyName ?? "Company",
            app.ApplicationUrl,
            app.Status,
            app.ReviewStatus,
            app.CompletionPercentage,
            app.SubmissionReference,
            app.SubmissionConfirmedAt,
            app.LastActivityAt,
            historyDtos);
    }
}
