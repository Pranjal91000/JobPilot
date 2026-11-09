using JobPilot.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace JobPilot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly IUserProfileService _profileService;

    public ProfileController(IUserProfileService profileService)
    {
        _profileService = profileService;
    }

    [HttpGet]
    public async Task<ActionResult<ProfileDto>> GetProfile(CancellationToken ct)
    {
        var profile = await _profileService.GetAsync(ct);
        return Ok(profile);
    }

    [HttpPut]
    public async Task<ActionResult<ProfileDto>> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var updated = await _profileService.UpdateAsync(request, ct);
        return Ok(updated);
    }

    [HttpGet("completeness")]
    public async Task<ActionResult<CompletenessReport>> GetCompleteness(CancellationToken ct)
    {
        var report = await _profileService.GetCompletenessAsync(ct);
        return Ok(report);
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> ConfirmFacts([FromBody] IReadOnlyList<FactRef> facts, CancellationToken ct)
    {
        await _profileService.ConfirmFactsAsync(facts, ct);
        return NoContent();
    }
}

