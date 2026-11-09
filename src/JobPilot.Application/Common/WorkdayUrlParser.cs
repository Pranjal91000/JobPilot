using System.Text.RegularExpressions;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.ValueObjects;

namespace JobPilot.Application.Common;

public static class WorkdayUrlParser
{
    // Matches {tenant}.wd{1..100}.myworkdayjobs.com or {tenant}.myworkdayjobs.com
    private static readonly Regex HostRegex = new(
        @"^(?<tenant>[a-z0-9_-]+)\.(?:wd\d+\.)?myworkdayjobs\.com$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Path segments: optional locale, site, and job slug
    private static readonly Regex PathRegex = new(
        @"^/(?:(?<locale>[a-z]{2}(?:-[a-z]{2,4})?)/)?(?<site>[^/]+)/job/(?:[^/]+/)?(?<slug>[^/?#]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReqIdRegex = new(
        @"[_-](?<reqId>(?:R-?|JR|REQ-?|[A-Z]{1,4}-?)\d{3,10}|\d{5,10})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static UrlClassification Classify(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return new UrlClassification(
                IsSupported: false,
                Ats: "unknown",
                TenantKey: null,
                Host: null,
                SiteKey: null,
                ExternalJobId: null,
                Locale: null,
                NormalizedUrl: string.Empty);
        }

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return new UrlClassification(
                IsSupported: false,
                Ats: "invalid",
                TenantKey: null,
                Host: null,
                SiteKey: null,
                ExternalJobId: null,
                Locale: null,
                NormalizedUrl: string.Empty);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return new UrlClassification(
                IsSupported: false,
                Ats: "unsupported-scheme",
                TenantKey: null,
                Host: uri.Host,
                SiteKey: null,
                ExternalJobId: null,
                Locale: null,
                NormalizedUrl: string.Empty);
        }

        var hostMatch = HostRegex.Match(uri.Host);
        if (!hostMatch.Success)
        {
            return new UrlClassification(
                IsSupported: false,
                Ats: "unsupported",
                TenantKey: null,
                Host: uri.Host,
                SiteKey: null,
                ExternalJobId: null,
                Locale: null,
                NormalizedUrl: string.Empty);
        }

        var tenantKey = hostMatch.Groups["tenant"].Value.ToLowerInvariant();
        var normalizedUrl = NormalizedJobUrl.Normalize(rawUrl);

        string? siteKey = null;
        string? locale = null;
        string? externalJobId = null;

        var pathMatch = PathRegex.Match(uri.AbsolutePath);
        if (pathMatch.Success)
        {
            if (pathMatch.Groups["locale"].Success)
                locale = pathMatch.Groups["locale"].Value;

            siteKey = pathMatch.Groups["site"].Value;

            var slug = pathMatch.Groups["slug"].Value;
            var lastUnderscore = slug.LastIndexOf('_');
            if (lastUnderscore >= 0 && lastUnderscore < slug.Length - 1)
            {
                externalJobId = slug[(lastUnderscore + 1)..];
            }
            else
            {
                var reqMatch = ReqIdRegex.Match(slug);
                if (reqMatch.Success)
                {
                    externalJobId = reqMatch.Groups["reqId"].Value;
                }
            }
        }

        return new UrlClassification(
            IsSupported: true,
            Ats: "workday",
            TenantKey: tenantKey,
            Host: uri.Host.ToLowerInvariant(),
            SiteKey: siteKey,
            ExternalJobId: externalJobId,
            Locale: locale,
            NormalizedUrl: normalizedUrl);
    }
}
