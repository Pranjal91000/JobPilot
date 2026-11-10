using FluentAssertions;
using JobPilot.Application.Common;

namespace JobPilot.Application.Tests;

public class AtsUrlClassifierTests
{
    [Theory]
    [InlineData("https://boards.greenhouse.io/acme/jobs/4012345", "acme", "4012345", "https://boards.greenhouse.io/acme/jobs/4012345")]
    [InlineData("https://job-boards.greenhouse.io/acme/jobs/4012345?gh_src=abc", "acme", "4012345", "https://job-boards.greenhouse.io/acme/jobs/4012345")]
    [InlineData("https://job-boards.eu.greenhouse.io/Acme/jobs/77", "acme", "77", "https://job-boards.eu.greenhouse.io/acme/jobs/77")]
    [InlineData("https://boards.greenhouse.io/embed/job_app?for=acme&token=555", "acme", "555", "https://boards.greenhouse.io/embed/job_app?for=acme&token=555")]
    public void Greenhouse_board_links(string url, string tenant, string jobId, string normalized)
    {
        var c = AtsUrlClassifier.Classify(url);
        c.IsSupported.Should().BeTrue();
        c.Ats.Should().Be(AtsKeys.Greenhouse);
        c.TenantKey.Should().Be(tenant);
        c.ExternalJobId.Should().Be(jobId);
        c.NormalizedUrl.Should().Be(normalized);
    }

    [Fact]
    public void Greenhouse_jobs_on_a_company_site_open_the_embedded_form()
    {
        var c = AtsUrlClassifier.Classify("https://careers.acme.com/open-roles/?gh_jid=4012345&utm_source=x");
        c.Ats.Should().Be(AtsKeys.Greenhouse);
        c.TenantKey.Should().Be("acme");
        c.ExternalJobId.Should().Be("4012345");
        c.NormalizedUrl.Should().Be("https://boards.greenhouse.io/embed/job_app?for=acme&token=4012345");
    }

    [Theory]
    [InlineData("https://jobs.lever.co/acme/3f2c1d4e-5b6a-4c7d-8e9f-0a1b2c3d4e5f")]
    [InlineData("https://jobs.lever.co/acme/3f2c1d4e-5b6a-4c7d-8e9f-0a1b2c3d4e5f/apply?lever-source=LinkedIn")]
    public void Lever_links_normalise_to_the_posting(string url)
    {
        var c = AtsUrlClassifier.Classify(url);
        c.Ats.Should().Be(AtsKeys.Lever);
        c.TenantKey.Should().Be("acme");
        c.NormalizedUrl.Should().Be("https://jobs.lever.co/acme/3f2c1d4e-5b6a-4c7d-8e9f-0a1b2c3d4e5f");
    }

    [Fact]
    public void Ashby_links_normalise_to_the_posting()
    {
        var c = AtsUrlClassifier.Classify("https://jobs.ashbyhq.com/Acme/3f2c1d4e-5b6a-4c7d-8e9f-0a1b2c3d4e5f/application");
        c.Ats.Should().Be(AtsKeys.Ashby);
        c.TenantKey.Should().Be("acme");
        c.NormalizedUrl.Should().Be("https://jobs.ashbyhq.com/acme/3f2c1d4e-5b6a-4c7d-8e9f-0a1b2c3d4e5f");
    }

    [Fact]
    public void Workday_links_are_unchanged()
    {
        var c = AtsUrlClassifier.Classify("https://cisco.wd5.myworkdayjobs.com/en-US/Cisco_Careers/job/RTP/Engineer_2028795");
        c.Ats.Should().Be(AtsKeys.Workday);
        c.TenantKey.Should().Be("cisco");
    }

    [Theory]
    [InlineData("https://www.linkedin.com/jobs/view/123")]
    [InlineData("https://jobs.lever.co/acme")]               // company page, not a job
    [InlineData("https://boards.greenhouse.io/acme")]        // board index, not a job
    public void Other_links_are_not_supported(string url)
    {
        AtsUrlClassifier.Classify(url).IsSupported.Should().BeFalse();
    }
}
