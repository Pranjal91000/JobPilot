using FluentAssertions;
using JobPilot.Application.Common;

namespace JobPilot.Application.Tests;

public class WorkdayUrlParserTests
{
    [Theory]
    // 1-10: Standard Workday public URLs across various tenants, data-centres, and locales
    [InlineData("https://acme.wd5.myworkdayjobs.com/en-US/AcmeCareers/job/Pune-India/Senior-NET-Developer_R-0123456", "acme", "AcmeCareers", "R-0123456", "en-US", true)]
    [InlineData("https://nvidia.wd5.myworkdayjobs.com/NVIDIAExternalCareerSite/job/US-CA-Santa-Clara/Senior-System-Software-Engineer_JR1982736", "nvidia", "NVIDIAExternalCareerSite", "JR1982736", null, true)]
    [InlineData("https://adobe.wd5.myworkdayjobs.com/en-US/external_experienced/job/San-Jose-CA/Lead-Architect_24-10023", "adobe", "external_experienced", "24-10023", "en-US", true)]
    [InlineData("https://salesforce.wd1.myworkdayjobs.com/fr-FR/External_Career_Site/job/Paris-France/Staff-Engineer_JR209182", "salesforce", "External_Career_Site", "JR209182", "fr-FR", true)]
    [InlineData("https://target.wd5.myworkdayjobs.com/targetcareers/job/Minneapolis-MN/Software-Engineer_R0000345678", "target", "targetcareers", "R0000345678", null, true)]
    [InlineData("https://walmart.wd5.myworkdayjobs.com/WalmartExternal/job/Bentonville-AR/Principal-Data-Scientist_WD987654", "walmart", "WalmartExternal", "WD987654", null, true)]
    [InlineData("https://citi.wd5.myworkdayjobs.com/en-US/2/job/London-UK/VP-Cloud-Platform_24789123", "citi", "2", "24789123", "en-US", true)]
    [InlineData("https://dell.wd1.myworkdayjobs.com/DellCareers/job/Bangalore-India/Director-Engineering_R239012", "dell", "DellCareers", "R239012", null, true)]
    [InlineData("https://sony.wd1.myworkdayjobs.com/en-US/Sony_Careers/job/Tokyo-Japan/Game-Programmer_REQ-12345", "sony", "Sony_Careers", "REQ-12345", "en-US", true)]
    [InlineData("https://hp.wd5.myworkdayjobs.com/HP_Careers/job/Spring-TX/Firmware-Dev_3129845", "hp", "HP_Careers", "3129845", null, true)]
    // 11-15: Direct tenant host without wdN shard
    [InlineData("https://mycompany.myworkdayjobs.com/en-US/Careers/job/Remote/Senior-Engineer_R-99901", "mycompany", "Careers", "R-99901", "en-US", true)]
    [InlineData("https://globalcorp.myworkdayjobs.com/Global/job/New-York/Accountant_JR-11223", "globalcorp", "Global", "JR-11223", null, true)]
    [InlineData("https://fintech.myworkdayjobs.com/es-ES/Jobs/job/Madrid/DevOps_REQ987", "fintech", "Jobs", "REQ987", "es-ES", true)]
    [InlineData("https://healthtech.myworkdayjobs.com/External/job/Boston/Bioinformatician_982341", "healthtech", "External", "982341", null, true)]
    [InlineData("https://auto.myworkdayjobs.com/AutoCareers/job/Detroit/Controls-Engineer_R-44332", "auto", "AutoCareers", "R-44332", null, true)]
    // 16-20: URLs with apply subpaths, trailing slashes, or query strings
    [InlineData("https://acme.wd5.myworkdayjobs.com/en-US/AcmeCareers/job/Pune-India/Senior-NET-Developer_R-0123456/apply", "acme", "AcmeCareers", "R-0123456", "en-US", true)]
    [InlineData("https://acme.wd5.myworkdayjobs.com/AcmeCareers/job/Pune-India/Senior-NET-Developer_R-0123456/?source=LinkedIn", "acme", "AcmeCareers", "R-0123456", null, true)]
    [InlineData("https://acme.wd5.myworkdayjobs.com/AcmeCareers/job/Senior-NET-Developer_R-0123456/", "acme", "AcmeCareers", "R-0123456", null, true)]
    [InlineData("https://acme.wd5.myworkdayjobs.com/AcmeCareers/job/Bengaluru/Developer_R-0123456#apply-section", "acme", "AcmeCareers", "R-0123456", null, true)]
    [InlineData("https://acme.wd5.myworkdayjobs.com/en-US/AcmeCareers/job/Pune-India/Senior-NET-Developer_R-0123456/apply/applyManually", "acme", "AcmeCareers", "R-0123456", "en-US", true)]
    // 21-25: Requisition IDs without location segment
    [InlineData("https://bank.wd3.myworkdayjobs.com/Careers/job/Senior-Auditor_R-771122", "bank", "Careers", "R-771122", null, true)]
    [InlineData("https://telecom.wd2.myworkdayjobs.com/en-US/Jobs/job/RF-Engineer_JR10098", "telecom", "Jobs", "JR10098", "en-US", true)]
    [InlineData("https://biotech.wd4.myworkdayjobs.com/de-DE/Science/job/Research-Associate_REQ-5432", "biotech", "Science", "REQ-5432", "de-DE", true)]
    [InlineData("https://pharma.wd1.myworkdayjobs.com/PharmaJobs/job/Chemist_998877", "pharma", "PharmaJobs", "998877", null, true)]
    [InlineData("https://insurance.wd5.myworkdayjobs.com/en-US/Claims/job/Claims-Analyst_R-112233", "insurance", "Claims", "R-112233", "en-US", true)]
    public void Classify_ValidWorkdayUrls_ShouldExtractMetadataCorrectly(
        string url, string expectedTenant, string expectedSite, string expectedReqId, string? expectedLocale, bool expectedSupported)
    {
        var result = WorkdayUrlParser.Classify(url);

        result.IsSupported.Should().Be(expectedSupported);
        result.Ats.Should().Be("workday");
        result.TenantKey.Should().Be(expectedTenant);
        result.SiteKey.Should().Be(expectedSite);
        result.ExternalJobId.Should().Be(expectedReqId);
        result.Locale.Should().Be(expectedLocale);
    }

    [Theory]
    // 26-35: Unsupported portals and malformed URLs
    [InlineData("https://boards.greenhouse.io/stripe/jobs/1234567")]
    [InlineData("https://jobs.lever.co/netflix/98765432-1234-5678-90ab-cdef12345678")]
    [InlineData("https://jobs.smartrecruiters.com/Acme/743999999999999-software-engineer")]
    [InlineData("https://careers.icims.com/jobs/12345/job")]
    [InlineData("https://www.linkedin.com/jobs/view/1234567890/")]
    [InlineData("https://www.naukri.com/job-listings-senior-developer-12345")]
    [InlineData("https://randomcompany.com/careers/job/123")]
    [InlineData("ftp://acme.wd5.myworkdayjobs.com/job/123")]
    [InlineData("not-a-valid-url")]
    [InlineData("")]
    public void Classify_UnsupportedOrInvalidUrls_ShouldReturnNotSupported(string url)
    {
        var result = WorkdayUrlParser.Classify(url);
        result.IsSupported.Should().BeFalse();
    }
}
