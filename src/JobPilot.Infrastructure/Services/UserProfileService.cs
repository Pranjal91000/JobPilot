using JobPilot.Application.Abstractions;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Domain.ValueObjects;
using JobPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Infrastructure.Services;

public class UserProfileService : IUserProfileService
{
    private readonly JobPilotDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ISecretProtector _protector;

    public UserProfileService(
        JobPilotDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        ISecretProtector protector)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _protector = protector;
    }

    public async Task<ProfileDto> GetAsync(CancellationToken ct)
    {
        var profile = await GetOrCreateProfileEntityAsync(ct);

        var educations = await _db.Educations
            .Where(e => e.UserId == _currentUser.UserId)
            .OrderBy(e => e.SortOrder)
            .Select(e => new EducationDto(
                e.EducationId, e.Institution, e.Degree, e.Specialization,
                e.StartDate, e.EndDate, e.Grade, e.Description, e.CountryCode,
                e.SortOrder, e.Provenance.Status))
            .ToListAsync(ct);

        var employments = await _db.EmploymentHistories
            .Where(e => e.UserId == _currentUser.UserId)
            .OrderBy(e => e.SortOrder)
            .Select(e => new EmploymentDto(
                e.EmploymentId, e.CompanyName, e.JobTitle, e.EmploymentType, e.Location,
                e.StartDate, e.EndDate, e.IsCurrent, e.Responsibilities, e.Technologies,
                e.SortOrder, e.Provenance.Status))
            .ToListAsync(ct);

        var skills = await _db.Skills
            .Where(s => s.UserId == _currentUser.UserId)
            .OrderBy(s => s.SkillName)
            .Select(s => new SkillDto(
                s.SkillId, s.SkillName, s.NormalizedName, s.ProficiencyLevel, s.YearsOfExperience,
                s.Provenance.Status))
            .ToListAsync(ct);

        var projects = await _db.Projects
            .Where(p => p.UserId == _currentUser.UserId)
            .OrderByDescending(p => p.StartDate)
            .Select(p => new ProjectDto(
                p.ProjectId, p.Name, p.Role, p.Description, p.Technologies, p.Url,
                p.StartDate, p.EndDate, p.Provenance.Status))
            .ToListAsync(ct);

        var certs = await _db.Certifications
            .Where(c => c.UserId == _currentUser.UserId)
            .OrderByDescending(c => c.IssuedOn)
            .Select(c => new CertificationDto(
                c.CertificationId, c.Name, c.Issuer, c.CredentialId, c.IssuedOn, c.ExpiresOn, c.CredentialUrl,
                c.Provenance.Status))
            .ToListAsync(ct);

        var achievements = await _db.Achievements
            .Where(a => a.UserId == _currentUser.UserId)
            .OrderByDescending(a => a.AchievedOn)
            .Select(a => new AchievementDto(
                a.AchievementId, a.Title, a.Description, a.AchievedOn,
                a.Provenance.Status))
            .ToListAsync(ct);

        var links = await _db.ProfessionalLinks
            .Where(l => l.UserId == _currentUser.UserId)
            .Select(l => new ProfessionalLinkDto(
                l.LinkId, l.LinkType, l.Url, l.Provenance.Status))
            .ToListAsync(ct);

        var workAuths = await _db.WorkAuthorizations
            .Where(w => w.UserId == _currentUser.UserId)
            .Select(w => new WorkAuthorizationDto(
                w.AuthorizationId, w.CountryCode, w.StatusDescription, w.RequiresSponsorship, w.ValidUntil,
                w.Provenance.Status))
            .ToListAsync(ct);

        var locations = await _db.LocationPreferences
            .Where(l => l.UserId == _currentUser.UserId)
            .OrderBy(l => l.Priority)
            .Select(l => new LocationPreferenceDto(
                l.PreferenceId, l.LocationText, l.CountryCode, l.IsRelocationOk, l.RemotePreference, l.Priority,
                l.Provenance.Status))
            .ToListAsync(ct);

        var verifications = await _db.ProfileFieldVerifications
            .Where(v => v.UserId == _currentUser.UserId)
            .ToDictionaryAsync(v => v.ProfilePath, v => v.VerificationStatus, ct);

        string? expectedSalaryDecrypted = null;
        if (profile.ExpectedSalaryEnc != null && profile.ExpectedSalaryEnc.Length > 0)
        {
            try
            {
                var decrypted = _protector.Unprotect(profile.ExpectedSalaryEnc);
                expectedSalaryDecrypted = System.Text.Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                expectedSalaryDecrypted = null;
            }
        }

        return new ProfileDto(
            profile.UserId,
            profile.FirstName,
            profile.MiddleName,
            profile.LastName,
            profile.PreferredName,
            profile.Email,
            profile.PhoneE164,
            profile.PhoneCountryCode,
            profile.AddressLine1,
            profile.AddressLine2,
            profile.City,
            profile.StateRegion,
            profile.PostalCode,
            profile.CountryCode,
            profile.ProfessionalSummary,
            profile.CurrentLocation,
            profile.NoticePeriodDays,
            expectedSalaryDecrypted,
            profile.ExpectedSalaryCurrency,
            profile.TotalExperienceMonths,
            educations,
            employments,
            skills,
            projects,
            certs,
            achievements,
            links,
            workAuths,
            locations,
            verifications);
    }

    public async Task<ProfileDto> UpdateAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var profile = await GetOrCreateProfileEntityAsync(ct);

        profile.FirstName = request.FirstName.Trim();
        profile.MiddleName = request.MiddleName?.Trim();
        profile.LastName = request.LastName.Trim();
        profile.PreferredName = request.PreferredName?.Trim();
        profile.Email = request.Email.Trim();
        profile.PhoneE164 = request.PhoneE164?.Trim();
        profile.PhoneCountryCode = request.PhoneCountryCode?.Trim();
        profile.AddressLine1 = request.AddressLine1?.Trim();
        profile.AddressLine2 = request.AddressLine2?.Trim();
        profile.City = request.City?.Trim();
        profile.StateRegion = request.StateRegion?.Trim();
        profile.PostalCode = request.PostalCode?.Trim();
        profile.CountryCode = request.CountryCode?.Trim();
        profile.ProfessionalSummary = request.ProfessionalSummary?.Trim();
        profile.CurrentLocation = request.CurrentLocation?.Trim();
        profile.NoticePeriodDays = request.NoticePeriodDays;
        profile.ExpectedSalaryCurrency = request.ExpectedSalaryCurrency?.Trim();

        if (!string.IsNullOrWhiteSpace(request.ExpectedSalary))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(request.ExpectedSalary.Trim());
            profile.ExpectedSalaryEnc = _protector.Protect(bytes);
        }
        else
        {
            profile.ExpectedSalaryEnc = null;
        }

        // FR-P3: Explicit user action sets Verified status for scalar profile facts
        await SetVerificationAsync("Personal.FirstName", now, ct);
        await SetVerificationAsync("Personal.LastName", now, ct);
        await SetVerificationAsync("Contact.Email", now, ct);
        if (!string.IsNullOrWhiteSpace(profile.PhoneE164))
            await SetVerificationAsync("Contact.PhoneNational", now, ct);
        if (!string.IsNullOrWhiteSpace(profile.AddressLine1))
            await SetVerificationAsync("Address.Line1", now, ct);
        if (!string.IsNullOrWhiteSpace(profile.City))
            await SetVerificationAsync("Address.City", now, ct);
        if (!string.IsNullOrWhiteSpace(profile.PostalCode))
            await SetVerificationAsync("Address.PostalCode", now, ct);
        if (!string.IsNullOrWhiteSpace(profile.CountryCode))
            await SetVerificationAsync("Address.Country", now, ct);
        if (profile.NoticePeriodDays.HasValue)
            await SetVerificationAsync("Preferences.NoticePeriodDays", now, ct);
        if (profile.ExpectedSalaryEnc != null)
            await SetVerificationAsync("Compensation.ExpectedSalary", now, ct);

        await _db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public async Task ConfirmFactsAsync(IReadOnlyList<FactRef> facts, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        foreach (var fact in facts)
        {
            if (!string.IsNullOrWhiteSpace(fact.ProfilePath))
            {
                await SetVerificationAsync(fact.ProfilePath, now, ct);
            }

            if (fact.EntityId.HasValue)
            {
                switch (fact.EntityType.ToLowerInvariant())
                {
                    case "education":
                        var edu = await _db.Educations.FirstOrDefaultAsync(e => e.EducationId == fact.EntityId.Value && e.UserId == _currentUser.UserId, ct);
                        if (edu != null) edu.Provenance = edu.Provenance.Confirm(now);
                        break;
                    case "employment":
                        var emp = await _db.EmploymentHistories.FirstOrDefaultAsync(e => e.EmploymentId == fact.EntityId.Value && e.UserId == _currentUser.UserId, ct);
                        if (emp != null) emp.Provenance = emp.Provenance.Confirm(now);
                        break;
                    case "skill":
                        var sk = await _db.Skills.FirstOrDefaultAsync(s => s.SkillId == fact.EntityId.Value && s.UserId == _currentUser.UserId, ct);
                        if (sk != null) sk.Provenance = sk.Provenance.Confirm(now);
                        break;
                    case "project":
                        var pr = await _db.Projects.FirstOrDefaultAsync(p => p.ProjectId == fact.EntityId.Value && p.UserId == _currentUser.UserId, ct);
                        if (pr != null) pr.Provenance = pr.Provenance.Confirm(now);
                        break;
                    case "certification":
                        var cert = await _db.Certifications.FirstOrDefaultAsync(c => c.CertificationId == fact.EntityId.Value && c.UserId == _currentUser.UserId, ct);
                        if (cert != null) cert.Provenance = cert.Provenance.Confirm(now);
                        break;
                    case "achievement":
                        var ach = await _db.Achievements.FirstOrDefaultAsync(a => a.AchievementId == fact.EntityId.Value && a.UserId == _currentUser.UserId, ct);
                        if (ach != null) ach.Provenance = ach.Provenance.Confirm(now);
                        break;
                    case "link":
                        var link = await _db.ProfessionalLinks.FirstOrDefaultAsync(l => l.LinkId == fact.EntityId.Value && l.UserId == _currentUser.UserId, ct);
                        if (link != null) link.Provenance = link.Provenance.Confirm(now);
                        break;
                    case "workauthorization":
                        var wa = await _db.WorkAuthorizations.FirstOrDefaultAsync(w => w.AuthorizationId == fact.EntityId.Value && w.UserId == _currentUser.UserId, ct);
                        if (wa != null) wa.Provenance = wa.Provenance.Confirm(now);
                        break;
                    case "locationpreference":
                        var lp = await _db.LocationPreferences.FirstOrDefaultAsync(l => l.PreferenceId == fact.EntityId.Value && l.UserId == _currentUser.UserId, ct);
                        if (lp != null) lp.Provenance = lp.Provenance.Confirm(now);
                        break;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<VerifiedProfileSnapshot> GetVerifiedSnapshotAsync(CancellationToken ct)
    {
        var profile = await GetOrCreateProfileEntityAsync(ct);
        var verifiedFacts = new Dictionary<string, object?>();

        var verifications = await _db.ProfileFieldVerifications
            .Where(v => v.UserId == _currentUser.UserId && v.VerificationStatus == VerificationStatus.Verified)
            .Select(v => v.ProfilePath)
            .ToListAsync(ct);

        var verifiedSet = new HashSet<string>(verifications, StringComparer.OrdinalIgnoreCase);

        if (verifiedSet.Contains("Personal.FirstName") && !string.IsNullOrWhiteSpace(profile.FirstName))
            verifiedFacts["Personal.FirstName"] = profile.FirstName;

        if (verifiedSet.Contains("Personal.LastName") && !string.IsNullOrWhiteSpace(profile.LastName))
            verifiedFacts["Personal.LastName"] = profile.LastName;

        if (verifiedSet.Contains("Contact.Email") && !string.IsNullOrWhiteSpace(profile.Email))
            verifiedFacts["Contact.Email"] = profile.Email;

        if (verifiedSet.Contains("Contact.PhoneNational") && !string.IsNullOrWhiteSpace(profile.PhoneE164))
            verifiedFacts["Contact.PhoneNational"] = profile.PhoneE164;

        if (verifiedSet.Contains("Address.Line1") && !string.IsNullOrWhiteSpace(profile.AddressLine1))
            verifiedFacts["Address.Line1"] = profile.AddressLine1;

        if (verifiedSet.Contains("Address.City") && !string.IsNullOrWhiteSpace(profile.City))
            verifiedFacts["Address.City"] = profile.City;

        if (verifiedSet.Contains("Address.PostalCode") && !string.IsNullOrWhiteSpace(profile.PostalCode))
            verifiedFacts["Address.PostalCode"] = profile.PostalCode;

        if (verifiedSet.Contains("Address.Country") && !string.IsNullOrWhiteSpace(profile.CountryCode))
            verifiedFacts["Address.Country"] = profile.CountryCode;

        if (verifiedSet.Contains("Preferences.NoticePeriodDays") && profile.NoticePeriodDays.HasValue)
            verifiedFacts["Preferences.NoticePeriodDays"] = profile.NoticePeriodDays.Value;

        // Verified Employments
        var verifiedEmps = await _db.EmploymentHistories
            .Where(e => e.UserId == _currentUser.UserId && e.Provenance.Status == VerificationStatus.Verified)
            .OrderBy(e => e.SortOrder)
            .ToListAsync(ct);

        verifiedFacts["EmploymentHistories"] = verifiedEmps;
        if (verifiedEmps.Count > 0)
        {
            var cur = verifiedEmps.FirstOrDefault(e => e.IsCurrent) ?? verifiedEmps[0];
            verifiedFacts["Employment.Current.JobTitle"] = cur.JobTitle;
            verifiedFacts["Employment.Current.CompanyName"] = cur.CompanyName;
            if (!string.IsNullOrWhiteSpace(cur.Location))
                verifiedFacts["Employment.Current.Location"] = cur.Location;
            verifiedFacts["Employment.Current.StartDate"] = cur.StartDate.ToString("yyyy-MM-dd");
            if (cur.EndDate.HasValue)
                verifiedFacts["Employment.Current.EndDate"] = cur.EndDate.Value.ToString("yyyy-MM-dd");
            if (!string.IsNullOrWhiteSpace(cur.Responsibilities))
                verifiedFacts["Employment.Current.Description"] = cur.Responsibilities;
        }

        // Verified Educations
        var verifiedEdus = await _db.Educations
            .Where(e => e.UserId == _currentUser.UserId && e.Provenance.Status == VerificationStatus.Verified)
            .OrderBy(e => e.SortOrder)
            .ToListAsync(ct);

        verifiedFacts["Educations"] = verifiedEdus;
        if (verifiedEdus.Count > 0)
        {
            var edu = verifiedEdus[0];
            verifiedFacts["Education.Highest.School"] = edu.Institution;
            verifiedFacts["Education.Highest.Degree"] = edu.Degree;
            if (!string.IsNullOrWhiteSpace(edu.Specialization))
                verifiedFacts["Education.Highest.FieldOfStudy"] = edu.Specialization;
            if (edu.StartDate.HasValue)
                verifiedFacts["Education.Highest.StartDate"] = edu.StartDate.Value.ToString("yyyy-MM-dd");
            if (edu.EndDate.HasValue)
                verifiedFacts["Education.Highest.EndDate"] = edu.EndDate.Value.ToString("yyyy-MM-dd");
            if (!string.IsNullOrWhiteSpace(edu.Grade))
                verifiedFacts["Education.Highest.Grade"] = edu.Grade;
        }

        // Verified Skills
        var verifiedSkills = await _db.Skills
            .Where(s => s.UserId == _currentUser.UserId && s.Provenance.Status == VerificationStatus.Verified)
            .OrderBy(s => s.SkillName)
            .ToListAsync(ct);

        verifiedFacts["Skills"] = verifiedSkills;
        if (verifiedSkills.Count > 0)
        {
            verifiedFacts["Skills.All"] = string.Join(", ", verifiedSkills.Select(s => s.SkillName));
        }

        // Default Active Resume for File Uploads
        var defaultResume = await _db.Resumes
            .Where(r => r.UserId == _currentUser.UserId && r.IsDefault && r.IsActive && r.DeletedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (defaultResume != null)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "JobPilot", "resumes");
                Directory.CreateDirectory(tempDir);
                var tempFile = Path.Combine(tempDir, defaultResume.FileName);
                if (File.Exists(defaultResume.StorageReference))
                {
                    var encryptedBytes = await File.ReadAllBytesAsync(defaultResume.StorageReference, ct);
                    var plainBytes = _protector.Unprotect(encryptedBytes);
                    await File.WriteAllBytesAsync(tempFile, plainBytes, ct);
                    verifiedFacts["Documents.Resume"] = tempFile;
                }
            }
            catch
            {
                // Resume preparation failure ignored for fallback
            }
        }

        return new VerifiedProfileSnapshot(
            profile.UserId,
            $"{profile.FirstName} {profile.LastName}".Trim(),
            profile.Email,
            profile.PhoneE164,
            verifiedFacts);
    }

    public async Task<CompletenessReport> GetCompletenessAsync(CancellationToken ct)
    {
        var profile = await GetOrCreateProfileEntityAsync(ct);
        var issues = new List<CompletenessIssue>();

        var verifications = await _db.ProfileFieldVerifications
            .Where(v => v.UserId == _currentUser.UserId)
            .ToDictionaryAsync(v => v.ProfilePath, v => v.VerificationStatus, ct);

        void CheckField(string key, string? val, string desc, string cat, bool req)
        {
            if (string.IsNullOrWhiteSpace(val))
            {
                issues.Add(new CompletenessIssue(key, desc, cat, req));
            }
            else if (!verifications.TryGetValue(key, out var status) || status != VerificationStatus.Verified)
            {
                issues.Add(new CompletenessIssue(key, $"{desc} is present but unconfirmed.", cat, req));
            }
        }

        CheckField("Personal.FirstName", profile.FirstName, "First name", "Personal", true);
        CheckField("Personal.LastName", profile.LastName, "Last name", "Personal", true);
        CheckField("Contact.Email", profile.Email, "Email address", "Contact", true);
        CheckField("Contact.PhoneNational", profile.PhoneE164, "Phone number", "Contact", true);
        CheckField("Address.Country", profile.CountryCode, "Country", "Address", true);

        var eduCount = await _db.Educations.CountAsync(e => e.UserId == _currentUser.UserId, ct);
        if (eduCount == 0) issues.Add(new CompletenessIssue("Education", "No education history added", "Education", true));

        var empCount = await _db.EmploymentHistories.CountAsync(e => e.UserId == _currentUser.UserId, ct);
        if (empCount == 0) issues.Add(new CompletenessIssue("Employment", "No employment history added", "Employment", true));

        var skillCount = await _db.Skills.CountAsync(s => s.UserId == _currentUser.UserId, ct);
        if (skillCount == 0) issues.Add(new CompletenessIssue("Skills", "No skills added", "Skills", true));

        var resumeCount = await _db.Resumes.CountAsync(r => r.UserId == _currentUser.UserId && r.DeletedAt == null, ct);
        if (resumeCount == 0) issues.Add(new CompletenessIssue("Resume", "No resume uploaded", "Resume", true));

        int totalChecked = 9;
        int missingCount = issues.Count;
        int verifiedCount = totalChecked - missingCount;
        decimal percentage = Math.Max(0m, Math.Round((decimal)verifiedCount / totalChecked * 100m, 1));

        return new CompletenessReport(
            totalChecked,
            verifiedCount,
            missingCount,
            0,
            percentage,
            issues);
    }

    private async Task SetVerificationAsync(string path, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.ProfileFieldVerifications
            .FirstOrDefaultAsync(v => v.UserId == _currentUser.UserId && v.ProfilePath == path, ct);

        if (existing == null)
        {
            _db.ProfileFieldVerifications.Add(new ProfileFieldVerification
            {
                UserId = _currentUser.UserId,
                ProfilePath = path,
                VerificationStatus = VerificationStatus.Verified,
                Source = FactSource.UserEntered,
                VerifiedAt = now
            });
        }
        else
        {
            existing.VerificationStatus = VerificationStatus.Verified;
            existing.Source = FactSource.UserEntered;
            existing.VerifiedAt = now;
        }
    }

    private async Task<UserProfile> GetOrCreateProfileEntityAsync(CancellationToken ct)
    {
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
        }

        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == _currentUser.UserId, ct);
        if (profile == null)
        {
            profile = new UserProfile
            {
                UserId = _currentUser.UserId,
                FirstName = "Pranjal",
                LastName = "User",
                Email = _currentUser.Email
            };
            _db.UserProfiles.Add(profile);
            await _db.SaveChangesAsync(ct);
        }

        return profile;
    }
}

