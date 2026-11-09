using System.Text.Json;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Enums;
using JobPilot.Domain.Policies;

namespace JobPilot.Application.Features.Mapping;

public sealed class AdapterRuleMapper : IAdapterRuleMapper
{
    private static readonly Dictionary<string, (string ProfilePath, decimal Confidence)> WorkdayRules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["legalNameSection_firstName"] = ("Personal.FirstName", 0.99m),
            ["legalNameSection-firstName"] = ("Personal.FirstName", 0.99m),
            ["firstName"] = ("Personal.FirstName", 0.98m),
            ["legalNameSection_lastName"] = ("Personal.LastName", 0.99m),
            ["legalNameSection-lastName"] = ("Personal.LastName", 0.99m),
            ["lastName"] = ("Personal.LastName", 0.98m),
            ["email"] = ("Contact.Email", 0.99m),
            ["emailAddress"] = ("Contact.Email", 0.99m),
            ["contactSection_email"] = ("Contact.Email", 0.99m),
            ["phone-number"] = ("Contact.PhoneNational", 0.99m),
            ["phoneNumber"] = ("Contact.PhoneNational", 0.99m),
            ["contactSection_phone"] = ("Contact.PhoneNational", 0.99m),
            ["addressSection_addressLine1"] = ("Address.Line1", 0.99m),
            ["addressLine1"] = ("Address.Line1", 0.98m),
            ["addressSection_city"] = ("Address.City", 0.99m),
            ["city"] = ("Address.City", 0.98m),
            ["addressSection_countryRegion"] = ("Address.Country", 0.99m),
            ["countryDropdown"] = ("Address.Country", 0.99m),
            ["addressSection_postalCode"] = ("Address.PostalCode", 0.99m),
            ["postalCode"] = ("Address.PostalCode", 0.98m),
            ["linkedin-url"] = ("Links.LinkedIn", 0.99m),
            ["linkedinUrl"] = ("Links.LinkedIn", 0.99m),
            ["github-url"] = ("Links.GitHub", 0.99m),
            ["notice-period"] = ("Preferences.NoticePeriodDays", 0.98m),
            ["experience-years"] = ("Employment.TotalExperienceYears", 0.98m),
            ["current-company"] = ("Employment.Current.CompanyName", 0.98m),
            ["salary-expectation"] = ("Compensation.ExpectedSalary", 0.95m)
        };

    public FieldMappingResult? TryMap(FieldDescriptor field, VerifiedProfileSnapshot profile, MappingContext context)
    {
        // 1. Check identifier / automation ID
        string? identifier = field.FieldIdentifier;
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        // Try exact match or cleaned automation-id
        var cleanId = identifier.Replace("data-automation-id=", "").Trim('"', '\'', ' ');
        if (!WorkdayRules.TryGetValue(cleanId, out var rule))
        {
            // Try strip common Workday prefix
            var idx = cleanId.LastIndexOf('_');
            if (idx >= 0 && WorkdayRules.TryGetValue(cleanId[(idx + 1)..], out rule))
            {
                // matched suffix
            }
            else
            {
                return null;
            }
        }

        // Check if the profile fact is verified in profile
        if (!profile.VerifiedFacts.TryGetValue(rule.ProfilePath, out var factValue) || factValue == null)
        {
            return new FieldMappingResult(
                field.FieldId,
                rule.ProfilePath,
                SuggestedValue: null,
                SuggestedOptionKey: null,
                ConfidenceScore: 0.50m,
                Method: MappingMethod.AdapterRule,
                Source: MappingSource.VerifiedProfile,
                Status: MappingStatus.NeedsReview,
                RequiresIntervention: true,
                IsSensitive: false,
                Reason: $"Matched adapter rule '{cleanId}' -> '{rule.ProfilePath}', but profile fact is not verified.");
        }

        var valStr = factValue.ToString();

        return new FieldMappingResult(
            field.FieldId,
            rule.ProfilePath,
            SuggestedValue: valStr,
            SuggestedOptionKey: null,
            ConfidenceScore: rule.Confidence,
            Method: MappingMethod.AdapterRule,
            Source: MappingSource.VerifiedProfile,
            Status: MappingStatus.Proposed,
            RequiresIntervention: false,
            IsSensitive: false,
            Reason: $"Matched Workday adapter rule '{cleanId}' with verified profile fact.");
    }
}

