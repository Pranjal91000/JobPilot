using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Enums;
using Microsoft.Playwright;

namespace JobPilot.AutomationWorker.Adapters;

public sealed record DetectedOption(string Key, string Label);

public sealed record FillResult(
    bool Success,
    string? ReadBackValue,
    string? Error);

/// <summary>
/// Drives one ATS's application pages. Workday is a multi-step flow with candidate accounts;
/// Greenhouse, Lever and Ashby are single-page forms (<see cref="IsSinglePage"/>).
/// </summary>
public interface IApplyFormAdapter
{
    string AtsKey { get; }
    /// <summary>The whole application is one form: fill it and stop for review (no Next steps).</summary>
    bool IsSinglePage { get; }

    Task<AuthState> DetectAuthStateAsync(IPage page, CancellationToken ct = default);
    Task<string?> DetectCurrentStepKeyAsync(IPage page, CancellationToken ct = default);
    Task<IReadOnlyList<FieldDescriptor>> InspectFieldsAsync(IPage page, string? stepKey, CancellationToken ct = default);
    Task<FillResult> FillFieldAsync(IPage page, FieldDescriptor field, string targetValue, string? optionKey = null, CancellationToken ct = default);
    Task<bool> NavigateNextAsync(IPage page, CancellationToken ct = default);
    Task<bool> NavigateBackAsync(IPage page, CancellationToken ct = default);
    Task<IReadOnlyList<string>> DetectValidationErrorsAsync(IPage page, CancellationToken ct = default);
    Task<bool> DetectSubmissionConfirmationAsync(IPage page, CancellationToken ct = default);
    Task<string?> ExtractSubmissionReferenceAsync(IPage page, CancellationToken ct = default);
    Task<JobDetails> ReadJobDetailsAsync(IPage page);
    Task<string?> StartApplicationAsync(IPage page, CancellationToken ct = default);
    Task<AuthOutcome> SignInOrCreateAccountAsync(IPage page, string email, string password, bool accountKnownToExist, CancellationToken ct = default);
    Task<bool> IsReviewStepAsync(IPage page, string? stepKey);
    Task<IReadOnlyList<string>> ReadDropdownOptionsAsync(IPage page, FieldDescriptor field);
    Task<int> AddRepeatEntriesAsync(IPage page, IReadOnlyList<(string Section, int Wanted)> targets);
}

public interface IWorkdayAdapter : IApplyFormAdapter;

public sealed partial class WorkdayAdapter : IWorkdayAdapter
{
    public string AtsKey => "workday";
    public bool IsSinglePage => false;

    // Workday navigation and submit selector constants
    private const string NextButtonSelector = "[data-automation-id='bottom-navigation-next-button'], [data-automation-id='pageFooterNextButton'], button:has-text('Save and Continue'), button:has-text('Next')";
    private const string BackButtonSelector = "[data-automation-id='bottom-navigation-back-button'], [data-automation-id='pageFooterBackButton'], a:has-text('Back'), button:has-text('Back')";
    public const string FinalSubmitSelector = "[data-automation-id='bottom-navigation-submit-button']";

    public async Task<AuthState> DetectAuthStateAsync(IPage page, CancellationToken ct = default)
    {
        var isAuth = await page.EvaluateAsync<bool>(@"() => {
            const hasPassword = !!document.querySelector('input[type=""password""]');
            const hasSignIn = !!document.querySelector('[data-automation-id=""signInPage""], [data-automation-id=""createAccountCheckbox""], form#authForm, #input-password');
            return hasPassword || hasSignIn;
        }");

        if (isAuth)
        {
            return AuthState.SignInRequired;
        }

        var inApplyFlow = await page.EvaluateAsync<bool>(@"() => {
            return !!document.querySelector('[data-automation-id=""applicationFlow""], [data-automation-id=""step-header""], [data-automation-id=""bottom-navigation-next-button""], [data-automation-id=""pageFooterNextButton""], [data-automation-id=""progressBarActiveStep""]');
        }");

        if (inApplyFlow)
        {
            return AuthState.InApplyFlow;
        }

        return AuthState.Unknown;
    }

    public async Task<string?> DetectCurrentStepKeyAsync(IPage page, CancellationToken ct = default)
    {
        try
        {
            var stepText = await page.EvaluateAsync<string?>(@"() => {
                // 1. Check active step in progress bar
                const activeStep = document.querySelector('[data-automation-id=""progressBarActiveStep""], [aria-current=""step""]');
                if (activeStep && activeStep.innerText) {
                    const lines = activeStep.innerText.split('\n').map(l => l.trim()).filter(l => l.length > 0);
                    // Usually lines are ['step 1 of 5', 'My Information'] -> return the last line
                    const label = lines[lines.length - 1].replace(/^(?:current\s+)?step\s+\d+\s+of\s+\d+\s*/i, '').trim();
                    if (label) return label;
                }

                // 2. Step header
                const header = document.querySelector('[data-automation-id=""step-header""], [data-automation-id=""selectedStep""], [data-automation-id=""currentStep""], [data-automation-id=""pageHeader""]');
                if (header && header.innerText && header.innerText.trim()) {
                    return header.innerText.trim();
                }

                // 3. Look at h3 or h2 excluding jobTitleHeading
                const h3s = Array.from(document.querySelectorAll('h3, h2:not([data-automation-id=""jobTitleHeading""])'));
                for (const h of h3s) {
                    const txt = (h.innerText || '').trim();
                    if (txt && !txt.includes('.NET') && !txt.includes('Developer') && txt.length < 50) {
                        return txt;
                    }
                }

                return null;
            }");

            if (!string.IsNullOrWhiteSpace(stepText))
                return stepText.Trim();
        }
        catch
        {
            // fallback
        }

        var title = await page.TitleAsync();
        return !string.IsNullOrWhiteSpace(title) ? title.Trim() : null;
    }

    public async Task<IReadOnlyList<FieldDescriptor>> InspectFieldsAsync(
        IPage page,
        string? stepKey,
        CancellationToken ct = default)
    {
        try
        {
            // Wait for form inputs or active step to attach and mount
            await page.WaitForSelectorAsync("input, select, textarea, div[role='combobox'], [data-automation-id='progressBarActiveStep'], [data-automation-id='pageFooterNextButton']", new PageWaitForSelectorOptions
            {
                Timeout = 5000,
                State = WaitForSelectorState.Attached
            });
        }
        catch
        {
            // If timeout, proceed immediately with whatever is present in DOM
        }

        var json = await page.EvaluateAsync<string>(InspectScript) ?? "[]";
        return BuildDescriptors(json, stepKey);
    }

    /// <summary>Turns the JSON produced by an inspection script into field descriptors.</summary>
    internal static IReadOnlyList<FieldDescriptor> BuildDescriptors(string json, string? stepKey)
    {
        var rawElements = JsonSerializer.Deserialize<List<RawElementDto>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        var descriptors = new List<FieldDescriptor>();
        var seenFingerprints = new HashSet<string>();

        foreach (var raw in rawElements)
        {
            var fieldType = !string.IsNullOrWhiteSpace(raw.FieldType) && Enum.TryParse<FieldType>(raw.FieldType, out var ft)
                ? ft
                : DetermineFieldType(raw.TagName, raw.Type);

            JsonDocument? optionsDoc = null;
            if (raw.Options != null && raw.Options.Count > 0)
            {
                optionsDoc = JsonSerializer.SerializeToDocument(raw.Options);
            }

            var recipeObj = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(raw.AutoId)) recipeObj["automationId"] = raw.AutoId;
            if (!string.IsNullOrWhiteSpace(raw.Id)) recipeObj["id"] = raw.Id;
            if (!string.IsNullOrWhiteSpace(raw.Name)) recipeObj["name"] = raw.Name;
            recipeObj["tagName"] = raw.TagName;
            if (!string.IsNullOrWhiteSpace(raw.Type)) recipeObj["type"] = raw.Type;
            if (!string.IsNullOrWhiteSpace(raw.Kind)) recipeObj["kind"] = raw.Kind;
            if (!string.IsNullOrWhiteSpace(raw.Container)) recipeObj["container"] = raw.Container;

            var locatorRecipe = JsonSerializer.SerializeToDocument(recipeObj);
            var identifier = raw.Ident ?? raw.AutoId ?? raw.Id ?? raw.Name ?? raw.Label;

            // Workday widgets are keyed by their unique control id, so the section is left out of the
            // fingerprint. That keeps fingerprints identical to the ones stored by the earlier inspector
            // (whose section was always empty on Workday pages) and so keeps existing approvals attached.
            var isWorkdayWidget = !string.IsNullOrWhiteSpace(raw.Kind) && raw.Kind != "generic";
            var fingerprintSection = isWorkdayWidget ? null : raw.Section;
            var baseFingerprint = ComputeFingerprint(stepKey, fingerprintSection, identifier, fieldType);
            var fingerprint = baseFingerprint;
            var disambiguator = 1;
            while (!seenFingerprints.Add(fingerprint))
            {
                disambiguator++;
                fingerprint = ComputeFingerprint(stepKey, fingerprintSection, $"{identifier}#{disambiguator}", fieldType);
            }

            descriptors.Add(new FieldDescriptor(
                FieldId: Guid.Empty,
                Fingerprint: fingerprint,
                FieldIdentifier: disambiguator > 1 ? $"{identifier}#{disambiguator}" : identifier,
                Label: raw.Label,
                Type: fieldType,
                Required: raw.IsRequired,
                Section: raw.Section,
                StepKey: stepKey,
                PageValue: raw.PageValue,
                AvailableOptions: optionsDoc,
                LocatorRecipe: locatorRecipe
            ));
        }

        return descriptors;
    }

    private sealed class RawElementDto
    {
        public string TagName { get; set; } = "";
        public string Type { get; set; } = "";
        public string? AutoId { get; set; }
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Ident { get; set; }
        public string Label { get; set; } = "";
        public bool IsRequired { get; set; }
        public string? Section { get; set; }
        public string? PageValue { get; set; }
        public List<OptionDto>? Options { get; set; }
        public string? Kind { get; set; }
        public string? FieldType { get; set; }
        public string? Container { get; set; }
    }

    private sealed class OptionDto
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
    }

    public async Task<FillResult> FillFieldAsync(
        IPage page,
        FieldDescriptor field,
        string targetValue,
        string? optionKey = null,
        CancellationToken ct = default)
    {
        // SAFETY INVARIANT: Technical barrier prohibiting submission automation
        if (field.FieldIdentifier == "bottom-navigation-submit-button" ||
            field.Label.Equals("Submit", StringComparison.OrdinalIgnoreCase) ||
            field.Label.Equals("Submit Application", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("JobPilot is technically prohibited from automating the final Workday Submit control.");
        }

        // Sanitize phone numbers: Workday national phone inputs reject country code prefixes
        if ((field.FieldIdentifier.Contains("phone", StringComparison.OrdinalIgnoreCase) ||
             field.Label.Contains("Phone", StringComparison.OrdinalIgnoreCase)) &&
            !field.FieldIdentifier.Contains("countryPhoneCode", StringComparison.OrdinalIgnoreCase))
        {
            if (targetValue.StartsWith("+91")) targetValue = targetValue[3..];
            else if (targetValue.StartsWith("+1")) targetValue = targetValue[2..];
            else targetValue = System.Text.RegularExpressions.Regex.Replace(targetValue, @"^\+\d{1,3}", "");
            targetValue = targetValue.TrimStart('0').Trim();
        }

        ILocator locator = ResolveLocator(page, field);
        if (await locator.CountAsync() == 0)
        {
            return new FillResult(false, null, $"Element for field '{field.Label}' could not be located on the current page.");
        }

        // Workday widgets detected by the formField-aware inspector get dedicated handling;
        // file uploads and rows recorded by the old inspector use the generic path below.
        var kind = GetRecipeValue(field, "kind");
        if (kind is "prompt" or "dropdown" or "radio" or "date" or "checkboxGroup" or "checkbox" or "text")
        {
            try
            {
                return await FillWorkdayWidgetAsync(page, field, kind, locator, targetValue, optionKey, ct);
            }
            catch (Exception ex)
            {
                return new FillResult(false, null, $"Fill error: {ex.Message}");
            }
        }

        try
        {
            switch (field.Type)
            {
                case FieldType.Text:
                case FieldType.Date:
                    await locator.FillAsync(targetValue);
                    break;

                case FieldType.Checkbox:
                    var shouldCheck = bool.TryParse(targetValue, out var b) && b;
                    await locator.SetCheckedAsync(shouldCheck);
                    break;

                case FieldType.Radio:
                    // Click the radio button
                    await locator.CheckAsync();
                    break;

                case FieldType.SingleSelect:
                    var tagName = (await locator.EvaluateAsync<string>("el => (el.tagName || '').toLowerCase()")) ?? "";
                    if (tagName == "select")
                    {
                        var selectVal = !string.IsNullOrWhiteSpace(optionKey) ? optionKey : targetValue;
                        try
                        {
                            await locator.SelectOptionAsync(new SelectOptionValue { Value = selectVal });
                        }
                        catch
                        {
                            await locator.SelectOptionAsync(new SelectOptionValue { Label = targetValue });
                        }
                    }
                    else if (tagName == "input")
                    {
                        await locator.FillAsync(targetValue);
                        await page.Keyboard.PressAsync("Enter");
                    }
                    else
                    {
                        await locator.ClickAsync();
                        await page.WaitForTimeoutAsync(300);
                        var optionLocator = page.Locator($"[role='option']:has-text('{targetValue}'), [data-automation-label='{targetValue}']").First;
                        if (await optionLocator.CountAsync() > 0)
                        {
                            await optionLocator.ClickAsync();
                        }
                        else
                        {
                            await page.Keyboard.TypeAsync(targetValue);
                            await page.Keyboard.PressAsync("Enter");
                        }
                    }
                    break;

                case FieldType.FileUpload:
                    if (File.Exists(targetValue))
                    {
                        var isFileInput = await locator.EvaluateAsync<bool>("el => (el.tagName || '').toLowerCase() === 'input' && (el.type || '').toLowerCase() === 'file'");
                        var fileInput = isFileInput ? locator : locator.Locator("input[type='file']").First;
                        if (await fileInput.CountAsync() == 0)
                        {
                            fileInput = page.Locator("input[type='file']").First;
                        }
                        await fileInput.SetInputFilesAsync(targetValue);
                        await page.WaitForTimeoutAsync(1000);
                    }
                    else
                    {
                        return new FillResult(false, null, $"Resume file at '{targetValue}' does not exist on disk.");
                    }
                    break;

                default:
                    await locator.FillAsync(targetValue);
                    break;
            }

            // Verify-after-write: read back value from DOM safely without timeout
            string? readBackValue = null;
            try
            {
                if (field.Type == FieldType.Checkbox)
                {
                    readBackValue = (await locator.IsCheckedAsync()).ToString();
                }
                else if (field.Type == FieldType.Radio)
                {
                    readBackValue = (await locator.IsCheckedAsync()) ? targetValue : "";
                }
                else if (field.Type == FieldType.FileUpload)
                {
                    readBackValue = Path.GetFileName(targetValue);
                }
                else
                {
                    readBackValue = await locator.EvaluateAsync<string>("el => ('value' in el) ? (el.value || '') : (el.innerText || '')");
                }
            }
            catch
            {
                readBackValue = targetValue;
            }

            return new FillResult(true, readBackValue, null);
        }
        catch (Exception ex)
        {
            return new FillResult(false, null, $"Fill error: {ex.Message}");
        }
    }

    public async Task<bool> NavigateNextAsync(IPage page, CancellationToken ct = default)
    {
        var nextBtn = page.Locator(NextButtonSelector);
        if (await nextBtn.CountAsync() > 0)
        {
            await nextBtn.First.ClickAsync();
            try
            {
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = 10000 });
            }
            catch
            {
                // SPA navigation might not trigger traditional DOMContentLoaded
            }

            // Allow Workday React router to mount next step components
            await Task.Delay(2000, ct);
            return true;
        }
        return false;
    }

    public async Task<bool> NavigateBackAsync(IPage page, CancellationToken ct = default)
    {
        var backBtn = page.Locator(BackButtonSelector);
        if (await backBtn.CountAsync() > 0)
        {
            await backBtn.First.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            return true;
        }
        return false;
    }

    public async Task<IReadOnlyList<string>> DetectValidationErrorsAsync(IPage page, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var errorLocators = page.Locator("[data-automation-id='errorMessage'], .alert-error, [role='alert']");
        var count = await errorLocators.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var text = await errorLocators.Nth(i).InnerTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                errors.Add(text.Trim());
            }
        }
        return errors;
    }

    public async Task<bool> DetectSubmissionConfirmationAsync(IPage page, CancellationToken ct = default)
    {
        var hasConfirmationContainer = await page.Locator("[data-automation-id='applicationSubmitted'], [data-automation-id='confirmationTitle']").CountAsync() > 0;
        var url = page.Url.ToLowerInvariant();
        var hasUrlIndicator = url.Contains("submitted") || url.Contains("confirmation");

        return hasConfirmationContainer || hasUrlIndicator;
    }

    public async Task<string?> ExtractSubmissionReferenceAsync(IPage page, CancellationToken ct = default)
    {
        var refLocator = page.Locator("[data-automation-id='submissionReferenceId']");
        if (await refLocator.CountAsync() > 0)
        {
            var text = await refLocator.First.InnerTextAsync();
            return text.Trim();
        }

        // Try regex match on page body
        var bodyText = await page.InnerTextAsync("body");
        var match = System.Text.RegularExpressions.Regex.Match(bodyText, @"APP-\d{4}-[\w-]+");
        if (match.Success)
        {
            return match.Value;
        }

        return null;
    }

    private static ILocator ResolveLocator(IPage page, FieldDescriptor field)
    {
        if (field.LocatorRecipe != null)
        {
            var root = field.LocatorRecipe.RootElement;
            if (root.TryGetProperty("automationId", out var autoIdProp) && !string.IsNullOrWhiteSpace(autoIdProp.GetString()))
            {
                return page.Locator($"[data-automation-id='{autoIdProp.GetString()}']").First;
            }
            if (root.TryGetProperty("id", out var idProp) && !string.IsNullOrWhiteSpace(idProp.GetString()))
            {
                // Attribute form: Workday ids can start with a digit, which "#id" can't express.
                return page.Locator($"[id='{CssAttr(idProp.GetString()!)}']").First;
            }
            if (root.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString()))
            {
                return page.Locator($"[name='{nameProp.GetString()}']").First;
            }
        }

        if (!string.IsNullOrWhiteSpace(field.FieldIdentifier))
        {
            return page.Locator($"[data-automation-id='{field.FieldIdentifier}'], #{field.FieldIdentifier}, [name='{field.FieldIdentifier}']").First;
        }

        if (field.Type == FieldType.FileUpload)
        {
            return page.Locator("input[type='file']").First;
        }

        return page.GetByLabel(field.Label).First;
    }

    private static async Task<string> ExtractLabelAsync(IPage page, IElementHandle handle, string? id, string? name)
    {
        return await handle.EvaluateAsync<string>(@"el => {
            // 1. Label by for attribute using CSS.escape safely
            if (el.id) {
                try {
                    const l = document.querySelector(`label[for=""${CSS.escape(el.id)}""]`);
                    if (l && l.innerText && l.innerText.trim()) return l.innerText.trim();
                } catch {}
            }

            // 2. aria-label
            const ariaLabel = el.getAttribute('aria-label');
            if (ariaLabel && ariaLabel.trim()) return ariaLabel.trim();

            // 3. Parent label
            const parentLabel = el.closest('label');
            if (parentLabel && parentLabel.innerText && parentLabel.innerText.trim()) return parentLabel.innerText.trim();

            // 4. Preceding legend or label element
            const fg = el.closest('.form-group, [data-automation-id*=""formField""], [data-automation-id*=""field""]');
            if (fg) {
                const l = fg.querySelector('label, .radio-legend, legend, [data-automation-id*=""label""]');
                if (l && l.innerText && l.innerText.trim()) return l.innerText.trim();
            }

            // 5. File upload dropzone container label
            const drop = el.closest('[data-automation-id*=""dropZone""], [data-automation-id*=""upload""], [data-automation-id*=""file""], [data-automation-id*=""attachment""], .dropzone, .file-upload');
            if (drop) {
                const header = drop.querySelector('h2, h3, h4, [data-automation-id*=""label""], .title, .file-upload-title');
                if (header && header.innerText && header.innerText.trim()) return header.innerText.trim();
                const lines = drop.innerText.split('\n').map(s => s.trim()).filter(s => s.length > 0);
                if (lines.length > 0) return lines[0];
            }

            return '';
        }") ?? "";
    }

    private static async Task<string?> ExtractSectionAsync(IElementHandle handle)
    {
        return await handle.EvaluateAsync<string?>(@"el => {
            const section = el.closest('[data-automation-id*=""Section""], .repeat-group, .review-section');
            if (section) {
                const autoId = section.getAttribute('data-automation-id');
                if (autoId) return autoId;
                const h = section.querySelector('.section-title, .section-header, h2, h3');
                if (h) return h.innerText.trim();
            }
            return null;
        }");
    }

    private static FieldType DetermineFieldType(string tagName, string type)
    {
        return type switch
        {
            "checkbox" => FieldType.Checkbox,
            "radio" => FieldType.Radio,
            "file" => FieldType.FileUpload,
            "date" => FieldType.Date,
            _ when tagName == "select" || tagName == "div" => FieldType.SingleSelect,
            _ => FieldType.Text
        };
    }

    private static string ComputeFingerprint(string? stepKey, string? section, string identifier, FieldType type)
    {
        var raw = $"{stepKey ?? ""}:{section ?? ""}:{identifier}:{type}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hashBytes).ToLowerInvariant()[..32];
    }
}
