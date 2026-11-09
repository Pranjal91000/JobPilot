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
}

