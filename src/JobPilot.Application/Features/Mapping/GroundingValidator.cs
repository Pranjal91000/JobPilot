using System.Text.Json;
using System.Text.RegularExpressions;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;

namespace JobPilot.Application.Features.Mapping;

public sealed class GroundingValidator : IGroundingValidator
{
    public (bool IsGrounded, string? GroundedValue, string Reason) Validate(
        string profilePath,
        string? transform,
        string? optionKey,
        FieldDescriptor field,
        VerifiedProfileSnapshot profile)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            return (false, null, "No profile path provided for grounding.");
        }

        // 1. Check if profilePath exists in verified facts
        if (!profile.VerifiedFacts.TryGetValue(profilePath, out var rawFact) || rawFact == null)
        {
            return (false, null, $"Profile path '{profilePath}' does not exist or is not marked Verified in user profile.");
        }

        var factStr = rawFact.ToString() ?? string.Empty;

        // 2. Apply deterministic server-side transform if requested
        var transformedValue = factStr;
        if (!string.IsNullOrWhiteSpace(transform) && !transform.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            transformedValue = ApplyTransform(transform, rawFact, factStr);
        }

        // 3. For SingleSelect / Radio / Choice fields, ensure the value matches an available option
        if (field.Type == FieldType.SingleSelect || field.Type == FieldType.Radio)
        {
            if (field.AvailableOptions != null)
            {
                var matchedOption = MatchOption(transformedValue, optionKey, field.AvailableOptions);
                if (matchedOption == null)
                {
                    return (false, null, $"Calculated value '{transformedValue}' does not match any valid option in the field's available options.");
                }

                return (true, matchedOption, $"Successfully grounded to verified profile fact '{profilePath}' matching option '{matchedOption}'.");
            }
        }

        return (true, transformedValue, $"Successfully grounded to verified profile fact '{profilePath}'.");
    }

    private static string ApplyTransform(string transform, object rawFact, string factStr)
    {
        switch (transform.ToLowerInvariant())
        {
            case "monthstoyearsrounded":
                if (decimal.TryParse(factStr, out var months))
                {
                    var years = Math.Round(months / 12m, 1);
                    return years.ToString("0.#");
                }
                break;

            case "numericrangebucket":
                return factStr;

            case "phonenational":
                // Strip +1 or country code if needed
                return Regex.Replace(factStr, @"^\+\d{1,3}", "").Trim();

            case "formatdate":
                if (DateTime.TryParse(factStr, out var dt))
                {
                    return dt.ToString("yyyy-MM-dd");
                }
                break;
        }

        return factStr;
    }

    private static string? MatchOption(string value, string? suggestedOptionKey, JsonDocument availableOptions)
    {
        if (availableOptions.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        var options = new List<(string Key, string Label)>();
        foreach (var opt in availableOptions.RootElement.EnumerateArray())
        {
            if (opt.ValueKind == JsonValueKind.String)
            {
                var s = opt.GetString() ?? "";
                options.Add((s, s));
            }
            else if (opt.ValueKind == JsonValueKind.Object)
            {
                var key = opt.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                var lbl = opt.TryGetProperty("label", out var l) ? l.GetString() ?? "" : key;
                options.Add((key, lbl));
            }
        }

        // Exact match on value or suggestedOptionKey
        var target = suggestedOptionKey ?? value;
        var exact = options.FirstOrDefault(o =>
            o.Key.Equals(target, StringComparison.OrdinalIgnoreCase) ||
            o.Label.Equals(target, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(exact.Key))
            return exact.Key;

        // Numeric range matching (e.g. 7 years matching "6-10")
        if (decimal.TryParse(value, out var num))
        {
            foreach (var opt in options)
            {
                if (IsNumberInRange(num, opt.Label) || IsNumberInRange(num, opt.Key))
                    return opt.Key;
            }
        }

        return null;
    }

    private static bool IsNumberInRange(decimal num, string text)
    {
        // Matches e.g. "5-10", "5 to 10", "10+", "More than 5", "Less than 2"
        var rangeMatch = Regex.Match(text, @"(\d+)\s*(?:-|to)\s*(\d+)");
        if (rangeMatch.Success)
        {
            var low = decimal.Parse(rangeMatch.Groups[1].Value);
            var high = decimal.Parse(rangeMatch.Groups[2].Value);
            return num >= low && num <= high;
        }

        var plusMatch = Regex.Match(text, @"(\d+)\s*\+");
        if (plusMatch.Success)
        {
            var min = decimal.Parse(plusMatch.Groups[1].Value);
            return num >= min;
        }

        return false;
    }
}
