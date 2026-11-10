using System.Text.Json;
using System.Text.RegularExpressions;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Enums;
using Microsoft.Playwright;

namespace JobPilot.AutomationWorker.Adapters;

// Workday-specific widget handling. Workday renders every question inside a
// [data-automation-id^='formField'] wrapper that holds the label, and uses custom widgets
// instead of native controls:
//   - "prompt" (type-ahead / multi-select): input[data-uxi-widget-type='selectinput'] inside a
//     multiSelectContainer; typing + Enter searches, results are [data-automation-id='promptOption'].
//   - "dropdown": button[aria-haspopup='listbox'] opening a [role='listbox'] of [role='option'].
//   - "radio": a group of radio inputs under one legend (the question).
//   - "date": separate month/day/year inputs (data-automation-id^='dateSection').
// A plain FillAsync on these types text but never selects anything, which is why skills,
// "How did you hear about us", country and similar fields stayed empty while autofill
// reported success.
public sealed partial class WorkdayAdapter
{
    private const string InspectScript = """
        async () => {
          const vis = el => !!(el && (el.offsetWidth > 0 || el.offsetHeight > 0 || (el.getClientRects && el.getClientRects().length > 0)));
          const clean = s => (s || '').replace(/\s+/g, ' ').trim();
          const genericSel = "input:not([type='hidden']), select, textarea, div[role='combobox'], div[role='listbox'], input[type='file']";

          if (document.querySelectorAll(genericSel).length === 0) {
            await new Promise(r => setTimeout(r, 800));
          }

          const labelFor = el => {
            if (!el) return '';
            if (el.id) {
              try {
                const l = document.querySelector(`label[for="${CSS.escape(el.id)}"]`);
                if (l && clean(l.innerText)) return clean(l.innerText);
              } catch {}
            }
            const aria = el.getAttribute('aria-label');
            if (aria && clean(aria)) return clean(aria);
            const pl = el.closest('label');
            if (pl && clean(pl.innerText)) return clean(pl.innerText);
            return '';
          };

          const sectionOf = el => {
            const g = el.closest("[role='group'][aria-labelledby], [data-automation-id$='-section'], [data-automation-id*='Section']");
            if (!g) return null;
            const lb = g.getAttribute('aria-labelledby');
            if (lb) {
              const h = document.getElementById(lb);
              if (h && clean(h.innerText)) return clean(h.innerText);
            }
            const h = g.querySelector('h2, h3, h4');
            if (h && clean(h.innerText)) return clean(h.innerText);
            return g.getAttribute('data-automation-id');
          };

          const selectedItems = box => {
            const pills = Array.from(box.querySelectorAll("[data-automation-id='selectedItem']")).map(e => clean(e.innerText)).filter(Boolean);
            if (pills.length > 0) return pills;
            return (box.innerText || '').split('\n').map(clean)
              .filter(s => s && !/^\d+ items? selected$/i.test(s) && !/^search$/i.test(s));
          };

          const results = [];
          const handled = new Set();

          // 1. Workday formField wrappers: one entry per question, typed by its widget.
          const containers = Array.from(document.querySelectorAll("[data-automation-id^='formField']"))
            .filter(c => vis(c) && !c.querySelector("[data-automation-id^='formField']"));

          for (const ff of containers) {
            const ffId = ff.getAttribute('data-automation-id');
            const labEl = ff.querySelector('legend, label');
            const rawLabel = clean(labEl ? labEl.innerText : '');
            const required = rawLabel.includes('*') || !!ff.querySelector("[aria-required='true'], [required]");
            let label = clean(rawLabel.replace(/\*/g, ''));

            const ms = ff.querySelector("input[data-uxi-widget-type='selectinput']");
            const dd = ff.querySelector("button[aria-haspopup='listbox']");
            const radios = Array.from(ff.querySelectorAll("input[type='radio']"));
            const cbs = Array.from(ff.querySelectorAll("input[type='checkbox']"));
            const fileIn = ff.querySelector("input[type='file']");
            const fileBtn = ff.querySelector("[data-automation-id='select-files']");
            const dateParts = Array.from(ff.querySelectorAll("input[data-automation-id^='dateSection']"));
            const ta = ff.querySelector('textarea');
            const txt = Array.from(ff.querySelectorAll('input')).find(i =>
              !['hidden', 'radio', 'checkbox', 'file'].includes((i.getAttribute('type') || '').toLowerCase()) &&
              i.getAttribute('data-uxi-widget-type') !== 'selectinput' &&
              !(i.getAttribute('data-automation-id') || '').startsWith('dateSection') &&
              vis(i));

            let item = null;
            if (ms) {
              const box = ms.closest("[data-automation-id='multiSelectContainer']") || ff;
              item = { kind: 'prompt', fieldType: 'MultiSelect', el: ms, pageValue: selectedItems(box).join(', ') || null };
            } else if (dd) {
              const t = clean(dd.innerText);
              item = { kind: 'dropdown', fieldType: 'SingleSelect', el: dd, pageValue: (!t || /^select one$/i.test(t)) ? null : t };
            } else if (radios.length > 0) {
              const opts = radios.map(r => ({ key: r.value || r.id, label: labelFor(r) || r.value }));
              const ci = radios.findIndex(r => r.checked);
              item = { kind: 'radio', fieldType: 'Radio', el: radios[0], ident: radios[0].getAttribute('name') || radios[0].id,
                       options: opts, pageValue: ci >= 0 ? opts[ci].label : null };
            } else if (dateParts.length > 0) {
              item = { kind: 'date', fieldType: 'Date', el: dateParts[0], ident: dateParts[0].id || ffId,
                       pageValue: dateParts.map(d => d.value).filter(Boolean).join('/') || null };
            } else if (fileIn || fileBtn) {
              const names = Array.from(ff.querySelectorAll("[data-automation-id='file-upload-item-name'], [data-automation-id*='fileName']"))
                .map(e => clean(e.innerText)).filter(Boolean);
              item = { kind: 'file', fieldType: 'FileUpload', el: fileIn || fileBtn, pageValue: names.join(', ') || null };
              if (!label) label = 'Resume / CV';
            } else if (cbs.length > 1) {
              const opts = cbs.map(c => ({ key: c.value || c.id, label: labelFor(c) || c.value }));
              item = { kind: 'checkboxGroup', fieldType: 'MultiSelect', el: cbs[0], ident: cbs[0].getAttribute('name') || ffId,
                       options: opts, pageValue: cbs.filter(c => c.checked).map(c => labelFor(c)).join(', ') || null };
            } else if (cbs.length === 1) {
              item = { kind: 'checkbox', fieldType: 'Checkbox', el: cbs[0], pageValue: cbs[0].checked ? 'true' : 'false' };
              if (!label) label = labelFor(cbs[0]);
            } else if (ta) {
              item = { kind: 'text', fieldType: 'Text', el: ta, pageValue: ta.value || null };
            } else if (txt) {
              item = { kind: 'text', fieldType: 'Text', el: txt, pageValue: txt.value || null };
            } else {
              continue;
            }

            const el = item.el;
            const keepAutoId = item.kind === 'text' || item.kind === 'file' || item.kind === 'checkbox';
            results.push({
              tagName: (el.tagName || '').toLowerCase(),
              type: (el.getAttribute('type') || '').toLowerCase(),
              autoId: keepAutoId ? el.getAttribute('data-automation-id') : null,
              id: el.id || null,
              name: el.getAttribute('name') || null,
              ident: item.ident || null,
              label: label || labelFor(el) || ffId,
              isRequired: required,
              section: sectionOf(ff),
              pageValue: item.pageValue,
              options: item.options || null,
              kind: item.kind,
              fieldType: item.fieldType,
              container: ffId
            });
            ff.querySelectorAll("input, select, textarea, button, div[role='combobox'], div[role='listbox']").forEach(e => handled.add(e));
          }

          // 2. Anything outside formField wrappers (non-Workday pages, the local mock fixtures).
          for (const el of Array.from(document.querySelectorAll(genericSel))) {
            if (handled.has(el) || el.closest("[data-automation-id^='formField']")) continue;

            const tagName = (el.tagName || '').toLowerCase();
            const type = (el.getAttribute('type') || '').toLowerCase();
            const autoId = el.getAttribute('data-automation-id') || null;
            if (!vis(el) && type !== 'file') continue;
            if (autoId === 'bottom-navigation-submit-button' || autoId === 'bottom-navigation-next-button' ||
                autoId === 'bottom-navigation-back-button' || type === 'submit' || type === 'button' ||
                el.getAttribute('role') === 'button') continue;

            let label = labelFor(el);
            if (!label) {
              const fg = el.closest(".form-group, [data-automation-id*='field']");
              if (fg) {
                const l = fg.querySelector("label, .radio-legend, legend, [data-automation-id*='label']");
                if (l && clean(l.innerText)) label = clean(l.innerText);
              }
            }
            if (!label && type === 'file') {
              const drop = el.closest("[data-automation-id*='dropZone'], [data-automation-id*='upload'], [data-automation-id*='file'], [data-automation-id*='attachment'], .dropzone, .file-upload");
              if (drop) {
                const header = drop.querySelector("h2, h3, h4, [data-automation-id*='label'], .title, .file-upload-title");
                if (header && clean(header.innerText)) label = clean(header.innerText);
                else {
                  const lines = (drop.innerText || '').split('\n').map(clean).filter(Boolean);
                  if (lines.length > 0) label = lines[0];
                }
              }
            }
            if (!label) label = type === 'file' ? 'Resume / CV' : (autoId || el.id || el.getAttribute('name') || 'Unknown Field');

            const isRequired = el.hasAttribute('required') || el.getAttribute('aria-required') === 'true' || label.includes('*');
            label = clean(label.replace(/\*/g, ''));

            let section = null;
            const sectionEl = el.closest("[data-automation-id*='Section'], .repeat-group, .review-section");
            if (sectionEl) {
              section = sectionEl.getAttribute('data-automation-id') || null;
              if (!section) {
                const h = sectionEl.querySelector('.section-title, .section-header, h2, h3');
                if (h && h.innerText) section = clean(h.innerText);
              }
            }

            let options = null;
            if (tagName === 'select' && el.options) {
              options = [];
              for (const opt of Array.from(el.options)) {
                if (opt && opt.value) options.push({ key: opt.value, label: clean(opt.text || opt.value) });
              }
            } else if (type === 'radio') {
              options = [{ key: el.value || '', label: label }];
            }

            let pageValue = null;
            if (tagName === 'input' && type !== 'file') pageValue = el.value || null;
            else if (tagName === 'textarea' || tagName === 'select') pageValue = el.value || null;
            else if (el.getAttribute('role') === 'combobox') pageValue = el.innerText || null;

            results.push({ tagName, type, autoId, id: el.id || null, name: el.getAttribute('name') || null,
                           label, isRequired, section, pageValue, options, kind: 'generic' });
          }

          return JSON.stringify(results);
        }
        """;

    // Tags the currently visible list options with data-jp-opt=<index> and returns their texts,
    // so C# can choose one by text and click it precisely (Playwright 1.50 has no visible-only filter).
    private const string CollectOptionsScript = """
        () => {
          const vis = el => !!(el && (el.offsetWidth > 0 || el.offsetHeight > 0 || (el.getClientRects && el.getClientRects().length > 0)));
          document.querySelectorAll('[data-jp-opt]').forEach(e => e.removeAttribute('data-jp-opt'));
          const all = Array.from(document.querySelectorAll("[data-automation-id='promptOption'], [role='option'], [data-automation-id='menuItem']")).filter(vis);
          const leaves = all.filter(o => !all.some(p => p !== o && o.contains(p)));
          return leaves.map((o, i) => {
            o.setAttribute('data-jp-opt', String(i));
            return (o.getAttribute('data-automation-label') || o.innerText || '').replace(/\s+/g, ' ').trim();
          });
        }
        """;

    // Is a Workday search/list still loading?
    private const string PromptBusyScript = """
        () => {
          const vis = el => !!(el && (el.offsetWidth > 0 || el.offsetHeight > 0 || (el.getClientRects && el.getClientRects().length > 0)));
          return Array.from(document.querySelectorAll(
            "[data-automation-id='loadingSpinner'], [data-automation-id*='Loading'], [data-automation-id*='loading'], [data-automation-id*='spinner'], [aria-busy='true']"))
            .some(vis);
        }
        """;

    private const string SelectedItemsScript = """
        el => {
          const clean = s => (s || '').replace(/\s+/g, ' ').trim();
          const ff = el.closest("[data-automation-id^='formField']") || el.parentElement;
          const box = el.closest("[data-automation-id='multiSelectContainer']") || ff;
          const pills = Array.from(box.querySelectorAll("[data-automation-id='selectedItem']")).map(e => clean(e.innerText)).filter(Boolean);
          if (pills.length > 0) return pills;
          return (box.innerText || '').split('\n').map(clean)
            .filter(s => s && !/^\d+ items? selected$/i.test(s) && !/^search$/i.test(s));
        }
        """;

    private static string? GetRecipeValue(FieldDescriptor field, string key)
    {
        if (field.LocatorRecipe == null) return null;
        return field.LocatorRecipe.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
    }

    private static string CssAttr(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    private async Task<FillResult> FillWorkdayWidgetAsync(
        IPage page, FieldDescriptor field, string kind, ILocator locator, string target, string? optionKey, CancellationToken ct)
    {
        switch (kind)
        {
            case "prompt":
                return await FillPromptAsync(page, locator, target, field.Type == FieldType.MultiSelect, ct);
            case "dropdown":
                return await FillDropdownAsync(page, locator, target, ct);
            case "radio":
                return await FillRadioAsync(page, field, target);
            case "date":
                return await FillDateAsync(page, locator, target);
            case "checkboxGroup":
                return await FillCheckboxGroupAsync(page, locator, target);
            case "checkbox":
            {
                var shouldCheck = IsTruthy(target);
                await locator.SetCheckedAsync(shouldCheck, new LocatorSetCheckedOptions { Force = true });
                var isChecked = await locator.IsCheckedAsync();
                return isChecked == shouldCheck
                    ? new FillResult(true, isChecked.ToString(), null)
                    : new FillResult(false, isChecked.ToString(), "Checkbox did not change state.");
            }
            default: // "text"
            {
                await locator.FillAsync(target);
                await locator.BlurAsync(); // Workday validates and commits on blur
                await page.WaitForTimeoutAsync(150);
                var readBack = await locator.InputValueAsync();
                return SameText(readBack, target)
                    ? new FillResult(true, readBack, null)
                    : new FillResult(false, readBack, $"Typed '{target}' but the page shows '{readBack}'.");
            }
        }
    }

    // Type-ahead and multi-select ("Type to Add Skills", "How Did You Hear About Us?", phone code...).
    // Each item is searched separately and only counted once Workday shows it as selected:
    //   type -> Enter -> wait for *fresh* results (not the previous popup, not while loading)
    //   -> click the best match -> wait until the selection actually contains it (retry the click once)
    // Items Workday doesn't offer (it may call ".NET Core" "ASP.NET Core") are reported, but don't
    // undo the ones that worked: a multi-select with at least one item counts as filled, with a warning.
    private async Task<FillResult> FillPromptAsync(IPage page, ILocator input, string target, bool multi, CancellationToken ct)
    {
        var items = multi ? SplitItems(target) : new List<string> { target.Trim() };
        var notes = new List<string>();
        var unmatched = new List<string>();

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var before = await input.EvaluateAsync<string[]>(SelectedItemsScript);
            // Whole-word match only: "C#" is not already covered by a selected "C#.NET".
            if (before.Any(s => MatchScore(s, item) >= 80)) continue;

            await input.ScrollIntoViewIfNeededAsync();
            await input.ClickAsync();
            await input.FillAsync("");
            var stale = await page.EvaluateAsync<string[]>(CollectOptionsScript);
            await input.PressSequentiallyAsync(item, new LocatorPressSequentiallyOptions { Delay = 40 });
            await input.PressAsync("Enter");

            var (picked, seen) = await PickOptionDetailedAsync(page, item, timeoutMs: 10000, stale);
            var gained = picked != null && await WaitForSelectionAsync(page, input, before, item, picked, 4000);

            if (picked != null && !gained)
            {
                var current = await page.EvaluateAsync<string[]>(CollectOptionsScript);
                var again = Array.FindIndex(current, o => o == picked);
                if (again >= 0)
                {
                    // Same list still showing: the click didn't register. Click it once more.
                    await page.Locator($"[data-jp-opt='{again}']").First.ClickAsync();
                }
                else
                {
                    // The list changed: a hierarchical prompt opened a category. Pick inside it.
                    var (inner, innerSeen) = await PickOptionDetailedAsync(page, item, timeoutMs: 4000, seen);
                    picked = inner ?? picked;
                    if (innerSeen.Length > 0) seen = innerSeen;
                }
                gained = await WaitForSelectionAsync(page, input, before, item, picked, 4000);
            }

            if (!gained)
            {
                unmatched.Add(item);
                notes.Add(picked == null
                    ? (seen.Length == 0
                        ? $"'{item}': Workday showed no results"
                        : $"'{item}': no close match among {string.Join(" / ", seen.Take(5))}")
                    : $"'{item}': clicked '{picked}' but Workday didn't add it");
            }

            // Close the popup and wait until it's gone, so the next search can't read old results.
            await page.Keyboard.PressAsync("Escape");
            for (var i = 0; i < 8 && (await page.EvaluateAsync<string[]>(CollectOptionsScript)).Length > 0; i++)
                await page.WaitForTimeoutAsync(150);
        }

        var finalSelected = await input.EvaluateAsync<string[]>(SelectedItemsScript);
        var readBack = string.Join(", ", finalSelected);

        if (unmatched.Count == 0)
            return new FillResult(true, readBack, null);

        var detail = string.Join("; ", notes);
        if (multi && finalSelected.Length > 0)
            return new FillResult(true, readBack, $"Partly filled. Not added: {detail}. Edit the value to use Workday's names for these.");

        return new FillResult(false, readBack, $"{detail}. Edit the value to match an option Workday offers.");
    }

    /// <summary>Polls until the selection grows or contains the item / clicked option.</summary>
    private static async Task<bool> WaitForSelectionAsync(IPage page, ILocator input, string[] before, string item, string? picked, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var after = await input.EvaluateAsync<string[]>(SelectedItemsScript);
            if (after.Length > before.Length ||
                after.Any(s => MatchScore(s, item) >= 80 || (picked != null && MatchScore(s, picked) >= 80)))
                return true;
            await page.WaitForTimeoutAsync(250);
        }
        return false;
    }

    // Workday dropdowns are buttons that open a listbox.
    private async Task<FillResult> FillDropdownAsync(IPage page, ILocator button, string target, CancellationToken ct)
    {
        var current = (await button.InnerTextAsync()).Trim();
        if (MatchScore(current, target) >= 60) return new FillResult(true, current, null);

        await button.ClickAsync();
        var picked = await PickOptionAsync(page, target, timeoutMs: 4000);

        if (picked == null)
        {
            // Long lists may be virtualised: type-ahead jumps to the matching entry.
            await page.Keyboard.TypeAsync(target, new KeyboardTypeOptions { Delay = 30 });
            await page.Keyboard.PressAsync("Enter");
        }

        await page.WaitForTimeoutAsync(300);
        var readBack = (await button.InnerTextAsync()).Trim();
        if (MatchScore(readBack, target) >= 30)
        {
            return new FillResult(true, readBack, null);
        }

        var available = await page.EvaluateAsync<string[]>(CollectOptionsScript);
        await page.Keyboard.PressAsync("Escape");
        var hint = available.Length > 0 ? $" Options include: {string.Join(", ", available.Take(8))}." : "";
        return new FillResult(false, readBack, $"No dropdown option matching '{target}'.{hint}");
    }

    private async Task<FillResult> FillRadioAsync(IPage page, FieldDescriptor field, string target)
    {
        var name = GetRecipeValue(field, "name");
        var radios = !string.IsNullOrWhiteSpace(name)
            ? page.Locator($"input[type='radio'][name='{CssAttr(name)}']")
            : ResolveLocator(page, field);

        var labels = await radios.EvaluateAllAsync<string[]>("""
            els => els.map(r => {
              const l = r.id ? document.querySelector(`label[for="${CSS.escape(r.id)}"]`) : null;
              return ((l && l.innerText) || r.getAttribute('aria-label') || r.value || '').replace(/\s+/g, ' ').trim();
            })
            """);

        var wanted = NormalizeBoolean(target);
        var idx = BestMatchIndex(labels, wanted);
        if (idx < 0)
        {
            return new FillResult(false, null, $"No radio option matching '{target}'. Options: {string.Join(", ", labels)}.");
        }

        var radio = radios.Nth(idx);
        await radio.CheckAsync(new LocatorCheckOptions { Force = true });
        await page.WaitForTimeoutAsync(150);

        return await radio.IsCheckedAsync()
            ? new FillResult(true, labels[idx], null)
            : new FillResult(false, null, $"Could not select '{labels[idx]}'.");
    }

    // Workday dates are separate month / day / year spinbutton inputs, 8px wide and covered by a
    // "display" div, so clicking them fails ("element is outside of the viewport"). Instead: scroll the
    // date into view, give each part keyboard focus and type its digits, then Tab to commit.
    private static async Task<FillResult> FillDateAsync(IPage page, ILocator firstPart, string target)
    {
        var (month, day, year) = ParseDate(target);
        if (year == null)
        {
            return new FillResult(false, null, $"Could not read '{target}' as a date. Use MM/YYYY or MM/DD/YYYY.");
        }

        var wrapper = firstPart.Locator("xpath=ancestor::*[@data-automation-id='dateInputWrapper'][1]");
        if (await wrapper.CountAsync() == 0)
            wrapper = firstPart.Locator("xpath=ancestor::*[starts-with(@data-automation-id,'formField')][1]");
        await wrapper.ScrollIntoViewIfNeededAsync();

        async Task TypePart(string partId, string? value)
        {
            if (value == null) return;
            var input = wrapper.Locator($"input[data-automation-id='{partId}-input']").First;
            if (await input.CountAsync() == 0) return;
            await input.FocusAsync();
            await page.Keyboard.TypeAsync(value, new KeyboardTypeOptions { Delay = 60 });
            await page.WaitForTimeoutAsync(120);
        }

        await TypePart("dateSectionMonth", month);
        await TypePart("dateSectionDay", day);
        await TypePart("dateSectionYear", year);
        await page.Keyboard.PressAsync("Tab"); // commit, so Workday validates and saves the value
        await page.WaitForTimeoutAsync(200);

        var parts = await wrapper.Locator("input[data-automation-id^='dateSection']").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.value || (/^\\d+$/.test(e.getAttribute('aria-valuetext') || '') ? e.getAttribute('aria-valuetext') : ''))");
        var readBack = string.Join("/", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        var ok = readBack.Contains(year) && (month == null || readBack.Contains(month.TrimStart('0')));
        return ok ? new FillResult(true, readBack, null) : new FillResult(false, readBack, $"Date shows '{readBack}' after typing '{target}'.");
    }

    private static async Task<FillResult> FillCheckboxGroupAsync(IPage page, ILocator firstBox, string target)
    {
        var container = firstBox.Locator("xpath=ancestor::*[starts-with(@data-automation-id,'formField')][1]");
        var boxes = container.Locator("input[type='checkbox']");
        var labels = await boxes.EvaluateAllAsync<string[]>("""
            els => els.map(c => {
              const l = c.id ? document.querySelector(`label[for="${CSS.escape(c.id)}"]`) : null;
              return ((l && l.innerText) || c.getAttribute('aria-label') || c.value || '').replace(/\s+/g, ' ').trim();
            })
            """);

        var unmatched = new List<string>();
        foreach (var item in SplitItems(target))
        {
            var idx = BestMatchIndex(labels, item);
            if (idx < 0) { unmatched.Add(item); continue; }
            await boxes.Nth(idx).SetCheckedAsync(true, new LocatorSetCheckedOptions { Force = true });
        }

        var checkedLabels = new List<string>();
        for (var i = 0; i < labels.Length; i++)
        {
            if (await boxes.Nth(i).IsCheckedAsync()) checkedLabels.Add(labels[i]);
        }

        var readBack = string.Join(", ", checkedLabels);
        return unmatched.Count == 0
            ? new FillResult(true, readBack, null)
            : new FillResult(false, readBack, $"No checkbox matching: {string.Join(", ", unmatched)}. Options: {string.Join(", ", labels)}.");
    }

    /// <summary>Waits for visible options, clicks the best text match. Returns the clicked text or null.</summary>
    private static async Task<string?> PickOptionAsync(IPage page, string target, int timeoutMs) =>
        (await PickOptionDetailedAsync(page, target, timeoutMs)).Picked;

    /// <summary>
    /// Waits for search results, then clicks the best text match. Returns the clicked text (or
    /// null) and the options that were visible. Results count as ready only when nothing is
    /// loading, they differ from <paramref name="stale"/> (what was showing before the search),
    /// and either one matches well or the list has stopped changing.
    /// </summary>
    private static async Task<(string? Picked, string[] Seen)> PickOptionDetailedAsync(IPage page, string target, int timeoutMs, string[]? stale = null)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string[] options = [];
        string[]? previous = null;
        var stableReads = 0;

        while (DateTime.UtcNow < deadline)
        {
            await page.WaitForTimeoutAsync(250);
            if (await page.EvaluateAsync<bool>(PromptBusyScript)) { stableReads = 0; continue; }

            options = await page.EvaluateAsync<string[]>(CollectOptionsScript);
            var fresh = options.Length > 0 && (stale == null || stale.Length == 0 || !options.SequenceEqual(stale));
            if (!fresh) { previous = null; stableReads = 0; continue; }

            if (options.Length == 1 && options[0].Equals("No Items.", StringComparison.OrdinalIgnoreCase))
            {
                if (++stableReads >= 2) break; // a definite "nothing found"
                continue;
            }

            var best = BestMatchIndex(options, target);
            if (best >= 0 && MatchScore(options[best], target) >= 80) break; // exact / whole-word hit

            stableReads = previous != null && options.SequenceEqual(previous) ? stableReads + 1 : 0;
            if (stableReads >= 3) break; // list settled (~750 ms unchanged)
            previous = options;
        }

        // Re-tag right before clicking so the index matches the live list.
        options = await page.EvaluateAsync<string[]>(CollectOptionsScript);
        var real = options.Where(o => !o.Equals("No Items.", StringComparison.OrdinalIgnoreCase)).ToArray();
        var idx = BestMatchIndex(options, target);
        if (idx < 0 || options[idx].Equals("No Items.", StringComparison.OrdinalIgnoreCase)) return (null, real);

        await page.Locator($"[data-jp-opt='{idx}']").First.ClickAsync();
        return (options[idx], real);
    }

    private static async Task<bool> IsSelectedAsync(ILocator input, string item)
    {
        var selected = await input.EvaluateAsync<string[]>(SelectedItemsScript);
        return selected.Any(s => MatchScore(s, item) >= 30);
    }

    public static int BestMatchIndex(IReadOnlyList<string> options, string target)
    {
        var best = -1;
        var bestScore = 0;
        for (var i = 0; i < options.Count; i++)
        {
            var score = MatchScore(options[i], target);
            // Prefer higher scores; on ties prefer the shorter (more specific) option.
            if (score > bestScore || (score == bestScore && score > 0 && best >= 0 && options[i].Length < options[best].Length))
            {
                best = i;
                bestScore = score;
            }
        }
        return bestScore >= 30 ? best : -1;
    }

    public static int MatchScore(string option, string target)
    {
        var o = NormalizeText(option);
        var t = NormalizeText(target);
        if (o.Length == 0 || t.Length == 0) return 0;
        if (o == t) return 100;
        if (Regex.IsMatch(o, $@"(^| ){Regex.Escape(t)}( |$)")) return 80; // whole-word, e.g. "+91" in "india (+91)"
        if (o.StartsWith(t, StringComparison.Ordinal)) return 60;
        if (o.Contains(t, StringComparison.Ordinal)) return 40;
        if (o.Length >= 3 && t.Contains(o, StringComparison.Ordinal)) return 30;
        return 0;
    }

    // Keeps characters that matter in skills and codes: "C#", "C++", ".NET", "+91".
    private static string NormalizeText(string s) =>
        Regex.Replace(Regex.Replace((s ?? "").ToLowerInvariant(), @"[^a-z0-9#+.]+", " "), @"\s+", " ").Trim();

    private static bool SameText(string? a, string? b)
    {
        var x = NormalizeText(a ?? "");
        var y = NormalizeText(b ?? "");
        return x == y || (x.Length > 0 && y.Length > 0 && (x.Contains(y) || y.Contains(x)));
    }

    private static List<string> SplitItems(string value) =>
        value.Split(new[] { ',', ';', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Where(s => s.Length > 0)
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .ToList();

    private static bool IsTruthy(string v) =>
        v.Trim().ToLowerInvariant() is "true" or "yes" or "y" or "1" or "checked" or "on";

    private static string NormalizeBoolean(string v) => v.Trim().ToLowerInvariant() switch
    {
        "true" or "y" => "Yes",
        "false" or "n" => "No",
        _ => v
    };

    public static (string? Month, string? Day, string? Year) ParseDate(string value)
    {
        var v = value.Trim();
        var iso = Regex.Match(v, @"^(\d{4})-(\d{1,2})(?:-(\d{1,2}))?");
        if (iso.Success)
            return (iso.Groups[2].Value.PadLeft(2, '0'), iso.Groups[3].Success ? iso.Groups[3].Value.PadLeft(2, '0') : null, iso.Groups[1].Value);

        var us = Regex.Match(v, @"^(\d{1,2})[/.\-](?:(\d{1,2})[/.\-])?(\d{4})$");
        if (us.Success)
            return (us.Groups[1].Value.PadLeft(2, '0'), us.Groups[2].Success ? us.Groups[2].Value.PadLeft(2, '0') : null, us.Groups[3].Value);

        var year = Regex.Match(v, @"^\d{4}$");
        return year.Success ? (null, null, v) : (null, null, null);
    }
}
