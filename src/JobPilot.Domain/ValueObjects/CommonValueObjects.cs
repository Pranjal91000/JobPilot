using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JobPilot.Domain.ValueObjects;

public sealed record NormalizedJobUrl
{
    private static readonly Regex LocaleRegex = new(@"/(?:en|fr|de|es|it|nl|pt|ja|zh|ko)(?:-[a-z]{2,4})?/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ApplySubpathRegex = new(@"/apply.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Value { get; }

    public NormalizedJobUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be empty.", nameof(url));

        Value = Normalize(url);
    }

    public static string Normalize(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid URL format: '{rawUrl}'", nameof(rawUrl));

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Only HTTP/HTTPS URLs are supported.", nameof(rawUrl));

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath;

        // Remove locale segment
        path = LocaleRegex.Replace(path, "/");

        // Remove apply subpath
        path = ApplySubpathRegex.Replace(path, string.Empty);

        // Normalize slashes
        path = Regex.Replace(path, @"/{2,}", "/");
        if (path.Length > 1 && path.EndsWith('/'))
            path = path[..^1];

        path = path.ToLowerInvariant();

        return $"{scheme}://{host}{path}";
    }

    public override string ToString() => Value;
    public static implicit operator string(NormalizedJobUrl url) => url.Value;
}

public sealed record ProfilePath
{
    public string Value { get; }

    public ProfilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Profile path cannot be empty.", nameof(path));

        Value = path.Trim();
    }

    public override string ToString() => Value;
    public static implicit operator string(ProfilePath p) => p.Value;
}

public sealed record ConfidenceScore
{
    public decimal Value { get; }

    public ConfidenceScore(decimal value)
    {
        Value = Math.Clamp(value, 0.0m, 1.0m);
    }

    public static ConfidenceScore Zero => new(0m);
    public static ConfidenceScore Max => new(1m);

    public override string ToString() => Value.ToString("0.000");
    public static implicit operator decimal(ConfidenceScore c) => c.Value;
}

public static class FieldFingerprint
{
    public static string Create(string stepKey, string sectionKey, int repeatIndex, string? identifierOrLabel, string fieldType)
    {
        var raw = $"{stepKey.Trim()}|{sectionKey.Trim()}|{repeatIndex}|{(identifierOrLabel ?? string.Empty).Trim().ToLowerInvariant()}|{fieldType.Trim()}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash);
    }
}

