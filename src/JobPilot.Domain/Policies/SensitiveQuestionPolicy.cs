using System.Text.RegularExpressions;
using JobPilot.Domain.Enums;

namespace JobPilot.Domain.Policies;

public sealed record SensitivityResult(bool IsSensitive, SensitiveCategory Category, string Reason);

public static class SensitiveQuestionPolicy
{
    private static readonly (Regex Pattern, SensitiveCategory Category, string Reason)[] Rules =
    [
        (new Regex(@"\b(gender|sex|male|female|non-binary|transgender)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            SensitiveCategory.Demographic, "Demographic / gender question"),

        (new Regex(@"\b(race|ethnicity|hispanic|latino|caucasian|black|asian|indigenous)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            SensitiveCategory.Demographic, "Demographic / race question"),

        (new Regex(@"\b(disabilit(y|ies)|handicap|physical condition|accommodation)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            SensitiveCategory.Disability, "Disability question requiring human decision"),

        (new Regex(@"\b(veteran|military|armed forces|service member)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            SensitiveCategory.Veteran, "Veteran status disclosure"),

        (new Regex(@"\b(felon(y|ies)?|convict(ed|ion)?|criminal|misdemeanor|arrest)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            SensitiveCategory.CriminalHistory, "Criminal record inquiry"),

        (new Regex(@"\b(sponsorship|visa|h-?1b|opt|cpt|work permit|require.*sponsorship)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            SensitiveCategory.Sponsorship, "Immigration and visa sponsorship question"),

        (new Regex(@"\b(authorized to work|legal.*right to work|work authorization)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            SensitiveCategory.LegalAuthorization, "Legal work authorization question"),

        (new Regex(@"\b(certify|attest|declare|under penalty of perjury|information is true)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            SensitiveCategory.Attestation, "Legal attestation / signature requiring manual review"),

        (new Regex(@"\b(consent|agree to terms|privacy notice|sms opt-?in|background check authorization)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            SensitiveCategory.Consent, "Consent declaration requiring explicit user agreement"),

        (new Regex(@"\b(current (salary|ctc|compensation)|expected (salary|ctc|compensation))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            SensitiveCategory.Compensation, "Compensation / salary inquiry"),

        (new Regex(@"\b(date of birth|birth date|how old are you|age)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            SensitiveCategory.Age, "Age / date of birth question")
    ];

    public static SensitivityResult Evaluate(string label, string? helpText = null, string? sectionTitle = null)
    {
        var combined = $"{sectionTitle} {label} {helpText}";

        // Specific category pattern matching first
        foreach (var (pattern, category, reason) in Rules)
        {
            if (pattern.IsMatch(combined))
            {
                return new SensitivityResult(true, category, reason);
            }
        }

        // Workday specific sections treated wholesale as sensitive if not specifically categorized above
        if (!string.IsNullOrWhiteSpace(sectionTitle))
        {
            var sec = sectionTitle.Trim().ToLowerInvariant();
            if (sec.Contains("voluntary disclosure") || sec.Contains("self identify") || sec.Contains("equal employment") || sec.Contains("eeo"))
            {
                return new SensitivityResult(true, SensitiveCategory.Demographic,
                    $"Section '{sectionTitle}' is classified as voluntary disclosure / sensitive.");
            }
        }

        return new SensitivityResult(false, SensitiveCategory.None, "Non-sensitive field");
    }
}

