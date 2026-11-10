using System.Text.Json;
using System.Text.RegularExpressions;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Enums;
using Microsoft.Playwright;

namespace JobPilot.AutomationWorker.Adapters;

/// <summary>
/// Shared engine for ATSs whose application is one HTML form with no candidate account
/// (Greenhouse, Lever, Ashby). It reads standard form controls — text, textarea, native selects,
/// type-ahead comboboxes (react-select), radio / checkbox groups, file and date inputs — labels them
/// from &lt;label for&gt;, aria-labelledby or the nearest label/legend, and fills them. It never clicks
/// the form's submit button: the run stops once everything is filled, for the user to review and submit.
/// Subclasses only describe where the form, the Apply button and the confirmation text are.
/// </summary>
public abstract class SinglePageFormAdapter : IApplyFormAdapter
{
    public abstract string AtsKey { get; }
    public bool IsSinglePage => true;

    /// <summary>CSS for the application form; the one with the most controls wins.</summary>
    protected abstract string FormSelector { get; }
    /// <summary>Buttons/links that reveal or open the application form from the job page.</summary>
    protected abstract string ApplySelector { get; }
    protected abstract string TitleSelector { get; }
    protected abstract string DescriptionSelector { get; }
    protected abstract Regex ConfirmationText { get; }
    protected virtual string[] ConfirmationUrlParts => [];
    /// <summary>A URL that shows the form directly (e.g. Lever's /apply), or null to click Apply instead.</summary>
    protected virtual string? FormUrl(string pageUrl) => null;

    private const string StepKey = "Application";

    // ---- page state ----------------------------------------------------------------------------

    public async Task<AuthState> DetectAuthStateAsync(IPage page, CancellationToken ct = default)
    {
        var controls = await page.EvaluateAsync<int>(CountControlsScript, FormSelector);
        return controls >= 3 ? AuthState.InApplyFlow : AuthState.Unknown;
    }

    public Task<string?> DetectCurrentStepKeyAsync(IPage page, CancellationToken ct = default) =>
        Task.FromResult<string?>(StepKey);

    public async Task<string?> StartApplicationAsync(IPage page, CancellationToken ct = default)
    {
        if (await DetectAuthStateAsync(page, ct) == AuthState.InApplyFlow) return "Form already open";

        var direct = FormUrl(page.Url);
        if (direct != null && !string.Equals(direct, page.Url, StringComparison.OrdinalIgnoreCase))
        {
            await page.GotoAsync(direct, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });
            await page.WaitForTimeoutAsync(2000);
            return "Opened the application page";
        }

        var apply = page.Locator(ApplySelector).First;
        if (await apply.CountAsync() > 0 && await apply.IsVisibleAsync())
        {
            await apply.ClickAsync();
            await page.WaitForTimeoutAsync(2000);
            return "Clicked Apply";
        }

        return null;
    }

    public Task<bool> IsReviewStepAsync(IPage page, string? stepKey) => Task.FromResult(false);
    public Task<bool> NavigateNextAsync(IPage page, CancellationToken ct = default) => Task.FromResult(false);
    public Task<bool> NavigateBackAsync(IPage page, CancellationToken ct = default) => Task.FromResult(false);
    public Task<int> AddRepeatEntriesAsync(IPage page, IReadOnlyList<(string Section, int Wanted)> targets) => Task.FromResult(0);

    public Task<AuthOutcome> SignInOrCreateAccountAsync(IPage page, string email, string password, bool accountKnownToExist, CancellationToken ct = default) =>
        Task.FromResult(new AuthOutcome(AuthOutcomeKind.Failed, $"{AtsKey} applications don't need an account."));

    public async Task<JobDetails> ReadJobDetailsAsync(IPage page)
    {
        var data = await page.EvaluateAsync<string[]>("""
            ([t, d]) => {
              const title = document.querySelector(t);
              const desc = document.querySelector(d);
              return [title ? title.innerText.trim() : '', desc ? desc.innerText.trim() : ''];
            }
            """, new object[] { TitleSelector, DescriptionSelector });
        return new JobDetails(
            string.IsNullOrWhiteSpace(data[0]) ? null : data[0],
            string.IsNullOrWhiteSpace(data[1]) ? null : data[1]);
    }

    public async Task<IReadOnlyList<string>> DetectValidationErrorsAsync(IPage page, CancellationToken ct = default)
    {
        return await page.EvaluateAsync<string[]>("""
            () => {
              const vis = el => !!(el && (el.offsetWidth > 0 || el.offsetHeight > 0));
              return Array.from(document.querySelectorAll("[role='alert'], .error, .field-error, [class*='error-message'], [class*='errorMessage']"))
                .filter(vis).map(e => (e.innerText || '').trim()).filter(Boolean).slice(0, 10);
            }
            """);
    }

    public async Task<bool> DetectSubmissionConfirmationAsync(IPage page, CancellationToken ct = default)
    {
        var url = page.Url.ToLowerInvariant();
        if (ConfirmationUrlParts.Any(url.Contains)) return true;
        var text = await page.EvaluateAsync<string>("() => (document.body.innerText || '').slice(0, 5000)");
        return ConfirmationText.IsMatch(text) && await DetectAuthStateAsync(page, ct) != AuthState.InApplyFlow;
    }

    public Task<string?> ExtractSubmissionReferenceAsync(IPage page, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    // ---- inspection ----------------------------------------------------------------------------

    public async Task<IReadOnlyList<FieldDescriptor>> InspectFieldsAsync(IPage page, string? stepKey, CancellationToken ct = default)
    {
        try
        {
            await page.WaitForSelectorAsync("input, select, textarea", new PageWaitForSelectorOptions { Timeout = 5000, State = WaitForSelectorState.Attached });
        }
        catch
        {
            // fall through with whatever is present
        }

        var json = await page.EvaluateAsync<string>(InspectScript, FormSelector) ?? "[]";
        return WorkdayAdapter.BuildDescriptors(json, stepKey ?? StepKey);
    }

    public async Task<IReadOnlyList<string>> ReadDropdownOptionsAsync(IPage page, FieldDescriptor field)
    {
        var input = Locate(page, field);
        if (await input.CountAsync() == 0) return [];
        try
        {
            await input.ScrollIntoViewIfNeededAsync();
            await input.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
            string[] options = [];
            for (var i = 0; i < 8 && options.Length == 0; i++)
            {
                await page.WaitForTimeoutAsync(250);
                options = await page.EvaluateAsync<string[]>(WorkdayAdapter.CollectOptionsScript);
            }
            return options.Where(o => !string.IsNullOrWhiteSpace(o) && !o.StartsWith("No options", StringComparison.OrdinalIgnoreCase)).ToList();
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

    // ---- filling -------------------------------------------------------------------------------

    public async Task<FillResult> FillFieldAsync(IPage page, FieldDescriptor field, string targetValue, string? optionKey = null, CancellationToken ct = default)
    {
        var kind = Recipe(field, "kind");
        var locator = kind is "sp-radio" or "sp-checkboxGroup" ? Group(page, field) : Locate(page, field);
        if (await locator.CountAsync() == 0)
            return new FillResult(false, null, $"Element for field '{field.Label}' could not be located on the current page.");

        try
        {
            return kind switch
            {
                "sp-select" => await FillSelectAsync(locator.First, targetValue, optionKey),
                "sp-combobox" => await FillComboboxAsync(page, locator.First, targetValue),
                "sp-radio" => await FillChoiceGroupAsync(page, locator, targetValue, multi: false),
                "sp-checkboxGroup" => await FillChoiceGroupAsync(page, locator, targetValue, multi: true),
                "sp-checkbox" => await FillCheckboxAsync(locator.First, targetValue),
                "sp-file" => await FillFileAsync(page, locator.First, targetValue),
                "sp-date" => await FillDateInputAsync(locator.First, targetValue),
                _ => await FillTextAsync(page, locator.First, targetValue)
            };
        }
        catch (Exception ex)
        {
            return new FillResult(false, null, $"Fill error: {ex.Message.Split('\n')[0]}");
        }
    }

    private static async Task<FillResult> FillTextAsync(IPage page, ILocator input, string value)
    {
        await input.ScrollIntoViewIfNeededAsync();
        await input.FillAsync(value);
        await input.BlurAsync();
        await page.WaitForTimeoutAsync(100);
        var readBack = await input.InputValueAsync();
        return WorkdayAdapter.SameText(readBack, value)
            ? new FillResult(true, readBack, null)
            : new FillResult(false, readBack, $"Typed '{value}' but the page shows '{readBack}'.");
    }

    private static async Task<FillResult> FillSelectAsync(ILocator select, string value, string? optionKey)
    {
        var options = await select.EvaluateAsync<string[][]>(
            "el => Array.from(el.options).filter(o => o.value).map(o => [o.value, (o.text || '').replace(/\\s+/g, ' ').trim()])");
        var labels = options.Select(o => o[1]).ToList();

        var idx = optionKey != null ? Array.FindIndex(options, o => o[0] == optionKey) : -1;
        if (idx < 0) idx = WorkdayAdapter.BestMatchIndex(labels, WorkdayAdapter.NormalizeBoolean(value));
        if (idx < 0)
            return new FillResult(false, null, $"No option matching '{value}'. Options: {string.Join(", ", labels.Take(10))}.");

        await select.SelectOptionAsync(new SelectOptionValue { Value = options[idx][0] });
        var shown = await select.EvaluateAsync<string>("el => el.selectedIndex >= 0 ? el.options[el.selectedIndex].text.trim() : ''");
        return shown == labels[idx]
            ? new FillResult(true, shown, null)
            : new FillResult(false, shown, $"Selected '{labels[idx]}' but the page shows '{shown}'.");
    }

    // react-select style type-ahead: type to filter, then click the matching option. Enter is not
    // pressed, because it would pick whatever option happens to be highlighted.
    private static async Task<FillResult> FillComboboxAsync(IPage page, ILocator input, string value)
    {
        await input.ScrollIntoViewIfNeededAsync();
        var current = await ComboboxShownValueAsync(input);
        if (WorkdayAdapter.MatchScore(current, value) >= 80) return new FillResult(true, current, null);

        await input.ClickAsync();
        var stale = await page.EvaluateAsync<string[]>(WorkdayAdapter.CollectOptionsScript);
        await input.FillAsync("");
        await input.PressSequentiallyAsync(WorkdayAdapter.NormalizeBoolean(value), new LocatorPressSequentiallyOptions { Delay = 40 });

        var (picked, seen) = await WorkdayAdapter.PickOptionDetailedAsync(page, WorkdayAdapter.NormalizeBoolean(value), 8000, stale);
        if (picked == null)
        {
            await page.Keyboard.PressAsync("Escape");
            var hint = seen.Length > 0 ? $" Options shown: {string.Join(" / ", seen.Take(8))}." : " No options were shown.";
            return new FillResult(false, null, $"No option matching '{value}'.{hint}");
        }

        await page.WaitForTimeoutAsync(300);
        var shown = await ComboboxShownValueAsync(input);
        return WorkdayAdapter.MatchScore(shown, picked) >= 60 || WorkdayAdapter.MatchScore(shown, value) >= 60
            ? new FillResult(true, shown, null)
            : new FillResult(false, shown, $"Picked '{picked}' but the field shows '{shown}'.");
    }

    private static Task<string> ComboboxShownValueAsync(ILocator input) =>
        input.EvaluateAsync<string>("""
            el => {
              const box = el.closest("[class*='container'], [class*='control']") || el.parentElement;
              const t = ((box && box.innerText) || '').replace(/\s+/g, ' ').trim();
              return /^select/i.test(t) ? (el.value || '') : (t || el.value || '');
            }
            """);

    private static async Task<FillResult> FillChoiceGroupAsync(IPage page, ILocator inputs, string value, bool multi)
    {
        var labels = await inputs.EvaluateAllAsync<string[]>("""
            els => els.map(i => {
              const clean = s => (s || '').replace(/\s+/g, ' ').trim();
              if (i.id) { const l = document.querySelector(`label[for="${CSS.escape(i.id)}"]`); if (l && clean(l.innerText)) return clean(l.innerText); }
              const pl = i.closest('label'); if (pl && clean(pl.innerText)) return clean(pl.innerText);
              if (i.getAttribute('aria-label')) return clean(i.getAttribute('aria-label'));
              const sib = i.nextElementSibling; if (sib && clean(sib.innerText)) return clean(sib.innerText);
              return clean(i.value);
            })
            """);

        var wanted = multi ? WorkdayAdapter.SplitItems(value) : new List<string> { WorkdayAdapter.NormalizeBoolean(value) };
        var unmatched = new List<string>();
        foreach (var item in wanted)
        {
            var idx = WorkdayAdapter.BestMatchIndex(labels, item);
            if (idx < 0) { unmatched.Add(item); continue; }
            var option = inputs.Nth(idx);
            await option.ScrollIntoViewIfNeededAsync();
            await option.CheckAsync(new LocatorCheckOptions { Force = true });
        }
        await page.WaitForTimeoutAsync(150);

        var chosen = new List<string>();
        for (var i = 0; i < labels.Length; i++)
            if (await inputs.Nth(i).IsCheckedAsync()) chosen.Add(labels[i]);

        var readBack = string.Join(", ", chosen);
        return unmatched.Count == 0 && chosen.Count > 0
            ? new FillResult(true, readBack, null)
            : new FillResult(false, readBack, $"No option matching: {string.Join(", ", unmatched)}. Options: {string.Join(", ", labels)}.");
    }

    private static async Task<FillResult> FillCheckboxAsync(ILocator box, string value)
    {
        var shouldCheck = WorkdayAdapter.IsTruthy(value);
        await box.SetCheckedAsync(shouldCheck, new LocatorSetCheckedOptions { Force = true });
        var isChecked = await box.IsCheckedAsync();
        return isChecked == shouldCheck
            ? new FillResult(true, isChecked.ToString(), null)
            : new FillResult(false, isChecked.ToString(), "Checkbox did not change state.");
    }

    // File inputs are often visually hidden behind an "Attach" button; setting files on the input
    // directly works regardless.
    private static async Task<FillResult> FillFileAsync(IPage page, ILocator input, string path)
    {
        if (!File.Exists(path))
            return new FillResult(false, null, $"Resume file at '{path}' does not exist on disk.");

        await input.SetInputFilesAsync(path);
        await page.WaitForTimeoutAsync(1500);
        var name = await input.EvaluateAsync<string>("el => el.files && el.files.length ? el.files[0].name : ''");
        return !string.IsNullOrEmpty(name)
            ? new FillResult(true, name, null)
            : new FillResult(true, Path.GetFileName(path), null); // some boards upload and clear the input
    }

    private static async Task<FillResult> FillDateInputAsync(ILocator input, string value)
    {
        var (month, day, year) = WorkdayAdapter.ParseDate(value);
        if (year == null)
            return new FillResult(false, null, $"Could not read '{value}' as a date.");
        var iso = $"{year}-{month ?? "01"}-{day ?? "01"}";
        await input.FillAsync(iso);
        var readBack = await input.InputValueAsync();
        return readBack == iso ? new FillResult(true, readBack, null) : new FillResult(false, readBack, $"Date shows '{readBack}'.");
    }

    // ---- locating ------------------------------------------------------------------------------

    private static string? Recipe(FieldDescriptor field, string key) =>
        field.LocatorRecipe != null && field.LocatorRecipe.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static ILocator Locate(IPage page, FieldDescriptor field)
    {
        var id = Recipe(field, "id");
        if (!string.IsNullOrWhiteSpace(id)) return page.Locator($"[id='{WorkdayAdapter.CssAttr(id)}']");
        var name = Recipe(field, "name");
        if (!string.IsNullOrWhiteSpace(name)) return page.Locator($"[name='{WorkdayAdapter.CssAttr(name)}']");
        return page.GetByLabel(field.Label);
    }

    private static ILocator Group(IPage page, FieldDescriptor field)
    {
        var name = Recipe(field, "name");
        var type = field.Type == FieldType.Radio ? "radio" : "checkbox";
        return !string.IsNullOrWhiteSpace(name)
            ? page.Locator($"input[type='{type}'][name='{WorkdayAdapter.CssAttr(name)}']")
            : Locate(page, field);
    }

    // ---- scripts -------------------------------------------------------------------------------

    private const string CountControlsScript = """
        (sel) => {
          const vis = el => !!(el && (el.offsetWidth > 0 || el.offsetHeight > 0));
          const forms = Array.from(document.querySelectorAll(sel)).filter(vis);
          const count = f => Array.from(f.querySelectorAll("input:not([type='hidden']), select, textarea")).filter(e => vis(e) || e.type === 'file').length;
          return forms.reduce((m, f) => Math.max(m, count(f)), 0);
        }
        """;

    private const string InspectScript = """
        (rootSelector) => {
          const vis = el => !!(el && (el.offsetWidth > 0 || el.offsetHeight > 0 || (el.getClientRects && el.getClientRects().length > 0)));
          const clean = s => (s || '').replace(/\s+/g, ' ').trim();
          const count = el => el.querySelectorAll("input:not([type='hidden']), select, textarea").length;
          const root = Array.from(document.querySelectorAll(rootSelector)).filter(vis).sort((a, b) => count(b) - count(a))[0] || document.body;

          const byId = id => { try { return document.getElementById(id); } catch { return null; } };
          const labelFor = el => {
            if (!el || !el.id) return '';
            try { const l = document.querySelector(`label[for="${CSS.escape(el.id)}"]`); return l ? clean(l.innerText) : ''; } catch { return ''; }
          };
          const optionLabel = el => labelFor(el) || clean((el.closest('label') || {}).innerText) ||
            clean(el.getAttribute('aria-label')) || clean((el.nextElementSibling || {}).innerText) || clean(el.value);

          // The question text for a control (or a group of controls, so option labels are skipped).
          const questionLabel = (el, group) => {
            const members = group || [el];
            const lb = (el.getAttribute('aria-labelledby') || '').split(/\s+/).map(byId).filter(Boolean).map(e => e.innerText).join(' ');
            if (!group && clean(lb)) return clean(lb);
            if (!group) {
              const lf = labelFor(el); if (lf) return lf;
              const al = el.getAttribute('aria-label'); if (clean(al)) return clean(al);
              const wrap = el.closest('label'); if (wrap && clean(wrap.innerText)) return clean(wrap.innerText);
            }
            let w = el.parentElement;
            for (let i = 0; i < 6 && w && w !== document.body; i++, w = w.parentElement) {
              // Once the wrapper holds other fields, any label in it belongs to one of them.
              const others = Array.from(w.querySelectorAll("input:not([type='hidden']), select, textarea")).filter(c => !members.includes(c));
              if (others.length > 0) break;
              const l = Array.from(w.querySelectorAll("legend, label, .application-label, [class*='label']")).find(x =>
                clean(x.innerText) && !x.querySelector('input, select, textarea') &&
                !members.some(m => x.contains(m) || (m.id && x.getAttribute('for') === m.id)));
              if (l) return clean(l.innerText);
            }
            return clean(el.getAttribute('placeholder') || el.getAttribute('name') || el.id || '');
          };

          const results = [];
          const handled = new Set();
          const push = (el, o) => results.push(Object.assign({
            tagName: (el.tagName || '').toLowerCase(), type: (el.getAttribute('type') || '').toLowerCase(),
            autoId: null, id: el.id || null, name: el.getAttribute('name') || null, section: null, container: null
          }, o));

          // Radio groups and multi-checkbox groups, keyed by name.
          const groups = {};
          root.querySelectorAll("input[type='radio'], input[type='checkbox']").forEach(i => {
            const k = i.getAttribute('name') || i.id; if (!k) return; (groups[k] = groups[k] || []).push(i);
          });
          for (const [name, inputs] of Object.entries(groups)) {
            const isRadio = inputs[0].type === 'radio';
            if (!isRadio && inputs.length === 1) continue;
            if (!inputs.some(i => vis(i) || vis(i.closest('label')) || (i.id && vis(document.querySelector(`label[for="${CSS.escape(i.id)}"]`))))) continue;
            const fs = inputs[0].closest('fieldset');
            const raw = (fs && fs.querySelector('legend') && fs.querySelector('legend').innerText) || questionLabel(inputs[0], inputs) || name;
            const opts = inputs.map(i => ({ key: i.value || i.id, label: optionLabel(i) }));
            const checked = inputs.filter(i => i.checked).map(optionLabel);
            push(inputs[0], {
              ident: name, label: clean(raw.replace(/\*/g, '')),
              isRequired: raw.includes('*') || inputs.some(i => i.required || i.getAttribute('aria-required') === 'true'),
              options: opts, pageValue: checked.join(', ') || null,
              kind: isRadio ? 'sp-radio' : 'sp-checkboxGroup', fieldType: isRadio ? 'Radio' : 'MultiSelect'
            });
            inputs.forEach(i => handled.add(i));
          }

          for (const el of Array.from(root.querySelectorAll('input, select, textarea'))) {
            if (handled.has(el)) continue;
            const type = (el.getAttribute('type') || '').toLowerCase();
            if (['hidden', 'submit', 'button', 'reset', 'image', 'search'].includes(type)) continue;
            if (type !== 'file' && type !== 'checkbox' && !vis(el)) continue;
            if (type !== 'file' && el.closest("[aria-hidden='true']")) continue;

            const raw = questionLabel(el);
            let label = clean(raw.replace(/\*/g, ''));
            const required = raw.includes('*') || el.required || el.getAttribute('aria-required') === 'true';
            let item;

            if (el.tagName === 'SELECT') {
              const opts = Array.from(el.options).filter(o => o.value).map(o => ({ key: o.value, label: clean(o.text) }));
              const sel = el.selectedIndex >= 0 && el.options[el.selectedIndex].value ? clean(el.options[el.selectedIndex].text) : null;
              item = { kind: 'sp-select', fieldType: el.multiple ? 'MultiSelect' : 'SingleSelect', options: opts, pageValue: sel };
            } else if (el.getAttribute('role') === 'combobox' || el.getAttribute('aria-autocomplete') === 'list') {
              const box = el.closest("[class*='container'], [class*='control']") || el.parentElement;
              const shown = clean((box && box.innerText) || '');
              item = { kind: 'sp-combobox', fieldType: 'SingleSelect', options: null,
                       pageValue: shown && !/^select/i.test(shown) ? shown : (el.value || null) };
            } else if (type === 'file') {
              const fileName = el.files && el.files.length ? el.files[0].name : null;
              item = { kind: 'sp-file', fieldType: 'FileUpload', options: null, pageValue: fileName };
              if (!label || label === el.getAttribute('name') || label === el.id) label = 'Resume / CV';
            } else if (type === 'checkbox') {
              if (!vis(el) && !vis(el.closest('label'))) continue;
              item = { kind: 'sp-checkbox', fieldType: 'Checkbox', options: null, pageValue: el.checked ? 'true' : 'false' };
              if (!label) label = optionLabel(el);
            } else if (type === 'date') {
              item = { kind: 'sp-date', fieldType: 'Date', options: null, pageValue: el.value || null };
            } else {
              item = { kind: 'sp-text', fieldType: 'Text', options: null, pageValue: el.value || null };
            }

            push(el, Object.assign(item, {
              ident: el.id || el.getAttribute('name') || label,
              label: label || el.getAttribute('name') || el.id || 'Field',
              isRequired: required
            }));
            handled.add(el);
          }

          return JSON.stringify(results);
        }
        """;
}

public sealed class GreenhouseAdapter : SinglePageFormAdapter
{
    public override string AtsKey => "greenhouse";
    protected override string FormSelector => "#application-form, #application_form, form[action*='applications'], #application form, form";
    protected override string ApplySelector => "#apply_button, a[href='#app'], a[href='#application'], button:text-is('Apply'), a:text-is('Apply'), button:has-text('Apply for this job')";
    protected override string TitleSelector => ".job__title h1, h1.app-title, h1";
    protected override string DescriptionSelector => ".job__description, #content, .job-post-content";
    protected override Regex ConfirmationText { get; } = new(@"thank you for applying|application (has been )?(received|submitted)", RegexOptions.IgnoreCase);
    protected override string[] ConfirmationUrlParts => ["/confirmation"];
}

public sealed class LeverAdapter : SinglePageFormAdapter
{
    private static readonly Regex Posting = new(@"^(https://jobs(\.eu)?\.lever\.co/[^/]+/[0-9a-f-]{36})/?(\?.*)?$", RegexOptions.IgnoreCase);

    public override string AtsKey => "lever";
    protected override string FormSelector => "#application-form, form[action*='/apply'], .application-form, form";
    protected override string ApplySelector => "a.postings-btn[href*='/apply'], a:has-text('Apply for this job')";
    protected override string TitleSelector => ".posting-headline h2, h2";
    protected override string DescriptionSelector => "[data-qa='job-description'], .posting-page .section-wrapper";
    protected override Regex ConfirmationText { get; } = new(@"application (submitted|received)|thanks? (you )?for applying", RegexOptions.IgnoreCase);
    protected override string[] ConfirmationUrlParts => ["/thanks"];

    protected override string? FormUrl(string pageUrl)
    {
        var m = Posting.Match(pageUrl);
        return m.Success ? m.Groups[1].Value + "/apply" : null;
    }
}

public sealed class AshbyAdapter : SinglePageFormAdapter
{
    private static readonly Regex Posting = new(@"^(https://jobs\.ashbyhq\.com/[^/]+/[0-9a-f-]{36})/?(\?.*)?$", RegexOptions.IgnoreCase);

    public override string AtsKey => "ashby";
    protected override string FormSelector => "form, [class*='application-form'], [class*='ApplicationForm']";
    protected override string ApplySelector => "a[href$='/application'], a:has-text('Apply for this Job'), button:has-text('Apply')";
    protected override string TitleSelector => "h1";
    protected override string DescriptionSelector => "[class*='descriptionText'], [class*='description']";
    protected override Regex ConfirmationText { get; } = new(@"thank(s| you) for applying|application (was )?(submitted|received)|we.ve received your application", RegexOptions.IgnoreCase);

    protected override string? FormUrl(string pageUrl)
    {
        var m = Posting.Match(pageUrl);
        return m.Success ? m.Groups[1].Value + "/application" : null;
    }
}

/// <summary>Picks the adapter for an application's ATS (Workday when unknown).</summary>
public sealed class AdapterRegistry(IEnumerable<IApplyFormAdapter> adapters)
{
    private readonly Dictionary<string, IApplyFormAdapter> _byKey =
        adapters.ToDictionary(a => a.AtsKey, StringComparer.OrdinalIgnoreCase);

    public IApplyFormAdapter For(string? atsKey) =>
        atsKey != null && _byKey.TryGetValue(atsKey, out var adapter) ? adapter : _byKey["workday"];
}
