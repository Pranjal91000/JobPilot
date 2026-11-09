using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobPilot.Api.Controllers;
using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Mapping;
using JobPilot.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobPilot.Api.Tests;

public class FieldMappingApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public FieldMappingApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AiSettings_GetAndUpdate_ShouldPersistCorrectly()
    {
        // 1. Get initial settings
        var getRes = await _client.GetAsync("/api/settings/ai");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await getRes.Content.ReadFromJsonAsync<AiSettingsDto>();
        settings.Should().NotBeNull();

        // 2. Update settings
        var updateReq = new UpdateAiSettingsRequest(
            ProviderName: "Google Gemini",
            DefaultModel: "gemini-1.5-flash",
            IsEnabled: true,
            ApiKey: "test-fake-api-key");

        var updateRes = await _client.PostAsJsonAsync("/api/settings/ai", updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateRes.Content.ReadFromJsonAsync<AiSettingsDto>();

        updated.Should().NotBeNull();
        updated!.ProviderName.Should().Be("Google Gemini");
        updated.DefaultModel.Should().Be("gemini-1.5-flash");
        updated.IsEnabled.Should().BeTrue();
        updated.HasApiKey.Should().BeTrue();
    }

    [Fact]
    public async Task FieldMapping_Workflow_SeedMapApproveAndBulkApprove()
    {
        // 1. Create an application via intake
        var intakeRes = await _client.PostAsJsonAsync("/api/applications/intake", new IntakeRequest(
            Url: $"https://acme.wd5.myworkdayjobs.com/AcmeCareers/job/FullStack_R-{Guid.NewGuid():N}",
            ResumeId: null));
        intakeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var intake = await intakeRes.Content.ReadFromJsonAsync<IntakeResult>();
        var appId = intake!.Application.ApplicationId;

        // 2. Ensure user has verified facts in profile so deterministic mappings succeed
        await _client.PutAsJsonAsync("/api/profile", new UpdateProfileRequest(
            FirstName: "Sarah",
            MiddleName: null,
            LastName: "Connor",
            PreferredName: null,
            Email: "sarah.connor@example.com",
            PhoneE164: "+15559876543",
            PhoneCountryCode: "+1",
            AddressLine1: "456 Cyberdyne Way",
            AddressLine2: null,
            City: "Los Angeles",
            StateRegion: "CA",
            PostalCode: "90001",
            CountryCode: "US",
            ProfessionalSummary: "Lead Engineer",
            CurrentLocation: "Los Angeles, CA",
            NoticePeriodDays: 30,
            ExpectedSalary: "160000",
            ExpectedSalaryCurrency: "USD"));

        // Confirm profile facts so they become Verified
        await _client.PostAsJsonAsync("/api/profile/confirm-facts", new[]
        {
            new FactRef("Profile", null, "Personal.FirstName"),
            new FactRef("Profile", null, "Personal.LastName"),
            new FactRef("Profile", null, "Contact.Email"),
            new FactRef("Profile", null, "Contact.PhoneNational"),
            new FactRef("Profile", null, "Address.Line1"),
            new FactRef("Profile", null, "Address.City"),
            new FactRef("Profile", null, "Address.Country"),
            new FactRef("Profile", null, "Preferences.NoticePeriodDays")
        });

        // 3. Get fields for application (auto-seeds Workday sample fields)
        var fieldsRes = await _client.GetAsync($"/api/applications/{appId}/fields");
        var fieldsBody = await fieldsRes.Content.ReadAsStringAsync();
        fieldsRes.StatusCode.Should().Be(HttpStatusCode.OK, fieldsBody);
        var fields = await fieldsRes.Content.ReadFromJsonAsync<List<FieldMappingController.FieldDetailDto>>();
        fields.Should().NotBeNull();
        fields!.Count.Should().BeGreaterThan(5);

        // 4. Trigger mapping pipeline
        var mapRes = await _client.PostAsync($"/api/applications/{appId}/fields/map", null);
        mapRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var mappedFields = await mapRes.Content.ReadFromJsonAsync<List<FieldMappingController.FieldDetailDto>>();

        mappedFields.Should().NotBeNull();
        var firstNameField = mappedFields!.First(f => f.FieldIdentifier == "legalNameSection_firstName");
        firstNameField.Mapping.Should().NotBeNull();
        firstNameField.Mapping!.SuggestedValue.Should().Be("Sarah");
        firstNameField.Mapping.Method.Should().Be(MappingMethod.AdapterRule);

        // Sensitive field verification (disability)
        var disabilityField = mappedFields.First(f => f.FieldIdentifier == "voluntary-disability");
        disabilityField.Mapping.Should().NotBeNull();
        disabilityField.Mapping!.IsSensitive.Should().BeTrue();
        disabilityField.Mapping.RequiresIntervention.Should().BeTrue();

        // 5. User Decision on a single field: Approve First Name
        var approveRes = await _client.PostAsJsonAsync(
            $"/api/applications/{appId}/fields/{firstNameField.FieldId}/decision",
            new FieldMappingController.FieldDecisionRequest("Approve", null, null));
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. User Decision: Override / SetValue on another field
        var emailField = mappedFields.First(f => f.FieldIdentifier == "contactSection_email");
        var overrideRes = await _client.PostAsJsonAsync(
            $"/api/applications/{appId}/fields/{emailField.FieldId}/decision",
            new FieldMappingController.FieldDecisionRequest("SetValue", "custom.override@example.com", null));
        overrideRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. Bulk Approve non-sensitive deterministic fields
        var bulkRes = await _client.PostAsync($"/api/applications/{appId}/fields/bulk-approve", null);
        bulkRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 8. Re-fetch and verify updated states
        var verifiedFieldsRes = await _client.GetAsync($"/api/applications/{appId}/fields");
        var finalFields = await verifiedFieldsRes.Content.ReadFromJsonAsync<List<FieldMappingController.FieldDetailDto>>();

        var finalFirst = finalFields!.First(f => f.FieldIdentifier == "legalNameSection_firstName");
        finalFirst.Mapping!.Status.Should().Be(MappingStatus.Approved);
        finalFirst.Answer.Should().NotBeNull();
        finalFirst.Answer!.IsUserApproved.Should().BeTrue();
        finalFirst.Answer.AnswerValue.Should().Be("Sarah");

        var finalEmail = finalFields.First(f => f.FieldIdentifier == "contactSection_email");
        finalEmail.Answer.Should().NotBeNull();
        finalEmail.Answer!.AnswerValue.Should().Be("custom.override@example.com");

        // Sensitive field must NOT have been auto-approved!
        var finalDisability = finalFields.First(f => f.FieldIdentifier == "voluntary-disability");
        finalDisability.Answer?.IsUserApproved.Should().NotBe(true);
    }
}
