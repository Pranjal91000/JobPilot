using System.Text.RegularExpressions;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Enums;

namespace JobPilot.Application.Features.Mapping;

public sealed class DictionaryMapper : IDictionaryMapper
{
    private sealed record DictionaryRule(Regex Pattern, string ProfilePath, decimal Confidence);

    private static readonly DictionaryRule[] Rules =
    [
        new(new Regex(@"^(first\s*names?(\(s\))?|given\s*names?(\(s\))?|prénom|forename)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Personal.FirstName", 0.97m),
        new(new Regex(@"^(last\s*names?(\(s\))?|family\s*names?(\(s\))?|surname|nom(\s*de\s*famille)?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Personal.LastName", 0.97m),
        new(new Regex(@"^(email(\s*address)?|e-mail(\s*address)?|courriel)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Contact.Email", 0.97m),
        new(new Regex(@"^(phone(\s*number)?|mobile(\s*phone)?|cell(\s*phone)?|telephone|contact\s*number)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Contact.PhoneNational", 0.97m),
        new(new Regex(@"^(street(\s*address)?|address(\s*line\s*1)?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Address.Line1", 0.96m),
        new(new Regex(@"^(city|town|city\s*\/\s*town)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Address.City", 0.96m),
        new(new Regex(@"^(country|country\s*\/\s*region)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Address.Country", 0.96m),
        new(new Regex(@"^(postal\s*code|zip(\s*code)?|pin\s*code)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Address.PostalCode", 0.96m),
        new(new Regex(@"(linkedin(\s*url|\s*profile)?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Links.LinkedIn", 0.97m),
        new(new Regex(@"(github(\s*url|\s*profile)?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Links.GitHub", 0.97m),
        new(new Regex(@"^(notice\s*period(\s*in\s*days|\s*\(days\))?|availability(\s*\(days\))?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Preferences.NoticePeriodDays", 0.95m),
        new(new Regex(@"^(total\s*years(\s*of)?\s*experience|years\s*of\s*experience|total\s*experience)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Employment.TotalExperienceYears", 0.95m),
        new(new Regex(@"^(current\s*(employer|company(\s*name)?)|present\s*employer)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Employment.Current.CompanyName", 0.95m),
        new(new Regex(@"^(job\s*title|position|role|title)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Employment.Current.JobTitle", 0.96m),
        new(new Regex(@"^(company(\s*name)?|employer(\s*name)?|organization)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Employment.Current.CompanyName", 0.96m),
        new(new Regex(@"^(school(\s*or\s*university)?|university|institution|college)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Education.Highest.School", 0.96m),
        new(new Regex(@"^(degree(\s*level)?|qualification)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Education.Highest.Degree", 0.96m),
        new(new Regex(@"^(field\s*of\s*study|major|specialization)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Education.Highest.FieldOfStudy", 0.96m),
        new(new Regex(@"^(skills(\s*and\s*competencies)?|technical\s*skills)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Skills.All", 0.95m),
        new(new Regex(@"(salary|ctc|compensation).*expectation|expected\s*(salary|ctc|compensation)|desired\s*salary", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Compensation.ExpectedSalary", 0.92m),
        new(new Regex(@"^(resume(\s*\/\s*cv)?|cv|curriculum\s*vitae|attach(\s*your)?\s*resume)$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Documents.Resume", 0.98m)
    ];

    public FieldMappingResult? TryMap(FieldDescriptor field, VerifiedProfileSnapshot profile, MappingContext context)
    {
        var label = field.Label.Trim();
        if (string.IsNullOrWhiteSpace(label))
            return null;

        // Clean label: remove trailing asterisks, colons, question marks
        var cleaned = Regex.Replace(label, @"[\*:\?]+$", "").Trim();

        foreach (var rule in Rules)
        {
            if (rule.Pattern.IsMatch(cleaned))
            {
                if (!profile.VerifiedFacts.TryGetValue(rule.ProfilePath, out var factValue) || factValue == null)
                {
                    return new FieldMappingResult(
                        field.FieldId,
                        rule.ProfilePath,
                        SuggestedValue: null,
                        SuggestedOptionKey: null,
                        ConfidenceScore: 0.50m,
                        Method: MappingMethod.Dictionary,
                        Source: MappingSource.VerifiedProfile,
                        Status: MappingStatus.NeedsReview,
                        RequiresIntervention: true,
                        IsSensitive: false,
                        Reason: $"Matched dictionary rule '{cleaned}' -> '{rule.ProfilePath}', but fact is not verified.");
                }

                var valStr = factValue.ToString();

                return new FieldMappingResult(
                    field.FieldId,
                    rule.ProfilePath,
                    SuggestedValue: valStr,
                    SuggestedOptionKey: null,
                    ConfidenceScore: rule.Confidence,
                    Method: MappingMethod.Dictionary,
                    Source: MappingSource.VerifiedProfile,
                    Status: MappingStatus.Proposed,
                    RequiresIntervention: false,
                    IsSensitive: false,
                    Reason: $"Matched label dictionary rule for '{cleaned}'.");
            }
        }

        return null;
    }
}

