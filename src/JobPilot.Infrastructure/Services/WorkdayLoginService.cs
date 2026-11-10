using JobPilot.Application.Abstractions;
using JobPilot.Domain.Entities;
using JobPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Infrastructure.Services;

public sealed record WorkdayLoginDto(string? Email, bool HasPassword, IReadOnlyList<string> SitesWithAccounts);

public sealed record WorkdayLoginSecret(string Email, string Password, bool AccountExists);

/// <summary>
/// Stores the candidate's Workday login. Most people use one email and password for every
/// company's Workday site, so there is a default ("*") entry; a per-site entry is added once an
/// account exists on that site (created by JobPilot or confirmed by a successful sign-in).
/// Passwords are encrypted with the API's data-protection keys and only decrypted for the worker.
/// </summary>
public sealed class WorkdayLoginService(JobPilotDbContext db, ICurrentUser currentUser, ISecretProtector protector)
{
    private const string DefaultKey = "*";

    public async Task<WorkdayLoginDto> GetAsync(CancellationToken ct)
    {
        var rows = await db.WorkdayCredentials.AsNoTracking().ToListAsync(ct);
        var def = rows.FirstOrDefault(r => r.TenantKey == DefaultKey);
        return new WorkdayLoginDto(
            def?.Email,
            def != null && !string.IsNullOrEmpty(def.PasswordEnc),
            rows.Where(r => r.TenantKey != DefaultKey && r.AccountExists).Select(r => r.TenantKey).OrderBy(t => t).ToList());
    }

    public async Task SaveDefaultAsync(string email, string? password, CancellationToken ct)
    {
        var row = await db.WorkdayCredentials.FirstOrDefaultAsync(r => r.TenantKey == DefaultKey, ct);
        if (row == null)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("A password is required the first time.");
            row = new WorkdayCredential { UserId = currentUser.UserId, TenantKey = DefaultKey };
            db.WorkdayCredentials.Add(row);
        }

        row.Email = email.Trim();
        if (!string.IsNullOrWhiteSpace(password))
        {
            row.PasswordEnc = protector.ProtectString(password);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<WorkdayLoginSecret?> GetSecretAsync(string tenantKey, CancellationToken ct)
    {
        var rows = await db.WorkdayCredentials.AsNoTracking()
            .Where(r => r.TenantKey == tenantKey || r.TenantKey == DefaultKey)
            .ToListAsync(ct);

        var tenant = rows.FirstOrDefault(r => r.TenantKey == tenantKey);
        var source = tenant ?? rows.FirstOrDefault(r => r.TenantKey == DefaultKey);
        if (source == null || string.IsNullOrEmpty(source.PasswordEnc)) return null;

        return new WorkdayLoginSecret(source.Email, protector.UnprotectString(source.PasswordEnc), tenant?.AccountExists ?? false);
    }

    /// <summary>Records that an account exists on this site, copying the default login.</summary>
    public async Task MarkAccountExistsAsync(string tenantKey, CancellationToken ct)
    {
        var tenant = await db.WorkdayCredentials.FirstOrDefaultAsync(r => r.TenantKey == tenantKey, ct);
        var def = await db.WorkdayCredentials.AsNoTracking().FirstOrDefaultAsync(r => r.TenantKey == DefaultKey, ct);

        if (tenant == null)
        {
            if (def == null) return;
            tenant = new WorkdayCredential
            {
                UserId = currentUser.UserId,
                TenantKey = tenantKey,
                Email = def.Email,
                PasswordEnc = def.PasswordEnc
            };
            db.WorkdayCredentials.Add(tenant);
        }

        tenant.AccountExists = true;
        tenant.LastUsedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
