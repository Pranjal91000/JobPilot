using JobPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace JobPilot.Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    private readonly JobPilotDbContext _db;

    public HealthController(JobPilotDbContext db)
    {
        _db = db;
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        var dbOk = await _db.Database.CanConnectAsync(ct);
        return Ok(new
        {
            status = dbOk ? "Healthy" : "Degraded",
            database = dbOk ? "Connected" : "Disconnected",
            timestamp = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("health/ai-key-info")]
    public async Task<IActionResult> GetAiKeyInfo([FromServices] JobPilot.Application.Features.Mapping.IAiSettingsService aiService, CancellationToken ct)
    {
        var key = await aiService.GetDecryptedApiKeyAsync(ct);
        if (key == null) return Ok(new { hasKey = false });
        return Ok(new
        {
            hasKey = true,
            length = key.Length,
            prefix = key.Length > 6 ? key[..6] + "..." : "short",
            suffix = key.Length > 4 ? "..." + key[^4..] : ""
        });
    }
}

