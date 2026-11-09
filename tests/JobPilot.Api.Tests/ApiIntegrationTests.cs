using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobPilot.Api.Tests;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_Endpoint_ShouldReturnHealthy()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
        content.Should().Contain("Connected");
    }

    [Fact]
    public async Task Profile_GetAndUpdate_ShouldSetVerifiedStatusOnUserEdit()
    {
        // 1. Get initial profile
        var getRes = await _client.GetAsync("/api/profile");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var initial = await getRes.Content.ReadFromJsonAsync<ProfileDto>();
        initial.Should().NotBeNull();

        // 2. Update profile with user-entered values
        var updateReq = new UpdateProfileRequest(
            FirstName: "Pranjal",
            MiddleName: null,
            LastName: "Developer",
            PreferredName: "Pranjal",
            Email: "pranjal@jobpilot.local",
            PhoneE164: "+919876543210",
            PhoneCountryCode: "+91",
            AddressLine1: "123 Tech Park",
            AddressLine2: "Suite 400",
            City: "Pune",
            StateRegion: "Maharashtra",
            PostalCode: "411001",
            CountryCode: "IN",
            ProfessionalSummary: "Senior .NET & Full Stack Engineer with 7+ years of experience.",
            CurrentLocation: "Pune, India",
            NoticePeriodDays: 30,
            ExpectedSalary: "2500000",
            ExpectedSalaryCurrency: "INR");

        var putRes = await _client.PutAsJsonAsync("/api/profile", updateReq);
        putRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await putRes.Content.ReadFromJsonAsync<ProfileDto>();

        updated.Should().NotBeNull();
        updated!.FirstName.Should().Be("Pranjal");
        updated.LastName.Should().Be("Developer");
        updated.City.Should().Be("Pune");
        updated.ExpectedSalary.Should().Be("2500000");

        // FR-P3: Explicit user edits MUST set Verified status on the profile paths
        updated.FieldVerifications.Should().ContainKey("Personal.FirstName");
        updated.FieldVerifications["Personal.FirstName"].Should().Be(VerificationStatus.Verified);
        updated.FieldVerifications["Contact.Email"].Should().Be(VerificationStatus.Verified);
        updated.FieldVerifications["Contact.PhoneNational"].Should().Be(VerificationStatus.Verified);
        updated.FieldVerifications["Address.City"].Should().Be(VerificationStatus.Verified);
    }

    [Fact]
    public async Task Profile_Completeness_ShouldReturnCalculatedPercentage()
    {
        var res = await _client.GetAsync("/api/profile/completeness");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var report = await res.Content.ReadFromJsonAsync<CompletenessReport>();
        report.Should().NotBeNull();
        report!.TotalChecked.Should().BeGreaterThan(0);
        report.CompletenessPercentage.Should().BeInRange(0m, 100m);
    }

    [Fact]
    public async Task Resume_UploadDownload_ShouldSucceedWithPdf()
    {
        // Construct a synthetic valid PDF (%PDF header)
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF");
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "test_resume.pdf");
        content.Add(new StringContent("Backend"), "resumeType");
        content.Add(new StringContent("My Primary Backend CV"), "label");
        content.Add(new StringContent("true"), "isDefault");

        var uploadRes = await _client.PostAsync("/api/resumes", content);
        uploadRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var resume = await uploadRes.Content.ReadFromJsonAsync<ResumeDto>();
        resume.Should().NotBeNull();
        resume!.FileName.Should().Be("test_resume.pdf");
        resume.IsDefault.Should().BeTrue();
        resume.Sha256.Should().NotBeNullOrEmpty();

        // Download and verify content matches original
        var downloadRes = await _client.GetAsync($"/api/resumes/{resume.ResumeId}/content");
        downloadRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var downloadedBytes = await downloadRes.Content.ReadAsByteArrayAsync();

        downloadedBytes.Should().Equal(pdfBytes);
    }

    [Fact]
    public async Task Resume_Upload_ShouldRejectInvalidContent()
    {
        var invalidBytes = Encoding.ASCII.GetBytes("This is not a real PDF or DOCX file content.");
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(invalidBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "fake.pdf");

        var res = await _client.PostAsync("/api/resumes", content);
        res.StatusCode.Should().Be(HttpStatusCode.InternalServerError); // throws InvalidOperationException caught by problem details
    }
}

