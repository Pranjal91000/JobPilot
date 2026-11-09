using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobPilot.Api.Tests;

public class JobApplicationIntakeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public JobApplicationIntakeTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Intake_ValidWorkdayUrl_ShouldCreateJobPostAndApplication()
    {
        var reqId = $"R-{Guid.NewGuid():N}";
        var req = new IntakeRequest(
            Url: $"https://acme.wd5.myworkdayjobs.com/en-US/AcmeCareers/job/Pune-India/Senior-NET-Developer_{reqId}",
            ResumeId: null);

        var res = await _client.PostAsJsonAsync("/api/applications/intake", req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await res.Content.ReadFromJsonAsync<IntakeResult>();
        result.Should().NotBeNull();
        result!.Classification.IsSupported.Should().BeTrue();
        result.Classification.TenantKey.Should().Be("acme");
        result.Classification.ExternalJobId.Should().Be(reqId);

        result.JobPost.JobPostId.Should().NotBeEmpty();
        result.Application.Status.Should().Be(ApplicationStatus.ReadyToApply);
        result.DuplicateWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Intake_DuplicateUrlOrLocaleVariant_ShouldReturnExistingApplicationWithWarning()
    {
        var reqId = $"R-{Guid.NewGuid():N}";
        // First intake
        var req1 = new IntakeRequest(
            Url: $"https://acme.wd5.myworkdayjobs.com/en-US/AcmeCareers/job/Pune-India/Lead-Dev_{reqId}",
            ResumeId: null);

        var res1 = await _client.PostAsJsonAsync("/api/applications/intake", req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var initial = await res1.Content.ReadFromJsonAsync<IntakeResult>();

        // Second intake with different locale segment and apply subpath
        var req2 = new IntakeRequest(
            Url: $"https://acme.wd5.myworkdayjobs.com/fr-FR/AcmeCareers/job/Pune-India/Lead-Dev_{reqId}/apply",
            ResumeId: null);

        var res2 = await _client.PostAsJsonAsync("/api/applications/intake", req2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK); // 200 OK because existing application detected
        var duplicateResult = await res2.Content.ReadFromJsonAsync<IntakeResult>();
        duplicateResult.Should().NotBeNull();
        duplicateResult!.Application.ApplicationId.Should().Be(initial!.Application.ApplicationId);
        duplicateResult.DuplicateWarnings.Should().NotBeEmpty();
        duplicateResult.DuplicateWarnings[0].Should().Contain("Existing application detected");
    }

    [Fact]
    public async Task Intake_DifferentReqIds_ShouldCreateSeparateJobPosts()
    {
        var req1 = new IntakeRequest(
            Url: $"https://acme.wd5.myworkdayjobs.com/AcmeCareers/job/Engineer_R-{Guid.NewGuid():N}",
            ResumeId: null);
        var req2 = new IntakeRequest(
            Url: $"https://acme.wd5.myworkdayjobs.com/AcmeCareers/job/Engineer_R-{Guid.NewGuid():N}",
            ResumeId: null);

        var res1 = await _client.PostAsJsonAsync("/api/applications/intake", req1);
        var res2 = await _client.PostAsJsonAsync("/api/applications/intake", req2);

        var result1 = await res1.Content.ReadFromJsonAsync<IntakeResult>();
        var result2 = await res2.Content.ReadFromJsonAsync<IntakeResult>();

        result1!.JobPost.JobPostId.Should().NotBe(result2!.JobPost.JobPostId);
    }

    [Fact]
    public async Task Intake_UnsupportedAts_ShouldReturn422UnprocessableEntity()
    {
        var req = new IntakeRequest(
            Url: "https://boards.greenhouse.io/stripe/jobs/1234567",
            ResumeId: null);

        var res = await _client.PostAsJsonAsync("/api/applications/intake", req);
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Detail.Should().Contain("Only Workday career sites");
    }

    [Fact]
    public async Task Application_InvalidStatusTransition_ShouldReturn409ConflictWithAllowedTargets()
    {
        // 1. Create an application
        var intakeRes = await _client.PostAsJsonAsync("/api/applications/intake", new IntakeRequest(
            Url: $"https://acme.wd5.myworkdayjobs.com/AcmeCareers/job/QA_R-{Guid.NewGuid():N}",
            ResumeId: null));
        var intake = await intakeRes.Content.ReadFromJsonAsync<IntakeResult>();
        var appId = intake!.Application.ApplicationId;

        // 2. Attempt invalid transition from ReadyToApply directly to Offer
        var transitionRes = await _client.PutAsJsonAsync($"/api/applications/{appId}/status", new
        {
            Status = ApplicationStatus.Offer,
            Notes = "Jumping straight to offer"
        });

        transitionRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await transitionRes.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Invalid status transition");
    }

    [Fact]
    public async Task Application_ConfirmSubmission_TransitionsToSubmitted()
    {
        // 1. Create application
        var intakeRes = await _client.PostAsJsonAsync("/api/applications/intake", new IntakeRequest(
            Url: $"https://acme.wd5.myworkdayjobs.com/AcmeCareers/job/Architect_R-{Guid.NewGuid():N}",
            ResumeId: null));
        var intake = await intakeRes.Content.ReadFromJsonAsync<IntakeResult>();
        var appId = intake!.Application.ApplicationId;

        // 2. Advance to InProgress then AwaitingUserReview
        await _client.PutAsJsonAsync($"/api/applications/{appId}/status", new
        {
            Status = ApplicationStatus.InProgress,
            Notes = "User started filling"
        });
        await _client.PutAsJsonAsync($"/api/applications/{appId}/status", new
        {
            Status = ApplicationStatus.AwaitingUserReview,
            Notes = "Review step reached"
        });

        // 3. User confirms submission
        var confirmRes = await _client.PostAsJsonAsync($"/api/applications/{appId}/confirm-submission", new
        {
            Reference = "WD-REF-998877"
        });

        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed = await confirmRes.Content.ReadFromJsonAsync<JobApplicationDto>();

        confirmed.Should().NotBeNull();
        confirmed!.Status.Should().Be(ApplicationStatus.Submitted);
        confirmed.SubmissionReference.Should().Be("WD-REF-998877");
        confirmed.SubmissionConfirmedAt.Should().NotBeNull();
    }
}
