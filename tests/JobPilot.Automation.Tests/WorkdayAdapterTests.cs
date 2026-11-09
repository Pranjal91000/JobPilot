using JobPilot.Application.Abstractions;
using JobPilot.Application.Features.Mapping;
using JobPilot.AutomationWorker.Adapters;
using JobPilot.Domain.Enums;
using JobPilot.Domain.Policies;
using Microsoft.Playwright;

namespace JobPilot.Automation.Tests;

public class WorkdayAdapterTests : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private readonly WorkdayAdapter _adapter = new();
    private string _fixturesDir = string.Empty;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        _page = await _browser.NewPageAsync();

        // Locate fixtures/mock-workday
        var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "mock-workday"));
        if (Directory.Exists(candidate))
        {
            _fixturesDir = candidate;
        }
        else
        {
            var alt = Path.GetFullPath("fixtures/mock-workday");
            if (Directory.Exists(alt))
            {
                _fixturesDir = alt;
            }
            else
            {
                throw new DirectoryNotFoundException($"Could not locate fixtures/mock-workday from {AppContext.BaseDirectory}");
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_page != null) await _page.CloseAsync();
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }

    private string GetFixtureUri(string filename)
    {
        var filePath = Path.Combine(_fixturesDir, filename).Replace('\\', '/');
        return $"file:///{filePath}";
    }

    [Fact]
    public async Task Step1_Auth_DetectsSignInRequired()
    {
        var uri = GetFixtureUri("step1_auth.html");
        await _page!.GotoAsync(uri);

        var authState = await _adapter.DetectAuthStateAsync(_page);
        Assert.Equal(AuthState.SignInRequired, authState);
    }

    [Fact]
    public async Task Step2_Contact_DetectsAllRequiredFieldsWithHighAccuracy()
    {
        var uri = GetFixtureUri("step2_contact.html");
        await _page!.GotoAsync(uri);

        var authState = await _adapter.DetectAuthStateAsync(_page);
        Assert.Equal(AuthState.InApplyFlow, authState);

        var stepKey = await _adapter.DetectCurrentStepKeyAsync(_page);
        Assert.Equal("My Information", stepKey);

        var fields = await _adapter.InspectFieldsAsync(_page, stepKey);

        // Expect all 9 contact fields to be detected
        Assert.True(fields.Count >= 9, $"Expected at least 9 fields, found {fields.Count}");

        var firstNameField = fields.FirstOrDefault(f => f.FieldIdentifier == "legalNameSection_firstName");
        Assert.NotNull(firstNameField);
        Assert.Equal(FieldType.Text, firstNameField.Type);
        Assert.True(firstNameField.Required);
        Assert.Contains("First Name", firstNameField.Label);

        var stateField = fields.FirstOrDefault(f => f.FieldIdentifier == "addressSection_countryRegion");
        Assert.NotNull(stateField);
        Assert.Equal(FieldType.SingleSelect, stateField.Type);
        Assert.True(stateField.Required);
        Assert.NotNull(stateField.AvailableOptions);

        var emailField = fields.FirstOrDefault(f => f.FieldIdentifier == "contactSection_email");
        Assert.NotNull(emailField);
        Assert.Equal(FieldType.Text, emailField.Type);
        Assert.True(emailField.Required);
    }

    [Fact]
    public async Task Step2_Contact_AutofillAndReadBackVerification_Succeeds()
    {
        var uri = GetFixtureUri("step2_contact.html");
        await _page!.GotoAsync(uri);

        var stepKey = await _adapter.DetectCurrentStepKeyAsync(_page);
        var fields = await _adapter.InspectFieldsAsync(_page, stepKey);

        var firstNameField = fields.First(f => f.FieldIdentifier == "legalNameSection_firstName");
        var fillResult = await _adapter.FillFieldAsync(_page, firstNameField, "Ada");

        Assert.True(fillResult.Success);
        Assert.Equal("Ada", fillResult.ReadBackValue);

        var stateField = fields.First(f => f.FieldIdentifier == "addressSection_countryRegion");
        var stateResult = await _adapter.FillFieldAsync(_page, stateField, "Washington", optionKey: "WA");

        Assert.True(stateResult.Success);
        Assert.Equal("WA", stateResult.ReadBackValue);
    }

    [Fact]
    public async Task Step3_Experience_CheckboxAndDateFilling_Succeeds()
    {
        var uri = GetFixtureUri("step3_experience.html");
        await _page!.GotoAsync(uri);

        var stepKey = await _adapter.DetectCurrentStepKeyAsync(_page);
        var fields = await _adapter.InspectFieldsAsync(_page, stepKey);

        var checkField = fields.FirstOrDefault(f => f.FieldIdentifier == "currentlyWorkHere");
        Assert.NotNull(checkField);
        Assert.Equal(FieldType.Checkbox, checkField.Type);

        var fillResult = await _adapter.FillFieldAsync(_page, checkField, "true");
        Assert.True(fillResult.Success);
        Assert.Equal("True", fillResult.ReadBackValue);

        var resumeField = fields.FirstOrDefault(f => f.Type == FieldType.FileUpload);
        Assert.NotNull(resumeField);
    }

    [Fact]
    public async Task Step4_Questions_RadioAndDropdownFilling_Succeeds()
    {
        var uri = GetFixtureUri("step4_questions.html");
        await _page!.GotoAsync(uri);

        var stepKey = await _adapter.DetectCurrentStepKeyAsync(_page);
        var fields = await _adapter.InspectFieldsAsync(_page, stepKey);

        var noticeField = fields.First(f => f.FieldIdentifier == "noticePeriod");
        var noticeResult = await _adapter.FillFieldAsync(_page, noticeField, "Immediate", optionKey: "Immediate");
        Assert.True(noticeResult.Success);
        Assert.Equal("Immediate", noticeResult.ReadBackValue);

        var relocateRadio = fields.First(f => f.FieldIdentifier == "relocation_yes");
        var radioResult = await _adapter.FillFieldAsync(_page, relocateRadio, "Yes");
        Assert.True(radioResult.Success);
    }

    [Fact]
    public async Task Step5_Disclosures_AllQuestionsFlaggedAsSensitive()
    {
        var uri = GetFixtureUri("step5_disclosures.html");
        await _page!.GotoAsync(uri);

        var stepKey = await _adapter.DetectCurrentStepKeyAsync(_page);
        var fields = await _adapter.InspectFieldsAsync(_page, stepKey);

        foreach (var field in fields)
        {
            var result = SensitiveQuestionPolicy.Evaluate(field.Label, null, field.Section ?? "Voluntary Disclosures");
            Assert.True(result.IsSensitive, $"Field '{field.Label}' in section '{field.Section}' should be flagged as sensitive.");
        }
    }

    [Fact]
    public async Task Step6_Review_ProhibitsAutomatingFinalSubmitButton()
    {
        var uri = GetFixtureUri("step6_review.html");
        await _page!.GotoAsync(uri);

        var stepKey = await _adapter.DetectCurrentStepKeyAsync(_page);
        var fields = await _adapter.InspectFieldsAsync(_page, stepKey);

        // Verification: bottom-navigation-submit-button must NEVER be included in inspected fields
        Assert.DoesNotContain(fields, f => f.FieldIdentifier == "bottom-navigation-submit-button");

        // Verification: Calling FillFieldAsync with submit button directly throws InvalidOperationException
        var maliciousField = new FieldDescriptor(
            Guid.NewGuid(),
            "fp_submit",
            "bottom-navigation-submit-button",
            "Submit Application",
            FieldType.Text,
            false,
            "Review",
            stepKey,
            null,
            null,
            null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _adapter.FillFieldAsync(_page, maliciousField, "click"));
    }

    [Fact]
    public async Task Step7_Submitted_DetectsSubmissionConfirmationAndReference()
    {
        var uri = GetFixtureUri("step7_submitted.html");
        await _page!.GotoAsync(uri);

        var isConfirmed = await _adapter.DetectSubmissionConfirmationAsync(_page);
        Assert.True(isConfirmed);

        var reference = await _adapter.ExtractSubmissionReferenceAsync(_page);
        Assert.Equal("APP-2026-WD-889104", reference);
    }
}
