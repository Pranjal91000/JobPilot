using JobPilot.AutomationWorker.Adapters;

namespace JobPilot.Automation.Tests;

public class OptionMatchingTests
{
    [Fact]
    public void Picks_exact_match_over_partial()
    {
        var options = new[] { "Java", "JavaScript", "Java EE" };
        Assert.Equal(0, WorkdayAdapter.BestMatchIndex(options, "java"));
    }

    [Fact]
    public void Matches_phone_code_as_whole_token_not_inside_other_codes()
    {
        var options = new[] { "Bolivia (+591)", "India (+91)", "Ireland (+353)" };
        Assert.Equal(1, WorkdayAdapter.BestMatchIndex(options, "+91"));
    }

    [Fact]
    public void Keeps_symbols_that_distinguish_skills()
    {
        var options = new[] { "C", "C++", "C#" };
        Assert.Equal(2, WorkdayAdapter.BestMatchIndex(options, "C#"));
        Assert.Equal(1, WorkdayAdapter.BestMatchIndex(options, "c++"));
    }

    [Fact]
    public void Prefers_shorter_option_on_ties()
    {
        var options = new[] { "LinkedIn Jobs Board", "LinkedIn" };
        Assert.Equal(1, WorkdayAdapter.BestMatchIndex(options, "LinkedIn"));
    }

    [Fact]
    public void Returns_minus_one_when_nothing_matches()
    {
        Assert.Equal(-1, WorkdayAdapter.BestMatchIndex(new[] { "Yes", "No" }, "Maybe"));
    }

    [Theory]
    [InlineData("United States of America", "United States", 60)]
    [InlineData("Yes", "yes", 100)]
    public void Scores_are_case_insensitive(string option, string target, int min)
    {
        Assert.True(WorkdayAdapter.MatchScore(option, target) >= min);
    }
}

public class DateParsingTests
{
    [Theory]
    [InlineData("01/2025", "01", null, "2025")]
    [InlineData("01-2025", "01", null, "2025")]   // what en-IN formatting produced
    [InlineData("1.2025", "01", null, "2025")]
    [InlineData("03/15/2024", "03", "15", "2024")]
    [InlineData("2024-03", "03", null, "2024")]
    [InlineData("2024-03-15", "03", "15", "2024")]
    [InlineData("2019", null, null, "2019")]
    public void Parses_common_formats(string input, string? month, string? day, string year)
    {
        var (m, d, y) = WorkdayAdapter.ParseDate(input);
        Assert.Equal(month, m);
        Assert.Equal(day, d);
        Assert.Equal(year, y);
    }

    [Fact]
    public void Rejects_text()
    {
        Assert.Null(WorkdayAdapter.ParseDate("present").Year);
    }
}
