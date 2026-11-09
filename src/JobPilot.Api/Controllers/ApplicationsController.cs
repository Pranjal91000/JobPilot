using JobPilot.Application.Abstractions;
using JobPilot.Domain.Enums;
using JobPilot.Domain.Policies;
using Microsoft.AspNetCore.Mvc;

namespace JobPilot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApplicationsController : ControllerBase
{
    private readonly IJobApplicationService _applicationService;

    public ApplicationsController(IJobApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    [HttpPost("intake")]
    public async Task<IActionResult> Intake([FromBody] IntakeRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _applicationService.IntakeAsync(request, ct);
            if (result.DuplicateWarnings.Count > 0)
            {
                return Ok(result);
            }
            return CreatedAtAction(nameof(GetApplication), new { id = result.Application.ApplicationId }, result);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "Unsupported portal or invalid URL",
                Detail = ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ListApplications(
        [FromServices] JobPilot.Infrastructure.Services.JobApplicationService concreteService,
        CancellationToken ct)
    {
        var list = await concreteService.ListAsync(ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JobApplicationDto>> GetApplication(Guid id, CancellationToken ct)
    {
        try
        {
            var app = await _applicationService.GetAsync(id, ct);
            return Ok(app);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    public sealed record StatusChangeRequest(ApplicationStatus Status, string? Notes);

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(
        Guid id,
        [FromBody] StatusChangeRequest request,
        CancellationToken ct)
    {
        try
        {
            var updated = await _applicationService.ChangeStatusAsync(id, request.Status, ChangeSource.User, request.Notes, ct);
            return Ok(updated);
        }
        catch (InvalidStatusTransitionException ex)
        {
            var allowed = ApplicationStatusTransitions.GetAllowedTargets(ex.From);
            return Conflict(new ProblemDetails
            {
                Title = "Invalid status transition",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict,
                Extensions = { ["allowedTargets"] = allowed }
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    public sealed record ConfirmSubmissionRequest(string? Reference);

    [HttpPost("{id:guid}/confirm-submission")]
    public async Task<ActionResult<JobApplicationDto>> ConfirmSubmission(
        Guid id,
        [FromBody] ConfirmSubmissionRequest? request,
        CancellationToken ct)
    {
        try
        {
            var updated = await _applicationService.ConfirmSubmissionAsync(id, request?.Reference, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}

[ApiController]
[Route("api/jobs")]
public class JobsController : ControllerBase
{
    private readonly IJobPostService _jobPostService;

    public JobsController(IJobPostService jobPostService)
    {
        _jobPostService = jobPostService;
    }

    public sealed record ClassifyRequest(string Url);

    [HttpPost("classify-url")]
    public async Task<ActionResult<UrlClassification>> ClassifyUrl([FromBody] ClassifyRequest request, CancellationToken ct)
    {
        var classification = await _jobPostService.ClassifyUrlAsync(request.Url, ct);
        return Ok(classification);
    }
}

