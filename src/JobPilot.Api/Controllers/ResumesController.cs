using JobPilot.Application.Abstractions;
using JobPilot.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace JobPilot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResumesController : ControllerBase
{
    private readonly IResumeService _resumeService;

    public ResumesController(IResumeService resumeService)
    {
        _resumeService = resumeService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResumeDto>>> ListResumes(CancellationToken ct)
    {
        var resumes = await _resumeService.ListAsync(ct);
        return Ok(resumes);
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB limit for safety
    public async Task<ActionResult<ResumeDto>> UploadResume(
        IFormFile file,
        [FromForm] ResumeType resumeType = ResumeType.General,
        [FromForm] string? label = null,
        [FromForm] bool isDefault = false,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest("A valid file is required.");

        var meta = new ResumeMetadata(resumeType, label, isDefault, true);

        await using var stream = file.OpenReadStream();
        var resume = await _resumeService.UploadAsync(
            stream,
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            meta,
            ct);

        return CreatedAtAction(nameof(ListResumes), new { id = resume.ResumeId }, resume);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ResumeDto>> UpdateResume(
        Guid id,
        [FromBody] ResumeMetadata meta,
        CancellationToken ct)
    {
        try
        {
            var updated = await _resumeService.UpdateAsync(id, meta, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteResume(Guid id, CancellationToken ct)
    {
        try
        {
            await _resumeService.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> DownloadResume(Guid id, CancellationToken ct)
    {
        try
        {
            var stream = await _resumeService.OpenForReadAsync(id, ct);
            return File(stream, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}

