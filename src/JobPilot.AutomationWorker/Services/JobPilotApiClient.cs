using System.Net;
using System.Net.Http.Json;
using JobPilot.Infrastructure.Services;

namespace JobPilot.AutomationWorker.Services;

/// <summary>
/// Calls the local JobPilot API for decisions that need its encryption keys (decrypting the
/// resume, AI key and Workday password) — the worker only drives the browser.
/// </summary>
public sealed class JobPilotApiClient(HttpClient http)
{
    public async Task<StepPlan> PlanStepAsync(Guid sessionId, CancellationToken ct)
    {
        using var response = await http.PostAsync($"api/internal/sessions/{sessionId}/plan-step", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StepPlan>(ct)
               ?? throw new InvalidOperationException("Empty step plan from API.");
    }

    public async Task<WorkdayLoginSecret?> GetLoginAsync(string tenantKey, CancellationToken ct)
    {
        using var response = await http.GetAsync($"api/internal/workday-login/{Uri.EscapeDataString(tenantKey)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkdayLoginSecret>(ct);
    }

    public async Task MarkAccountExistsAsync(string tenantKey, CancellationToken ct)
    {
        using var response = await http.PostAsync($"api/internal/workday-login/{Uri.EscapeDataString(tenantKey)}/account-exists", null, ct);
        response.EnsureSuccessStatusCode();
    }
}
