using JobPilot.Application.Features.Mapping;
using Microsoft.AspNetCore.Mvc;

namespace JobPilot.Api.Controllers;

[ApiController]
[Route("api/settings/ai")]
public class AiSettingsController : ControllerBase
{
    private readonly IAiSettingsService _settingsService;

    public AiSettingsController(IAiSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet]
    public async Task<ActionResult<AiSettingsDto>> GetSettings(CancellationToken ct)
    {
        var settings = await _settingsService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [HttpPost]
    public async Task<ActionResult<AiSettingsDto>> UpdateSettings([FromBody] UpdateAiSettingsRequest request, CancellationToken ct)
    {
        var updated = await _settingsService.UpdateSettingsAsync(request, ct);
        return Ok(updated);
    }

    [HttpPost("test")]
    public async Task<ActionResult<TestAiConnectionResponse>> TestConnection(CancellationToken ct)
    {
        var result = await _settingsService.TestConnectionAsync(ct);
        return Ok(result);
    }
}

