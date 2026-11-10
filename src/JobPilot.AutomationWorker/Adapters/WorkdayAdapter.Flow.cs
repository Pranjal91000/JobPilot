using JobPilot.Application.Abstractions;
using JobPilot.Domain.Enums;
using Microsoft.Playwright;

namespace JobPilot.AutomationWorker.Adapters;

public enum AuthOutcomeKind { SignedIn, AccountCreated, VerificationNeeded, Failed }

public sealed record AuthOutcome(AuthOutcomeKind Kind, string? Message = null);

public sealed record JobDetails(string? Title, string? Description);

// Workday navigation outside the form itself: the job posting page, the Apply options,
// sign-in / account creation, the Review step, and dropdown option discovery.
public sealed partial class WorkdayAdapter
{
    public async Task<JobDetails> ReadJobDetailsAsync(IPage page)
    {
        var data = await page.EvaluateAsync<string[]>("""
            () => {
              const t = document.querySelector("[data-automation-id='jobPostingHeader'], h2[data-automation-id='jobTitleHeading']");
              const d = document.querySelector("[data-automation-id='jobPostingDescription']");
              return [t ? t.innerText.trim() : '', d ? d.innerText.trim() : ''];
            }
            """);
        return new JobDetails(
            string.IsNullOrWhiteSpace(data[0]) ? null : data[0],
            string.IsNullOrWhiteSpace(data[1]) ? null : data[1]);
    }

    /// <summary>
    /// On a job posting page: clicks Apply and picks the start option that pre-fills the most.
    /// "Use My Last Application" (copies a previous application on this site) beats "Autofill with
    /// Resume" (Workday parses the resume into work experience and education), which beats
    /// "Apply Manually". Returns the option chosen, or null if no Apply button was found.
    /// </summary>
    public async Task<string?> StartApplicationAsync(IPage page, CancellationToken ct = default)
    {
        var apply = page.Locator("[data-automation-id='adventureButton'], a:text-is('Apply'), button:text-is('Apply')").First;
        if (await apply.CountAsync() > 0 && await apply.IsVisibleAsync())
        {
            await apply.ClickAsync();
            await page.WaitForTimeoutAsync(1500);
        }

        foreach (var (automationId, label) in new[]
                 {
                     ("useMyLastApplication", "Use My Last Application"),
                     ("autofillWithResume", "Autofill with Resume"),
                     ("applyManually", "Apply Manually")
                 })
        {
            var option = page.Locator($"[data-automation-id='{automationId}']").First;
            if (await option.CountAsync() > 0 && await option.IsVisibleAsync())
            {
                await option.ClickAsync();
                await page.WaitForTimeoutAsync(2500);
                return label;
            }
        }

        // Some sites go straight to sign-in or the form after Apply.
        var state = await DetectAuthStateAsync(page, ct);
        return state is AuthState.SignInRequired or AuthState.InApplyFlow ? "Apply" : null;
    }

    /// <summary>
    /// Signs in with the stored login; if the account doesn't exist yet on this site, creates it.
    /// Email verification and CAPTCHAs can't be automated and are reported back to the user.
    /// </summary>
    public async Task<AuthOutcome> SignInOrCreateAccountAsync(IPage page, string email, string password, bool accountKnownToExist, CancellationToken ct = default)
    {
        if (await HasCaptchaAsync(page))
            return new AuthOutcome(AuthOutcomeKind.Failed, "The sign-in page shows a CAPTCHA.");

        var onCreateForm = await page.Locator("[data-automation-id='verifyPassword']").CountAsync() > 0;
        var signInLink = page.Locator("[data-automation-id='signInLink'], button:has-text('Sign In'), a:has-text('Sign In')").First;

        // 1. Try signing in first (switch away from the create-account form if that's what's shown).
        if (onCreateForm && await signInLink.CountAsync() > 0)
        {
            await signInLink.ClickAsync();
            await page.WaitForTimeoutAsync(1200);
        }

        if (await page.Locator("[data-automation-id='verifyPassword']").CountAsync() == 0)
        {
            await FillIfPresentAsync(page, "[data-automation-id='email']", email);
            await FillIfPresentAsync(page, "[data-automation-id='password']", password);
            await ClickWorkdayButtonAsync(page, "signInSubmitButton", "Sign In");
            await page.WaitForTimeoutAsync(4000);

            var state = await DetectAuthStateAsync(page, ct);
            if (state != AuthState.SignInRequired) return new AuthOutcome(AuthOutcomeKind.SignedIn);

            var errors = await DetectValidationErrorsAsync(page, ct);
            var errorText = string.Join(" ", errors);
            if (await NeedsVerificationAsync(page, errorText))
                return new AuthOutcome(AuthOutcomeKind.VerificationNeeded);

            // A wrong password on an account we know exists: don't try to create a duplicate.
            if (accountKnownToExist)
                return new AuthOutcome(AuthOutcomeKind.Failed, string.IsNullOrWhiteSpace(errorText) ? "Sign-in did not succeed." : errorText);
        }

        // 2. Create the account.
        var createLink = page.Locator("[data-automation-id='createAccountLink'], button:has-text('Create Account'), a:has-text('Create Account')").First;
        if (await page.Locator("[data-automation-id='verifyPassword']").CountAsync() == 0 && await createLink.CountAsync() > 0)
        {
            await createLink.ClickAsync();
            await page.WaitForTimeoutAsync(1200);
        }

        if (await page.Locator("[data-automation-id='verifyPassword']").CountAsync() == 0)
            return new AuthOutcome(AuthOutcomeKind.Failed, "Couldn't find the sign-in or create-account form.");

        await FillIfPresentAsync(page, "[data-automation-id='email']", email);
        await FillIfPresentAsync(page, "[data-automation-id='password']", password);
        await FillIfPresentAsync(page, "[data-automation-id='verifyPassword']", password);
        var terms = page.Locator("[data-automation-id='createAccountCheckbox']").First;
        if (await terms.CountAsync() > 0 && !await terms.IsCheckedAsync())
            await terms.CheckAsync(new LocatorCheckOptions { Force = true });

        await ClickWorkdayButtonAsync(page, "createAccountSubmitButton", "Create Account");
        await page.WaitForTimeoutAsync(5000);

        var after = await DetectAuthStateAsync(page, ct);
        var afterErrors = string.Join(" ", await DetectValidationErrorsAsync(page, ct));
        if (await NeedsVerificationAsync(page, afterErrors)) return new AuthOutcome(AuthOutcomeKind.VerificationNeeded);
        if (after != AuthState.SignInRequired) return new AuthOutcome(AuthOutcomeKind.AccountCreated);

        return new AuthOutcome(AuthOutcomeKind.Failed,
            string.IsNullOrWhiteSpace(afterErrors) ? "Account creation did not complete." : afterErrors);
    }

    /// <summary>True on Workday's final Review step, where the run stops for the user.</summary>
    public async Task<bool> IsReviewStepAsync(IPage page, string? stepKey)
    {
        if (!string.IsNullOrWhiteSpace(stepKey) && stepKey.Contains("Review", StringComparison.OrdinalIgnoreCase))
            return true;
        var submit = page.Locator($"{FinalSubmitSelector}, [data-automation-id='pageFooterSubmitButton'], button:has-text('Submit')").First;
        return await submit.CountAsync() > 0 && await submit.IsVisibleAsync();
    }

    /// <summary>
    /// Opens a Workday dropdown, reads its options and closes it again, so the planner (and AI)
    /// can choose a valid option instead of guessing text.
    /// </summary>
    public async Task<IReadOnlyList<string>> ReadDropdownOptionsAsync(IPage page, FieldDescriptor field)
    {
        var button = ResolveLocator(page, field);
        if (await button.CountAsync() == 0) return [];
        try
        {
            await button.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
            string[] options = [];
            for (var i = 0; i < 8 && options.Length == 0; i++)
            {
                await page.WaitForTimeoutAsync(250);
                options = await page.EvaluateAsync<string[]>(CollectOptionsScript);
            }
            return options.Where(o => !string.IsNullOrWhiteSpace(o) && !o.Equals("Select One", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch
        {
            return [];
        }
        finally
        {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(150);
        }
    }

    // Finds a repeatable section by its heading ("Work Experience" / "Where have you worked?",
    // "Education" / "Schools Attended"), counts its entries and tags its Add button.
    private const string RepeatSectionScript = """
        ([section]) => {
          const vis = el => !!(el && (el.offsetWidth > 0 || el.offsetHeight > 0 || el.getClientRects().length > 0));
          const heading = section === 'workExperience'
            ? /work experience|where have you worked|employment/i
            : /education|schools? attended/i;
          const entryField = section === 'workExperience'
            ? "[data-automation-id='formField-jobTitle']"
            : "[data-automation-id='formField-schoolName'], [data-automation-id='formField-school'], [data-automation-id='formField-degree']";
          document.querySelectorAll('[data-jp-add]').forEach(e => e.removeAttribute('data-jp-add'));

          const groups = Array.from(document.querySelectorAll("[role='group'][aria-labelledby]")).filter(vis);
          for (const g of groups) {
            const h = document.getElementById(g.getAttribute('aria-labelledby'));
            const title = (h && h.innerText) || g.getAttribute('aria-labelledby') || '';
            if (!heading.test(title) || /-panel$/.test(g.getAttribute('aria-labelledby') || '')) continue;
            const add = Array.from(g.querySelectorAll("[data-automation-id='add-button']")).filter(vis).pop();
            if (!add) continue;
            add.setAttribute('data-jp-add', '1');
            const panels = new Set(Array.from(g.querySelectorAll(entryField)).filter(vis)
              .map(f => f.closest("[role='group'][aria-labelledby$='-panel']") || f));
            return panels.size;
          }
          return -1;
        }
        """;

    /// <summary>
    /// Clicks Add / Add Another in work experience and education until each has the wanted number
    /// of entries. Returns how many entries were added (0 if the sections aren't on this page).
    /// </summary>
    public async Task<int> AddRepeatEntriesAsync(IPage page, IReadOnlyList<(string Section, int Wanted)> targets)
    {
        var added = 0;
        foreach (var (section, wanted) in targets)
        {
            for (var attempt = 0; attempt < wanted + 2; attempt++)
            {
                var existing = await page.EvaluateAsync<int>(RepeatSectionScript, new object[] { section });
                if (existing < 0 || existing >= wanted) break; // section not on this page, or enough entries

                await page.Locator("[data-jp-add='1']").First.ClickAsync();
                await page.WaitForTimeoutAsync(900);

                var after = await page.EvaluateAsync<int>(RepeatSectionScript, new object[] { section });
                if (after <= existing) break; // the click didn't add an entry; let the user look
                added += after - existing;
            }
        }
        return added;
    }

    private static async Task FillIfPresentAsync(IPage page, string selector, string value)
    {
        var input = page.Locator(selector).First;
        if (await input.CountAsync() > 0) await input.FillAsync(value);
    }

    // Workday puts a transparent "click_filter" div over its buttons; a forced click at the
    // button's position lands on that overlay, which is what Workday listens to.
    private static async Task ClickWorkdayButtonAsync(IPage page, string automationId, string ariaLabel)
    {
        var filter = page.Locator($"[data-automation-id='click_filter'][aria-label='{ariaLabel}']").First;
        if (await filter.CountAsync() > 0)
        {
            await filter.ClickAsync(new LocatorClickOptions { Force = true });
            return;
        }
        var button = page.Locator($"[data-automation-id='{automationId}']").First;
        if (await button.CountAsync() > 0)
        {
            await button.ClickAsync(new LocatorClickOptions { Force = true });
            return;
        }
        await page.Keyboard.PressAsync("Enter");
    }

    private static async Task<bool> HasCaptchaAsync(IPage page) =>
        await page.Locator("iframe[src*='captcha'], iframe[title*='challenge'], [data-automation-id*='captcha']").CountAsync() > 0;

    private static async Task<bool> NeedsVerificationAsync(IPage page, string errorText)
    {
        if (errorText.Contains("verif", StringComparison.OrdinalIgnoreCase)) return true;
        var body = await page.EvaluateAsync<string>("() => (document.body.innerText || '').slice(0, 4000)");
        return body.Contains("verify your", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("verification email", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("check your email", StringComparison.OrdinalIgnoreCase);
    }
}
