using JobPilot.Application.Abstractions;
using JobPilot.AutomationWorker.Adapters;
using JobPilot.Domain.Enums;
using Microsoft.Playwright;

namespace JobPilot.Automation.Tests;

/// <summary>
/// Runs the Greenhouse adapter against a local mock of a Greenhouse application form
/// (fixtures/mock-greenhouse/apply.html) in headless Chromium. No database, no network.
/// </summary>
public sealed class SinglePageFormAdapterTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;
    private readonly GreenhouseAdapter _adapter = new();
    private string _resumePath = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _page = await _browser.NewPageAsync();
        await _page.GotoAsync(new Uri(FindFixture("mock-greenhouse", "apply.html")).AbsoluteUri);

        _resumePath = Path.Combine(Path.GetTempPath(), $"jp-test-resume-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(_resumePath, "%PDF-1.4 test");
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
        if (File.Exists(_resumePath)) File.Delete(_resumePath);
    }

    // Walk up from the test binaries (normal or --artifacts-path builds) to the repo's fixtures.
    private static string FindFixture(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName, "fixtures" }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        // --artifacts-path builds live outside the repo; fall back to the source tree.
        var fromSource = Path.Combine(new[] { "F:", "JobPilot", "fixtures" }.Concat(parts).ToArray());
        if (File.Exists(fromSource)) return fromSource;
        throw new FileNotFoundException($"Fixture {string.Join('/', parts)} not found from {AppContext.BaseDirectory}");
    }

    private async Task<Dictionary<string, FieldDescriptor>> InspectAsync()
    {
        var fields = await _adapter.InspectFieldsAsync(_page, null);
        return fields.ToDictionary(f => f.Label);
    }

    [Fact]
    public async Task Detects_the_form_and_every_control_with_its_question()
    {
        Assert.Equal(AuthState.InApplyFlow, await _adapter.DetectAuthStateAsync(_page));

        var f = await InspectAsync();

        Assert.Equal(FieldType.Text, f["First Name"].Type);
        Assert.True(f["First Name"].Required);
        Assert.False(f["Phone"].Required);
        Assert.Equal(FieldType.FileUpload, f["Resume/CV"].Type);
        Assert.True(f["Resume/CV"].Required);
        Assert.Equal(FieldType.SingleSelect, f["Are you legally authorized to work in India?"].Type);
        Assert.NotNull(f["Are you legally authorized to work in India?"].AvailableOptions);
        Assert.Equal(FieldType.SingleSelect, f["How did you hear about us?"].Type); // combobox
        Assert.Equal(FieldType.Radio, f["Are you willing to relocate?"].Type);
        Assert.True(f["Are you willing to relocate?"].Required);
        Assert.Equal(FieldType.MultiSelect, f["Which languages do you speak?"].Type);
        Assert.Equal(FieldType.Text, f["Why do you want to work at Acme?"].Type);
        Assert.Equal(FieldType.Date, f["Earliest start date"].Type);
        Assert.Equal(FieldType.Checkbox, f["I agree to the privacy notice"].Type);
        Assert.DoesNotContain(f.Values, x => x.Label.Contains("Submit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Fills_every_control_type_and_reads_it_back()
    {
        var f = await InspectAsync();

        async Task Fill(string label, string value) =>
            Assert.True((await _adapter.FillFieldAsync(_page, f[label], value)).Success, $"Filling '{label}' failed");

        await Fill("First Name", "Pranjal");
        await Fill("Email", "pranjal@example.com");
        await Fill("Resume/CV", _resumePath);
        await Fill("Are you legally authorized to work in India?", "Yes");
        await Fill("How did you hear about us?", "LinkedIn");
        await Fill("Are you willing to relocate?", "true");          // booleans map to Yes/No
        await Fill("Which languages do you speak?", "English, Hindi");
        await Fill("Why do you want to work at Acme?", "I build reliable .NET services.");
        await Fill("Earliest start date", "03/2026");
        await Fill("I agree to the privacy notice", "true");

        Assert.Equal("Pranjal", await _page.InputValueAsync("#first_name"));
        Assert.Equal("1", await _page.InputValueAsync("#question_2"));
        Assert.Equal("LinkedIn", await _page.InnerTextAsync("#question_3-value"));
        Assert.True(await _page.IsCheckedAsync("#relocate_yes"));
        Assert.True(await _page.IsCheckedAsync("input[value='en']"));
        Assert.True(await _page.IsCheckedAsync("input[value='hi']"));
        Assert.False(await _page.IsCheckedAsync("input[value='fr']"));
        Assert.Equal("2026-03-01", await _page.InputValueAsync("#start_date"));
        Assert.Equal(1, await _page.EvalOnSelectorAsync<int>("#resume", "el => el.files.length"));

        // Never submitted.
        Assert.False(await _adapter.DetectSubmissionConfirmationAsync(_page));
    }

    [Fact]
    public async Task Unknown_combobox_option_is_reported_with_what_was_offered()
    {
        var f = await InspectAsync();
        var result = await _adapter.FillFieldAsync(_page, f["How did you hear about us?"], "Billboard");
        Assert.False(result.Success);
        Assert.Contains("Billboard", result.Error);
    }
}
