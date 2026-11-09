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

public interface IWorkdayAdapter
{
    Task<AuthState> DetectAuthStateAsync(IPage page, CancellationToken ct = default);
    Task<string?> DetectCurrentStepKeyAsync(IPage page, CancellationToken ct = default);
    Task<IReadOnlyList<FieldDescriptor>> InspectFieldsAsync(IPage page, string? stepKey, CancellationToken ct = default);
    Task<FillResult> FillFieldAsync(IPage page, FieldDescriptor field, string targetValue, string? optionKey = null, CancellationToken ct = default);
    Task<bool> NavigateNextAsync(IPage page, CancellationToken ct = default);
    Task<bool> NavigateBackAsync(IPage page, CancellationToken ct = default);
    Task<IReadOnlyList<string>> DetectValidationErrorsAsync(IPage page, CancellationToken ct = default);
    Task<bool> DetectSubmissionConfirmationAsync(IPage page, CancellationToken ct = default);
    Task<string?> ExtractSubmissionReferenceAsync(IPage page, CancellationToken ct = default);
}

public sealed class WorkdayAdapter : IWorkdayAdapter
{
    // Workday navigation and submit selector constants
    private const string NextButtonSelector = "[data-automation-id='bottom-navigation-next-button'], button:has-text('Save and Continue'), button:has-text('Next')";
    private const string BackButtonSelector = "[data-automation-id='bottom-navigation-back-button'], a:has-text('Back')";
    public const string FinalSubmitSelector = "[data-automation-id='bottom-navigation-submit-button']";

    public async Task<AuthState> DetectAuthStateAsync(IPage page, CancellationToken ct = default)
    {
        // Check for sign-in or password inputs
        var hasPasswordInput = await page.Locator("input[type='password']").CountAsync() > 0;
        var hasSignInForm = await page.Locator("[data-automation-id='signInPage'], form#authForm, #input-password").CountAsync() > 0;

        if (hasPasswordInput || hasSignInForm)
        {
            return AuthState.SignInRequired;
        }

        var inApplyFlow = await page.Locator("[data-automation-id='applicationFlow'], [data-automation-id='step-header'], [data-automation-id='bottom-navigation-next-button']").CountAsync() > 0;
        if (inApplyFlow)
        {
            return AuthState.InApplyFlow;
        }

        return AuthState.Unknown;
    }

    public async Task<string?> DetectCurrentStepKeyAsync(IPage page, CancellationToken ct = default)
    {
        var stepHeaderLocator = page.Locator("[data-automation-id='step-header'], h1");
        if (await stepHeaderLocator.CountAsync() > 0)
        {
            var text = await stepHeaderLocator.First.InnerTextAsync();
            return text.Trim();
        }

        var title = await page.TitleAsync();
        return title;
    }

    public async Task<IReadOnlyList<FieldDescriptor>> InspectFieldsAsync(
        IPage page,
        string? stepKey,
        CancellationToken ct = default)
    {
        var descriptors = new List<FieldDescriptor>();

        // Query all candidate interactive elements
        var elementHandles = await page.Locator(
            "input:not([type='hidden']), select, textarea, div[role='combobox'], div[role='listbox']"
        ).ElementHandlesAsync();

        foreach (var handle in elementHandles)
        {
            var isVisible = await handle.IsVisibleAsync();
            if (!isVisible) continue;

            // Extract element properties
            var tagName = (await handle.EvaluateAsync<string>("el => el.tagName.toLowerCase()")) ?? "";
            var type = (await handle.GetAttributeAsync("type"))?.ToLowerInvariant() ?? "";
            var autoId = await handle.GetAttributeAsync("data-automation-id");
            var id = await handle.GetAttributeAsync("id");
            var name = await handle.GetAttributeAsync("name");

            // SAFETY: Explicitly exclude any button or submit controls from field descriptors
            if (autoId == "bottom-navigation-submit-button" ||
                autoId == "bottom-navigation-next-button" ||
                autoId == "bottom-navigation-back-button" ||
                type == "submit" || type == "button")
            {
                continue;
            }

            // Extract label
            var label = await ExtractLabelAsync(page, handle, id, name);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = autoId ?? id ?? name ?? "Unknown Field";
            }

            // Clean asterisk if present in label
            var isRequired = (await handle.GetAttributeAsync("required") != null) ||
                             (await handle.GetAttributeAsync("aria-required") == "true") ||
                             label.Contains('*');
            label = label.Replace("*", "").Trim();

            // Extract section
            var section = await ExtractSectionAsync(handle);

            // Determine FieldType
            var fieldType = DetermineFieldType(tagName, type);

            // Extract options if select or radio
            JsonDocument? optionsDoc = null;
            if (fieldType == FieldType.SingleSelect && tagName == "select")
            {
                var rawOptions = await handle.EvaluateAsync<string[][]>(@"el => {
                    if (!el.options) return [];
                    const opts = [];
                    for (let i = 0; i < el.options.length; i++) {
                        const opt = el.options[i];
                        if (opt && opt.value) {
                            opts.push([opt.value, (opt.text || opt.value).trim()]);
                        }
                    }
                    return opts;
                }");

                if (rawOptions != null && rawOptions.Length > 0)
                {
                    var optionsList = rawOptions.Select(r => new { Key = r[0], Label = r[1] }).ToList();
                    optionsDoc = JsonSerializer.SerializeToDocument(optionsList);
                }
            }
            else if (fieldType == FieldType.Radio)
            {
                var value = await handle.GetAttributeAsync("value") ?? "";
                var opts = new[] { new { Key = value, Label = label } };
                optionsDoc = JsonSerializer.SerializeToDocument(opts);
            }

            // Extract current page value
            string? pageValue = null;
            try
            {
                pageValue = await handle.InputValueAsync();
            }
            catch
            {
                // Non-input elements might not support InputValue
            }

            // Build locator recipe
            var recipeObj = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(autoId)) recipeObj["automationId"] = autoId;
            if (!string.IsNullOrWhiteSpace(id)) recipeObj["id"] = id;
            if (!string.IsNullOrWhiteSpace(name)) recipeObj["name"] = name;
            recipeObj["tagName"] = tagName;
            if (!string.IsNullOrWhiteSpace(type)) recipeObj["type"] = type;

            var locatorRecipe = JsonSerializer.SerializeToDocument(recipeObj);

            // Compute deterministic fingerprint
            var identifier = autoId ?? id ?? name ?? label;
            var fingerprint = ComputeFingerprint(stepKey, section, identifier, fieldType);

            descriptors.Add(new FieldDescriptor(
                FieldId: Guid.Empty, // Will be set or matched by caller
                Fingerprint: fingerprint,
                FieldIdentifier: identifier,
                Label: label,
                Type: fieldType,
                Required: isRequired,
                Section: section,
                StepKey: stepKey,
                PageValue: pageValue,
                AvailableOptions: optionsDoc,
                LocatorRecipe: locatorRecipe
            ));
        }

        return descriptors;
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

        ILocator locator = ResolveLocator(page, field);
        if (await locator.CountAsync() == 0)
        {
            return new FillResult(false, null, $"Element for field '{field.Label}' could not be located.");
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
                    // Attempt select by value or label
                    var selectVal = !string.IsNullOrWhiteSpace(optionKey) ? optionKey : targetValue;
                    try
                    {
                        await locator.SelectOptionAsync(new SelectOptionValue { Value = selectVal });
                    }
                    catch
                    {
                        // Fallback: select by label
                        await locator.SelectOptionAsync(new SelectOptionValue { Label = targetValue });
                    }
                    break;

                case FieldType.FileUpload:
                    if (File.Exists(targetValue))
                    {
                        await locator.SetInputFilesAsync(targetValue);
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

            // Verify-after-write: read back value from DOM
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
                    readBackValue = await locator.InputValueAsync();
                }
            }
            catch
            {
                readBackValue = targetValue; // fallback if element does not support InputValue
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
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
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
                return page.Locator($"#{idProp.GetString()}").First;
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

        return page.GetByLabel(field.Label).First;
    }

    private static async Task<string> ExtractLabelAsync(IPage page, IElementHandle handle, string? id, string? name)
    {
        // 1. Label by for attribute
        if (!string.IsNullOrWhiteSpace(id))
        {
            var labelEl = await page.QuerySelectorAsync($"label[for='{id}']");
            if (labelEl != null)
            {
                var txt = await labelEl.InnerTextAsync();
                if (!string.IsNullOrWhiteSpace(txt)) return txt.Trim();
            }
        }

        // 2. aria-label
        var ariaLabel = await handle.GetAttributeAsync("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel)) return ariaLabel.Trim();

        // 3. Parent label
        var parentLabel = await handle.EvaluateAsync<string>(@"el => {
            const label = el.closest('label');
            return label ? label.innerText.trim() : null;
        }");
        if (!string.IsNullOrWhiteSpace(parentLabel)) return parentLabel;

        // 4. Preceding legend or label element
        var precedingText = await handle.EvaluateAsync<string>(@"el => {
            const fg = el.closest('.form-group');
            if (fg) {
                const l = fg.querySelector('label, .radio-legend');
                if (l) return l.innerText.trim();
            }
            return null;
        }");
        if (!string.IsNullOrWhiteSpace(precedingText)) return precedingText;

        return "";
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
