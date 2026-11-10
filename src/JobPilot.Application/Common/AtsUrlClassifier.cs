using System.Text.RegularExpressions;
using JobPilot.Application.Abstractions;

namespace JobPilot.Application.Common;

/// <summary>Applicant tracking systems JobPilot can fill.</summary>
public static class AtsKeys
{
    public const string Workday = "workday";
    public const string Greenhouse = "greenhouse";
    public const string Lever = "lever";
    public const string Ashby = "ashby";

    /// <summary>ATSs whose application is a single page with no candidate account.</summary>
    public static bool IsSinglePageForm(string? ats) => ats is Greenhouse or Lever or Ashby;

    public static string DisplayName(string? ats) => ats switch
    {
        Workday => "Workday",
        Greenhouse => "Greenhouse",
        Lever => "Lever",
        Ashby => "Ashby",
        _ => ats ?? "Unknown"
    };
}

/// <summary>
/// Recognises job links from every supported ATS and normalises them, so the same job pasted twice
/// (with tracking parameters, /apply suffixes, etc.) is detected as a duplicate.
/// </summary>
public static class AtsUrlClassifier
{
    // boards.greenhouse.io / job-boards.greenhouse.io (and .eu.) /{company}/jobs/{id}
    private static readonly Regex GreenhouseHost = new(@"^(job-)?boards(\.eu)?\.greenhouse\.io$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GreenhousePath = new(@"^/(?<company>[^/]+)/jobs/(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // jobs.lever.co (and jobs.eu.lever.co) /{company}/{uuid}[/apply]
    private static readonly Regex LeverHost = new(@"^jobs(\.eu)?\.lever\.co$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LeverPath = new(@"^/(?<company>[^/]+)/(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // jobs.ashbyhq.com/{company}/{uuid}[/application]
    private static readonly Regex AshbyPath = new(@"^/(?<company>[^/]+)/(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static UrlClassification Classify(string rawUrl)
    {
        var workday = WorkdayUrlParser.Classify(rawUrl);
        if (workday.IsSupported || workday.Ats is "invalid" or "unsupported-scheme" || string.IsNullOrWhiteSpace(rawUrl))
            return workday;

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
            return workday;

        var host = uri.Host.ToLowerInvariant();
        var query = ParseQuery(uri.Query);

        // Greenhouse boards
        if (GreenhouseHost.IsMatch(host))
        {
            var m = GreenhousePath.Match(uri.AbsolutePath);
            if (m.Success)
            {
                var company = m.Groups["company"].Value.ToLowerInvariant();
                var id = m.Groups["id"].Value;
                return Supported(AtsKeys.Greenhouse, company, host, id, $"https://{host}/{company}/jobs/{id}");
            }

            // Embedded application: /embed/job_app?for={company}&token={id}
            if (uri.AbsolutePath.StartsWith("/embed/job_app", StringComparison.OrdinalIgnoreCase) &&
                query.TryGetValue("token", out var token) && Regex.IsMatch(token, @"^\d+$"))
            {
                var company = query.TryGetValue("for", out var f) && f.Length > 0 ? f.ToLowerInvariant() : "greenhouse";
                return Supported(AtsKeys.Greenhouse, company, host, token, EmbedUrl(company, token));
            }
        }

        // Greenhouse jobs embedded on a company's own careers site: ...?gh_jid=12345
        if (query.TryGetValue("gh_jid", out var ghJid) && Regex.IsMatch(ghJid, @"^\d+$"))
        {
            var company = CompanyFromHost(host);
            return Supported(AtsKeys.Greenhouse, company, host, ghJid, EmbedUrl(company, ghJid));
        }

        // Lever
        if (LeverHost.IsMatch(host))
        {
            var m = LeverPath.Match(uri.AbsolutePath);
            if (m.Success)
            {
                var company = m.Groups["company"].Value.ToLowerInvariant();
                var id = m.Groups["id"].Value.ToLowerInvariant();
                return Supported(AtsKeys.Lever, company, host, id, $"https://{host}/{company}/{id}");
            }
        }

        // Ashby
        if (host == "jobs.ashbyhq.com")
        {
            var m = AshbyPath.Match(uri.AbsolutePath);
            if (m.Success)
            {
                var company = m.Groups["company"].Value.ToLowerInvariant();
                var id = m.Groups["id"].Value.ToLowerInvariant();
                return Supported(AtsKeys.Ashby, company, host, id, $"https://{host}/{company}/{id}");
            }
        }

        return workday; // "unsupported" with the host filled in
    }

    // The embed URL renders the Greenhouse form directly (no iframe), which the worker can fill.
    private static string EmbedUrl(string company, string jobId) =>
        $"https://boards.greenhouse.io/embed/job_app?for={Uri.EscapeDataString(company)}&token={jobId}";

    private static UrlClassification Supported(string ats, string tenant, string host, string jobId, string normalized) =>
        new(IsSupported: true, Ats: ats, TenantKey: tenant, Host: host, SiteKey: null,
            ExternalJobId: jobId, Locale: null, NormalizedUrl: normalized);

    // careers.acme.com -> "acme"; www.acme.co.uk -> "acme"
    private static string CompanyFromHost(string host)
    {
        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var generic = new HashSet<string> { "www", "careers", "jobs", "boards", "apply", "co", "com", "org", "net", "io", "uk", "in" };
        return labels.Reverse().Skip(1).FirstOrDefault(l => !generic.Contains(l)) ?? labels.FirstOrDefault() ?? "company";
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            result[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
        }
        return result;
    }
}
