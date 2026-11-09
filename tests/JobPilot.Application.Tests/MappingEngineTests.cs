using System.Text.Json;
using FluentAssertions;
using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Mapping;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Domain.Policies;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobPilot.Application.Tests;

public class MappingEngineTests
{
    private readonly VerifiedProfileSnapshot _sampleProfile;

    public MappingEngineTests()
    {
        var facts = new Dictionary<string, object?>
        {
            ["Personal.FirstName"] = "Jane",
            ["Personal.LastName"] = "Doe",
            ["Contact.Email"] = "jane.doe@example.com",
            ["Contact.PhoneNational"] = "+15551234567",
            ["Address.Line1"] = "123 Tech Boulevard",
            ["Address.City"] = "Austin",
            ["Address.Country"] = "US",
            ["Address.PostalCode"] = "78701",
            ["Employment.TotalExperienceYears"] = "7.5",
            ["Employment.Current.CompanyName"] = "Acme Corp",
            ["Preferences.NoticePeriodDays"] = 30,
            ["Links.LinkedIn"] = "https://linkedin.com/in/janedoe"
        };

        _sampleProfile = new VerifiedProfileSnapshot(
            UserId: Guid.NewGuid(),
            FullName: "Jane Doe",
            Email: "jane.doe@example.com",
            Phone: "+15551234567",
            VerifiedFacts: facts);
    }

    [Fact]
    public void AdapterRuleMapper_KnownAutomationId_ShouldMapToVerifiedFact()
    {
        var mapper = new AdapterRuleMapper();
        var field = new FieldDescriptor(
            FieldId: Guid.NewGuid(),
            Fingerprint: "fp1",
            FieldIdentifier: "legalNameSection_firstName",
            Label: "First Name",
            Type: FieldType.Text,
            Required: true,
            Section: "Contact Information",
            StepKey: "Contact",
            PageValue: null,
            AvailableOptions: null,
            LocatorRecipe: null);

        var context = new MappingContext("workday", "acme", "Contact", "Contact Information", false);
        var result = mapper.TryMap(field, _sampleProfile, context);

        result.Should().NotBeNull();
        result!.SuggestedValue.Should().Be("Jane");
        result.ProfilePath.Should().Be("Personal.FirstName");
        result.ConfidenceScore.Should().Be(0.99m);
        result.Method.Should().Be(MappingMethod.AdapterRule);
        result.RequiresIntervention.Should().BeFalse();
    }

    [Fact]
    public void DictionaryMapper_Synonyms_ShouldMapNormalizedLabels()
    {
        var mapper = new DictionaryMapper();
        var field = new FieldDescriptor(
            FieldId: Guid.NewGuid(),
            Fingerprint: "fp2",
            FieldIdentifier: null,
            Label: "Electronic Mail Address *",
            Type: FieldType.Text,
            Required: true,
            Section: "Contact",
            StepKey: "Contact",
            PageValue: null,
            AvailableOptions: null,
            LocatorRecipe: null);

        // Test "Email Address" synonym
        var fieldEmail = field with { Label = "Email Address" };
        var context = new MappingContext("workday", "acme", "Contact", "Contact", false);
        var result = mapper.TryMap(fieldEmail, _sampleProfile, context);

        result.Should().NotBeNull();
        result!.SuggestedValue.Should().Be("jane.doe@example.com");
        result.ProfilePath.Should().Be("Contact.Email");
        result.Method.Should().Be(MappingMethod.Dictionary);
    }

    [Fact]
    public void GroundingValidator_VerifiedFact_ShouldSucceed()
    {
        var validator = new GroundingValidator();
        var field = new FieldDescriptor(
            FieldId: Guid.NewGuid(),
            Fingerprint: "fp3",
            FieldIdentifier: null,
            Label: "Years of Experience",
            Type: FieldType.Text,
            Required: true,
            Section: "Experience",
            StepKey: null,
            PageValue: null,
            AvailableOptions: null,
            LocatorRecipe: null);

        var (isGrounded, value, reason) = validator.Validate(
            "Employment.TotalExperienceYears", "None", null, field, _sampleProfile);

        isGrounded.Should().BeTrue();
        value.Should().Be("7.5");
    }

    [Fact]
    public void GroundingValidator_UnverifiedOrFabricatedFact_ShouldFail()
    {
        var validator = new GroundingValidator();
        var field = new FieldDescriptor(
            FieldId: Guid.NewGuid(),
            Fingerprint: "fp4",
            FieldIdentifier: null,
            Label: "Security Clearance",
            Type: FieldType.Text,
            Required: false,
            Section: "Security",
            StepKey: null,
            PageValue: null,
            AvailableOptions: null,
            LocatorRecipe: null);

        // Path that does NOT exist in verified profile
        var (isGrounded, value, reason) = validator.Validate(
            "Security.TopSecretClearance", "None", null, field, _sampleProfile);

        isGrounded.Should().BeFalse();
        value.Should().BeNull();
        reason.Should().Contain("does not exist or is not marked Verified");
    }

    [Fact]
    public void GroundingValidator_ChoiceField_NumericRangeBucketMatching()
    {
        var validator = new GroundingValidator();
        var options = JsonDocument.Parse(@"[
            {""key"":""0-2"",""label"":""0 - 2 years""},
            {""key"":""3-5"",""label"":""3 - 5 years""},
            {""key"":""6-10"",""label"":""6 - 10 years""},
            {""key"":""10+"",""label"":""10+ years""}
        ]");

        var field = new FieldDescriptor(
            FieldId: Guid.NewGuid(),
            Fingerprint: "fp5",
            FieldIdentifier: null,
            Label: "Experience Range",
            Type: FieldType.SingleSelect,
            Required: true,
            Section: "Experience",
            StepKey: null,
            PageValue: null,
            AvailableOptions: options,
            LocatorRecipe: null);

        // 7.5 years should fall into "6-10" bucket deterministically
        var (isGrounded, value, reason) = validator.Validate(
            "Employment.TotalExperienceYears", "NumericRangeBucket", null, field, _sampleProfile);

        isGrounded.Should().BeTrue();
        value.Should().Be("6-10");
    }

    [Fact]
    public void SensitiveQuestionPolicy_DisabilityAndVeteran_ShouldBeFlaggedSensitive()
    {
        var disability = SensitiveQuestionPolicy.Evaluate("Do you have a disability?", null, "Voluntary Disclosures");
        disability.IsSensitive.Should().BeTrue();
        disability.Category.Should().Be(SensitiveCategory.Disability);

        var veteran = SensitiveQuestionPolicy.Evaluate("Veteran Status", null, "Self Identify");
        veteran.IsSensitive.Should().BeTrue();
        veteran.Category.Should().Be(SensitiveCategory.Veteran);

        var attestation = SensitiveQuestionPolicy.Evaluate("I certify that all information is true", null, "Sign");
        attestation.IsSensitive.Should().BeTrue();
        attestation.Category.Should().Be(SensitiveCategory.Attestation);
    }

    [Fact]
    public async Task FieldMappingService_AiDisabledMode_ResolvesDeterministicAndBlocksSensitive()
    {
        var adapterMapper = new AdapterRuleMapper();
        var dictMapper = new DictionaryMapper();
        var savedMatcher = new SavedAnswerMatcher();
        var grounding = new GroundingValidator();
        var confidence = new ConfidenceCalculator();
        var dummyAi = new MockAiProvider();

        var service = new FieldMappingService(
            adapterMapper, dictMapper, savedMatcher, grounding, confidence, dummyAi, NullLogger<FieldMappingService>.Instance);

        var fields = new List<FieldDescriptor>
        {
            new(Guid.NewGuid(), "fp1", "legalNameSection_firstName", "First Name", FieldType.Text, true, "Contact", null, null, null, null),
            new(Guid.NewGuid(), "fp2", null, "Years of Experience", FieldType.Text, true, "Experience", null, null, null, null),
            new(Guid.NewGuid(), "fp3", "disability", "Voluntary Self-Identification of Disability", FieldType.SingleSelect, false, "Voluntary Disclosures", null, null, null, null)
        };

        var context = new MappingContext("workday", "acme", null, null, AiEnabled: false);
        var results = await service.MapFieldsAsync(fields, _sampleProfile, new List<SavedAnswer>(), context, CancellationToken.None);

        results.Should().HaveCount(3);

        // First Name: mapped via adapter rule
        var firstNameResult = results.First(r => r.FieldId == fields[0].FieldId);
        firstNameResult.SuggestedValue.Should().Be("Jane");
        firstNameResult.Method.Should().Be(MappingMethod.AdapterRule);
        firstNameResult.RequiresIntervention.Should().BeFalse();

        // Years of Experience: mapped via dictionary
        var expResult = results.First(r => r.FieldId == fields[1].FieldId);
        expResult.SuggestedValue.Should().Be("7.5");
        expResult.Method.Should().Be(MappingMethod.Dictionary);

        // Disability: strictly blocked by SensitiveQuestionPolicy
        var disabilityResult = results.First(r => r.FieldId == fields[2].FieldId);
        disabilityResult.IsSensitive.Should().BeTrue();
        disabilityResult.RequiresIntervention.Should().BeTrue();
        disabilityResult.SuggestedValue.Should().BeNull();
        disabilityResult.Reason.Should().Contain("Safety policy blocks automatic fill");
    }

    private sealed class MockAiProvider : IAiProvider
    {
        public string Name => "Mock";
        public AiCapabilities Capabilities => new(true, 1000, true);
        public Task<AiStructuredResponse> CompleteStructuredAsync(AiStructuredRequest request, CancellationToken ct)
        {
            throw new InvalidOperationException("AI should not be invoked when AI is disabled.");
        }
    }
}
